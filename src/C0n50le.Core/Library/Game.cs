using System.Text.Json.Serialization;

namespace C0n50le.Core.Library;

/// <summary>Where a game in the library comes from.</summary>
/// <summary>The description sources a game can be set to.</summary>
public static class DescriptionSources
{
    public const string Automatic = "";
    public const string LaunchBox = "launchbox";
    public const string Wikipedia = "wikipedia";
}

public static class GameSource
{
    public const string Manual = "manual";
    public const string Folder = "folder";
    public const string Steam = "steam";
    public const string Epic = "epic";
    /// <summary>A starter card the home opens with, so it is never empty: artwork and texts, nothing to start.</summary>
    public const string Preset = "preset";
}

/// <summary>The starter cards: well-known games, matched in the catalog like any other, that can be hidden.</summary>
public static class PresetGames
{
    public static IReadOnlyList<Game> All { get; } =
    [
        new() { Id = "preset-gta6", Name = "Grand Theft Auto VI", Source = GameSource.Preset },
        new() { Id = "preset-fc27", Name = "EA Sports FC 27", Source = GameSource.Preset },
        new() { Id = "preset-pokopia", Name = "Pokémon Pokopia", Source = GameSource.Preset },
        new() { Id = "preset-control-resonant", Name = "Control Resonant", Source = GameSource.Preset },
        new() { Id = "preset-ac-shadows", Name = "Assassin's Creed Shadows", Source = GameSource.Preset },
        new() { Id = "preset-rdr2", Name = "Red Dead Redemption 2", Source = GameSource.Preset },
        new() { Id = "preset-ghost-of-yotei", Name = "Ghost of Yōtei", Source = GameSource.Preset },
        new() { Id = "preset-spider-man-2", Name = "Marvel's Spider-Man 2", Source = GameSource.Preset }
    ];
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

    [JsonIgnore]
    public bool IsPreset => Source == GameSource.Preset;
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
    /// <summary>The logo chosen in the detail page; it replaces the catalog's.</summary>
    public string LogoImage { get; set; } = "";
    /// <summary>Where this game's description comes from: automatic (LaunchBox, translated), or Wikipedia.</summary>
    public string DescriptionSource { get; set; } = DescriptionSources.Automatic;

    /// <summary>The logo to show: the chosen one, else the catalog's (or the store's), when the file is there.</summary>
    public string? LogoToShow(Func<string, bool> exists) =>
        LogoImage.Length > 0 && exists(LogoImage) ? LogoImage : Logo.Length > 0 && exists(Logo) ? Logo : null;
    /// <summary>The backgrounds chosen in the detail page, shown in turn behind the home.</summary>
    public List<string> Backgrounds { get; set; } = [];
    /// <summary>The catalog's other wide pictures, files in the data folder, shown in turn after <see cref="Background"/>.</summary>
    public List<string> MoreBackgrounds { get; set; } = [];
    /// <summary>The catalog's facts for the badges under the title.</summary>
    public Catalog.GameInfo Info { get; set; } = new();

    /// <summary>The backgrounds to show: the chosen ones still on disk, else the catalog's.</summary>
    public IReadOnlyList<string> BackgroundsToShow(Func<string, bool> exists)
    {
        var chosen = (Backgrounds ?? []).Where(path => path.Length > 0 && exists(path)).ToList();
        if (chosen.Count > 0) return chosen;
        if (!HasCatalogMedia || Background.Length == 0 || !exists(Background)) return [];
        return [Background, .. (MoreBackgrounds ?? []).Where(path => path.Length > 0 && exists(path))];
    }

    /// <summary>Which pictures were fetched: older art (without logo and banner) is completed by itself.</summary>
    public int MediaVersion { get; set; }

    // 3: more backgrounds in turn and the facts for the badges.
    public const int CurrentMediaVersion = 3;

    [JsonIgnore]
    public bool NeedsMedia => !IsMatched || !HasCatalogMedia || MediaVersion < CurrentMediaVersion;

    [JsonIgnore]
    public bool IsMatched => CatalogId.StartsWith("launchbox:", StringComparison.Ordinal);
}
