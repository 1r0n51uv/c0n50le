using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace C0n50le.Core.Catalog;

/// <summary>
/// Reads a game's description in the user's language from public sources, since LaunchBox has English only: the
/// summary of the game's Wikipedia article, or an English Steam store description. Pure, so it is
/// tested on saved answers; <see cref="DescriptionFinder"/> downloads them.
/// </summary>
public static class DescriptionParser
{
    /// <summary>The Steam app whose name is the title (accents, ™ and punctuation ignored), from a store search.</summary>
    public static uint? ParseSteamSearch(string json, string title)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return null;
        var wanted = LaunchBoxParser.Normalize(title);
        foreach (var item in items.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var value) ? value.GetString() ?? "" : "";
            if (LaunchBoxParser.Normalize(name) != wanted) continue;
            if (item.TryGetProperty("id", out var id) && id.TryGetUInt32(out var appId)) return appId;
        }
        return null;
    }

    /// <summary>The short description of a Steam app, as plain text.</summary>
    public static string ParseSteamDetails(string json, uint appId)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(appId.ToString(), out var app)
            || !app.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True
            || !app.TryGetProperty("data", out var data) || !data.TryGetProperty("short_description", out var text)) return "";
        return Plain(text.GetString() ?? "");
    }

    /// <summary>An exact title match, allowing Wikipedia's parenthesized disambiguation suffix.</summary>
    public static string? ParseWikipediaSearch(string json, string title)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("query", out var query) || !query.TryGetProperty("search", out var results)) return null;
        var wanted = LaunchBoxParser.Normalize(title);
        foreach (var result in results.EnumerateArray().OrderByDescending(result =>
                     result.TryGetProperty("title", out var name) &&
                     (name.GetString() ?? "").Contains("(videogioco", StringComparison.OrdinalIgnoreCase)))
        {
            var page = result.TryGetProperty("title", out var value) ? value.GetString() ?? "" : "";
            var baseTitle = Regex.Replace(page, @"\s*\([^)]*\)\s*$", "");
            if (wanted.Length > 0 && LaunchBoxParser.Normalize(baseTitle) == wanted) return page;
        }
        return null;
    }

    /// <summary>The article's summary, when it is about a video game (so a film or a book with the same name is skipped).</summary>
    public static string ParseWikipediaSummary(string json, string language)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("type", out var type) && type.GetString() == "disambiguation") return "";
        if (!document.RootElement.TryGetProperty("extract", out var extract)) return "";
        var text = Plain(extract.GetString() ?? "");
        if (Regex.IsMatch(text, @"\b(?:videogio(?:co|chi)|video[ -]games?)\b", RegexOptions.IgnoreCase)) return text;
        // Some articles introduce the game by genre alone (e.g. Control Resonant).
        return Regex.IsMatch(text, @"\b(?:action RPG|RPG d.azione|sparatutto|platformer)\b", RegexOptions.IgnoreCase)
            && Regex.IsMatch(text, @"\b(?:sviluppat[oa]|developed)\b", RegexOptions.IgnoreCase)
            ? text : "";
    }

    /// <summary>The title of the same article in another language, from a Wikipedia langlinks query.</summary>
    public static string? ParseLangLink(string json, string language)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages)) return null;
        foreach (var page in pages.EnumerateObject())
        {
            if (!page.Value.TryGetProperty("langlinks", out var links) || links.ValueKind != JsonValueKind.Array) continue;
            foreach (var link in links.EnumerateArray())
                if (link.TryGetProperty("lang", out var lang) && lang.GetString() == language
                    && link.TryGetProperty("*", out var title) && title.GetString() is { Length: > 0 } name) return name;
        }
        return null;
    }

    private static readonly Regex Edition = new(
        @"\s*[-–:]?\s*(?:standard|deluxe|premium|gold|ultimate|complete|definitive|game of the year|goty)\s+edition\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The Microsoft Store (Xbox and PC) product whose name is the title, from an autosuggest answer. The plain game wins
    /// over its editions, which are taken only when nothing else matches.
    /// </summary>
    public static string? ParseStoreSuggest(string json, string title)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("Results", out var results) || results.ValueKind != JsonValueKind.Array) return null;
        var wanted = LaunchBoxParser.Normalize(title);
        if (wanted.Length == 0) return null;
        string? edition = null;
        foreach (var family in results.EnumerateArray())
        {
            if (!family.TryGetProperty("Products", out var products) || products.ValueKind != JsonValueKind.Array) continue;
            foreach (var product in products.EnumerateArray())
            {
                var name = product.TryGetProperty("Title", out var value) ? value.GetString() ?? "" : "";
                var id = product.TryGetProperty("ProductId", out var productId) ? productId.GetString() ?? "" : "";
                if (id.Length == 0) continue;
                if (LaunchBoxParser.Normalize(name) == wanted) return id;
                if (edition is null && LaunchBoxParser.Normalize(Edition.Replace(name, "")) == wanted) edition = id;
            }
        }
        return edition;
    }

    /// <summary>A Microsoft Store product's description: the short one, or the first paragraph of the long one.</summary>
    public static string ParseStoreProduct(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("Products", out var products) || products.ValueKind != JsonValueKind.Array) return "";
        foreach (var product in products.EnumerateArray())
        {
            if (!product.TryGetProperty("LocalizedProperties", out var localized) || localized.ValueKind != JsonValueKind.Array) continue;
            foreach (var entry in localized.EnumerateArray())
            {
                var text = entry.TryGetProperty("ShortDescription", out var shortText) ? Plain(shortText.GetString() ?? "") : "";
                if (text.Length < 40 && entry.TryGetProperty("ProductDescription", out var longText))
                {
                    var paragraph = (longText.GetString() ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .FirstOrDefault(part => part.Length >= 40) ?? "";
                    text = Plain(paragraph);
                }
                if (text.Length > 0) return text.Length > 700 ? text[..text.LastIndexOf(' ', 700)].TrimEnd(',', ';', ' ') + "…" : text;
            }
        }
        return "";
    }

    private static readonly HashSet<string> ItalianWords =
        ["il", "lo", "la", "gli", "le", "di", "del", "della", "dei", "delle", "che", "e", "è", "un", "una", "per", "con", "nel", "nella", "sono", "suo", "sua", "dal", "alla", "al", "ad", "tra", "fra", "più", "anche", "come", "questo", "ogni"];

    private static readonly HashSet<string> EnglishWords =
        ["the", "of", "and", "to", "in", "is", "a", "an", "with", "for", "your", "you", "on", "from", "that", "this", "as", "are", "its", "by"];

    /// <summary>
    /// Whether a text is Italian, by its common short words. Steam and the Store fall back to English without saying so,
    /// so their answers are checked before they are shown as Italian.
    /// </summary>
    public static bool LooksItalian(string text)
    {
        var words = Regex.Matches(text.ToLowerInvariant(), @"\p{L}+").Select(match => match.Value).ToList();
        if (words.Count < 6) return false;
        var italian = words.Count(ItalianWords.Contains);
        var english = words.Count(EnglishWords.Contains);
        return italian >= 3 && italian > english * 2;
    }

    internal static string Plain(string text)
    {
        var withoutTags = Regex.Replace(text, "<[^>]+>", " ");
        return Regex.Replace(WebUtility.HtmlDecode(withoutTags), @"\s+", " ").Trim();
    }
}

