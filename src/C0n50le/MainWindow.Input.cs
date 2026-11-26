using C0n50le.Core.Audio;
using C0n50le.Core.Input;
using C0n50le.Core.Localization;
using C0n50le.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace C0n50le;

public sealed partial class MainWindow
{
    private readonly ControllerReader _controller = new();
    private readonly RepeatGate _repeat = new();
    private readonly InputModeTracker _input = new();
    private DispatcherQueueTimer? _pollTimer;
    private int _previousHeld;
    private bool _cursorHidden;
    private ControllerState _lastPad;

    private void InitializeInput()
    {
        _input.Changed += mode => DispatcherQueue.TryEnqueue(() => ApplyInputMode(mode));
        Root.PointerMoved += (_, args) =>
        {
            if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
            var point = args.GetCurrentPoint(Root).Position;
            _input.MouseMoved(point.X, point.Y, DateTimeOffset.Now);
        };
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
        {
            if (args.Pointer.PointerDeviceType == PointerDeviceType.Mouse) _input.MouseClicked();
        }), true);
        Root.PointerWheelChanged += (_, _) => _input.MouseWheel();
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnKeyDown), true);
        Home.Mode = _input.Mode;
    }

    private void StartPolling()
    {
        _pollTimer = DispatcherQueue.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromMilliseconds(30);
        _pollTimer.Tick += (_, _) => PollController();
        _pollTimer.Start();
    }

    private bool IsForeground => NativeWindows.GetForegroundWindow() == _handle && AppWindow.IsVisible;

    private void PollController()
    {
        ControllerState pad;
        try { pad = _controller.Poll(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { return; }
        var previous = _previousHeld;
        _previousHeld = pad.Held;
        if (pad.Connected != _lastPad.Connected || pad.Name != _lastPad.Name)
        {
            _lastPad = pad;
            UpdateInputLabel();
        }
        _lastPad = pad;
        UpdateControllerTest(pad);

        // The shortcut back to the home works from anywhere, a game in front included.
        if (ShortcutChoices.Triggered(_prefs.ControllerShortcut, previous, pad.Held) && (!IsForeground || _session is not null))
        {
            ShowHome();
            return;
        }
        if (!IsForeground || Root.XamlRoot is null) return;
        if (pad.Active)
        {
            Home.Family = pad.Family;
            _input.ControllerUsed();
        }
        if (IntroVisible) return;

        var moves = _repeat.Moves(pad.Held, DateTimeOffset.Now);
        if (moves != 0) MoveFocus(moves);
        var pressed = pad.Pressed;
        if ((pressed & ControllerButtons.Confirm) != 0) Confirm();
        if ((pressed & ControllerButtons.Back) != 0) GoBack();
        if ((pressed & ControllerButtons.Y) != 0) OpenOptions();
        if ((pressed & ControllerButtons.Menu) != 0) ToggleSystem();
        if ((pressed & ControllerButtons.LeftShoulder) != 0) StepCategory(-1);
        if ((pressed & ControllerButtons.RightShoulder) != 0) StepCategory(1);
    }

    private void MoveFocus(int moves)
    {
        if (_activeDialog is null && TourVisible) return;
        var direction = (moves & ControllerButtons.Up) != 0 ? FocusNavigationDirection.Up
            : (moves & ControllerButtons.Down) != 0 ? FocusNavigationDirection.Down
            : (moves & ControllerButtons.Left) != 0 ? FocusNavigationDirection.Left
            : FocusNavigationDirection.Right;
        if (_activeDialog is not null)
        {
            FocusManager.TryMoveFocus(direction, new FindNextElementOptions { SearchRoot = _activeDialog.XamlRoot.Content });
            return;
        }
        var scope = ActiveLayer;
        var focused = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
        if (focused is null || !IsInside(focused, scope))
        {
            FocusFirst(scope);
            return;
        }
        if (FocusManager.TryMoveFocus(direction, new FindNextElementOptions { SearchRoot = scope }))
            Sounds.Play(UiSound.Move);
    }

    /// <summary>The part of the screen focus may move in: the topmost open layer.</summary>
    private UIElement ActiveLayer =>
        Picker.Visibility == Visibility.Visible ? Picker
        : SystemPage.Visibility == Visibility.Visible ? SystemPage
        : HomePage;

    private static bool IsInside(DependencyObject element, DependencyObject scope)
    {
        for (var node = element; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, scope)) return true;
        return false;
    }

    private void FocusFirst(UIElement scope)
    {
        if (scope == HomePage) FocusSelection();
        else if (scope == SystemPage) FocusCategory();
        else if (FocusManager.FindFirstFocusableElement(scope) is Control control) control.Focus(FocusState.Keyboard);
    }

    private void Confirm()
    {
        if (_activeDialog is not null || !TourVisible)
        {
            ReturnHotkey.Invoke(FocusManager.GetFocusedElement(Root.XamlRoot));
            return;
        }
        TourAdvance();
    }

    /// <summary>B / Esc: closes the topmost thing, and from the home goes back to the desktop.</summary>
    private void GoBack()
    {
        if (_activeDialog is not null) { _activeDialog.Hide(); return; }
        if (TourVisible) { EndTour(); return; }
        if (Picker.Visibility == Visibility.Visible) { ClosePicker(); return; }
        if (SystemPage.Visibility == Visibility.Visible) { CloseSystem(); return; }
        Sounds.Play(UiSound.Back);
        HideHome();
    }

    private void OpenOptions()
    {
        if (_activeDialog is not null || TourVisible || Picker.Visibility == Visibility.Visible) return;
        if (SystemPage.Visibility == Visibility.Visible) return;
        OpenCardMenu();
    }

    private void ToggleSystem()
    {
        if (_activeDialog is not null || TourVisible || Picker.Visibility == Visibility.Visible) return;
        if (SystemPage.Visibility == Visibility.Visible) CloseSystem();
        else OpenSystem();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is >= VirtualKey.GamepadA and <= VirtualKey.GamepadRightThumbstickLeft)
        {
            // The controller is read directly; WinUI's own gamepad keys would move focus twice.
            args.Handled = true;
            return;
        }
        _input.KeyPressed();
        if (args.Handled) return;
        switch (args.Key)
        {
            case VirtualKey.Escape:
                GoBack();
                args.Handled = true;
                break;
            case VirtualKey.Application:
            case VirtualKey.F10 when IsShiftDown():
                OpenOptions();
                args.Handled = true;
                break;
            case VirtualKey.F1:
                ToggleSystem();
                args.Handled = true;
                break;
            case VirtualKey.Enter when TourVisible && _activeDialog is null:
                TourAdvance();
                args.Handled = true;
                break;
        }
    }

    private static bool IsShiftDown() =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    /// <summary>The layout follows the device in use: hints and focus for the controller, buttons and pointer for the mouse.</summary>
    private void ApplyInputMode(InputMode mode)
    {
        Home.Mode = mode;
        UpdateCards();
        UpdateInputLabel();
        if (mode == InputMode.Controller)
        {
            HideCursor();
            var focused = Root.XamlRoot is null ? null : FocusManager.GetFocusedElement(Root.XamlRoot) as Control;
            if (_activeDialog is null && (focused is null || !IsInside(focused, ActiveLayer) || focused.FocusState == FocusState.Pointer))
            {
                if (focused is not null && IsInside(focused, ActiveLayer) && focused.Visibility == Visibility.Visible)
                    focused.Focus(FocusState.Keyboard);
                else FocusFirst(ActiveLayer);
            }
        }
        else
        {
            RestoreCursor();
            // The focus ring is for the controller and the keyboard; a mouse user sees hover instead.
            if (Root.XamlRoot is not null && FocusManager.GetFocusedElement(Root.XamlRoot) is Control control && control.FocusState == FocusState.Keyboard)
                control.Focus(FocusState.Pointer);
        }
    }

    private void UpdateInputLabel()
    {
        Home.InputLabel = _input.Mode == InputMode.Controller
            ? (_lastPad.Name.Length > 0 ? _lastPad.Name : Strings.Get("Controller"))
            : Strings.Get("MouseKeyboard");
    }

    private void HideCursor()
    {
        if (_cursorHidden || !IsForeground) return;
        _cursorHidden = true;
        NativeWindows.ShowCursor(false);
    }

    private void RestoreCursor()
    {
        if (!_cursorHidden) return;
        _cursorHidden = false;
        NativeWindows.ShowCursor(true);
    }
}
