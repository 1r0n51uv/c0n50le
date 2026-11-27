using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace C0n50le.Core.Catalog;

/// <summary>What a picture is for: the card in the dock, or one of the backgrounds behind the home.</summary>
public enum ArtUse { Card, Background, Logo }

/// <summary>A picture offered in a game's detail page, from a catalog or a store, before it is downloaded.</summary>
public sealed record ArtCandidate(Uri Url, string Source, string Label, int Width, int Height)
{
    public double Aspect => Height > 0 ? (double)Width / Height : 0;
}

/// <summary>
/// The pictures a game can use, gathered from several sources (the LaunchBox gallery of the assigned title and the
/// Steam store) and filtered by what they are for. Pure, so it is tested on saved pages.
/// </summary>
public static class ArtSources
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(1);

    private static readonly HashSet<string> SteamHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "cdn.cloudflare.steamstatic.com", "shared.cloudflare.steamstatic.com", "cdn.akamai.steamstatic.com",
        "shared.akamai.steamstatic.com", "shared.fastly.steamstatic.com", "cdn.fastly.steamstatic.com",
        "steamcdn-a.akamaihd.net", "media.steampowered.com"
    };

    /// <summary>Only https pictures from the catalog's image host and Steam's CDN are downloaded.</summary>
    public static bool IsAllowed(Uri? uri) =>
        uri is { Scheme: "https" } && (uri.Host == "images.launchbox-app.com" || SteamHosts.Contains(uri.Host));

    /// <summary>Every picture of a LaunchBox gallery page, with its type ("Fanart - Background", "Box - Front"…).</summary>
    public static List<ArtCandidate> ParseLaunchBoxGallery(string gallery)
    {
        var images = Regex.Matches(gallery,
            """<a\b[^>]*href="(https://images\.launchbox-app\.com/[^"]+)"[^>]*?data-title="([^"]+)"[^>]*?data-footer="(\d+) x (\d+)\b""",
            RegexOptions.IgnoreCase, Limit);
        var result = new List<ArtCandidate>();
        foreach (Match image in images)
        {
            if (!Uri.TryCreate(WebUtility.HtmlDecode(image.Groups[1].Value), UriKind.Absolute, out var url) || !IsAllowed(url)) continue;
            if (!int.TryParse(image.Groups[3].Value, out var width) || !int.TryParse(image.Groups[4].Value, out var height)) continue;
            result.Add(new ArtCandidate(url, "LaunchBox", Kind(WebUtility.HtmlDecode(image.Groups[2].Value)), width, height));
        }
        return result.DistinctBy(candidate => candidate.Url).ToList();
    }

    /// <summary>"Game - Fanart - Background Image (Europe)" → "Fanart - Background".</summary>
    private static string Kind(string title)
    {
        var match = Regex.Match(title, @" - (.+?) Image\b", RegexOptions.None, Limit);
        return match.Success ? match.Groups[1].Value : title;
    }

    /// <summary>A Steam app's store art: its fixed library pictures and the screenshots listed by appdetails.</summary>
    public static List<ArtCandidate> ParseSteam(uint appId, string? appDetails)
    {
        var cdn = $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{appId}/";
        var result = new List<ArtCandidate>
        {
            new(new Uri(cdn + "library_hero.jpg"), "Steam", "Library Hero", 3840, 1240),
            new(new Uri(cdn + "header.jpg"), "Steam", "Header", 920, 430),
            new(new Uri(cdn + "capsule_616x353.jpg"), "Steam", "Capsule", 616, 353),
            new(new Uri(cdn + "library_600x900.jpg"), "Steam", "Library Capsule", 600, 900),
            new(new Uri(cdn + "logo.png"), "Steam", "Library Logo", 1280, 720)
        };
        if (string.IsNullOrWhiteSpace(appDetails)) return result;
        try
        {
            using var document = JsonDocument.Parse(appDetails);
            if (!document.RootElement.TryGetProperty(appId.ToString(), out var app) || !app.TryGetProperty("data", out var data)) return result;
            if (data.TryGetProperty("screenshots", out var shots) && shots.ValueKind == JsonValueKind.Array)
                foreach (var shot in shots.EnumerateArray().Take(12))
                    if (shot.TryGetProperty("path_full", out var path) && Uri.TryCreate(path.GetString(), UriKind.Absolute, out var uri)
                        && IsAllowed(uri))
                        result.Add(new ArtCandidate(uri, "Steam", "Screenshot", 1920, 1080));
        }
        catch (JsonException) { }
        return result;
    }

    /// <summary>
    /// The pictures that suit a use. A card is wide (250×150): banners, headers, wide box art and fanart. A background
    /// fills the screen: wide fanart, heroes and screenshots of a decent size.
    /// </summary>
    public static List<ArtCandidate> For(ArtUse use, IEnumerable<ArtCandidate> candidates) => use switch
    {
        // A logo is a transparent title: LaunchBox's clear logos (worldwide first), then Steam's library logo.
        ArtUse.Logo => candidates.Where(IsLogo)
            .OrderBy(candidate => candidate.Source == "Steam" ? 1 : 0)
            .ThenByDescending(candidate => candidate.Label.Contains("World", StringComparison.OrdinalIgnoreCase)
                || candidate.Label.Contains("North America", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(candidate => candidate.Width).ToList(),
        ArtUse.Card => candidates
            .Where(candidate => candidate.Aspect >= 1.2 && candidate.Width >= 400 && !IsLogo(candidate))
            .OrderBy(candidate => CardRank(candidate)).ThenByDescending(candidate => candidate.Width).ToList(),
        _ => candidates
            .Where(candidate => candidate.Aspect >= 1.4 && candidate.Width >= 1200 && !IsLogo(candidate)
                && !candidate.Label.Contains("Banner", StringComparison.OrdinalIgnoreCase)
                && !candidate.Label.Contains("Header", StringComparison.OrdinalIgnoreCase)
                && !candidate.Label.Contains("Capsule", StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => BackgroundRank(candidate)).ThenByDescending(candidate => candidate.Width).ToList()
    };

    private static bool IsLogo(ArtCandidate candidate) => candidate.Label.Contains("Logo", StringComparison.OrdinalIgnoreCase);

    private static int CardRank(ArtCandidate candidate) =>
        candidate.Label.Contains("Banner", StringComparison.OrdinalIgnoreCase) || candidate.Label is "Header" or "Capsule" ? 0
        : candidate.Label.Contains("Fanart", StringComparison.OrdinalIgnoreCase) || candidate.Label == "Library Hero" ? 1
        : 2;

    private static int BackgroundRank(ArtCandidate candidate) =>
        candidate.Label.Contains("Fanart", StringComparison.OrdinalIgnoreCase) || candidate.Label == "Library Hero" ? 0
        : candidate.Label.Contains("Screenshot", StringComparison.OrdinalIgnoreCase) ? 1
        : 2;
}
