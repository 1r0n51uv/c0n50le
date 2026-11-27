using System.Runtime.InteropServices;
using Microsoft.UI;
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
/// The pill shown while recording: a red dot and the time running, at the top of the screen being recorded, over games
/// and the desktop alike. It never takes the focus, stays on top, and is left out of the recording itself.
/// </summary>
public sealed class RecordingIndicator
{
    private const int Width = 196, Height = 56;
    private readonly Window _window;
    private readonly TextBlock _time;
    private readonly Ellipse _dot;
    private readonly DispatcherQueueTimer _timer;
    private DateTimeOffset _started;

    public RecordingIndicator()
    {
        _dot = new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(Colors.Red), VerticalAlignment = VerticalAlignment.Center };
        _time = new TextBlock
        {
            Text = "REC 0:00", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Segoe UI Variable Display")
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(_dot);
        row.Children.Add(_time);
        _window = new Window
        {
            Content = new Grid { Background = new SolidColorBrush(ColorHelper.FromArgb(0xF2, 0x16, 0x16, 0x18)), Children = { row } },
            Title = "c0n50le REC"
        };
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
        SetWindowDisplayAffinity(handle, 0x11);
        var round = 2;
        DwmSetWindowAttribute(handle, 33, ref round, sizeof(int));
    }

    /// <summary>Shows it at the top centre of the monitor under <paramref name="foreground"/>, from now.</summary>
    public void Show(nint foreground)
    {
        _started = DateTimeOffset.Now;
        var scale = GetDpiForWindow(foreground) is > 0 and var dpi ? dpi / 96.0 : 1.0;
        var monitor = MonitorFromWindow(foreground, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var area = GetMonitorInfo(monitor, ref info) ? info.Work : new Rect { Right = 1920, Bottom = 1080 };
        var width = (int)Math.Round(Width * scale);
        var height = (int)Math.Round(Height * scale);
        _window.AppWindow.MoveAndResize(new RectInt32(area.Left + (area.Right - area.Left - width) / 2, area.Top + (int)(24 * scale), width, height));
        Tick();
        _window.AppWindow.Show(false);
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
        _time.Text = "REC " + (elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"m\:ss"));
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
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
