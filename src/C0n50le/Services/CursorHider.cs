using System.Runtime.InteropServices;

namespace C0n50le.Services;

/// <summary>
/// Hides the mouse pointer while the controller drives the home, the way consolemode does. WinUI can't do it (a null
/// ProtectedCursor only goes back to the arrow, and ShowCursor counts don't reach its input windows), so every system
/// cursor is swapped for a blank one (SetSystemCursor) and the user's own scheme is reloaded afterwards
/// (SPI_SETCURSORS). It is shown again as soon as the mouse moves, c0n50le loses the foreground or exits; a marker file
/// covers a crash in between, and the next start puts the cursors back.
/// </summary>
internal static class CursorHider
{
    // OCR_NORMAL, IBEAM, WAIT, CROSS, UP, SIZENWSE, SIZENESW, SIZEWE, SIZENS, SIZEALL, NO, HAND, APPSTARTING, HELP, PIN, PERSON.
    private static readonly uint[] SystemCursors =
        [32512, 32513, 32514, 32515, 32516, 32642, 32643, 32644, 32645, 32646, 32648, 32649, 32650, 32651, 32671, 32672];
    private const uint SpiSetCursors = 0x0057;
    private const int SmCxCursor = 13, SmCyCursor = 14;

    private static readonly object Gate = new();
    private static string _marker = "";
    private static bool _hidden, _exitHooked;

    public static bool IsHidden => _hidden;

    /// <summary>At startup: where the marker lives, and the cursors back if a previous run died with them hidden.</summary>
    public static void Initialize(string dataFolder)
    {
        _marker = Path.Combine(dataFolder, "cursor-hidden");
        try
        {
            if (!File.Exists(_marker)) return;
            Restore();
            AppLog.Write("Puntatore ripristinato dopo una chiusura inattesa");
        }
        catch (Exception ex) { AppLog.Write($"Puntatore: {ex.Message}"); }
    }

    public static void Hide()
    {
        lock (Gate)
        {
            if (_hidden) return;
            if (!_exitHooked)
            {
                _exitHooked = true;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => Show();
                AppDomain.CurrentDomain.UnhandledException += (_, _) => Show();
            }
            var blank = CreateBlankCursor();
            if (blank == 0)
            {
                AppLog.Write($"Puntatore: cursore vuoto non creato ({Marshal.GetLastWin32Error()})");
                return;
            }
            _hidden = true;
            WriteMarker(true);
            // SetSystemCursor takes ownership of each handle, so every slot gets its own copy.
            foreach (var id in SystemCursors) SetSystemCursor(CopyIcon(blank), id);
            DestroyCursor(blank);
        }
    }

    public static void Show()
    {
        lock (Gate)
        {
            if (!_hidden) return;
            _hidden = false;
            Restore();
        }
    }

    private static void Restore()
    {
        // Reloads the user's own scheme (Settings → Mouse pointer), whatever it is.
        if (!SystemParametersInfo(SpiSetCursors, 0, 0, 0))
            AppLog.Write($"Puntatore: cursori non ripristinati ({Marshal.GetLastWin32Error()})");
        WriteMarker(false);
    }

    private static void WriteMarker(bool hidden)
    {
        if (_marker.Length == 0) return;
        try
        {
            if (hidden)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_marker)!);
                File.WriteAllText(_marker, "");
            }
            else if (File.Exists(_marker)) File.Delete(_marker);
        }
        catch (Exception ex) { AppLog.Write($"Puntatore: marcatore: {ex.Message}"); }
    }

    /// <summary>A cursor of the system size whose AND mask keeps the screen and whose XOR mask adds nothing.</summary>
    private static nint CreateBlankCursor()
    {
        var width = Math.Max(GetSystemMetrics(SmCxCursor), 32);
        var height = Math.Max(GetSystemMetrics(SmCyCursor), 32);
        var size = (width + 15) / 16 * 2 * height;   // rows are WORD-aligned
        var and = new byte[size];
        Array.Fill(and, (byte)0xFF);
        var xor = new byte[size];
        return CreateCursor(GetModuleHandle(null), 0, 0, width, height, and, xor);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreateCursor(nint instance, int hotX, int hotY, int width, int height, byte[] andPlane, byte[] xorPlane);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyCursor(nint cursor);

    [DllImport("user32.dll")]
    private static extern nint CopyIcon(nint icon);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSystemCursor(nint cursor, uint id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, nint value, uint winIni);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
