using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace C0n50le.Core.Widgets;

public sealed record NewsItem(string Title, string Link, DateTimeOffset? Published);

public sealed record NewsFeed(string Source, IReadOnlyList<NewsItem> Items);

/// <summary>Headlines from an RSS 2.0 or Atom feed chosen in Settings.</summary>
public static partial class News
{
    /// <summary>Ready-made feeds; "" picks the one for the interface language.</summary>
    public static readonly IReadOnlyList<(string Name, string Url)> Presets =
    [
        ("ANSA", "https://www.ansa.it/sito/notizie/topnews/topnews_rss.xml"),
        ("Il Post", "https://www.ilpost.it/feed/"),
        ("BBC News", "https://feeds.bbci.co.uk/news/rss.xml"),
        ("The Verge", "https://www.theverge.com/rss/index.xml"),
        ("Eurogamer", "https://www.eurogamer.net/feed")
    ];

    public static string Resolve(string feed, string language) =>
        feed.Length > 0 ? feed : language.StartsWith("it", StringComparison.OrdinalIgnoreCase) ? Presets[0].Url : Presets[2].Url;

    public static bool IsValidUrl(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";

    public static async Task<NewsFeed> GetAsync(HttpClient client, string url, int count, CancellationToken token) =>
        Parse(await client.GetStringAsync(url, token), count);

    public static NewsFeed Parse(string xml, int count)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        var document = XDocument.Load(reader);
        var root = document.Root ?? throw new FormatException("Empty feed");
        XNamespace atom = "http://www.w3.org/2005/Atom";
        if (root.Name == atom + "feed")
        {
            var entries = root.Elements(atom + "entry").Select(entry => new NewsItem(
                Clean((string?)entry.Element(atom + "title")),
                (string?)entry.Elements(atom + "link").FirstOrDefault(link => (string?)link.Attribute("rel") is null or "alternate")?.Attribute("href") ?? "",
                Date((string?)entry.Element(atom + "published") ?? (string?)entry.Element(atom + "updated"))));
            return new NewsFeed(Clean((string?)root.Element(atom + "title")), Take(entries, count));
        }
        static XElement? Child(XElement parent, string name) => parent.Elements().FirstOrDefault(e => e.Name.LocalName == name);
        var channel = Child(root, "channel") ?? root;
        var items = channel.Elements().Where(e => e.Name.LocalName == "item").ToList();
        if (items.Count == 0 && channel != root) items = root.Elements().Where(e => e.Name.LocalName == "item").ToList();
        var rss = items.Select(item => new NewsItem(
            Clean((string?)Child(item, "title")),
            ((string?)Child(item, "link") ?? "").Trim(),
            Date((string?)Child(item, "pubDate") ?? (string?)Child(item, "date"))));
        return new NewsFeed(Clean((string?)Child(channel, "title")), Take(rss, count));
    }

    private static List<NewsItem> Take(IEnumerable<NewsItem> items, int count) =>
        items.Where(item => item.Title.Length > 0).Take(Math.Max(0, count)).ToList();

    /// <summary>Headlines without markup, entities or runs of spaces.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var plain = WebUtility.HtmlDecode(Tags().Replace(text, " "));
        return Spaces().Replace(plain, " ").Trim();
    }

    private static DateTimeOffset? Date(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = text.Trim();
        if (DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)) return parsed;
        // RFC 822 with a zone name ("Tue, 10 Jun 2025 04:00:00 GMT"): drop the name and read it as UTC.
        var cut = value.LastIndexOf(' ');
        return cut > 0 && DateTimeOffset.TryParse(value[..cut], System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out parsed) ? parsed : null;
    }

    [GeneratedRegex("<[^>]+>")] private static partial Regex Tags();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
}
