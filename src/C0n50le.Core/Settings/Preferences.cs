using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using C0n50le.Core.Library;

namespace C0n50le.Core.Settings;

/// <summary>Everything c0n50le remembers, saved as one JSON file in the data folder.</summary>
public sealed class Preferences
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    // Library
    public string GamesFolder { get; set; } = "";
    public bool ScanSteam { get; set; } = true;
    public bool ScanEpic { get; set; } = true;
    public List<Game> Manual { get; set; } = [];
    public List<string> Hidden { get; set; } = [];
    public Dictionary<string, string> Watched { get; set; } = [];
    public Dictionary<string, Artwork> Art { get; set; } = [];
    public Dictionary<string, DateTimeOffset> Played { get; set; } = [];
    public string LastPlayed { get; set; } = "";

    // Screen
    public string Screen { get; set; } = "";
    public double UiScale { get; set; } = 1.0;

    // Controls
    public string KeyboardShortcut { get; set; } = "F11";
    public int ControllerShortcut { get; set; } = Input.ControllerButtons.View | Input.ControllerButtons.Y;

    // Look and sound
    public string Theme { get; set; } = Theming.ThemeCatalog.DefaultId;
    /// <summary>Liquid Glass: 0 off, 1–3 the depths.</summary>
    public int Glass { get; set; } = (int)Theming.ThemeCatalog.DefaultGlass;
    public bool ShowSystemCard { get; set; } = true;
    public bool ReduceMotion { get; set; }
    public bool Sounds { get; set; } = true;
    public bool Music { get; set; } = true;
    public bool Intro { get; set; } = true;
    public bool TourDone { get; set; }

    // Home widgets: weather, news and gallery, on the right of the top shelf.
    public bool ShowWidgets { get; set; } = true;
    public string WeatherCity { get; set; } = "";
    public bool WeatherFahrenheit { get; set; }
    /// <summary>RSS or Atom address; "" follows the interface language.</summary>
    public string NewsFeed { get; set; } = "";
    /// <summary>Pictures folder; "" is the Windows Pictures folder.</summary>
    public string GalleryFolder { get; set; } = "";

    // Start
    public bool OpenFullscreen { get; set; } = true;
    public bool CloseToTray { get; set; } = true;

    // Updates
    public bool CheckUpdates { get; set; } = true;
    public bool BetaUpdates { get; set; }

    // Language: "" follows Windows.
    public string Language { get; set; } = "";

    public Artwork ArtFor(Game game)
    {
        if (!Art.TryGetValue(game.Id, out var art)) Art[game.Id] = art = new Artwork();
        return art;
    }

    public void MarkPlayed(Game game, DateTimeOffset when)
    {
        LastPlayed = game.Id;
        Played[game.Id] = when;
    }

    /// <summary>Reads the file. A missing file gives the defaults; an unreadable one throws, so it is never overwritten.</summary>
    public static Preferences Load(string file)
    {
        if (!File.Exists(file)) return new Preferences();
        var preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(file), Json) ?? new Preferences();
        preferences.Normalize();
        return preferences;
    }

    /// <summary>Writes a temporary file and moves it over the old one, so a crash never leaves half a file.</summary>
    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var stage = file + ".tmp";
        File.WriteAllText(stage, JsonSerializer.Serialize(this, Json));
        File.Move(stage, file, overwrite: true);
    }

    /// <summary>Repairs values a hand edit could break.</summary>
    public void Normalize()
    {
        Manual ??= [];
        Hidden ??= [];
        Watched ??= [];
        Art ??= [];
        Played ??= [];
        Manual.RemoveAll(game => game is null || string.IsNullOrWhiteSpace(game.Path));
        foreach (var game in Manual)
        {
            if (string.IsNullOrWhiteSpace(game.Id)) game.Id = Guid.NewGuid().ToString("N");
            game.Source = GameSource.Manual;
        }
        UiScale = Theming.UiScale.Clamp(UiScale);
        Theme = Theming.ThemeCatalog.Normalize(Theme);
        Glass = (int)Theming.ThemeCatalog.NormalizeGlass(Glass);
        if (!Input.ShortcutChoices.IsKnownKeyboard(KeyboardShortcut)) KeyboardShortcut = "F11";
        if (!Input.ShortcutChoices.IsKnownController(ControllerShortcut))
            ControllerShortcut = Input.ControllerButtons.View | Input.ControllerButtons.Y;
        WeatherCity = (WeatherCity ?? "").Trim();
        NewsFeed = (NewsFeed ?? "").Trim();
        if (NewsFeed.Length > 0 && !Widgets.News.IsValidUrl(NewsFeed)) NewsFeed = "";
        GalleryFolder = (GalleryFolder ?? "").Trim();
        if (Language is not ("" or "it-IT" or "en-US")) Language = "";
    }

    /// <summary>Copies a downloaded picture into the art folder under a name tied to the game.</summary>
    public static string StoreImage(string artFolder, string gameId, string kind, string source)
    {
        Directory.CreateDirectory(artFolder);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(gameId)))[..20];
        var destination = Path.Combine(artFolder, $"{key}-{kind}-{Guid.NewGuid():N}{Path.GetExtension(source).ToLowerInvariant()}");
        File.Copy(source, destination + ".tmp", overwrite: true);
        File.Move(destination + ".tmp", destination, overwrite: true);
        return destination;
    }

    /// <summary>Deletes pictures in the art folder that no game points at any more.</summary>
    public int PruneArt(string artFolder)
    {
        if (!Directory.Exists(artFolder)) return 0;
        var used = Art.Values.SelectMany(art => new[] { art.Card, art.Background, art.Logo, art.Banner })
            .Where(path => path.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = 0;
        foreach (var file in Directory.GetFiles(artFolder))
        {
            if (used.Contains(file)) continue;
            try { File.Delete(file); removed++; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return removed;
    }
}