/// <summary>Finds localized descriptions across platforms through Wikipedia, with Steam as an English fallback.</summary>
public sealed class DescriptionFinder
{
    private readonly HttpClient _client;

    public DescriptionFinder(HttpClient client) => _client = client;

    public DescriptionFinder(string version)
    {
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd($"c0n50le/{version} (+https://github.com/1r0n51uv/c0n50le)");
    }

    public static (string Steam, string Wikipedia, string Country) Codes(string language) =>
        language.StartsWith("it", StringComparison.OrdinalIgnoreCase) ? ("italian", "it", "IT") : ("english", "en", "US");

    public async Task<string> FindAsync(string title, uint? steamId, string language, CancellationToken token)
    {
        return (await FindDescriptionAsync(title, steamId, language, token)).Text;
    }

    /// <summary>Searches by game title, independently of its launcher or console platform.</summary>
    public async Task<LocalizedDescription> FindDescriptionAsync(string title, uint? steamId, string language, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(title)) return new();
        var (steam, wiki, country) = Codes(language);
        // The local encyclopedia covers console exclusives as well as PC games.
        try
        {
            var keyword = wiki == "it" ? "videogioco" : "video game";
            var search = await _client.GetStringAsync(
                $"https://{wiki}.wikipedia.org/w/api.php?action=query&list=search&format=json&srlimit=10&srsearch={Uri.EscapeDataString(title + " " + keyword)}", token);
            if (DescriptionParser.ParseWikipediaSearch(search, title) is { } page)
            {
                var summary = await _client.GetStringAsync(
                    $"https://{wiki}.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(page.Replace(' ', '_'))}", token);
                var text = DescriptionParser.ParseWikipediaSummary(summary, language);
                if (text.Length > 0) return new(text,
                    $"https://{wiki}.wikipedia.org/wiki/{Uri.EscapeDataString(page.Replace(' ', '_'))}", "Wikipedia · CC BY-SA");
            }
        }
        catch (Exception ex) when (Recoverable(ex, token)) { }

