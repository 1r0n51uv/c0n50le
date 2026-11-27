using C0n50le.Core.Theming;
using LiquidGlassWinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace C0n50le.Ui;

/// <summary>
/// The material under a glass panel (app bar, dock, menus, alerts, widgets). Normally the shared acrylic MaterialBrush;
/// with the "Liquid" glass it is a LiquidGlassBrush of its own (refraction, dispersion, glare over the live backdrop),
/// shaped to the panel's corners. If the liquid effect can't start on this PC, every panel goes back to acrylic.
/// </summary>
public sealed class GlassSurface : Grid
{
    private static readonly List<WeakReference<GlassSurface>> Live = [];
    private static bool _liquid, _dark = true;
    private static GlassDepth _level = GlassDepth.Liquid;

    /// <summary>Raised once when the liquid effect failed and the panels went back to acrylic.</summary>
    public static event Action<string>? LiquidFailed;

    public static bool IsLiquidBroken { get; private set; }

    public GlassSurface()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) =>
        {
            lock (Live) Live.Add(new WeakReference<GlassSurface>(this));
            Apply();
        };
        Unloaded += (_, _) =>
        {
            lock (Live) Live.RemoveAll(reference => !reference.TryGetTarget(out var surface) || surface == this);
            Background = null;
        };
        SizeChanged += (_, _) => Shape();
    }

    /// <summary>Switches every panel between acrylic and liquid glass.</summary>
    public static void Use(GlassDepth glass, bool dark)
    {
        _level = glass;
        _liquid = ThemeCatalog.IsLiquid(glass) && !IsLiquidBroken;
        _dark = dark;
        List<GlassSurface> surfaces;
        lock (Live)
        {
            Live.RemoveAll(reference => !reference.TryGetTarget(out _));
            surfaces = Live.Select(reference => reference.TryGetTarget(out var surface) ? surface : null).OfType<GlassSurface>().ToList();
        }
        foreach (var surface in surfaces) surface.Apply();
    }

    private void Apply()
    {
        if (!_liquid)
        {
            Background = ThemeManager.Brush("MaterialBrush");
            return;
        }
        var preset = LiquidPreset.For(_level, _dark);
        Background = new LiquidGlassBrush
        {
            BlurAmount = preset.Blur,
            RefThickness = preset.Thickness,
            RefFactor = preset.Refraction,
            RefDispersion = preset.Dispersion,
            DispersionRange = preset.DispersionRange,
            GlareFactor = preset.Glare,
            GlareOppositeFactor = preset.GlareOpposite,
            GlareAngle = -45,
            BloomAmount = preset.Bloom,
            Saturation = preset.Saturation,
            // A dark (or light) tint keeps white (or black) text readable over bright artwork.
            TintR = _dark ? 24 : 250,
            TintG = _dark ? 24 : 250,
            TintB = _dark ? 28 : 252,
            TintA = preset.Tint,
            Brightness = preset.Brightness,
            ShapeRoundness = 5
        };
        Shape();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, CheckLiquid);
    }

    /// <summary>The shader draws its own rounded shape: the corner as a fraction of the shorter half side.</summary>
    private void Shape()
    {
        if (Background is not LiquidGlassBrush brush || ActualWidth <= 0 || ActualHeight <= 0) return;
        var half = Math.Min(ActualWidth, ActualHeight) / 2;
        brush.ShapeRadius = Math.Clamp(CornerRadius.TopLeft / half, 0, 1);
    }

    private static void CheckLiquid()
    {
        if (IsLiquidBroken || LiquidGlassBrush.LastError is not { Length: > 0 } error) return;
        IsLiquidBroken = true;
        Use(GlassDepth.Medium, _dark);
        LiquidFailed?.Invoke(error.Split('\n')[0]);
    }
}
