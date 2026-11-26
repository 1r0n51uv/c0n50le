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

    [JsonIgnore]
    public bool IsMatched => CatalogId.StartsWith("launchbox:", StringComparison.Ordinal);
}
