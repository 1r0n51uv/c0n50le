using C0n50le.Core.Catalog;
using C0n50le.Core.Library;
using C0n50le.Core.Localization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace C0n50le.Ui;

/// <summary>
/// A game's facts under its title: the age rating as a PEGI-coloured square (green, amber, red), then where it comes from,
/// genre, year, platform, developer and players as plain secondary text with an icon each, dot-separated.
/// </summary>
public static class GameBadges
{
    /// <summary>The badges for a game; <paramref name="source"/> is the label of where it was found.</summary>
    public static List<FrameworkElement> For(Game game, Artwork? art, string source, double size = 22)
    {
        var info = art?.IsMatched == true ? art.Info ?? new GameInfo() : new GameInfo();
        var badges = new List<FrameworkElement>();
        if (Pegi(info.Rating) is { } pegi) badges.Add(Rating(pegi, size));
        badges.Add(Chip(SourceGlyph(game.Source), source, size));
        if (info.Genre.Length > 0) badges.Add(Chip("", info.Genre, size, GenreColor(info.Genre)));
        if (info.Year.Length > 0) badges.Add(Chip("", info.Year, size));
        if (info.Platform.Length > 0) badges.Add(Chip(PlatformGlyph(info.Platform), ShortPlatform(info.Platform), size));
        if (info.Developer.Length > 0) badges.Add(Chip("", FirstName(info.Developer), size));
        if (int.TryParse(info.Players, out var players) && players > 0)
            badges.Add(Chip("", players == 1 ? Strings.Get("OnePlayer") : Strings.Format("UpToPlayers", players), size));
        // A game matched before the badges existed still has its plain facts: show those as chips.
        if (info.IsEmpty && art?.IsMatched == true)
            badges.AddRange(art.Details.Where(detail => detail.Length > 0).Select(detail => Chip("", detail, size)));
        // The rating square leads; the facts follow on one line, dot-separated as before.
        var line = new List<FrameworkElement>();
        foreach (var badge in badges)
        {
            if (badge is Border) { line.Add(badge); continue; }
            if (line.Count > 0 && line[^1] is not Border) line.Add(Dot(size));
            line.Add(badge);
        }
        return line;
    }

    /// <summary>The PEGI age for an ESRB rating (the system used in Europe), or null when unrated.</summary>
    public static int? Pegi(string? esrb)
    {
        var code = (esrb ?? "").Split(' ', '-')[0].Trim().ToUpperInvariant();
        return code switch
        {
            "EC" or "E" => 3,
            "E10+" => 7,
            "T" => 12,
            "M" => 16,
            "AO" => 18,
            _ => null
        };
    }

    /// <summary>PEGI's own colours: green for 3 and 7, amber for 12 and 16, red for 18.</summary>
    public static Color PegiColor(int age) => age switch
    {
        <= 7 => Color.FromArgb(0xFF, 0x4C, 0xAF, 0x50),
        <= 16 => Color.FromArgb(0xFF, 0xF2, 0x9D, 0x12),
        _ => Color.FromArgb(0xFF, 0xE5, 0x39, 0x35)
    };

    private static FrameworkElement Rating(int age, double size)
    {
        var square = new Border
        {
            Width = size * 1.9, Height = size * 1.9, CornerRadius = new CornerRadius(size * 0.35),
            Background = new SolidColorBrush(PegiColor(age)), VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = -2,
                Children =
                {
                    new TextBlock
                    {
                        Text = age.ToString(), FontSize = size * 0.95, FontWeight = FontWeights.Black, HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                    },
                    new TextBlock
                    {
                        Text = "PEGI", FontSize = size * 0.38, FontWeight = FontWeights.Bold, CharacterSpacing = 40,
                        HorizontalAlignment = HorizontalAlignment.Center, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                    }
                }
            }
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(square, $"PEGI {age}");
        return square;
    }

    /// <summary>
    /// One fact as before, plain secondary text, now with its icon in front; a dot separates it from the previous one
    /// (the caller adds the separators).
    /// </summary>
    private static FrameworkElement Chip(string glyph, string text, double size, Color? tint = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = size * 0.35, VerticalAlignment = VerticalAlignment.Center };
        if (glyph.Length > 0)
            row.Children.Add(new FontIcon
            {
                Glyph = glyph, FontSize = size * 0.8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, size * 0.08, 0, 0),
                Foreground = ThemeManager.Brush("TextSecondaryBrush")
            });
        row.Children.Add(new TextBlock
        {
            Text = text, FontSize = size, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = size * 16, Foreground = ThemeManager.Brush("TextSecondaryBrush")
        });
        return row;
    }

    private static FrameworkElement Dot(double size) => new TextBlock
    {
        Text = "·", FontSize = size, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center,
        Foreground = ThemeManager.Brush("TextSecondaryBrush")
    };
    /// <summary>Each genre keeps one colour, from a small palette that reads on both themes.</summary>
    public static Color GenreColor(string genre)
    {
        Color[] palette =
        [
            Color.FromArgb(0xFF, 0x5E, 0x9E, 0xFF), Color.FromArgb(0xFF, 0xB3, 0x7F, 0xEB), Color.FromArgb(0xFF, 0x3D, 0xC2, 0x8D),
            Color.FromArgb(0xFF, 0xFF, 0x8A, 0x4C), Color.FromArgb(0xFF, 0xFF, 0x5C, 0x8A), Color.FromArgb(0xFF, 0x2E, 0xBF, 0xD1),
            Color.FromArgb(0xFF, 0xE6, 0xB8, 0x00)
        ];
        var hash = 0;
        foreach (var letter in genre.ToLowerInvariant()) hash = unchecked(hash * 31 + letter);
        return palette[(hash & 0x7FFFFFFF) % palette.Length];
    }

    private static string SourceGlyph(string source) => source switch
    {
        GameSource.Steam or GameSource.Epic => "",
        GameSource.Folder => "",
        GameSource.Preset => "",
        _ => ""
    };

    private static string PlatformGlyph(string platform) =>
        platform.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "" : "";

    /// <summary>"Sony Playstation 5" → "PlayStation 5", "Microsoft Xbox Series X/S" → "Xbox Series X/S".</summary>
    public static string ShortPlatform(string platform)
    {
        var name = platform.Trim();
        foreach (var maker in new[] { "Sony ", "Microsoft ", "Nintendo " })
            if (name.StartsWith(maker, StringComparison.OrdinalIgnoreCase) && !name.Equals("Nintendo Switch", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("Nintendo Switch", StringComparison.OrdinalIgnoreCase))
                name = name[maker.Length..];
        return name.Replace("Playstation", "PlayStation", StringComparison.Ordinal).Replace("Windows", "PC", StringComparison.Ordinal);
    }

    /// <summary>The first of several developers ("Game Freak Omega Force…" keeps its first studio when LaunchBox separates them).</summary>
    private static string FirstName(string names) => names.Split([',', ';', '/'], StringSplitOptions.RemoveEmptyEntries)[0].Trim();
}
