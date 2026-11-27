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
    private const uint DisplayDeviceActive = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice displayDevice, uint flags);

    public sealed record Choice(string Id, string Label);

    public static string ActiveLabel(DisplayArea? area, string language)
    {
        var displays = All();
        var index = displays.FindIndex(display => display.DisplayId.Value == area?.DisplayId.Value);
        var number = Math.Max(0, index);
        var name = MonitorNames().ElementAtOrDefault(number);
        if (string.IsNullOrWhiteSpace(name)) name = language == "it-IT" ? $"Schermo {number + 1}" : $"Display {number + 1}";
        return $"{name} · {area?.OuterBounds.Width ?? 0}×{area?.OuterBounds.Height ?? 0}";
    }

    private static List<string> MonitorNames()
    {
        var names = new List<string>();
        for (uint index = 0; index < 16; index++)
        {
            var adapter = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, index, ref adapter, 0)) break;
            if ((adapter.StateFlags & DisplayDeviceActive) == 0) continue;
            for (uint monitorIndex = 0; monitorIndex < 16; monitorIndex++)
            {
                var monitor = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
                if (!EnumDisplayDevices(adapter.DeviceName, monitorIndex, ref monitor, 0)) break;
                if ((monitor.StateFlags & DisplayDeviceActive) != 0 && !string.IsNullOrWhiteSpace(monitor.DeviceString))
                    names.Add(monitor.DeviceString);
            }
        }
        return names;
    }

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
