using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace C0n50le.Core.Localization;

/// <summary>
/// The texts of the interface, from the catalogs Strings.it-IT.json and Strings.en-US.json (embedded). A key missing in
/// the current language falls back to English, then to the key itself, so a mistake shows instead of crashing.
/// </summary>
public static class Strings
{
    public const string Italian = "it-IT";
    public const string English = "en-US";
    public static IReadOnlyList<string> Languages { get; } = [Italian, English];

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Catalogs = [];
    private static IReadOnlyDictionary<string, string> _current = Catalog(English);
    private static readonly IReadOnlyDictionary<string, string> Fallback = Catalog(English);

    public static string Language { get; private set; } = English;

    public static event Action? Changed;

    /// <summary>The language to use: the chosen one, else Italian on an Italian Windows, else English.</summary>
    public static string Resolve(string? chosen, CultureInfo? ui = null)
    {
        if (Languages.Contains(chosen ?? "")) return chosen!;
        ui ??= CultureInfo.CurrentUICulture;
        return ui.TwoLetterISOLanguageName == "it" ? Italian : English;
    }

    public static void Use(string language)
    {
        var resolved = Languages.Contains(language) ? language : English;
        var changed = resolved != Language;
        Language = resolved;
        _current = Catalog(resolved);
        if (changed) Changed?.Invoke();
    }

    public static string Get(string key) =>
        _current.TryGetValue(key, out var value) ? value : Fallback.TryGetValue(key, out var english) ? english : key;

    public static string Format(string key, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), values);

    /// <summary>A whole catalog, read from the embedded file (the tests compare them).</summary>
    public static IReadOnlyDictionary<string, string> Catalog(string language)
    {
        lock (Catalogs)
        {
            if (Catalogs.TryGetValue(language, out var cached)) return cached;
            using var stream = typeof(Strings).Assembly.GetManifestResourceStream($"C0n50le.Strings.Strings.{language}.json")
                ?? throw new InvalidOperationException($"No catalog for {language}.");
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
            Catalogs[language] = values;
            return values;
        }
    }

    /// <summary>The {0}, {1}… placeholders a text uses, to check translations keep them.</summary>
    public static IReadOnlySet<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{\d+(?:[:,][^}]*)?\}").Select(match => Regex.Match(match.Value, @"\d+").Value).ToHashSet();
}
