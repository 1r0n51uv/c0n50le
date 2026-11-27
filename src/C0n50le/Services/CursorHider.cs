using System.Runtime.InteropServices;

namespace C0n50le.Services;

/// <summary>
/// Hides the mouse pointer over c0n50le while the controller is in use. WinUI has no "no cursor" shape (a null
/// ProtectedCursor only goes back to the arrow), so this works at the Win32 level: the thread's cursor display count is
/// taken below zero, and WM_SETCURSOR on the window and its WinUI child windows sets no cursor. Showing it again undoes
/// exactly what hiding did.
/// </summary>
internal static class CursorHider
{
    private const uint WmSetCursor = 0x0020, WmNcDestroy = 0x0082;
    private const nuint SubclassId = 0xC0C5;
    private delegate nint Subclass(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    private delegate bool EnumWindowsProc(nint window, nint data);

    private static readonly Subclass Callback = OnMessage;
    private static readonly HashSet<nint> Hooked = [];
    private static int _hiddenSteps;
    private static bool _hidden;

    [DllImport("user32.dll")] private static extern int ShowCursor(bool show);
    [DllImport("user32.dll")] private static extern nint SetCursor(nint cursor);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, Subclass callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, Subclass callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);

    public static bool IsHidden => _hidden;

    public static void Hide(nint window)
    {
        if (_hidden) return;
        _hidden = true;
        Hook(window);
        // WinUI draws into child windows (the content bridge and its input site): they get WM_SETCURSOR, not the frame.
        EnumChildWindows(window, (child, _) => { Hook(child); return true; }, 0);
        _hiddenSteps = 0;
        while (_hiddenSteps < 16 && ShowCursor(false) >= 0) _hiddenSteps++;
        if (_hiddenSteps < 16) _hiddenSteps++;
        SetCursor(0);
    }

    public static void Show()
    {
        if (!_hidden) return;
        _hidden = false;
        for (; _hiddenSteps > 0; _hiddenSteps--) ShowCursor(true);
    }

    private static void Hook(nint window)
    {
        if (window != 0 && Hooked.Add(window)) SetWindowSubclass(window, Callback, SubclassId, 0);
    }

    private static nint OnMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == WmSetCursor && _hidden && (lParam & 0xFFFF) == 1 /* HTCLIENT */)
        {
            SetCursor(0);
            return 1;
        }
        if (message == WmNcDestroy)
        {
            RemoveWindowSubclass(window, Callback, SubclassId);
            Hooked.Remove(window);
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }
}
