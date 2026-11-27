using C0n50le.Core.Audio;
using C0n50le.Core.Input;
using C0n50le.Core.Localization;
using C0n50le.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
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
    private bool _topBarPointerInside;
    private ControllerState _lastPad;

    private void InitializeInput()
    {
        _input.Changed += mode => DispatcherQueue.TryEnqueue(() => ApplyInputMode(mode));
        Root.PointerMoved += (_, args) =>
        {
            if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
            var point = args.GetCurrentPoint(Root).Position;
            _input.MouseMoved(point.X, point.Y, DateTimeOffset.Now);
            UpdateTopBarPointer(point);
            if (_input.Mode == InputMode.MouseKeyboard) FocusUnderPointer(point);
        };
        Root.PointerExited += (_, args) =>
        {
            if (args.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
            _topBarPointerInside = false;
            UpdateTopBar();
        };
        SystemPill.GotFocus += (_, _) => UpdateTopBar();
        DesktopPill.GotFocus += (_, _) => UpdateTopBar();
        SystemPill.LostFocus += (_, _) => DispatcherQueue.TryEnqueue(UpdateTopBar);
        DesktopPill.LostFocus += (_, _) => DispatcherQueue.TryEnqueue(UpdateTopBar);
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
        if (_input.Mode == InputMode.Controller) HideCursor();
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
        if (TourVisible) return;
        var direction = (moves & ControllerButtons.Up) != 0 ? FocusNavigationDirection.Up
            : (moves & ControllerButtons.Down) != 0 ? FocusNavigationDirection.Down
            : (moves & ControllerButtons.Left) != 0 ? FocusNavigationDirection.Left
            : FocusNavigationDirection.Right;
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
        AlertOpen ? AlertLayer
        : MenuOpen ? MenuLayer
        : SystemPage.Visibility == Visibility.Visible ? SystemPage
        : DetailsOpen ? DetailsPage
        : Stage;

    private static bool IsInside(DependencyObject element, DependencyObject scope)
    {
        for (var node = element; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, scope)) return true;
        return false;
    }

    private void FocusFirst(UIElement scope)
    {
        if (scope == Stage) FocusSelection();
        else if (scope == SystemPage) FocusCategory();
        else if (scope == DetailsPage) FocusDetails();
        else if (FocusManager.FindFirstFocusableElement(scope) is Control control) control.Focus(FocusState.Keyboard);
    }

    private void Confirm()
    {
        if (TourVisible && !AlertOpen) { TourAdvance(); return; }
        ReturnHotkey.Invoke(FocusManager.GetFocusedElement(Root.XamlRoot));
    }

    /// <summary>B / Esc: closes the topmost thing, and from the home goes back to the desktop.</summary>
    private void GoBack()
    {
        if (AlertOpen) { DismissAlert(); return; }
        if (TourVisible) { EndTour(); return; }
        if (MenuOpen) { CloseMenu(); return; }
        if (SystemPage.Visibility == Visibility.Visible) { GoBackInSystem(); return; }
        if (DetailsOpen) { CloseDetails(); return; }
        Sounds.Play(UiSound.Back);
        HideHome();
    }

    private void OpenOptions()
    {
        if (IsOverlayOpen || PageOpen) return;
        OpenCardMenu();
    }

    private void ToggleSystem()
    {
        if (IsOverlayOpen || DetailsOpen) return;
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
            case VirtualKey.Enter when TourVisible && !AlertOpen:
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
            if (focused is null || !IsInside(focused, ActiveLayer))
            {
                if (focused is not null && IsInside(focused, ActiveLayer) && focused.Visibility == Visibility.Visible)
                    focused.Focus(FocusState.Keyboard);
                else FocusFirst(ActiveLayer);
            }
        }
        else RestoreCursor();
    }

    /// <summary>
    /// With the mouse, pointing at something gives it the focus, as the Siri Remote's touch surface does on tvOS: the same
    /// platter and lift show for mouse and controller, and there is never a second highlight.
    /// </summary>
    private void FocusUnderPointer(Windows.Foundation.Point point)
    {
        if (Root.XamlRoot is null || IntroVisible || TourVisible) return;
        var layer = ActiveLayer;
        foreach (var element in Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(point, Root))
        {
            if (element is not Button { IsEnabled: true, IsTabStop: true } button || !IsInside(button, layer)) continue;
            if (button.FocusState == FocusState.Unfocused) button.Focus(FocusState.Pointer);
            return;
        }
    }

    private void UpdateInputLabel()
    {
        Home.InputLabel = _input.Mode == InputMode.Controller
            ? (_lastPad.Name.Length > 0 ? _lastPad.Name : Strings.Get("Controller"))
            : Strings.Get("MouseKeyboard");
        Home.DisplayLabel = Displays.ActiveLabel(_handle, Strings.Language);
        UpdateTopBar();
    }

    private void UpdateTopBarPointer(Windows.Foundation.Point point)
    {
        var bounds = TopBar.TransformToVisual(Root).TransformBounds(new Windows.Foundation.Rect(0, 0, TopBar.ActualWidth, TopBar.ActualHeight));
        _topBarPointerInside = bounds.Contains(point);
        UpdateTopBar();
    }

    private void UpdateTopBar()
    {
        var focused = Root.XamlRoot is null ? null : FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
        var expanded = _topBarPointerInside ||
                       (Home.IsControllerMode && focused is not null && IsInside(focused, TopBar));
        if (Home.IsAppBarExpanded == expanded) return;
        Home.IsAppBarExpanded = expanded;
        // Each side opens to the width of its text (capped, so the bar never runs across the screen) and fades in.
        AnimateDetail(InputDetail, InputDetailText, expanded, 380);
        AnimateDetail(DisplayDetail, DisplayDetailText, expanded, 620);
    }

    private void AnimateDetail(FrameworkElement host, TextBlock text, bool open, double maxWidth)
    {
        // The margin sits outside the text's own width: leave room for it, or the last letters are cut by the host.
        var margin = text.Margin.Left + text.Margin.Right;
        text.MaxWidth = maxWidth - margin;
        text.ClearValue(FrameworkElement.WidthProperty);
        text.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var target = open ? Math.Ceiling(Math.Min(maxWidth, text.DesiredSize.Width)) + 1 : 0;
        // The text keeps its open width while the host slides, so it is revealed instead of re-trimmed on every frame.
        if (open) text.Width = Math.Max(0, target - margin);
        var duration = new Duration(TimeSpan.FromMilliseconds(Motion.Enabled(Motion.Reduce) ? 280 : 0));
        var ease = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut };
        var width = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = target, Duration = duration, EasingFunction = ease, EnableDependentAnimation = true
        };
        var fade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = open ? 1 : 0, Duration = new Duration(TimeSpan.FromMilliseconds(Motion.Enabled(Motion.Reduce) ? (open ? 320 : 160) : 0)),
            EasingFunction = ease
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(width, host);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(width, "Width");
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, host);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");
        var story = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        story.Children.Add(width);
        story.Children.Add(fade);
        story.Begin();
    }

    /// <summary>In controller mode the pointer disappears over the home; it comes back as soon as the mouse moves.</summary>
    private void HideCursor()
    {
        if (!IsForeground) return;
        _cursorHidden = true;
        CursorHider.Hide();
    }

    private void RestoreCursor()
    {
        if (!_cursorHidden) return;
        _cursorHidden = false;
        CursorHider.Show();
    }
}
