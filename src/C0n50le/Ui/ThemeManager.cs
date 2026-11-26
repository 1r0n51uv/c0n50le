using C0n50le.Core.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace C0n50le.Ui;

/// <summary>
/// Applies Light or Dark and the Liquid Glass depth by recolouring the shared brushes of Theme.xaml in place, so the
/// change is instant everywhere.
/// </summary>
public static class ThemeManager
{
    private static string _id = ThemeCatalog.DefaultId;
    private static GlassDepth _glass = ThemeCatalog.DefaultGlass;
    private static Argb? _artwork;

    public static ThemeTokens Current { get; private set; } = ThemeCatalog.Dark(ThemeCatalog.DefaultGlass);

    public static void Use(string id, GlassDepth glass)
    {
        _id = ThemeCatalog.Normalize(id);
        _glass = glass;
        Apply();
    }

    /// <summary>The focused game's colour, for the soft glow behind the home.</summary>
    public static void UseArtwork(Argb? color)
    {
        if (_artwork == color) return;
        _artwork = color;
        Ambient();
    }

    private static void Apply()
    {
        var theme = Current = ThemeCatalog.Get(_id, _glass);
        Solid("BackdropBrush", theme.Backdrop);
        Solid("TextBrush", theme.Text);
        Solid("TextSecondaryBrush", theme.TextSecondary);
        Solid("FocusBrush", theme.Focus);
        Solid("FocusTextBrush", theme.FocusText);
        Solid("ControlBrush", theme.Control);
        Solid("MaterialStrokeBrush", theme.MaterialStroke);
        Solid("ScrimBrush", theme.Scrim);
        Solid("SeparatorBrush", theme.Separator);
        Solid("GlyphCircleBrush", theme.IsLight ? Argb.FromHex("#26FFFFFF") : Argb.FromHex("#1AFFFFFF"));
        Solid("DimBrush", theme.Backdrop.WithAlpha(theme.IsLight ? (byte)235 : (byte)230));

        var material = (AcrylicBrush)Application.Current.Resources["MaterialBrush"];
        var tint = theme.Material.WithAlpha(255);
        material.TintColor = Color(tint);
        material.TintOpacity = theme.TintOpacity;
        material.TintLuminosityOpacity = theme.IsGlass ? Math.Clamp(theme.TintOpacity + 0.15, 0, 1) : 1;
        material.FallbackColor = Color(theme.Material.WithAlpha(theme.IsGlass ? (byte)230 : (byte)255));

        // Liquid Glass: sheen on top and a lit rim, scaled by the depth; nothing when it is off.
        var sheen = theme.Sheen;
        var top = (LinearGradientBrush)Application.Current.Resources["SheenBrush"];
        SetStops(top, Alpha(0.20 * sheen), Alpha(0.06 * sheen), Alpha(0), Alpha(0.04 * sheen));
        var rim = (LinearGradientBrush)Application.Current.Resources["RimBrush"];
        SetStops(rim, Alpha(0.60 * sheen), Alpha(0.10 * sheen), Alpha(0.05 * sheen), Alpha(0.35 * sheen));

        var backdrop = theme.Backdrop;
        var fade = (LinearGradientBrush)Application.Current.Resources["ShelfFadeBrush"];
        SetStops(fade, backdrop.WithAlpha(0), backdrop.WithAlpha(0x1A), backdrop.WithAlpha(theme.IsLight ? (byte)0xC0 : (byte)0xB3),
            backdrop.WithAlpha(0xF2));
        var topVeil = (LinearGradientBrush)Application.Current.Resources["ShelfTopBrush"];
        SetStops(topVeil, backdrop.WithAlpha(theme.IsLight ? (byte)0xA6 : (byte)0x8C), backdrop.WithAlpha(0));
        var side = (LinearGradientBrush)Application.Current.Resources["ShelfSideBrush"];
        SetStops(side, backdrop.WithAlpha(theme.IsLight ? (byte)0xC8 : (byte)0xB3), backdrop.WithAlpha(0x4D), backdrop.WithAlpha(0));
        Ambient();
    }

    private static void Ambient()
    {
        var theme = Current;
        var ambient = (RadialGradientBrush)Application.Current.Resources["AmbientBrush"];
        ambient.GradientStops[0].Color = Color(ThemeCatalog.Ambient(theme, _artwork).WithAlpha(theme.IsLight ? (byte)140 : (byte)120));
        ambient.GradientStops[1].Color = Color(theme.Backdrop.WithAlpha(0));
    }

    private static Argb Alpha(double alpha) => new((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), 255, 255, 255);

    private static void SetStops(LinearGradientBrush brush, params Argb[] colors)
    {
        for (var index = 0; index < colors.Length && index < brush.GradientStops.Count; index++)
            brush.GradientStops[index].Color = Color(colors[index]);
    }

    private static void Solid(string key, Argb color) => ((SolidColorBrush)Application.Current.Resources[key]).Color = Color(color);

    public static Color Color(Argb color) => Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B);

    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    public static Style Style(string key) => (Style)Application.Current.Resources[key];

    public static ElementTheme ElementTheme => Current.IsLight ? ElementTheme.Light : ElementTheme.Dark;
}
