using System.Text.Json.Serialization;

namespace C0n50le.Core.Library;

/// <summary>Where a game in the library comes from.</summary>
public static class GameSource
{
    public const string Manual = "manual";
    public const string Folder = "folder";
    public const string Steam = "steam";
    public const string Epic = "epic";
}

/// <summary>A game or app the home can start.</summary>
public sealed class Game
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    /// <summary>A store link (steam://, com.epicgames.launcher://) used instead of the exe to start it.</summary>
    public string LaunchUri { get; set; } = "";
    public string Arguments { get; set; } = "";
    /// <summary>The process to follow when it is not the exe itself (a launcher that starts the real game).</summary>
    public string WatchProcess { get; set; } = "";
    public string Source { get; set; } = GameSource.Manual;
    public uint? SteamId { get; set; }

    [JsonIgnore]
    public string Title => string.IsNullOrWhiteSpace(Name) ? System.IO.Path.GetFileNameWithoutExtension(Path) : Name;

    [JsonIgnore]
    public bool IsManual => Source == GameSource.Manual;
}

/// <summary>What the home shows for a game: catalog texts and the pictures saved on disk.</summary>
public sealed class Artwork
{
    public string Description { get; set; } = "";
    /// <summary>Localized texts keyed by language and catalog title, including their attribution.</summary>
    public Dictionary<string, Catalog.LocalizedDescription> Descriptions { get; set; } = [];
    /// <summary>The cover picture (portrait box art), a file in the data folder.</summary>
    public string Card { get; set; } = "";
    /// <summary>The wide picture behind the home, a file in the data folder.</summary>
    public string Background { get; set; } = "";
    public string CatalogId { get; set; } = "";
    public string CatalogTitle { get; set; } = "";
    public string CatalogUrl { get; set; } = "";
    public string[] Details { get; set; } = [];
    public bool HasCatalogMedia { get; set; }
    public double CoverAspect { get; set; }
    /// <summary>The game's transparent logo, laid over the background on a card.</summary>
    public string Logo { get; set; } = "";
    /// <summary>A wide picture with the title, for a card when there is no logo.</summary>
    public string Banner { get; set; } = "";
    /// <summary>The picture chosen for the card in the detail page; it replaces the automatic one.</summary>
    public string CardImage { get; set; } = "";
    /// <summary>The backgrounds chosen in the detail page, shown in turn behind the home.</summary>
    public List<string> Backgrounds { get; set; } = [];

    /// <summary>The backgrounds to show: the chosen ones still on disk, else the catalog's.</summary>
    public IReadOnlyList<string> BackgroundsToShow(Func<string, bool> exists)
    {
        var chosen = (Backgrounds ?? []).Where(path => path.Length > 0 && exists(path)).ToList();
        if (chosen.Count > 0) return chosen;
        return HasCatalogMedia && Background.Length > 0 && exists(Background) ? [Background] : [];
    }

    /// <summary>Which pictures were fetched: older art (without logo and banner) is completed by itself.</summary>
    public int MediaVersion { get; set; }

    public const int CurrentMediaVersion = 2;

    [JsonIgnore]
    public bool NeedsMedia => !IsMatched || !HasCatalogMedia || MediaVersion < CurrentMediaVersion;

    [JsonIgnore]
    public bool IsMatched => CatalogId.StartsWith("launchbox:", StringComparison.Ordinal);
}
