using C0n50le.Core.Settings;
using C0n50le.Core.Widgets;

namespace C0n50le.Tests;

public sealed class WidgetTests
{
    [Fact]
    public void Geocoding_takes_the_first_place()
    {
        const string json = """{"results":[{"name":"Milano","country":"Italia","latitude":45.46,"longitude":9.19},{"name":"Milano","latitude":1,"longitude":2}]}""";
        var place = WeatherClient.ParsePlace(json);
        Assert.Equal(new WeatherPlace("Milano", "Italia", 45.46, 9.19), place);
        Assert.Null(WeatherClient.ParsePlace("""{"generationtime_ms":0.5}"""));
    }

    [Fact]
    public void Forecast_reads_now_low_high_and_the_sky()
    {
        const string json = """
            {"current":{"time":"2026-11-27T10:00","temperature_2m":7.4,"weather_code":61,"is_day":1},
             "daily":{"time":["2026-11-27"],"temperature_2m_max":[9.8],"temperature_2m_min":[3.1]}}
            """;
        var report = WeatherClient.ParseForecast(json, "Milano");
        Assert.NotNull(report);
        Assert.Equal(7.4, report.Temperature);
        Assert.Equal(3.1, report.Low);
        Assert.Equal(9.8, report.High);
        Assert.Equal(WeatherKind.Rain, report.Kind);
        Assert.True(report.IsDay);
        Assert.Null(WeatherClient.ParseForecast("{}", "Milano"));
    }

    [Theory]
    [InlineData(0, WeatherKind.Clear)]
    [InlineData(2, WeatherKind.PartlyCloudy)]
    [InlineData(3, WeatherKind.Cloudy)]
    [InlineData(48, WeatherKind.Fog)]
    [InlineData(53, WeatherKind.Drizzle)]
    [InlineData(65, WeatherKind.Rain)]
    [InlineData(75, WeatherKind.Snow)]
    [InlineData(86, WeatherKind.Snow)]
    [InlineData(81, WeatherKind.Showers)]
    [InlineData(99, WeatherKind.Thunderstorm)]
    public void Wmo_codes_map_to_icons(int code, WeatherKind kind) => Assert.Equal(kind, WeatherClient.Kind(code));

    [Fact]
    public void Rss_headlines_are_plain_text()
    {
        const string xml = """
            <?xml version="1.0"?>
            <rss version="2.0"><channel><title>ANSA &amp; co</title>
              <item><title><![CDATA[<b>Prima</b>   notizia]]></title><link>https://example.com/1</link><pubDate>Tue, 10 Jun 2025 04:00:00 GMT</pubDate></item>
              <item><title>Seconda &#232; qui</title><link> https://example.com/2 </link></item>
              <item><title> </title></item>
              <item><title>Terza</title></item>
            </channel></rss>
            """;
        var feed = News.Parse(xml, 2);
        Assert.Equal("ANSA & co", feed.Source);
        Assert.Equal(["Prima notizia", "Seconda è qui"], feed.Items.Select(item => item.Title));
        Assert.Equal("https://example.com/2", feed.Items[1].Link);
        Assert.Equal(new DateTimeOffset(2025, 6, 10, 4, 0, 0, TimeSpan.Zero), feed.Items[0].Published);
    }

    [Fact]
    public void Atom_entries_are_read()
    {
        const string xml = """
            <feed xmlns="http://www.w3.org/2005/Atom"><title>The Verge</title>
              <entry><title type="html">Uno</title><link rel="alternate" href="https://example.com/a"/><published>2026-11-26T08:00:00Z</published></entry>
              <entry><title>Due</title><updated>2026-11-25T08:00:00Z</updated></entry>
            </feed>
            """;
        var feed = News.Parse(xml, 5);
        Assert.Equal("The Verge", feed.Source);
        Assert.Equal(["Uno", "Due"], feed.Items.Select(item => item.Title));
        Assert.Equal("https://example.com/a", feed.Items[0].Link);
    }

    [Fact]
    public void Rss_1_items_sit_beside_the_channel()
    {
        const string xml = """
            <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns="http://purl.org/rss/1.0/">
              <channel><title>Feed</title></channel>
              <item><title>Una</title><link>https://example.com</link></item>
            </rdf:RDF>
            """;
        var feed = News.Parse(xml, 5);
        Assert.Equal("Feed", feed.Source);
        Assert.Single(feed.Items);
    }

    [Fact]
    public void Automatic_feed_follows_the_language()
    {
        Assert.Equal(News.Presets[0].Url, News.Resolve("", "it-IT"));
        Assert.Equal(News.Presets[2].Url, News.Resolve("", "en-US"));
        Assert.Equal("https://x.test/feed", News.Resolve("https://x.test/feed", "it-IT"));
        Assert.False(News.IsValidUrl("ftp://x.test"));
        Assert.False(News.IsValidUrl("not a url"));
    }

    [Fact]
    public void Gallery_loops_over_the_pictures_of_a_folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "c0n50le-gallery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        try
        {
            File.WriteAllText(Path.Combine(folder, "a.jpg"), "");
            File.WriteAllText(Path.Combine(folder, "sub", "b.PNG"), "");
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "");
            var gallery = Gallery.Load(folder, new Random(1));
            Assert.Equal(2, gallery.Count);
            var seen = new[] { gallery.Next(), gallery.Next(), gallery.Next() };
            Assert.Equal(seen[0], seen[2]);
            Assert.NotEqual(seen[0], seen[1]);
            Assert.Null(Gallery.Load(Path.Combine(folder, "missing")).Next());
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Widget_preferences_are_repaired()
    {
        var preferences = new Preferences { WeatherCity = "  Roma ", NewsFeed = "javascript:alert(1)", GalleryFolder = null! };
        preferences.Normalize();
        Assert.Equal("Roma", preferences.WeatherCity);
        Assert.Equal("", preferences.NewsFeed);
        Assert.Equal("", preferences.GalleryFolder);
        Assert.True(new Preferences().ShowWidgets);
    }

    [Fact]
    public void Gallery_shows_up_to_four_different_pictures_at_once()
    {
        var gallery = new Gallery(["a", "b", "c", "d", "e", "f"]);
        Assert.Equal(["a", "b", "c", "d"], gallery.NextBatch());
        Assert.Equal(["e", "f", "a", "b"], gallery.NextBatch());
        Assert.Equal(["x", "y"], new Gallery(["x", "y"]).NextBatch());
        Assert.Empty(new Gallery([]).NextBatch());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Gallery_layout_fills_the_two_by_two_grid_without_overlaps(int count)
    {
        var cells = Gallery.Layout(count);
        Assert.Equal(count, cells.Count);
        var covered = cells.SelectMany(cell => from row in Enumerable.Range(cell.Row, cell.RowSpan)
                                               from column in Enumerable.Range(cell.Column, cell.ColumnSpan)
                                               select (row, column)).ToList();
        Assert.Equal(4, covered.Count);
        Assert.Equal(4, covered.Distinct().Count());
        Assert.All(covered, place => Assert.True(place.row is >= 0 and < 2 && place.column is >= 0 and < 2));
    }
}