        // Not found in the local Wikipedia by name: find the English article and follow its link to this language.
        if (wiki != "en" && await FromEnglishWikipediaAsync(title, wiki, language, token) is { } linked) return linked;

        // Steam and the Store may silently answer in English when Italian is asked: their text is checked first.
        bool Fits(string text) => text.Length > 0 && (wiki == "en" || DescriptionParser.LooksItalian(text));
        token.ThrowIfCancellationRequested();
        try
        {
            var appId = steamId ?? DescriptionParser.ParseSteamSearch(await _client.GetStringAsync(
                $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(title)}&l={steam}&cc={country}", token), title);
            if (appId is { } id)
            {
                var json = await _client.GetStringAsync(
                    $"https://store.steampowered.com/api/appdetails?appids={id}&l={steam}&cc={country}", token);
                var text = DescriptionParser.ParseSteamDetails(json, id);
                if (Fits(text)) return new(text, $"https://store.steampowered.com/app/{id}/", "Steam");
            }
        }
        catch (Exception ex) when (Recoverable(ex, token)) { }

        // The Microsoft Store has Xbox and PC games, also ones that are not on Steam.
        token.ThrowIfCancellationRequested();
        try
        {
            var locale = wiki == "it" ? "it-it" : "en-us";
            var suggest = await _client.GetStringAsync(
                $"https://displaycatalog.mp.microsoft.com/v7.0/productFamilies/autosuggest?market={country}&languages={locale}&productFamilyNames=Games&query={Uri.EscapeDataString(title)}", token);
            if (DescriptionParser.ParseStoreSuggest(suggest, title) is { } product)
            {
                var json = await _client.GetStringAsync(
                    $"https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds={product}&market={country}&languages={locale}", token);
                var text = DescriptionParser.ParseStoreProduct(json);
                if (Fits(text)) return new(text, $"https://www.xbox.com/{locale}/games/store/p/{product}", "Microsoft Store");
            }
        }
        catch (Exception ex) when (Recoverable(ex, token)) { }
        token.ThrowIfCancellationRequested();
        return new();
    }

    private async Task<LocalizedDescription?> FromEnglishWikipediaAsync(string title, string wiki, string language, CancellationToken token)
    {
        try
        {
            var search = await _client.GetStringAsync(
                $"https://en.wikipedia.org/w/api.php?action=query&list=search&format=json&srlimit=10&srsearch={Uri.EscapeDataString(title + " video game")}", token);
            if (DescriptionParser.ParseWikipediaSearch(search, title) is not { } english) return null;
            var links = await _client.GetStringAsync(
                $"https://en.wikipedia.org/w/api.php?action=query&prop=langlinks&format=json&redirects=1&lllang={wiki}&titles={Uri.EscapeDataString(english)}", token);
            if (DescriptionParser.ParseLangLink(links, wiki) is not { } page) return null;
            var summary = await _client.GetStringAsync(
                $"https://{wiki}.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(page.Replace(' ', '_'))}", token);
            var text = DescriptionParser.ParseWikipediaSummary(summary, language);
            return text.Length > 0
                ? new(text, $"https://{wiki}.wikipedia.org/wiki/{Uri.EscapeDataString(page.Replace(' ', '_'))}", "Wikipedia · CC BY-SA")
                : null;
        }
        catch (Exception ex) when (Recoverable(ex, token)) { return null; }
    }

    private static bool Recoverable(Exception ex, CancellationToken token) => !token.IsCancellationRequested
        && ex is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException or FormatException;
}

public sealed record LocalizedDescription(string Text = "", string SourceUrl = "", string SourceName = "");
