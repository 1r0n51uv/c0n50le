namespace C0n50le.Core.Theming;

/// <summary>Light or dark, like tvOS.</summary>
public enum Appearance
{
    Dark,
    Light
}

/// <summary>
/// How much Liquid Glass the materials (dock, menus, alerts, buttons) have: off is an opaque material; the three depths
/// make it more see-through, with a brighter rim and sheen.
/// </summary>
public enum GlassDepth
{
    Off = 0,
    Subtle = 1,
    Medium = 2,
    Deep = 3
}

/// <summary>
/// The colours of the interface, taken from Apple's tvOS UI kit (Figma, 1920×1080). Every control reads these, never a
/// colour of its own.
/// </summary>
public sealed record ThemeTokens
{
    public required Appearance Appearance { get; init; }
    public required GlassDepth Glass { get; init; }
    /// <summary>The page behind everything when no artwork is shown.</summary>
    public required Argb Backdrop { get; init; }
    /// <summary>The veil over the artwork, so text and materials stay readable.</summary>
    public required Argb Overlay { get; init; }
    /// <summary>Platters: dock, menus, alerts, unfocused buttons. Translucent when glass is on.</summary>
    public required Argb Material { get; init; }
    public required Argb MaterialStroke { get; init; }
    /// <summary>Unfocused controls on a material: alert buttons, text fields.</summary>
    public required Argb Control { get; init; }
    public required Argb Text { get; init; }
    /// <summary>Secondary text: captions, values, headers (white or black at 50–60%).</summary>
    public required Argb TextSecondary { get; init; }
    /// <summary>The focused element's platter (white at 90%) and its text.</summary>
    public required Argb Focus { get; init; }
    public required Argb FocusText { get; init; }
    /// <summary>The dim behind an alert.</summary>
    public required Argb Scrim { get; init; }
    public required Argb Separator { get; init; }
    /// <summary>How strong the glass rim and sheen are, 0 (off) to 1 (deep).</summary>
    public double Sheen { get; init; }
    /// <summary>Tint opacity of the glass: how much of the material colour covers the blur.</summary>
    public double TintOpacity { get; init; } = 1;
    public bool IsLight => Appearance == Appearance.Light;
    public bool IsGlass => Glass != GlassDepth.Off;
}

/// <summary>The two appearances and the glass depths.</summary>
public static class ThemeCatalog
{
    public const string DarkId = "dark";
    public const string LightId = "light";
    public const string DefaultId = DarkId;
    public const GlassDepth DefaultGlass = GlassDepth.Medium;

    public static IReadOnlyList<string> Ids { get; } = [DarkId, LightId];
    public static IReadOnlyList<GlassDepth> Depths { get; } = [GlassDepth.Off, GlassDepth.Subtle, GlassDepth.Medium, GlassDepth.Deep];

    public static string Normalize(string? id) => string.Equals(id, LightId, StringComparison.OrdinalIgnoreCase) ? LightId : DarkId;

    public static GlassDepth NormalizeGlass(int value) => Enum.IsDefined(typeof(GlassDepth), value) ? (GlassDepth)value : DefaultGlass;

    public static ThemeTokens Get(string? id, GlassDepth glass) =>
        Normalize(id) == LightId ? Light(glass) : Dark(glass);

    /// <summary>Material opacity decreases as the glass effect deepens.</summary>
    internal static byte MaterialAlpha(GlassDepth glass) => glass switch
    {
        GlassDepth.Off => 0xF5,
        GlassDepth.Subtle => 0xA6,
        GlassDepth.Medium => 0x70,
        _ => 0x42
    };

    private static double Sheen(GlassDepth glass) => glass switch
    {
        GlassDepth.Off => 0,
        GlassDepth.Subtle => 0.35,
        GlassDepth.Medium => 0.6,
        _ => 1
    };

    private static double Tint(GlassDepth glass) => glass switch
    {
        GlassDepth.Off => 1,
        GlassDepth.Subtle => 0.70,
        GlassDepth.Medium => 0.45,
        _ => 0.23
    };

    public static ThemeTokens Dark(GlassDepth glass) => new()
    {
        Appearance = Appearance.Dark,
        Glass = glass,
        Backdrop = Argb.FromHex("#0F0F10"),
        Overlay = Argb.FromHex("#991E1E1E"),
        Material = Argb.FromHex("#1E1E1E").WithAlpha(MaterialAlpha(glass)),
        MaterialStroke = Argb.FromHex(glass == GlassDepth.Off ? "#14FFFFFF" : "#1AFFFFFF"),
        Control = Argb.FromHex("#FF3A3A3C"),
        Text = Argb.FromHex("#FFFFFF"),
        TextSecondary = Argb.FromHex("#99FFFFFF"),
        Focus = Argb.FromHex("#E5FFFFFF"),
        FocusText = Argb.FromHex("#000000"),
        Scrim = Argb.FromHex("#80000000"),
        Separator = Argb.FromHex("#26FFFFFF"),
        Sheen = Sheen(glass),
        TintOpacity = Tint(glass)
    };

    public static ThemeTokens Light(GlassDepth glass) => new()
    {
        Appearance = Appearance.Light,
        Glass = glass,
        Backdrop = Argb.FromHex("#EFEFF4"),
        Overlay = Argb.FromHex("#B2E1E1E1"),
        Material = Argb.FromHex("#FEFDFF").WithAlpha(MaterialAlpha(glass)),
        MaterialStroke = Argb.FromHex("#29FFFFFF"),
        Control = Argb.FromHex("#FFE5E5EA"),
        Text = Argb.FromHex("#000000"),
        TextSecondary = Argb.FromHex("#99000000"),
        Focus = Argb.FromHex("#F2FFFFFF"),
        FocusText = Argb.FromHex("#000000"),
        Scrim = Argb.FromHex("#66000000"),
        Separator = Argb.FromHex("#1F000000"),
        Sheen = Sheen(glass),
        TintOpacity = Tint(glass)
    };

    /// <summary>A soft colour from the focused game's artwork, for the ambient glow behind the home.</summary>
    public static Argb Ambient(ThemeTokens theme, Argb? artwork)
    {
        if (artwork is not { } color) return theme.Backdrop;
        var (h, s, _) = color.ToHsl();
        return Argb.FromHsl(h, Math.Min(s, 0.6), theme.IsLight ? 0.82 : 0.22);
    }
}

/// <summary>The interface scale, so the home reads well from the sofa on any screen and DPI.</summary>
public static class UiScale
{
    public static IReadOnlyList<double> Choices { get; } = [0.8, 0.9, 1.0, 1.1, 1.25, 1.5];

    public static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, Choices[0], Choices[^1]) : 1.0;

    /// <summary>The height the interface is laid out for, in its own pixels (the tvOS kit's 1920×1080).</summary>
    public const double DesignHeight = 1080;

    /// <summary>The narrowest it is laid out for: a narrow window shrinks it instead of cutting it.</summary>
    public const double MinimumWidth = 1280;

    /// <summary>How much the interface is grown in a window of that size, times the chosen scale.</summary>
    public static double For(double width, double height, double chosen)
    {
        if (width <= 0 || height <= 0) return Clamp(chosen);
        return Math.Min(height / DesignHeight, width / MinimumWidth) * Clamp(chosen);
    }

    /// <summary>The size the interface is laid out at to fill the window once grown by <see cref="For"/>.</summary>
    public static (double Width, double Height) LayoutSize(double width, double height, double chosen)
    {
        var scale = For(width, height, chosen);
        return (width / scale, height / scale);
    }
}
