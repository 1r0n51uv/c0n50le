using C0n50le.Core.Settings;

namespace C0n50le.Core.Library;

/// <summary>Builds the row of the home from the games added by hand and the ones found on the PC.</summary>
public static class LibraryOrder
{
    /// <summary>
    /// Games added by hand win over a found one with the same exe; hidden games are left out. The most recently played
    /// come first, like a console's home; never-played ones follow in name order.
    /// </summary>
    public static List<Game> Build(Preferences preferences, IEnumerable<Game> detected)
    {
        var manualPaths = preferences.Manual.Select(game => game.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hidden = preferences.Hidden.ToHashSet(StringComparer.Ordinal);
        var real = preferences.Manual.Concat(detected.Where(game => !manualPaths.Contains(game.Path))).ToList();
        // A starter card gives way to the same game once it is really on the PC.
        var titles = real.Select(game => Catalog.LaunchBoxParser.Normalize(DisplayTitle(game, preferences.Art.GetValueOrDefault(game.Id)))).ToHashSet();
        var presets = preferences.ShowPresets
            ? PresetGames.All.Where(preset => !titles.Contains(Catalog.LaunchBoxParser.Normalize(DisplayTitle(preset, preferences.Art.GetValueOrDefault(preset.Id)))))
            : [];
        return real
            .Concat(presets)
            .DistinctBy(game => game.Id)
            .Where(game => !hidden.Contains(game.Id))
            .OrderByDescending(game => preferences.Played.TryGetValue(game.Id, out var when) ? when : DateTimeOffset.MinValue)
            .ThenBy(game => DisplayTitle(game, preferences.Art.GetValueOrDefault(game.Id)), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>The games that are hidden, to offer them back in System → Library.</summary>
    public static List<Game> HiddenGames(Preferences preferences, IEnumerable<Game> detected)
    {
        var hidden = preferences.Hidden.ToHashSet(StringComparer.Ordinal);
        return preferences.Manual.Concat(detected).Concat(PresetGames.All).DistinctBy(game => game.Id).Where(game => hidden.Contains(game.Id))
            .OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>The catalog's title once matched, else the name it was added or found with.</summary>
    public static string DisplayTitle(Game game, Artwork? art) =>
        art?.IsMatched == true && !string.IsNullOrWhiteSpace(art.CatalogTitle) ? art.CatalogTitle : game.Title;

    /// <summary>The title to look up in the catalog: the matched one, else the game's.</summary>
    public static string SearchTitle(Game game, Artwork? art) =>
        !string.IsNullOrWhiteSpace(art?.CatalogTitle) ? art!.CatalogTitle : game.Title;
}
