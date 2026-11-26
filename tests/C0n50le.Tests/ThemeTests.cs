using C0n50le.Core.Theming;

namespace C0n50le.Tests;

public sealed class ThemeTests
{
    public static IEnumerable<object[]> Themes => ThemeCatalog.All.Select(theme => new object[] { theme.Id });

    private static Argb Opaque(ThemeTokens theme, Argb color) => color.Over(theme.Backdrop);

    [Theory]
    [MemberData(nameof(Themes))]
    public void Text_is_readable_on_every_surface(string id)
    {
        var theme = ThemeCatalog.Get(id);
        var surface = Opaque(theme, theme.Surface);
        Assert.True(Argb.Contrast(theme.Text, surface) >= 7, $"{id}: text on surface");
        Assert.True(Argb.Contrast(theme.TextMuted, surface) >= 4.5, $"{id}: muted text on surface");
        Assert.True(Argb.Contrast(theme.Text, Opaque(theme, theme.SurfaceRaised)) >= 7, $"{id}: text on raised surface");
        Assert.True(Argb.Contrast(theme.OnAccent, theme.Accent) >= 4.5, $"{id}: text on accent");
        Assert.True(Argb.Contrast(theme.Accent, surface) >= 3, $"{id}: accent on surface");
        Assert.True(Argb.Contrast(theme.Focus, surface) >= 3, $"{id}: focus ring on surface");
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Every_theme_has_a_name_in_both_languages(string id)
    {
        var key = ThemeCatalog.Get(id).NameKey;
        Assert.Contains(key, Core.Localization.Strings.Catalog("it-IT").Keys);
        Assert.Contains(key, Core.Localization.Strings.Catalog("en-US").Keys);
    }

    [Fact]
    public void Themes_are_five_with_distinct_ids_and_night_first() =>
        Assert.Equal(["night", "glass", "game", "dawn", "arcade"], ThemeCatalog.All.Select(theme => theme.Id));

    [Fact]
    public void Unknown_theme_falls_back_to_night() => Assert.Same(ThemeCatalog.Night, ThemeCatalog.Get("neon"));

    [Theory]
    [InlineData("#FF2A2A")]
    [InlineData("#2A40FF")]
    [InlineData("#F4E21A")]
    [InlineData("#1D7A2C")]
    public void Artwork_colors_keep_the_accent_readable(string hex)
    {
        foreach (var theme in ThemeCatalog.All.Where(theme => theme.FollowsArtwork))
        {
            var tinted = ThemeCatalog.WithArtwork(theme, Argb.FromHex(hex));
            Assert.True(Argb.Contrast(tinted.Accent, tinted.Surface.Over(tinted.Backdrop)) >= 4.5, $"{theme.Id} {hex}");
            Assert.True(Argb.Contrast(tinted.OnAccent, tinted.Accent) >= 4.5, $"{theme.Id} {hex} on accent");
            Assert.True(Argb.Contrast(tinted.Text, tinted.Surface.Over(tinted.Backdrop)) >= 7, $"{theme.Id} {hex} text");
        }
    }

    [Fact]
    public void Themes_that_do_not_follow_the_artwork_stay_as_they_are()
    {
        Assert.Same(ThemeCatalog.Night, ThemeCatalog.WithArtwork(ThemeCatalog.Night, Argb.FromHex("#FF0000")));
        Assert.Same(ThemeCatalog.GameColor, ThemeCatalog.WithArtwork(ThemeCatalog.GameColor, Argb.FromHex("#808080")));
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
