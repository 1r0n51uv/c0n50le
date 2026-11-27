using System.Runtime.InteropServices;
using C0n50le.Core.Theming;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace C0n50le.Services;

/// <summary>
/// The keyboard shortcut back to the home: Ctrl+Alt+F-key, registered for the window so it works while a game is in
/// front. Adapted from RON1N Mode.
/// </summary>
public static class ReturnHotkey
{
    private const int Id = 1;
    private const uint ModAlt = 0x0001, ModControl = 0x0002, ModNoRepeat = 0x4000;
    private delegate nint Subclass(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    private static Subclass? _callback;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, Subclass callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, Subclass callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);

    public static void Hook(nint window, Action action)
    {
        _callback = (hwnd, message, wParam, lParam, _, _) =>
        {
            if (message == 0x0312 && wParam == Id)
            {
                try { action(); }
                catch (Exception ex) { AppLog.Write($"Scorciatoia: {ex.Message}"); }
                return 0;
            }
            return DefSubclassProc(hwnd, message, wParam, lParam);
        };
        SetWindowSubclass(window, _callback, Id, 0);
    }

    /// <summary>Registers F8–F12 with Ctrl+Alt, or nothing for an empty key. False when another app holds it.</summary>
    public static bool Register(nint window, string key)
    {
        UnregisterHotKey(window, Id);
        if (string.IsNullOrEmpty(key) || !key.StartsWith('F') || !int.TryParse(key[1..], out var number) || number is < 1 or > 12)
            return true;
        return RegisterHotKey(window, Id, ModAlt | ModControl | ModNoRepeat, (uint)(0x70 + number - 1));
    }

    public static void Unhook(nint window)
    {
        UnregisterHotKey(window, Id);
        if (_callback is not null) RemoveWindowSubclass(window, _callback, Id);
    }

    /// <summary>Presses the focused control the way a click would (A on the controller).</summary>
    public static void Invoke(object? element)
    {
        if (element is not UIElement target) return;
        var peer = FrameworkElementAutomationPeer.FromElement(target) ?? FrameworkElementAutomationPeer.CreatePeerForElement(target);
        if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke) invoke.Invoke();
        else if (peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle) toggle.Toggle();
        else if (peer?.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider selection) selection.Select();
        else if (target is TextBox box) box.Focus(FocusState.Keyboard);
    }
}

/// <summary>"Start with Windows": a per-user Run entry that opens c0n50le (to the tray or full screen, per settings).</summary>
public static class StartupRegistration
{
    public const string TrayArgument = "--tray";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "c0n50le";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string value && value.Length > 0;
            }
            catch (Exception) { return false; }
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --startup");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
        AppLog.Write($"Avvio con Windows: {enabled}");
    }

    /// <summary>The language picked in the installer (C0n50le.iss writes it), for the very first run.</summary>
    public static string? InstallerLanguage()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\c0n50le");
            return key?.GetValue("Language") as string;
        }
        catch (Exception) { return null; }
    }
}

/// <summary>The dominant color of a game's picture, for the ambient light and the themes that follow it.</summary>
public static class ArtworkPalette
{
    public static async Task<Argb?> ReadAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var transform = new BitmapTransform { ScaledWidth = 24, ScaledHeight = 24 };
            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform,
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            var pixels = data.DetachPixelData();
            double red = 0, green = 0, blue = 0, weight = 0;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var r = pixels[i + 2];
                var g = pixels[i + 1];
                var b = pixels[i];
                var saturation = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
                if (pixels[i + 3] < 128 || saturation < 24) continue;
                weight += saturation;
                red += saturation * r;
                green += saturation * g;
                blue += saturation * b;
            }
            if (weight <= 0) return null;
            return new Argb(255, (byte)(red / weight), (byte)(green / weight), (byte)(blue / weight));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or COMException)
        {
            return null;
        }
    }
}

/// <summary>The screens the home can open on.</summary>
public static class Displays
{
    public sealed record Choice(string Id, string Label);

    /// <summary>
    /// A short line for the app bar: the TV or monitor's own name (from its EDID, e.g. "LG TV SSCR2"), its resolution
    /// and refresh rate ("4K · 120 Hz"), for the screen the window is on.
    /// </summary>
    public static string ActiveLabel(nint window, string language)
    {
        var fallback = language.StartsWith("it", StringComparison.OrdinalIgnoreCase) ? "Schermo" : "Display";
        try
        {
            var monitor = MonitorFromWindow(window, 2);
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (monitor == 0 || !GetMonitorInfo(monitor, ref info)) return fallback;
            var width = info.Monitor.Right - info.Monitor.Left;
            var height = info.Monitor.Bottom - info.Monitor.Top;
            var (name, hertz) = Describe(info.Device);
            return Core.Theming.DisplayText.Label(name.Length > 0 ? name : fallback, width, height, hertz);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException)
        {
            AppLog.Write($"Schermo: {ex.Message}");
            return fallback;
        }
    }

