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
        <a href="https://images.launchbox-app.com/logo-jp.png" data-title="Control Resonant - Clear Logo Image (Japan)" data-footer="1600 x 600">
        <a href="https://images.launchbox-app.com/logo.png" data-title="Control Resonant - Clear Logo Image (World)" data-footer="800 x 300">
        <a href="https://images.launchbox-app.com/steam.jpg" data-title="Control Resonant - Steam Banner Image" data-footer="460 x 215">
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
        Assert.Equal("https://images.launchbox-app.com/logo.png", entry.Logo!.AbsoluteUri);
        Assert.Equal("https://images.launchbox-app.com/steam.jpg", entry.Banner!.AbsoluteUri);
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

public sealed class ArtSourceTests
{
    private static Core.Catalog.CatalogMatch Match(string title, string platform, int id) =>
        new(title, new Uri($"https://gamesdb.launchbox-app.com/games/details/{id}-x"), platform);

    [Fact]
    public void A_title_on_several_platforms_is_listed_once_preferring_windows()
    {
        var found = new[]
        {
            Match("Control Resonant", "Sony Playstation 5", 1), Match("Control: Resonant", "Windows", 2),
            Match("Control Resonant", "Microsoft Xbox Series X/S", 3), Match("Control", "Windows", 4)
        };
        var collapsed = Core.Catalog.LaunchBoxParser.Collapse(found);
        Assert.Equal(2, collapsed.Count);
        Assert.Equal("Windows", collapsed[0].Platform);
        Assert.Equal("Control: Resonant", collapsed[0].Title);
        Assert.Equal("Control", collapsed[1].Title);
    }

    [Fact]
    public void Gallery_pictures_keep_their_type_and_size()
    {
        const string html = """
            <a href="https://images.launchbox-app.com/a.jpg" data-title="Game - Fanart - Background Image" data-footer="1920 x 1080">x</a>
            <a href="https://images.launchbox-app.com/b.png" data-title="Game - Clear Logo Image (World)" data-footer="800 x 300">x</a>
            <a href="https://images.launchbox-app.com/c.jpg" data-title="Game - Steam Banner Image" data-footer="460 x 215">x</a>
            <a href="https://evil.example/d.jpg" data-title="Game - Fanart - Background Image" data-footer="1920 x 1080">x</a>
            """;
        var all = Core.Catalog.ArtSources.ParseLaunchBoxGallery(html);
        Assert.Equal(["Fanart - Background", "Clear Logo", "Steam Banner"], all.Select(candidate => candidate.Label));
        Assert.Equal(["Steam Banner", "Fanart - Background"],
            Core.Catalog.ArtSources.For(Core.Catalog.ArtUse.Card, all).Select(candidate => candidate.Label));
        Assert.Equal(["Fanart - Background"],
            Core.Catalog.ArtSources.For(Core.Catalog.ArtUse.Background, all).Select(candidate => candidate.Label));
    }

    [Fact]
    public void The_current_LaunchBox_gallery_markup_is_read()
    {
        // November 2026: attributes on their own lines and the size followed by the format, the weight and an edit link.
        const string html = """
            <a b-dnxk7ptp1m style="text-decoration: none !important;"
               href="https://images.launchbox-app.com/r2_552d1df5.jpg"
               data-gameimageid="20304801"
               data-toggle="lightbox"
               data-gallery="gallery"
               data-title="Pok&#xE9;mon Pokopia - Box - Front Image (North America)"
               data-footer="494 x 800 JPEG, 0.1 MB</br><a data-gameimageid='20304801' class='edit-image' href='javascript:'>Edit Image</a>">
            """;
        var picture = Assert.Single(Core.Catalog.ArtSources.ParseLaunchBoxGallery(html));
        Assert.Equal("Box - Front", picture.Label);
        var entry = Core.Catalog.LaunchBoxParser.ParseEntry("", html, "en-US");
        Assert.Equal("https://images.launchbox-app.com/r2_552d1df5.jpg", entry.Cover?.ToString());
    }

    [Fact]
    public void LaunchBox_entries_carry_the_badge_facts_and_more_backgrounds()
    {
        const string details = """
            <meta name="description" content="A game.">
            <time datetime="March 05, 2026">March 05, 2026</time>
            <dt>Max Players</dt><dd>4</dd>
            <dt>ESRB</dt><dd>E - Everyone</dd>
            <dt>Platform</dt><dd><a href="/x">Nintendo Switch 2</a></dd>
            <dt>Developers</dt><dd><a href="/a">Game Freak</a><a href="/b">Omega Force</a></dd>
            <dt>Publishers</dt><dd><a href="/p">Nintendo</a></dd>
            <dt>Genre</dt><dd><a href="/g">Life Simulation</a></dd>
            """;
        const string gallery = """
            <a href="https://images.launchbox-app.com/f1.jpg" data-title="G - Fanart - Background Image" data-footer="3840 x 2160 JPEG">x</a>
            <a href="https://images.launchbox-app.com/f2.jpg" data-title="G - Fanart - Background Image" data-footer="1920 x 1080 JPEG">x</a>
            <a href="https://images.launchbox-app.com/s1.jpg" data-title="G - Screenshot - Gameplay Image" data-footer="1920 x 1080 JPEG">x</a>
            <a href="https://images.launchbox-app.com/small.jpg" data-title="G - Screenshot - Gameplay Image" data-footer="640 x 360 JPEG">x</a>
            """;
        var entry = Core.Catalog.LaunchBoxParser.ParseEntry(details, gallery, "en-US");
        Assert.Equal("https://images.launchbox-app.com/f1.jpg", entry.Background?.ToString());
        Assert.Equal(["https://images.launchbox-app.com/f2.jpg", "https://images.launchbox-app.com/s1.jpg"], entry.MoreBackgrounds.Select(uri => uri.ToString()));
        Assert.Equal("E - Everyone", entry.Info.Rating);
        Assert.Equal("4", entry.Info.Players);
        Assert.Equal("2026", entry.Info.Year);
        Assert.Equal("Nintendo Switch 2", entry.Info.Platform);
        Assert.StartsWith("Game Freak", entry.Info.Developer);
    }

    [Fact]
    public void Steam_art_lists_library_pictures_and_screenshots()
    {
        const string json = """{"620":{"success":true,"data":{"screenshots":[{"id":0,"path_full":"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/620/ss_1.jpg"},{"path_full":"https://evil.example/x.jpg"}]}}}""";
        var art = Core.Catalog.ArtSources.ParseSteam(620, json);
        Assert.Contains(art, candidate => candidate.Label == "Library Hero");
        Assert.Single(art, candidate => candidate.Label == "Screenshot");
        var backgrounds = Core.Catalog.ArtSources.For(Core.Catalog.ArtUse.Background, art);
        Assert.Equal("Library Hero", backgrounds[0].Label);
        Assert.DoesNotContain(backgrounds, candidate => candidate.Label is "Header" or "Library Capsule");
        Assert.False(Core.Catalog.ArtSources.IsAllowed(new Uri("http://shared.akamai.steamstatic.com/x.jpg")));
    }

    [Fact]
    public void Chosen_backgrounds_win_over_the_catalog_one()
    {
        var art = new Core.Library.Artwork { Background = "bg", HasCatalogMedia = true, Backgrounds = ["a", "gone", "b"] };
        Assert.Equal(["a", "b"], art.BackgroundsToShow(path => path != "gone"));
        art.Backgrounds = [];
        Assert.Equal(["bg"], art.BackgroundsToShow(_ => true));
    }
}
