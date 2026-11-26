using C0n50le.Core.Theming;

namespace C0n50le.Tests;

public sealed class ThemeTests
{
    public static IEnumerable<object[]> Every =>
        from id in ThemeCatalog.Ids from depth in ThemeCatalog.Depths select new object[] { id, depth };

    /// <summary>The material as it looks over the worst artwork behind it: black for the dark theme, white for the light.</summary>
    private static Argb MaterialOver(ThemeTokens theme, Argb behind) => theme.Material.Over(behind);

    [Theory]
    [MemberData(nameof(Every))]
    public void Text_is_readable_on_the_material_over_any_artwork(string id, GlassDepth depth)
    {
        var theme = ThemeCatalog.Get(id, depth);
        // The worst case is artwork the same tone as the text behind see-through glass.
        var behind = theme.IsLight ? Argb.FromHex("#000000") : Argb.FromHex("#FFFFFF");
        var worst = MaterialOver(theme, behind.Blend(theme.Backdrop, depth == GlassDepth.Deep ? 0.5 : 0.25));
        var floor = depth == GlassDepth.Off ? 7 : 3;
        Assert.True(Argb.Contrast(theme.Text, MaterialOver(theme, theme.Backdrop)) >= 7, $"{id}/{depth}: text on material");
        Assert.True(Argb.Contrast(theme.Text, worst) >= floor, $"{id}/{depth}: text on glass over artwork ({Argb.Contrast(theme.Text, worst):0.0})");
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void Focused_platter_reads_black_on_white(string id, GlassDepth depth)
    {
        var theme = ThemeCatalog.Get(id, depth);
        var platter = theme.Focus.Over(theme.Backdrop);
        Assert.True(Argb.Contrast(theme.FocusText, platter) >= 12, $"{id}/{depth}: focused text");
        Assert.True(Argb.Contrast(theme.TextSecondary.Over(theme.Backdrop), theme.Backdrop) >= 4.5, $"{id}/{depth}: secondary text");
    }

    [Fact]
    public void Glass_gets_more_see_through_with_depth()
    {
        foreach (var id in ThemeCatalog.Ids)
        {
            var alphas = ThemeCatalog.Depths.Select(depth => ThemeCatalog.Get(id, depth).Material.A).ToList();
            Assert.Equal(alphas.OrderByDescending(alpha => alpha), alphas);
            var sheens = ThemeCatalog.Depths.Select(depth => ThemeCatalog.Get(id, depth).Sheen).ToList();
            Assert.Equal(sheens.OrderBy(sheen => sheen), sheens);
            Assert.Equal(0, ThemeCatalog.Get(id, GlassDepth.Off).Sheen);
            Assert.False(ThemeCatalog.Get(id, GlassDepth.Off).IsGlass);
        }
    }

    [Fact]
    public void Glass_gets_more_transparent_at_each_depth_in_both_themes()
    {
        foreach (var id in ThemeCatalog.Ids)
        {
            var subtle = ThemeCatalog.Get(id, GlassDepth.Subtle);
            var medium = ThemeCatalog.Get(id, GlassDepth.Medium);
            var deep = ThemeCatalog.Get(id, GlassDepth.Deep);
            Assert.True(subtle.TintOpacity > medium.TintOpacity && medium.TintOpacity > deep.TintOpacity);
            Assert.True(subtle.Material.A > medium.Material.A && medium.Material.A > deep.Material.A);
        }
    }

    [Fact]
    public void Only_light_and_dark_exist_and_unknown_values_fall_back()
    {
        Assert.Equal(["dark", "light"], ThemeCatalog.Ids);
        Assert.Equal("dark", ThemeCatalog.Normalize("night"));
        Assert.Equal("light", ThemeCatalog.Normalize("LIGHT"));
        Assert.Equal(ThemeCatalog.DefaultGlass, ThemeCatalog.NormalizeGlass(9));
        Assert.Equal(GlassDepth.Deep, ThemeCatalog.NormalizeGlass(3));
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void Every_theme_and_depth_has_a_name_in_both_languages(string id, GlassDepth depth)
    {
        foreach (var language in new[] { "it-IT", "en-US" })
        {
            var catalog = Core.Localization.Strings.Catalog(language);
            Assert.Contains("Theme_" + id, catalog.Keys);
            Assert.Contains("Glass_" + depth, catalog.Keys);
        }
    }

    [Fact]
    public void Ambient_follows_the_artwork_softly()
    {
        var dark = ThemeCatalog.Dark(GlassDepth.Medium);
        Assert.Equal(dark.Backdrop, ThemeCatalog.Ambient(dark, null));
        var (_, _, lightness) = ThemeCatalog.Ambient(dark, Argb.FromHex("#FF2A2A")).ToHsl();
        Assert.InRange(lightness, 0.15, 0.3);
        var (_, _, lightLightness) = ThemeCatalog.Ambient(ThemeCatalog.Light(GlassDepth.Medium), Argb.FromHex("#FF2A2A")).ToHsl();
        Assert.InRange(lightLightness, 0.75, 0.9);
    }

    [Fact]
    public void Hsl_round_trips()
    {
        var color = Argb.FromHex("#5EE6CF");
        var (h, s, l) = color.ToHsl();
        var back = Argb.FromHsl(h, s, l);
        Assert.InRange(Math.Abs(back.R - color.R), 0, 1);
        Assert.InRange(Math.Abs(back.G - color.G), 0, 1);
        Assert.InRange(Math.Abs(back.B - color.B), 0, 1);
    }

    [Theory]
    [InlineData(1920, 1080, 1.0, 1920, 1080)]
    [InlineData(3840, 2160, 1.0, 1920, 1080)]
    [InlineData(1365, 720, 1.0, 2047.5, 1080)]
    [InlineData(1920, 1080, 1.25, 1536, 864)]
    [InlineData(1024, 768, 1.0, 1440, 1080)]
    public void Layout_is_designed_at_1080_and_scaled(double width, double height, double chosen, double layoutWidth, double layoutHeight)
    {
        var (w, h) = UiScale.LayoutSize(width, height, chosen);
        Assert.Equal(layoutWidth, w, 1);
        Assert.Equal(layoutHeight, h, 1);
    }

    [Fact]
    public void Scale_is_clamped() => Assert.Equal(1.0, UiScale.Clamp(double.NaN));
}