    /// <summary>The friendly name and refresh rate of the monitor shown by a GDI source (\\.\DISPLAY1).</summary>
    private static (string Name, double Hertz) Describe(string gdiName)
    {
        if (GetDisplayConfigBufferSizes(2, out var pathCount, out var modeCount) != 0) return ("", 0);
        var paths = new PathInfo[pathCount];
        var modes = new ModeInfo[modeCount];
        if (QueryDisplayConfig(2, ref pathCount, paths, ref modeCount, modes, 0) != 0) return ("", 0);
        for (var index = 0; index < pathCount; index++)
        {
            var path = paths[index];
            var source = new SourceName { Header = new InfoHeader { Type = 1, Size = Marshal.SizeOf<SourceName>(), Adapter = path.SourceAdapter, Id = path.SourceId } };
            if (DisplayConfigGetDeviceInfo(ref source) != 0 || !string.Equals(source.GdiName, gdiName, StringComparison.OrdinalIgnoreCase)) continue;
            var target = new TargetName { Header = new InfoHeader { Type = 2, Size = Marshal.SizeOf<TargetName>(), Adapter = path.TargetAdapter, Id = path.TargetId } };
            var name = DisplayConfigGetDeviceInfo(ref target) == 0 ? target.FriendlyName ?? "" : "";
            var hertz = path.RefreshDenominator > 0 ? (double)path.RefreshNumerator / path.RefreshDenominator : 0;
            return (name.Trim(), hertz);
        }
        return ("", 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint Low; public int High; }

    // DISPLAYCONFIG_PATH_INFO, flattened: source info (20 bytes), target info (48), flags.
    [StructLayout(LayoutKind.Sequential)]
    private struct PathInfo
    {
        public Luid SourceAdapter;
        public uint SourceId, SourceModeIndex, SourceFlags;
        public Luid TargetAdapter;
        public uint TargetId, TargetModeIndex;
        public int OutputTechnology, Rotation, Scaling;
        public uint RefreshNumerator, RefreshDenominator;
        public int ScanLineOrdering, TargetAvailable;
        public uint TargetFlags, Flags;
    }

    // DISPLAYCONFIG_MODE_INFO: only its size matters here.
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct ModeInfo { public uint InfoType, Id; }

    [StructLayout(LayoutKind.Sequential)]
    private struct InfoHeader { public int Type, Size; public Luid Adapter; public uint Id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SourceName
    {
        public InfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string GdiName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TargetName
    {
        public InfoHeader Header;
        public uint Flags;
        public int OutputTechnology;
        public ushort ManufactureId, ProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
    }

    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out int paths, out int modes);
    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref int pathCount, [Out] PathInfo[] paths, ref int modeCount, [Out] ModeInfo[] modes, nint topology);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref SourceName request);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref TargetName request);

    public static List<DisplayArea> All()
    {
        var displays = new List<DisplayArea>();
        try
        {
            var areas = DisplayArea.FindAll();
            // Index the WinRT vector directly: its IEnumerable projection can fail on desktop.
            for (var index = 0; index < areas.Count; index++) displays.Add(areas[index]);
        }
        catch (Exception ex) when (ex is InvalidCastException or COMException)
        {
            AppLog.Write($"Schermi: {ex.Message}");
        }
        if (displays.Count == 0 && DisplayArea.Primary is { } primary) displays.Add(primary);
        return displays;
    }

    public static DisplayArea? Find(string id) =>
        id.Length == 0 ? DisplayArea.Primary : All().FirstOrDefault(area => area.DisplayId.Value.ToString() == id) ?? DisplayArea.Primary;

    public static List<Choice> Choices(Func<int, bool, string, string> label)
    {
        var primary = DisplayArea.Primary?.DisplayId.Value;
        return All().Select((area, index) => new Choice(area.DisplayId.Value.ToString(),
            label(index + 1, area.DisplayId.Value == primary, $"{area.OuterBounds.Width}×{area.OuterBounds.Height}"))).ToList();
    }
}
