using C0n50le.Core.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace C0n50le.Ui;

/// <summary>The corner size an element takes from the theme.</summary>
public enum CornerSize
{
    None,
    Tiny,
    Small,
    Medium,
    Large,
    Pill
}

/// <summary>
/// Gives an element the theme's corners: set <c>ui:Corners.Size</c> (also from a style) and the element follows every
/// theme change, so Arcade's square edges and Night's soft ones need no second set of styles.
/// </summary>
public static class Corners
{
    private static readonly List<WeakReference<DependencyObject>> Elements = [];

    public static readonly DependencyProperty SizeProperty = DependencyProperty.RegisterAttached(
        "Size", typeof(CornerSize), typeof(Corners), new PropertyMetadata(CornerSize.None, OnSizeChanged));

    public static CornerSize GetSize(DependencyObject element) => (CornerSize)element.GetValue(SizeProperty);
    public static void SetSize(DependencyObject element, CornerSize value) => element.SetValue(SizeProperty, value);

    private static void OnSizeChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        lock (Elements)
        {
            if (Elements.Count > 512) Elements.RemoveAll(reference => !reference.TryGetTarget(out _));
            Elements.Add(new WeakReference<DependencyObject>(element));
        }
        Apply(element, (CornerSize)args.NewValue);
    }

    public static CornerRadius Radius(CornerSize size)
    {
        var theme = ThemeManager.Current;
        var value = size switch
        {
            CornerSize.Small => theme.CornerSmall,
            CornerSize.Medium => theme.CornerMedium,
            CornerSize.Large => theme.CornerLarge,
            // No capsules: every control shares the theme's small corner, so buttons, switches and fields match.
            CornerSize.Pill => theme.CornerSmall,
            CornerSize.Tiny => Math.Max(2, theme.CornerSmall / 2),
            _ => 0
        };
        return new CornerRadius(value);
    }

    internal static void RefreshAll()
    {
        List<DependencyObject> alive = [];
        lock (Elements)
        {
            Elements.RemoveAll(reference => !reference.TryGetTarget(out _));
            foreach (var reference in Elements)
                if (reference.TryGetTarget(out var element)) alive.Add(element);
        }
        foreach (var element in alive) Apply(element, GetSize(element));
    }

    private static void Apply(DependencyObject element, CornerSize size)
    {
        var radius = Radius(size);
        switch (element)
        {
            case Control control: control.CornerRadius = radius; break;
            case Border border: border.CornerRadius = radius; break;
            case Grid grid: grid.CornerRadius = radius; break;
            case StackPanel panel: panel.CornerRadius = radius; break;
        }
    }
}

/// <summary>
/// Applies a theme: recolours the shared brushes of Theme.xaml in place (so the change is instant everywhere) and the
/// corners. The themes that follow the artwork are re-applied with the selected game's color.
/// </summary>
public static class ThemeManager
{
    private static ThemeTokens _base = ThemeCatalog.Night;
    private static Argb? _artwork;

    public static ThemeTokens Current { get; private set; } = ThemeCatalog.Night;

    public static event Action? Changed;

    public static void Use(string id)
    {
        _base = ThemeCatalog.Get(id);
        Apply();
        Corners.RefreshAll();
        Changed?.Invoke();
    }

    /// <summary>The selected game's color: only the themes that follow the artwork change with it.</summary>
    public static void UseArtwork(Argb? color)
    {
        if (_artwork == color) return;
        _artwork = color;
        if (_base.FollowsArtwork) Apply();
        else Ambient(color);
    }

    private static void Apply()
    {
        var theme = Current = ThemeCatalog.WithArtwork(_base, _artwork);
        Solid("BackdropBrush", theme.Backdrop);
        Solid("SurfaceBrush", theme.Glass ? theme.Surface.WithAlpha(120) : theme.Surface);
        Solid("SurfaceRaisedBrush", theme.SurfaceRaised);
        Solid("HairlineBrush", theme.Border);
        Solid("TextBrush", theme.Text);
        Solid("TextMutedBrush", theme.TextMuted);
        Solid("AccentBrush", theme.Accent);
        Solid("OnAccentBrush", theme.OnAccent);
        Solid("FocusBrush", theme.Focus);
        Solid("DimBrush", theme.Backdrop.WithAlpha(theme.IsLight ? (byte)225 : (byte)217));
        Solid("HoverBrush", theme.IsLight ? Argb.FromHex("#14000000") : Argb.FromHex("#1FFFFFFF"));

        var glass = (AcrylicBrush)Application.Current.Resources["GlassBrush"];
        glass.Opacity = theme.Glass ? 1 : 0;
        glass.TintColor = Color(theme.Surface.WithAlpha(255));
        glass.TintOpacity = 0.68;
        glass.TintLuminosityOpacity = 0.85;
        glass.FallbackColor = Color(theme.Surface.WithAlpha(230));

        var edge = (LinearGradientBrush)Application.Current.Resources["EdgeBrush"];
        if (theme.Glass)
        {
            edge.GradientStops[0].Color = Color(Argb.FromHex("#8CFFFFFF"));
            edge.GradientStops[1].Color = Color(Argb.FromHex("#1FFFFFFF"));
            edge.GradientStops[2].Color = Color(Argb.FromHex("#0FFFFFFF"));
            edge.GradientStops[3].Color = Color(Argb.FromHex("#59FFFFFF"));
        }
        else
            // Outside the Glass theme buttons have no outline: fill and focus ring are enough.
            foreach (var stop in edge.GradientStops) stop.Color = Color(theme.Border.WithAlpha(0));
        Solid("TonalBrush", theme.IsLight ? Argb.FromHex("#141D1B18") : Argb.FromHex("#24FFFFFF"));

        var scrim = theme.Scrim;
        var left = (LinearGradientBrush)Application.Current.Resources["ScrimLeftBrush"];
        left.GradientStops[0].Color = Color(scrim);
        left.GradientStops[1].Color = Color(scrim.WithAlpha((byte)(scrim.A * 0.76)));
        left.GradientStops[2].Color = Color(scrim.WithAlpha((byte)(scrim.A * 0.16)));
        var topVeil = (LinearGradientBrush)Application.Current.Resources["ScrimTopBrush"];
        topVeil.GradientStops[0].Color = Color(scrim.WithAlpha((byte)(scrim.A * 0.75)));
        topVeil.GradientStops[1].Color = Color(scrim.WithAlpha(0));
        var bottom = (LinearGradientBrush)Application.Current.Resources["ScrimBottomBrush"];
        bottom.GradientStops[0].Color = Color(scrim.WithAlpha(245));
        bottom.GradientStops[1].Color = Color(scrim.WithAlpha(0));
        Ambient(_artwork);
    }

    private static void Ambient(Argb? artwork)
    {
        var theme = Current;
        var ambient = (RadialGradientBrush)Application.Current.Resources["AmbientBrush"];
        var tint = artwork ?? theme.Accent;
        ambient.GradientStops[0].Color = Color(tint.WithAlpha(theme.IsLight ? (byte)70 : (byte)88));
        ambient.GradientStops[1].Color = Color(theme.Backdrop.WithAlpha(0));
    }

    private static void Solid(string key, Argb color) => ((SolidColorBrush)Application.Current.Resources[key]).Color = Color(color);

    public static Color Color(Argb color) => Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B);

    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    public static Style Style(string key) => (Style)Application.Current.Resources[key];

    public static ElementTheme ElementTheme => Current.IsLight ? ElementTheme.Light : ElementTheme.Dark;
}
