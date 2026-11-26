namespace C0n50le.Core.Theming;

/// <summary>
/// One look of the interface. Every control reads these tokens, never a color of its own, so a theme changes all of it
/// at once and the sizes stay the same in every theme.
/// </summary>
public sealed record ThemeTokens
{
    public required string Id { get; init; }
    /// <summary>The key of the theme's name in the string catalogs.</summary>
    public string NameKey => "Theme_" + Id;
    public required Argb Backdrop { get; init; }
    /// <summary>The veil over the game's picture where the text is (left and bottom).</summary>
    public required Argb Scrim { get; init; }
    public required Argb Surface { get; init; }
    public required Argb SurfaceRaised { get; init; }
    public required Argb Border { get; init; }
    public required Argb Text { get; init; }
    public required Argb TextMuted { get; init; }
    public required Argb Accent { get; init; }
    public required Argb OnAccent { get; init; }
    public required Argb Focus { get; init; }
    public double CornerSmall { get; init; } = 12;
    public double CornerMedium { get; init; } = 20;
    public double CornerLarge { get; init; } = 28;
    /// <summary>Surfaces are see-through acrylic with a lit edge (the liquid glass of Console Mode).</summary>
    public bool Glass { get; init; }
    /// <summary>The accent and the surfaces take the color of the selected game's picture.</summary>
    public bool FollowsArtwork { get; init; }
    public bool IsLight { get; init; }
}

/// <summary>The themes of c0n50le.</summary>
public static class ThemeCatalog
{
    public const string DefaultId = "night";

    public static ThemeTokens Night { get; } = new()
    {
        Id = "night",
        Backdrop = Argb.FromHex("#07090D"),
        Scrim = Argb.FromHex("#F207090D"),
        Surface = Argb.FromHex("#F0131922"),
        SurfaceRaised = Argb.FromHex("#FF1F2835"),
        Border = Argb.FromHex("#2EFFFFFF"),
        Text = Argb.FromHex("#F4F7FB"),
        TextMuted = Argb.FromHex("#A9B4C2"),
        Accent = Argb.FromHex("#5EE6CF"),
        OnAccent = Argb.FromHex("#04211D"),
        Focus = Argb.FromHex("#FFFFFF")
    };

    public static ThemeTokens Glass { get; } = new()
    {
        Id = "glass",
        Backdrop = Argb.FromHex("#080B12"),
        Scrim = Argb.FromHex("#D90A0E17"),
        Surface = Argb.FromHex("#B30D141D"),
        SurfaceRaised = Argb.FromHex("#D91A2433"),
        Border = Argb.FromHex("#66FFFFFF"),
        Text = Argb.FromHex("#FFFFFF"),
        TextMuted = Argb.FromHex("#C9D3E0"),
        Accent = Argb.FromHex("#8EE8FF"),
        OnAccent = Argb.FromHex("#04212B"),
        Focus = Argb.FromHex("#FFFFFF"),
        CornerSmall = 14,
        CornerMedium = 22,
        CornerLarge = 30,
        Glass = true,
        FollowsArtwork = true
    };

    public static ThemeTokens GameColor { get; } = new()
    {
        Id = "game",
        Backdrop = Argb.FromHex("#0A0C10"),
        Scrim = Argb.FromHex("#EB0A0C10"),
        Surface = Argb.FromHex("#F2161A22"),
        SurfaceRaised = Argb.FromHex("#FF232935"),
        Border = Argb.FromHex("#33FFFFFF"),
        Text = Argb.FromHex("#F6F7F9"),
        TextMuted = Argb.FromHex("#B3BAC6"),
        Accent = Argb.FromHex("#FFB36B"),
        OnAccent = Argb.FromHex("#2A1600"),
        Focus = Argb.FromHex("#FFFFFF"),
        FollowsArtwork = true
    };

