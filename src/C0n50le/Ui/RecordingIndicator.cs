using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;

namespace C0n50le.Ui;

/// <summary>
/// The pill shown while recording, in the home's app bar style: the same glass, height, corner, type and colours, with
/// a red dot, REC and the time running. It sits at the top of the screen being recorded, over games and the desktop,
/// never takes the focus, and is left out of the recording itself.
/// </summary>
public sealed class RecordingIndicator
{
    // The app bar's own measures, in the home's 1080-high layout.
    private const double Width = 214, Height = 72, Top = 48;
    private readonly Window _window;
    private readonly TextBlock _label;
    private readonly TextBlock _time;
    private readonly Ellipse _dot;
    private readonly Grid _surface;
    private readonly Border _tint;
    private readonly DispatcherQueueTimer _timer;
    private DateTimeOffset _started;

    public RecordingIndicator()
    {
        var font = (FontFamily)Application.Current.Resources["AppFont"];
        _dot = new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xFF, 0x3B, 0x30)), VerticalAlignment = VerticalAlignment.Center };
        _label = new TextBlock { Text = "REC", FontFamily = font, FontSize = 21, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        _time = new TextBlock { Text = "0:00", FontFamily = font, FontSize = 21, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Children = { _dot, _label, _time }
        };
        // The app bar's glass over the desktop: the window is clear and only the pill is drawn, with the bar's tint,
        // sheen and lit rim (a separate window can't blur or bend the screen behind it), the text on top.
        _tint = new Border { CornerRadius = new CornerRadius(Height / 2) };
        _surface = new Grid
        {
            Children =
            {
                _tint,
                new Border { Background = ThemeManager.Brush("SheenBrush"), CornerRadius = new CornerRadius(Height / 2), IsHitTestVisible = false },
                row,
                new Border
                {
                    BorderBrush = ThemeManager.Brush("RimBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(Height / 2),
                    IsHitTestVisible = false
                }
            }
        };
        // The layout is drawn at the app bar's size and scaled with the screen, like the home.
        var stage = new Viewbox { Stretch = Stretch.Uniform, Child = new Grid { Width = Width, Height = Height, Children = { _surface } } };
        _window = new Window { Content = stage, Title = "c0n50le REC", SystemBackdrop = new ClearBackdrop() };
        _timer = _window.DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.Tick += (_, _) => Tick();
        Setup();
    }

    private nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(_window);

    private void Setup()
    {
        var appWindow = _window.AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }
        appWindow.IsShownInSwitchers = false;
        var handle = Handle;
        // No taskbar button, never activated by a click, and left out of every screen capture (ours included).
        SetWindowLongPtr(handle, -20, GetWindowLongPtr(handle, -20) | 0x80 | 0x08000000);
        // No frame of its own: square corners and no border, so nothing shows around the pill.
        var square = 1;
        DwmSetWindowAttribute(handle, 33, ref square, sizeof(int));
        var none = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(handle, 34, ref none, sizeof(int));
        SetWindowDisplayAffinity(handle, 0x11);
    }

    /// <summary>Takes the theme's text colour; the glass follows the theme by itself.</summary>
    private void Paint()
    {
        var theme = ThemeManager.Current;
        _tint.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(theme.IsLight ? (byte)0xC8 : (byte)0xBF,
            theme.Material.R, theme.Material.G, theme.Material.B));
        _label.Foreground = _time.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(theme.Text.A, theme.Text.R, theme.Text.G, theme.Text.B));
        if (_window.Content is FrameworkElement root) root.RequestedTheme = ThemeManager.ElementTheme;
    }

    /// <summary>Shows it at the top centre of the monitor under <paramref name="foreground"/>, from now.</summary>
    public void Show(nint foreground)
    {
        _started = DateTimeOffset.Now;
        Paint();
        var monitor = MonitorFromWindow(foreground, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var screen = GetMonitorInfo(monitor, ref info) ? info.Monitor : new Rect { Right = 1920, Bottom = 1080 };
        // The home is laid out 1080 high and grown to the screen: the pill grows the same way.
        var scale = (screen.Bottom - screen.Top) / 1080.0;
        var width = (int)Math.Round(Width * scale);
        var height = (int)Math.Round(Height * scale);
        _window.AppWindow.MoveAndResize(new RectInt32(screen.Left + (screen.Right - screen.Left - width) / 2,
            screen.Top + (int)Math.Round(Top * scale), width, height));
        Tick();
        _window.AppWindow.Show(false);
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, height, height);
        if (region != 0 && SetWindowRgn(Handle, region, true) == 0) DeleteObject(region);
        _timer.Start();
    }

    public void Hide()
    {
        _timer.Stop();
        _window.AppWindow.Hide();
    }

    public void Close()
    {
        _timer.Stop();
        _window.Close();
    }

    private void Tick()
    {
        var elapsed = DateTimeOffset.Now - _started;
        _time.Text = elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"m\:ss");
        // The dot blinks every second, the way a camera's does.
        _dot.Opacity = elapsed.Milliseconds < 500 ? 1 : 0.35;
        // A game coming to the front may have pushed it down: keep it on top without stealing the focus.
        SetWindowPos(Handle, -1, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }

    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window, nint region, bool redraw);
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions { public int Size, ThreadType, Apartment; }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);

    /// <summary>A fully clear window background, so only what the content draws is visible.</summary>
    private sealed class ClearBackdrop : SystemBackdrop
    {
        private static Windows.System.DispatcherQueueController? _queueController;
        private Windows.UI.Composition.Compositor? _compositor;

        protected override void OnTargetConnected(Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop target, XamlRoot root)
        {
            base.OnTargetConnected(target, root);
            // The system compositor needs a system dispatcher queue on this thread (WinUI runs its own).
            if (Windows.System.DispatcherQueue.GetForCurrentThread() is null)
            {
                var options = new DispatcherQueueOptions { Size = Marshal.SizeOf<DispatcherQueueOptions>(), ThreadType = 2, Apartment = 2 };
                Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out var controller));
                try { _queueController = WinRT.MarshalInterface<Windows.System.DispatcherQueueController>.FromAbi(controller); }
                finally { Marshal.Release(controller); }
            }
            _compositor ??= new Windows.UI.Composition.Compositor();
            target.SystemBackdrop = _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }

        protected override void OnTargetDisconnected(Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop target)
        {
            base.OnTargetDisconnected(target);
            target.SystemBackdrop = null;
        }
    }
}
