using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace C0n50le.Core.Catalog;

/// <summary>A game found in the catalog.</summary>
public sealed record CatalogMatch(string Title, Uri Page, string Platform = "")
{
    public string Label => Platform.Length == 0 ? Title : $"{Title} · {Platform}";
    public string Id => $"launchbox:{Page.Segments[^1].Split('-')[0]}";
    public override string ToString() => Label;
}

/// <summary>What the catalog has for one game: its texts and where its pictures are.</summary>
/// <remarks>Logo is the transparent "Clear Logo" laid over the background on a card; Banner a wide picture with the title.</remarks>
public sealed record CatalogEntry(string Description, Uri? Cover, Uri? Background, double CoverAspect, string[] Details,
    Uri? Logo = null, Uri? Banner = null);

/// <summary>
/// Reads the pages of the public LaunchBox Games Database (search results, game details, image gallery). Pure, so it is
/// tested on saved pages; <see cref="GameCatalog"/> downloads them. Adapted from RON1N Mode.
/// </summary>
public static class LaunchBoxParser
{
    public static readonly Uri Site = new("https://gamesdb.launchbox-app.com");
    public const string DetailsPath = "/games/details/";
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(1);

    public static List<CatalogMatch> ParseSearch(string html)
    {
        var results = new List<CatalogMatch>();
        var links = Regex.Matches(html, """<a\b[^>]*href="(/games/details/\d+(?:-[^"?#/]+)?)"[^>]*>(.*?)</a>""",
            RegexOptions.Singleline, Limit);
        foreach (Match link in links)
        {
            var label = Regex.Match(link.Groups[2].Value, """<h3\b[^>]*>(.*?)</h3>\s*<p\b[^>]*>(.*?)</p>""",
                RegexOptions.Singleline, Limit);
            if (!label.Success) continue;
            var title = Text(label.Groups[1].Value);
            var platform = Text(label.Groups[2].Value);
            if (title.Length > 0 && Uri.TryCreate(Site, link.Groups[1].Value, out var page))
                results.Add(new CatalogMatch(title, page, platform));
        }
        return results.DistinctBy(result => result.Id).Take(15).ToList();
    }