    public static ThemeTokens Dawn { get; } = new()
    {
        Id = "dawn",
        Backdrop = Argb.FromHex("#F3EEE5"),
        Scrim = Argb.FromHex("#EBF3EEE5"),
        Surface = Argb.FromHex("#F5FBF8F2"),
        SurfaceRaised = Argb.FromHex("#FFFFFFFF"),
        Border = Argb.FromHex("#2E2B2118"),
        Text = Argb.FromHex("#1D1B18"),
        TextMuted = Argb.FromHex("#5A534A"),
        Accent = Argb.FromHex("#B4501A"),
        OnAccent = Argb.FromHex("#FFFFFF"),
        Focus = Argb.FromHex("#1D1B18"),
        IsLight = true
    };

    public static ThemeTokens Arcade { get; } = new()
    {
        Id = "arcade",
        Backdrop = Argb.FromHex("#000000"),
        Scrim = Argb.FromHex("#F0000000"),
        Surface = Argb.FromHex("#F50B0A10"),
        SurfaceRaised = Argb.FromHex("#FF1A1422"),
        Border = Argb.FromHex("#FF3D2D52"),
        Text = Argb.FromHex("#FFFFFF"),
        TextMuted = Argb.FromHex("#BBAED0"),
        Accent = Argb.FromHex("#FF2E88"),
        OnAccent = Argb.FromHex("#0A0008"),
        Focus = Argb.FromHex("#00F0FF"),
        CornerSmall = 4,
        CornerMedium = 6,
        CornerLarge = 8
    };

    public static IReadOnlyList<ThemeTokens> All { get; } = [Night, Glass, GameColor, Dawn, Arcade];

    public static ThemeTokens Get(string? id) =>
        All.FirstOrDefault(theme => string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Night;

    /// <summary>
    /// The theme with the selected game's color worked in, for the themes that follow the artwork: the accent becomes
    /// the picture's color made readable on the surfaces, and the surfaces take a little of its hue.
    /// </summary>
    public static ThemeTokens WithArtwork(ThemeTokens theme, Argb? artwork)
    {
        if (!theme.FollowsArtwork || artwork is not { } color) return theme;
        var (h, s, _) = color.ToHsl();
        if (s < 0.12) return theme;
        var surfaceBase = theme.Surface.Over(theme.Backdrop);
        var accent = Argb.FromHsl(h, Math.Clamp(s, 0.45, 0.85), 0.68);
        for (var lightness = 0.68; lightness < 0.92 && Argb.Contrast(accent, surfaceBase) < 4.5; lightness += 0.04)
            accent = Argb.FromHsl(h, Math.Clamp(s, 0.45, 0.85), lightness);
        var tint = Argb.FromHsl(h, Math.Min(s, 0.5), 0.14);
        var surfaceMix = theme.Glass ? 0.55 : 0.35;
        return theme with
        {
            Accent = accent,
            OnAccent = Argb.FromHsl(h, 0.6, 0.12),
            Surface = theme.Surface.Blend(tint with { A = theme.Surface.A }, surfaceMix),
            SurfaceRaised = theme.SurfaceRaised.Blend(Argb.FromHsl(h, Math.Min(s, 0.5), 0.22) with { A = theme.SurfaceRaised.A }, surfaceMix),
            Scrim = theme.Scrim.Blend(Argb.FromHsl(h, Math.Min(s, 0.5), 0.06) with { A = theme.Scrim.A }, 0.5)
        };
    }
}

/// <summary>The interface scale, so the home reads well from the sofa on any screen and DPI.</summary>
public static class UiScale
{
    public static IReadOnlyList<double> Choices { get; } = [0.8, 0.9, 1.0, 1.1, 1.25, 1.5];

    public static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, Choices[0], Choices[^1]) : 1.0;

    /// <summary>The height the interface is laid out for, in its own pixels (adapted from Console Mode).</summary>
    public const double DesignHeight = 1080;

    /// <summary>The narrowest it is laid out for: a narrow window shrinks it instead of cutting it.</summary>
    public const double MinimumWidth = 1280;

    /// <summary>
    /// How much the interface is grown in a window of that size: the window's height over 1080 (less when too narrow),
    /// times the scale chosen in System → Screen. A 720p window and a 4K TV show the same layout.
    /// </summary>
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
