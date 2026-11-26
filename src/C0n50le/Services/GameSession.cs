using System.Diagnostics;
using System.Runtime.InteropServices;
using C0n50le.Core.Library;

namespace C0n50le.Services;

/// <summary>
/// A game started from the home: starts it (its exe, or the store link), finds its window, brings it back on resume and
/// tells when it has closed. A second press on a running game resumes it instead of starting another copy. Adapted from
/// RON1N Mode.
/// </summary>
public sealed class GameSession : IDisposable
{
    private readonly string _targetName;
    private readonly HashSet<int> _existing;
    private readonly bool _explicitWatch;
    private readonly DateTime _started = DateTime.UtcNow;
    private readonly Process? _launcher;
    private Process? _game;
    private nint _window;

    public Game Game { get; }
    public bool TimedOut { get; private set; }
    public bool HasWindow => _window != 0;

    public GameSession(Game game, string watch)
    {
        Game = game;
        _explicitWatch = !string.IsNullOrWhiteSpace(watch);
        _targetName = Path.GetFileNameWithoutExtension(_explicitWatch ? watch.Trim() : game.Path);
        var before = Process.GetProcessesByName(_targetName);
        try { _existing = before.Select(process => process.Id).ToHashSet(); }
        finally { foreach (var process in before) process.Dispose(); }

        var info = new ProcessStartInfo { UseShellExecute = true, FileName = game.LaunchUri.Length > 0 ? game.LaunchUri : game.Path };
        if (game.LaunchUri.Length == 0)
        {
            if (!File.Exists(game.Path)) throw new FileNotFoundException(game.Path);
            info.Arguments = game.Arguments;
            info.WorkingDirectory = Path.GetDirectoryName(game.Path) ?? "";
        }
        else if (!Uri.TryCreate(game.LaunchUri, UriKind.Absolute, out var uri) || uri.Scheme is not ("steam" or "com.epicgames.launcher"))
            throw new ArgumentException("Store link not allowed.");
        _launcher = Process.Start(info);
        AppLog.Write($"Avvio: {game.Source} {Path.GetFileName(game.Path)}");
    }

    /// <summary>False once the game has closed (or never showed a window within 45 s).</summary>
    public bool Poll()
    {
        if (_game is not null)
        {
            if (!_game.HasExited)
            {
                _game.Refresh();
                if (_game.MainWindowHandle != 0) _window = _game.MainWindowHandle;
                return true;
            }
            if (FindGame(out var next))
            {
                _game.Dispose();
                _game = next;
                _window = _game.MainWindowHandle;
                return true;
            }
            return false;
        }
        if (FindGame(out _game))
        {
            _window = _game.MainWindowHandle;
            return true;
        }
        TimedOut = DateTime.UtcNow - _started > TimeSpan.FromSeconds(45);
        return !TimedOut;
    }

    private bool FindGame(out Process game)
    {
        game = null!;
        foreach (var process in Process.GetProcessesByName(_targetName))
        {
            try
            {
                if (game is null && !_existing.Contains(process.Id) && process.MainWindowHandle != 0) game = process;
                else process.Dispose();
            }
            catch (InvalidOperationException) { process.Dispose(); }
        }
        if (game is not null) return true;
        if (_explicitWatch || _launcher is null || Game.LaunchUri.Length > 0) return false;
        foreach (var pid in NativeWindows.ProcessDescendants(_launcher.Id))
        {
            Process? candidate = null;
            try
            {
                candidate = Process.GetProcessById(pid);
                if (candidate.MainWindowHandle == 0) continue;
                game = candidate;
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { }
            finally
            {
                if (!ReferenceEquals(candidate, game)) candidate?.Dispose();
            }
        }
        return false;
    }

    public bool Resume() => _window != 0 && NativeWindows.BringForward(_window);

    /// <summary>Asks the game to close, like its window's close button.</summary>
    public bool RequestClose()
    {
        try { return _game is { HasExited: false } && _game.CloseMainWindow(); }
        catch (InvalidOperationException) { return false; }
    }

    public void Dispose()
    {
        _launcher?.Dispose();
        _game?.Dispose();
    }
}

internal static class NativeWindows
{
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint hwnd, int command);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, nint process);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] private static extern nint CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern bool Process32FirstW(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern bool Process32NextW(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);

    public static bool BringForward(nint window)
    {
        if (IsIconic(window)) ShowWindowAsync(window, 9);
        if (SetForegroundWindow(window)) return true;
        var from = GetCurrentThreadId();
        var to = GetWindowThreadProcessId(GetForegroundWindow(), 0);
        if (to == 0 || to == from || !AttachThreadInput(from, to, true)) return false;
        try { return SetForegroundWindow(window); }
        finally { AttachThreadInput(from, to, false); }
    }

    public static List<int> ProcessDescendants(int root)
    {
        var handle = CreateToolhelp32Snapshot(2, 0);
        var result = new List<int>();
        if (handle == -1) return result;
        var children = new Dictionary<int, List<int>>();
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>(), Exe = "" };
            if (!Process32FirstW(handle, ref entry)) return result;
            do
            {
                if (!children.TryGetValue(entry.Parent, out var matches)) children[entry.Parent] = matches = [];
                matches.Add(entry.Id);
            } while (Process32NextW(handle, ref entry));
        }
        finally { CloseHandle(handle); }

        var seen = new HashSet<int> { root };
        var queue = new Queue<int>([root]);
        while (queue.TryDequeue(out var parent))
            if (children.TryGetValue(parent, out var matches))
                foreach (var child in matches.Where(seen.Add))
                {
                    result.Add(child);
                    queue.Enqueue(child);
                }
        return result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage;
        public int Id;
        public nint Heap;
        public uint Module, Threads;
        public int Parent, Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Exe;
    }
}