    public static CatalogEntry ParseEntry(string details, string gallery, string language)
    {
        var images = Regex.Matches(gallery,
            """<a\b[^>]*href="(https://images\.launchbox-app\.com/[^"]+)"[^>]*?data-title="([^"]+)"[^>]*?data-footer="(\d+) x (\d+)""",
            RegexOptions.IgnoreCase, Limit);
        var media = new List<Image>();
        foreach (Match image in images)
        {
            var url = AllowedImage(WebUtility.HtmlDecode(image.Groups[1].Value));
            if (url is null || !int.TryParse(image.Groups[3].Value, out var width)
                || !int.TryParse(image.Groups[4].Value, out var height) || width <= 0 || height <= 0) continue;
            media.Add(new Image(url, WebUtility.HtmlDecode(image.Groups[2].Value), width, height));
        }

        var cover = media.Where(image => image.HasType("Box - Front"))
            .OrderByDescending(image => image.Label.Contains("(Europe)", StringComparison.OrdinalIgnoreCase))
            .ThenBy(image => Math.Abs(Math.Log(image.Aspect / (2.0 / 3.0))))
            .FirstOrDefault();
        var background = media.Where(image => image.Width >= image.Height * 1.4
                && (image.HasType("Fanart - Background") || image.HasType("Screenshot - Gameplay")))
            .OrderByDescending(image => image.HasType("Fanart - Background"))
            .ThenByDescending(image => image.Width).FirstOrDefault();
        var description = Regex.Match(details, """<meta\s+name="description"\s+content="([^"]*)["]""",
            RegexOptions.IgnoreCase, Limit).Groups[1].Value;
        var time = Regex.Match(details, """<time\b[^>]*\bdatetime="([^"]+)["]""", RegexOptions.IgnoreCase, Limit).Groups[1].Value;
        var year = DateTime.TryParse(time, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var date)
            ? date.Year.ToString(CultureInfo.InvariantCulture) : "";
        var developer = Detail(details, "Developers");
        var metadata = new[]
            {
                Genres(Detail(details, "Genre"), language), year,
                developer.Length > 0 ? developer : Detail(details, "Publishers")
            }
            .Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        var logo = media.Where(image => image.HasType("Clear Logo"))
            .OrderByDescending(image => image.Label.Contains("(World)", StringComparison.OrdinalIgnoreCase)
                || image.Label.Contains("(North America)", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(image => image.Width).FirstOrDefault();
        var banner = media.Where(image => image.HasType("Steam Banner") || image.HasType("Banner"))
            .OrderByDescending(image => image.HasType("Steam Banner")).ThenByDescending(image => image.Width).FirstOrDefault();
        return new CatalogEntry(Text(description), cover?.Url, background?.Url, cover?.Aspect ?? 0, metadata, logo?.Url, banner?.Url);
    }

    public static bool IsDetailsPage(Uri page) =>
        page.Scheme == "https" && page.Host == Site.Host
        && page.AbsolutePath.StartsWith(DetailsPath, StringComparison.Ordinal)
        && Regex.IsMatch(page.AbsolutePath[DetailsPath.Length..], @"^\d+(?:-[^/]+)?$");

    /// <summary>Only https pictures from the catalog's own image host are ever downloaded.</summary>
    public static Uri? AllowedImage(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https"
        && uri.Host == "images.launchbox-app.com" ? uri : null;

    /// <summary>Accents, spaces and punctuation don't matter: "Pokémon Pokopia" equals "pokemon-pokopia".</summary>
    public static string Normalize(string input) => string.Concat(input.Normalize(System.Text.NormalizationForm.FormD)
        .Where(char.IsLetterOrDigit)).ToUpperInvariant();

    /// <summary>
    /// One result per title: a game out on several platforms is listed once, as its Windows release when there is one,
    /// else as the first platform found. The platforms stay in the order of the search.
    /// </summary>
    public static List<CatalogMatch> Collapse(IEnumerable<CatalogMatch> found) => found
        .GroupBy(match => Normalize(match.Title))
        .Select(group => group.FirstOrDefault(match => string.Equals(match.Platform, "Windows", StringComparison.OrdinalIgnoreCase))
            ?? group.First())
        .ToList();

    /// <summary>
    /// The match to apply by itself: the only result with exactly that title, preferring the Windows one when the
    /// title exists on several platforms. Null when it isn't clear: the user picks in "Search for another title".
    /// </summary>
    public static CatalogMatch? PickAutomatic(IEnumerable<CatalogMatch> found, string title)
    {
        var normalized = Normalize(title);
        var same = found.Where(result => Normalize(result.Title) == normalized).ToList();
        var windows = same.Where(result => string.Equals(result.Platform, "Windows", StringComparison.OrdinalIgnoreCase)).ToList();
        if (windows.Count == 1) return windows[0];
        return same.Count == 1 ? same[0] : null;
    }

    private static string Genres(string genres, string language) => string.Join(", ", genres
        .Split(", ", StringSplitOptions.RemoveEmptyEntries).Take(2)
        .Select(genre => language.StartsWith("it", StringComparison.OrdinalIgnoreCase) ? genre switch
        {
            "Action" => "Azione",
            "Adventure" => "Avventura",
            "Fighting" => "Picchiaduro",
            "Puzzle" => "Rompicapo",
            "Racing" => "Corse",
            "Role-Playing" => "Gioco di ruolo",
            "Shooter" => "Sparatutto",
            "Simulation" => "Simulazione",
            "Sports" => "Sport",
            "Strategy" => "Strategia",
            "Platform" => "Piattaforme",
            "Horror" => "Horror",
            "Sandbox" => "Sandbox",
            _ => genre
        } : genre));

    private static string Detail(string html, string label)
    {
        var value = Regex.Match(html, $@"<dt\b[^>]*>\s*{label}\s*</dt>\s*<dd\b[^>]*>(.*?)</dd>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline, Limit).Groups[1].Value;
        var links = Regex.Matches(value, @"<a\b[^>]*>(.*?)</a>", RegexOptions.Singleline, Limit);
        return links.Count == 0 ? Text(value) : string.Join(", ", links.Take(2).Select(link => Text(link.Groups[1].Value)));
    }

    internal static string Text(string text)
    {
        var plain = Regex.Replace(text, "<!--.*?-->|<[^>]+>", "", RegexOptions.Singleline, Limit);
        return Regex.Replace(WebUtility.HtmlDecode(plain), @"\s+", " ", RegexOptions.None, Limit).Trim();
    }

    private sealed record Image(Uri Url, string Label, int Width, int Height)
    {
        public double Aspect => (double)Width / Height;
        public bool HasType(string type) => Label.Contains($" - {type} Image", StringComparison.OrdinalIgnoreCase);
    }
}
