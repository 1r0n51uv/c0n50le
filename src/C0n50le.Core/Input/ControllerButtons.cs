namespace C0n50le.Core.Input;

/// <summary>The controller buttons as bits, the same for Xbox, PlayStation and Switch pads (by position).</summary>
public static class ControllerButtons
{
    public const int Up = 0x0001, Down = 0x0002, Left = 0x0004, Right = 0x0008;
    public const int Menu = 0x0010, View = 0x0020;
    public const int LeftShoulder = 0x0100, RightShoulder = 0x0200;
    public const int Confirm = 0x1000, Back = 0x2000, X = 0x4000, Y = 0x8000;
    public const int Directions = Up | Down | Left | Right;
}

/// <summary>The family of the pad last used, for the glyphs shown on screen.</summary>
public enum PadFamily
{
    Xbox,
    PlayStation,
    Nintendo
}

/// <summary>The labels of the buttons on each family of pads.</summary>
public static class ButtonGlyphs
{
    public static string Confirm(PadFamily family) => family switch
    {
        PadFamily.PlayStation => "✕",
        PadFamily.Nintendo => "A",
        _ => "A"
    };

    public static string Back(PadFamily family) => family switch
    {
        PadFamily.PlayStation => "○",
        PadFamily.Nintendo => "B",
        _ => "B"
    };

    public static string Options(PadFamily family) => family switch
    {
        PadFamily.PlayStation => "△",
        PadFamily.Nintendo => "X",
        _ => "Y"
    };

    public static string Menu(PadFamily family) => family switch
    {
        PadFamily.PlayStation => "OPTIONS",
        PadFamily.Nintendo => "+",
        _ => "☰"
    };

    public static string View(PadFamily family) => family switch
    {
        PadFamily.PlayStation => "CREATE",
        PadFamily.Nintendo => "−",
        _ => "⧉"
    };

    /// <summary>A combination as the user reads it, e.g. "⧉ + Y" or "CREATE + △".</summary>
    public static string Combination(int mask, PadFamily family)
    {
        var parts = new List<string>();
        if ((mask & ControllerButtons.View) != 0) parts.Add(View(family));
        if ((mask & ControllerButtons.Menu) != 0) parts.Add(Menu(family));
        if ((mask & ControllerButtons.LeftShoulder) != 0) parts.Add(family == PadFamily.PlayStation ? "L1" : family == PadFamily.Nintendo ? "L" : "LB");
        if ((mask & ControllerButtons.RightShoulder) != 0) parts.Add(family == PadFamily.PlayStation ? "R1" : family == PadFamily.Nintendo ? "R" : "RB");
        if ((mask & ControllerButtons.Confirm) != 0) parts.Add(Confirm(family));
        if ((mask & ControllerButtons.Back) != 0) parts.Add(Back(family));
        if ((mask & ControllerButtons.X) != 0) parts.Add(family == PadFamily.PlayStation ? "□" : family == PadFamily.Nintendo ? "Y" : "X");
        if ((mask & ControllerButtons.Y) != 0) parts.Add(Options(family));
        return string.Join(" + ", parts);
    }
}

/// <summary>The shortcuts that can bring the home back from a game.</summary>
public static class ShortcutChoices
{
    public const string KeyboardOff = "";

    /// <summary>Function keys only: they are free in almost every game, and Ctrl+Alt+key is what gets registered.</summary>
    public static IReadOnlyList<string> Keyboard { get; } = [KeyboardOff, "F8", "F9", "F10", "F11", "F12"];

    /// <summary>Two-button combinations a game rarely uses together; 0 turns the controller shortcut off.</summary>
    public static IReadOnlyList<int> Controller { get; } =
    [
        0,
        ControllerButtons.View | ControllerButtons.Y,
        ControllerButtons.View | ControllerButtons.Menu,
        ControllerButtons.LeftShoulder | ControllerButtons.RightShoulder | ControllerButtons.View,
        ControllerButtons.View | ControllerButtons.Back
    ];

    public static bool IsKnownKeyboard(string? key) => key is not null && Keyboard.Contains(key);
    public static bool IsKnownController(int mask) => Controller.Contains(mask);

    /// <summary>True on the poll the whole combination becomes held, never again until it is released.</summary>
    public static bool Triggered(int mask, int previous, int current) =>
        mask != 0 && (current & mask) == mask && (previous & mask) != mask;
}
