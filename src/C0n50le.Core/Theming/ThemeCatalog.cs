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
    Deep = 3,
    /// <summary>Real liquid glass (LiquidGlassWinUI): refraction, dispersion and glare over the live backdrop.</summary>
    Liquid = 4,
    /// <summary>Liquid glass, lighter: denser tint and softer refraction, the most readable.</summary>
    LiquidSubtle = 5,
    /// <summary>Liquid glass, deeper: clearer, with stronger refraction, dispersion and glare.</summary>
    LiquidDeep = 6
}

/// <summary>The numbers handed to the liquid glass shader for one level (see GlassSurface).</summary>
public sealed record LiquidPreset(
    double Blur, double Thickness, double Refraction, double Dispersion, double DispersionRange,
    double Glare, double GlareOpposite, double Bloom, double Saturation, double Tint, double Brightness)
{
    /// <summary>The three liquid levels, dark or light: the tint keeps the text readable over bright artwork.</summary>
    public static LiquidPreset For(GlassDepth glass, bool dark) => glass switch
    {
        // Blur is a radius in pixels; Bloom is how much of the sharp backdrop comes back (0 = fully frosted, 1 = sharp).
        GlassDepth.LiquidSubtle => new(22, 16, 1.2, 3, 0.35, 35, 25, 0, 1.05, dark ? 0.5 : 0.46, dark ? -0.06 : 0.04),
        GlassDepth.LiquidDeep => new(5, 48, 2.6, 12, 0.9, 95, 75, 0.25, 1.3, dark ? 0.16 : 0.14, 0),
        _ => new(12, 30, 1.8, 7, 0.6, 65, 50, 0.08, 1.18, dark ? 0.32 : 0.28, dark ? -0.03 : 0.02)
    };

    /// <summary>
    /// The same level for a popup (menu, alert, notice): what is behind must not compete with its text, so it is always
    /// well frosted and a little more tinted, while the refraction and glare of the level stay.
    /// </summary>
    public LiquidPreset ForPopup() => this with
    {
        Blur = Math.Max(Blur, 26), Bloom = 0, Tint = Math.Max(Tint, 0.34), Saturation = Math.Max(Saturation, 1.15)
    };
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
    public bool IsLiquid => ThemeCatalog.IsLiquid(Glass);
}

/// <summary>The two appearances and the glass depths.</summary>
public static class ThemeCatalog
{
    public const string DarkId = "dark";
    public const string LightId = "light";
    public const string DefaultId = DarkId;
    public const GlassDepth DefaultGlass = GlassDepth.Medium;

    public static IReadOnlyList<string> Ids { get; } = [DarkId, LightId];
    public static IReadOnlyList<GlassDepth> Depths { get; } = 
        [GlassDepth.Off, GlassDepth.Subtle, GlassDepth.Medium, GlassDepth.Deep, GlassDepth.LiquidSubtle, GlassDepth.Liquid, GlassDepth.LiquidDeep];

    public static bool IsLiquid(GlassDepth glass) => glass is GlassDepth.Liquid or GlassDepth.LiquidSubtle or GlassDepth.LiquidDeep;

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
        // Liquid: the fallback acrylic is as dense as Medium, matching the tint inside the liquid shader.
        _ when IsLiquid(glass) => 0x70,
        _ => 0x42
    };

    private static double Sheen(GlassDepth glass) => glass switch
    {
        GlassDepth.Off => 0,
        GlassDepth.Subtle => 0.35,
        GlassDepth.Medium => 0.6,
        // The liquid shader draws its own glare and rim: only a hint of sheen on top.
        GlassDepth.LiquidSubtle => 0.15,
        GlassDepth.Liquid => 0.3,
        GlassDepth.LiquidDeep => 0.5,
        _ => 1
    };

    private static double Tint(GlassDepth glass) => glass switch
    {
        GlassDepth.Off => 1,
        GlassDepth.Subtle => 0.70,
        GlassDepth.Medium => 0.45,
        _ when IsLiquid(glass) => 0.45,
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
        // With liquid glass the controls on a panel are a light veil, so the glass shows through them too.
        Control = Argb.FromHex(IsLiquid(glass) ? "#2EFFFFFF" : "#FF3A3A3C"),
        Text = Argb.FromHex("#FFFFFF"),
        TextSecondary = Argb.FromHex("#99FFFFFF"),
        Focus = Argb.FromHex("#E5FFFFFF"),
        FocusText = Argb.FromHex("#000000"),
        // A lighter dim keeps the artwork alive behind a liquid popup, for the glass to bend.
        Scrim = Argb.FromHex(IsLiquid(glass) ? "#47000000" : "#80000000"),
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
        Control = Argb.FromHex(IsLiquid(glass) ? "#1F000000" : "#FFE5E5EA"),
        Text = Argb.FromHex("#000000"),
        TextSecondary = Argb.FromHex("#99000000"),
        Focus = Argb.FromHex("#F2FFFFFF"),
        FocusText = Argb.FromHex("#000000"),
        Scrim = Argb.FromHex(IsLiquid(glass) ? "#33000000" : "#66000000"),
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
