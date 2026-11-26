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
        var marker = language.StartsWith("it", StringComparison.OrdinalIgnoreCase) ? "videogioco" : "video game";
        return text.Contains(marker, StringComparison.OrdinalIgnoreCase) ? text : "";
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

        // Steam may silently return English when Italian is requested, so it is not an Italian fallback.
        token.ThrowIfCancellationRequested();
        if (wiki != "en") return new();
        try
        {
            var appId = steamId ?? DescriptionParser.ParseSteamSearch(await _client.GetStringAsync(
                $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(title)}&l={steam}&cc={country}", token), title);
            if (appId is { } id)
            {
                var json = await _client.GetStringAsync(
                    $"https://store.steampowered.com/api/appdetails?appids={id}&l={steam}", token);
                var text = DescriptionParser.ParseSteamDetails(json, id);
                if (text.Length > 0) return new(text, $"https://store.steampowered.com/app/{id}/", "Steam");
            }
        }
        catch (Exception ex) when (Recoverable(ex, token)) { }
        token.ThrowIfCancellationRequested();
        return new();
    }

    private static bool Recoverable(Exception ex, CancellationToken token) => !token.IsCancellationRequested
        && ex is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException or FormatException;
}

public sealed record LocalizedDescription(string Text = "", string SourceUrl = "", string SourceName = "");
