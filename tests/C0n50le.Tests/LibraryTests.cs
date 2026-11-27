using C0n50le.Core.Library;
using C0n50le.Core.Settings;

namespace C0n50le.Tests;

public sealed class LibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "c0n50le-tests-" + Guid.NewGuid().ToString("N"));

    public LibraryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string Touch(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void FindExecutable_prefers_the_exe_named_like_the_game()
    {
        Touch("Game", "launcher.exe");
        Touch("Game", "unins000.exe");
        var main = Touch("Game", "bin", "Control Resonant.exe");
        Assert.Equal(main, GameDiscovery.FindExecutable(Path.Combine(_root, "Game"), "Control: Resonant"));
    }

    [Fact]
    public void FindExecutable_skips_installers_and_crash_reporters()
    {
        Touch("Game", "setup.exe");
        Touch("Game", "CrashReportClient.exe");
        Touch("Game", "vc_redist.x64.exe");
        var main = Touch("Game", "Binaries", "Win64", "Something-Win64-Shipping.exe");
        Assert.Equal(main, GameDiscovery.FindExecutable(Path.Combine(_root, "Game"), "Unrelated"));
    }

    [Fact]
    public void FindExecutable_is_null_without_executables_or_folder()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Empty"));
        Assert.Null(GameDiscovery.FindExecutable(Path.Combine(_root, "Empty"), "x"));
        Assert.Null(GameDiscovery.FindExecutable(Path.Combine(_root, "Missing"), "x"));
        Assert.Null(GameDiscovery.FindExecutable("", "x"));
    }

    [Fact]
    public void Folder_scan_makes_one_game_per_subfolder_with_stable_ids()
    {
        Touch("Library", "FIFA 27", "FIFA 27.exe");
        Touch("Library", "GTA 6", "GTA 6.exe");
        Directory.CreateDirectory(Path.Combine(_root, "Library", "Not a game"));
        var first = GameDiscovery.Scan(new DiscoveryRoots(Path.Combine(_root, "Library"), "", ""));
        var second = GameDiscovery.Scan(new DiscoveryRoots(Path.Combine(_root, "Library"), "", ""));
        Assert.Equal(["FIFA 27", "GTA 6"], first.Select(game => game.Name));
        Assert.All(first, game => Assert.Equal(GameSource.Folder, game.Source));
        Assert.Equal(first.Select(game => game.Id), second.Select(game => game.Id));
    }

    [Fact]
    public void Steam_scan_reads_manifests_and_skips_tools()
    {
        var steam = Path.Combine(_root, "Steam");
        Touch("Steam", "steamapps", "common", "Portal 2", "portal2.exe");
        File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_620.acf"),
            "\"AppState\"\n{\n\t\"appid\"\t\t\"620\"\n\t\"name\"\t\t\"Portal 2\"\n\t\"installdir\"\t\t\"Portal 2\"\n}");
        File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_228980.acf"),
            "\"AppState\"\n{\n\t\"appid\"\t\t\"228980\"\n\t\"name\"\t\t\"Steamworks Common Redistributables\"\n\t\"installdir\"\t\t\"Steamworks Shared\"\n}");
        var games = new List<Game>();
        GameDiscovery.ScanSteam(steam, games);
        var game = Assert.Single(games);
        Assert.Equal("Portal 2", game.Name);
        Assert.Equal(620u, game.SteamId);
        Assert.Equal("steam://rungameid/620", game.LaunchUri);
        Assert.Equal(GameSource.Steam, game.Source);
    }

    [Fact]
    public void Epic_scan_reads_manifests_and_skips_incomplete_installs()
    {
        var install = Path.Combine(_root, "Epic", "Alan Wake 2");
        Touch("Epic", "Alan Wake 2", "AlanWake2.exe");
        var manifests = Path.Combine(_root, "Manifests");
        Directory.CreateDirectory(manifests);
        File.WriteAllText(Path.Combine(manifests, "a.item"), $$"""
            {"AppName":"Dodo","DisplayName":"Alan Wake 2","LaunchExecutable":"AlanWake2.exe","InstallLocation":{{System.Text.Json.JsonSerializer.Serialize(install)}},"CatalogNamespace":"ns","CatalogItemId":"item"}
            """);
        File.WriteAllText(Path.Combine(manifests, "b.item"), $$"""
            {"AppName":"Half","DisplayName":"Half done","LaunchExecutable":"AlanWake2.exe","InstallLocation":{{System.Text.Json.JsonSerializer.Serialize(install)}},"bIsIncompleteInstall":true}
            """);
        File.WriteAllText(Path.Combine(manifests, "c.item"), "not json");
        var games = new List<Game>();
        GameDiscovery.Scan(new DiscoveryRoots("", "", manifests)).ForEach(games.Add);
        var game = Assert.Single(games);
        Assert.Equal("Alan Wake 2", game.Name);
        Assert.StartsWith("com.epicgames.launcher://apps/ns%3Aitem%3ADodo", game.LaunchUri);
    }

    [Fact]
    public void Library_order_puts_recently_played_first_and_leaves_hidden_out()
    {
        var preferences = new Preferences { ShowPresets = false };
        var a = new Game { Id = "a", Name = "Alpha", Path = @"C:\a.exe", Source = GameSource.Folder };
        var b = new Game { Id = "b", Name = "Beta", Path = @"C:\b.exe", Source = GameSource.Folder };
        var c = new Game { Id = "c", Name = "Gamma", Path = @"C:\c.exe", Source = GameSource.Folder };
        preferences.MarkPlayed(c, DateTimeOffset.Now.AddHours(-1));
        preferences.MarkPlayed(b, DateTimeOffset.Now);
        preferences.Hidden.Add("a");
        var order = LibraryOrder.Build(preferences, [a, b, c]);
        Assert.Equal(["b", "c"], order.Select(game => game.Id));
        Assert.Equal(["a"], LibraryOrder.HiddenGames(preferences, [a, b, c]).Select(game => game.Id));
    }

    [Fact]
    public void Library_order_lets_a_manual_game_win_over_a_found_one_with_the_same_exe()
    {
        var preferences = new Preferences { ShowPresets = false };
        preferences.Manual.Add(new Game { Id = "m", Name = "Mine", Path = @"C:\Games\x.exe" });
        var found = new Game { Id = "auto", Name = "Found", Path = @"c:\games\X.EXE", Source = GameSource.Folder };
        var order = LibraryOrder.Build(preferences, [found]);
        Assert.Equal("m", Assert.Single(order).Id);
    }

    [Fact]
    public void Display_title_uses_the_catalog_title_once_matched()
    {
        var game = new Game { Name = "Pokemon Pokopia", Path = @"C:\p.exe" };
        Assert.Equal("Pokemon Pokopia", LibraryOrder.DisplayTitle(game, null));
        var art = new Artwork { CatalogId = "launchbox:1", CatalogTitle = "Pokémon Pokopia" };
        Assert.Equal("Pokémon Pokopia", LibraryOrder.DisplayTitle(game, art));
        Assert.Equal("Pokémon Pokopia", LibraryOrder.SearchTitle(game, art));
    }

    [Fact]
    public void Starter_cards_fill_the_home_and_give_way_to_the_real_game()
    {
        var empty = new Preferences();
        Assert.Equal(PresetGames.All.Select(game => game.Id).OrderBy(id => id), LibraryOrder.Build(empty, []).Select(game => game.Id).OrderBy(id => id));

        var installed = new Game { Id = "real", Name = "Pokémon Pokopia", Path = @"C:\Games\Pokopia.exe", Source = GameSource.Manual };
        var withGame = new Preferences { Manual = [installed] };
        var built = LibraryOrder.Build(withGame, []);
        Assert.Contains(built, game => game.Id == "real");
        Assert.DoesNotContain(built, game => game.Id == "preset-pokopia");

        var hidden = new Preferences { Hidden = ["preset-gta6"] };
        Assert.DoesNotContain(LibraryOrder.Build(hidden, []), game => game.Id == "preset-gta6");
        Assert.Contains(LibraryOrder.HiddenGames(hidden, []), game => game.Id == "preset-gta6");

        Assert.Empty(LibraryOrder.Build(new Preferences { ShowPresets = false }, []));
    }
}
