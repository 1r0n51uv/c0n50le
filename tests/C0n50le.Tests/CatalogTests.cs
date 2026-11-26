using C0n50le.Core.Catalog;

namespace C0n50le.Tests;

public sealed class CatalogTests
{
    private const string SearchPage = """
        <div><a class="link" href="/games/details/446661-control-resonant"><div><h3 class="title">Control Resonant</h3><p>Windows</p></div></a>
        <a href="/games/details/436411-pokemon-pokopia"><h3>Pokémon Pokopia</h3> <p>Nintendo Switch 2</p></a>
        <a href="/games/details/436411-pokemon-pokopia"><h3>Pokémon Pokopia</h3><p>Nintendo Switch 2</p></a>
        <a href="https://evil.example/games/details/1"><h3>Evil</h3><p>Windows</p></a>
        <a href="/games/details/9-no-label">nothing</a></div>
        """;

    private const string DetailsPage = """
        <meta name="description" content="Explore a warped Manhattan &amp; more.">
        <dl><dt>Developers</dt><dd><a href="#">Remedy Entertainment</a></dd>
        <dt>Genre</dt><dd>Role-Playing, Action</dd>
        <dt>Release Date</dt><dd><time datetime="2026-02-24">Feb 24, 2026</time></dd></dl>
        """;

    private const string GalleryPage = """
        <a href="https://images.launchbox-app.com/front-us.jpg" data-title="Control Resonant - Box - Front Image (North America)" data-footer="1000 x 1500">
        <a href="https://images.launchbox-app.com/front-eu.jpg" data-title="Control Resonant - Box - Front Image (Europe)" data-footer="1000 x 1420">
        <a href="https://images.launchbox-app.com/shot.jpg" data-title="Control Resonant - Screenshot - Gameplay Image" data-footer="1920 x 1080">
        <a href="https://images.launchbox-app.com/fanart.jpg" data-title="Control Resonant - Fanart - Background Image" data-footer="3840 x 2160">
        <a href="https://evil.example/fanart.jpg" data-title="Control Resonant - Fanart - Background Image" data-footer="7680 x 4320">
        """;

    [Fact]
    public void Search_results_are_read_deduplicated_and_kept_on_the_site()
    {
        var results = LaunchBoxParser.ParseSearch(SearchPage);
        Assert.Equal(["Control Resonant", "Pokémon Pokopia"], results.Select(result => result.Title));
        Assert.Equal("launchbox:446661", results[0].Id);
        Assert.Equal("Pokémon Pokopia · Nintendo Switch 2", results[1].Label);
    }

    [Fact]
    public void Entry_picks_the_european_cover_the_fanart_and_translated_details()
    {
        var entry = LaunchBoxParser.ParseEntry(DetailsPage, GalleryPage, "it-IT");
        Assert.Equal("Explore a warped Manhattan & more.", entry.Description);
        Assert.Equal("https://images.launchbox-app.com/front-eu.jpg", entry.Cover!.AbsoluteUri);
        Assert.Equal("https://images.launchbox-app.com/fanart.jpg", entry.Background!.AbsoluteUri);
        Assert.Equal(["Gioco di ruolo, Azione", "2026", "Remedy Entertainment"], entry.Details);
        Assert.Equal(1000 / 1420.0, entry.CoverAspect, 3);
    }

    [Fact]
    public void Entry_keeps_english_genres_in_english()
    {
        var entry = LaunchBoxParser.ParseEntry(DetailsPage, GalleryPage, "en-US");
        Assert.Equal("Role-Playing, Action", entry.Details[0]);
    }

    [Fact]
    public void Only_catalog_pages_and_images_are_trusted()
    {
        Assert.True(LaunchBoxParser.IsDetailsPage(new Uri("https://gamesdb.launchbox-app.com/games/details/436411-pokemon-pokopia")));
        Assert.False(LaunchBoxParser.IsDetailsPage(new Uri("https://gamesdb.launchbox-app.com/games/details/../../admin")));
        Assert.False(LaunchBoxParser.IsDetailsPage(new Uri("http://gamesdb.launchbox-app.com/games/details/1")));
        Assert.NotNull(LaunchBoxParser.AllowedImage("https://images.launchbox-app.com/a.png"));
        Assert.Null(LaunchBoxParser.AllowedImage("https://images.launchbox-app.com.evil.example/a.png"));
        Assert.Null(LaunchBoxParser.AllowedImage("http://images.launchbox-app.com/a.png"));
    }

    [Fact]
    public void Automatic_match_needs_an_exact_title_and_prefers_windows()
    {
        var page = new Uri("https://gamesdb.launchbox-app.com/games/details/1");
        var found = new List<CatalogMatch>
        {
            new("Pokémon Pokopia", page, "Nintendo Switch 2"),
            new("Pokémon Pokopia Deluxe", new Uri("https://gamesdb.launchbox-app.com/games/details/2"), "Nintendo Switch 2")
        };
        Assert.Equal("Pokémon Pokopia", LaunchBoxParser.PickAutomatic(found, "Pokemon Pokopia")?.Title);
        Assert.Null(LaunchBoxParser.PickAutomatic(found, "GTA 6"));
        var twoPlatforms = new List<CatalogMatch>
        {
            new("FIFA 27", new Uri("https://gamesdb.launchbox-app.com/games/details/3"), "Sony Playstation 5"),
            new("FIFA 27", new Uri("https://gamesdb.launchbox-app.com/games/details/4"), "Windows")
        };
        Assert.Equal("Windows", LaunchBoxParser.PickAutomatic(twoPlatforms, "fifa-27")?.Platform);
        var ambiguous = new List<CatalogMatch>
        {
            new("Doom", new Uri("https://gamesdb.launchbox-app.com/games/details/5"), "Sony Playstation 5"),
            new("Doom", new Uri("https://gamesdb.launchbox-app.com/games/details/6"), "Xbox Series X")
        };
        Assert.Null(LaunchBoxParser.PickAutomatic(ambiguous, "Doom"));
    }

    [Fact]
    public void Normalize_ignores_accents_case_and_punctuation() =>
        Assert.Equal(LaunchBoxParser.Normalize("Pokémon: Pokopia!"), LaunchBoxParser.Normalize("pokemon pokopia"));
}
