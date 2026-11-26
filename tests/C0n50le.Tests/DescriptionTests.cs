using System.Net;
using System.Text.Json;
using C0n50le.Core.Catalog;
using C0n50le.Core.Library;

namespace C0n50le.Tests;

public sealed class DescriptionTests
{
    [Theory]
    [InlineData("Bloodborne")]
    [InlineData("Halo: Combat Evolved")]
    [InlineData("The Legend of Zelda: Breath of the Wild")]
    [InlineData("Fortnite")]
    [InlineData("The Witcher 3: Wild Hunt")]
    [InlineData("The Sims 4")]
    public async Task Italian_description_does_not_require_a_Steam_id_or_a_specific_launcher(string title)
    {
        var text = title + " è un videogioco di avventura.";
        var handler = new Replies(
            JsonSerializer.Serialize(new { query = new { search = new[] { new { title } } } }),
            JsonSerializer.Serialize(new { extract = text, type = "standard" }));
        using var client = new HttpClient(handler);
        var result = await new DescriptionFinder(client).FindDescriptionAsync(title, null, "it-IT", default);
        Assert.Equal(text, result.Text);
        Assert.StartsWith("https://it.wikipedia.org/wiki/", result.SourceUrl);
        Assert.All(handler.Urls, uri => Assert.Equal("it.wikipedia.org", uri.Host));
        Assert.Equal(2, handler.Urls.Count);
    }

    [Fact]
    public void Search_does_not_confuse_a_sequel_or_film_with_the_game()
    {
        const string json = """{"query":{"search":[{"title":"Control 2"},{"title":"Control (film)"},{"title":"Control (videogioco)"}]}}""";
        Assert.Equal("Control (videogioco)", DescriptionParser.ParseWikipediaSearch(json, "Control"));
        Assert.Null(DescriptionParser.ParseWikipediaSearch("""{"query":{"search":[{"title":"Control 2"}]}}""", "Control"));
        Assert.Empty(DescriptionParser.ParseWikipediaSummary("""{"extract":"Control è un film."}""", "it-IT"));
        Assert.Empty(DescriptionParser.ParseWikipediaSummary("""{"type":"disambiguation","extract":"Un videogioco o un film."}""", "it-IT"));
    }

    [Fact]
    public async Task Missing_Italian_text_does_not_fall_back_to_English()
    {
        var handler = new Replies("""{"query":{"search":[]}}""");
        using var client = new HttpClient(handler);
        Assert.Empty(await new DescriptionFinder(client).FindAsync("Unknown game", 123, "it-IT", default));
        Assert.Single(handler.Urls);
    }

    [Fact]
    public async Task Network_failure_is_optional_but_cancellation_is_preserved()
    {
        using var client = new HttpClient(new Replies());
        var finder = new DescriptionFinder(client);
        Assert.Empty(await finder.FindAsync("Halo", null, "it-IT", default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => finder.FindAsync("Halo", null, "it-IT", cancellation.Token));
    }

    [Fact]
    public void Localized_cache_keeps_language_title_and_attribution_across_save()
    {
        var art = new Artwork();
        art.Descriptions["it-IT|Halo"] = new("Un videogioco", "https://it.wikipedia.org/wiki/Halo", "Wikipedia");
        art.Descriptions["en-US|Halo"] = new("A video game");
        var loaded = JsonSerializer.Deserialize<Artwork>(JsonSerializer.Serialize(art))!;
        Assert.Equal("Un videogioco", loaded.Descriptions["it-IT|Halo"].Text);
        Assert.Equal("Wikipedia", loaded.Descriptions["it-IT|Halo"].SourceName);
        Assert.Equal("A video game", loaded.Descriptions["en-US|Halo"].Text);
        Assert.Empty(JsonSerializer.Deserialize<Artwork>("{}")!.Descriptions);
    }

    private sealed class Replies(params string[] replies) : HttpMessageHandler
    {
        private readonly Queue<string> _replies = new(replies);
        public List<Uri> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!);
            if (_replies.Count == 0) throw new HttpRequestException("Offline");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_replies.Dequeue()) });
        }
    }
}
