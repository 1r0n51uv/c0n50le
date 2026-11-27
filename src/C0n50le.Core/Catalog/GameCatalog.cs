namespace C0n50le.Core.Catalog;

/// <summary>Searches the LaunchBox Games Database and downloads covers and backgrounds.</summary>
public sealed class GameCatalog
{
    private const int MaximumImageBytes = 10 * 1024 * 1024;
    private readonly HttpClient _client;

    public GameCatalog(string version)
    {
        _client = new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(8) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd($"c0n50le/{version} (+https://github.com/1r0n51uv/c0n50le)");
    }

    public async Task<List<CatalogMatch>> SearchAsync(string title, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(title)) return [];
        var html = await _client.GetStringAsync(new Uri(LaunchBoxParser.Site, "/games/results/" + Uri.EscapeDataString(title.Trim())), token);
        var normalized = LaunchBoxParser.Normalize(title);
        return LaunchBoxParser.Collapse(LaunchBoxParser.ParseSearch(html))
            .OrderByDescending(match => LaunchBoxParser.Normalize(match.Title) == normalized).ToList();
    }

    /// <summary>Every picture in the LaunchBox gallery of an assigned title.</summary>
    public async Task<List<ArtCandidate>> GalleryAsync(Uri page, CancellationToken token = default)
    {
        if (!LaunchBoxParser.IsDetailsPage(page)) return [];
        var gallery = new Uri(LaunchBoxParser.Site, "/games/images/" + page.Segments[^1]);
        return ArtSources.ParseLaunchBoxGallery(await _client.GetStringAsync(gallery, token));
    }

    /// <summary>The Steam store art of the game: its own app id, else the store app whose name is the title.</summary>
    public async Task<List<ArtCandidate>> SteamArtAsync(uint? appId, string title, CancellationToken token = default)
    {
        appId ??= DescriptionParser.ParseSteamSearch(await _client.GetStringAsync(
            $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(title)}&l=english&cc=US", token), title);
        if (appId is not { } id) return [];
        string? details = null;
        try { details = await _client.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={id}&filters=screenshots", token); }
        catch (HttpRequestException) { }
        return ArtSources.ParseSteam(id, details);
    }

    public async Task<CatalogEntry> GetAsync(CatalogMatch match, string language, CancellationToken token = default)
    {
        if (!LaunchBoxParser.IsDetailsPage(match.Page)) throw new ArgumentException("Not a LaunchBox page.", nameof(match));
        var gallery = new Uri(LaunchBoxParser.Site, "/games/images/" + match.Page.Segments[^1]);
        var details = _client.GetStringAsync(match.Page, token);
        var images = _client.GetStringAsync(gallery, token);
        await Task.WhenAll(details, images);
        return LaunchBoxParser.ParseEntry(await details, await images, language);
    }

    /// <summary>Downloads a picture to a temporary file, or null when it isn't an allowed, reasonably sized image.</summary>
    public async Task<string?> DownloadAsync(Uri? uri, CancellationToken token)
    {
        if (!ArtSources.IsAllowed(uri)) return null;
        using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        var extension = response.Content.Headers.ContentType?.MediaType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => null
        };
        if (!response.IsSuccessStatusCode || extension is null || response.RequestMessage?.RequestUri is not { } final
            || !ArtSources.IsAllowed(final)
            || response.Content.Headers.ContentLength > MaximumImageBytes) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var contents = new MemoryStream();
        var buffer = new byte[65536];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) != 0)
        {
            if (contents.Length + count > MaximumImageBytes) return null;
            contents.Write(buffer, 0, count);
        }
        var path = Path.Combine(Path.GetTempPath(), "c0n50le-" + Guid.NewGuid().ToString("N") + extension);
        await File.WriteAllBytesAsync(path, contents.ToArray(), token);
        return path;
    }
}
