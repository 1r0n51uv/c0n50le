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
/// A game's facts under its title, as plain secondary text dot-separated like before (wrapping when long), led by the
/// age rating as a PEGI-coloured square (green, amber, red).
/// </summary>
public static class GameBadges
{
    /// <summary>The rating square (when rated) and the facts line, for a panel laid out horizontally or vertically.</summary>
    public static List<FrameworkElement> For(Game game, Artwork? art, string source, double size, double maxWidth)
    {
        var info = art?.IsMatched == true ? art.Info ?? new GameInfo() : new GameInfo();
        var result = new List<FrameworkElement>();
        if (Pegi(info.Rating) is { } pegi) result.Add(Rating(pegi, size));
        var facts = Facts(game, art, source);
        if (facts.Length > 0)
            result.Add(new TextBlock
            {
                Text = facts, FontSize = size, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap, MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = maxWidth - (result.Count > 0 ? size * 2.6 : 0), Foreground = ThemeManager.Brush("TextSecondaryBrush")
            });
        return result;
    }

    /// <summary>Where it comes from, genre, year, platform, developer and players, dot-separated.</summary>
    public static string Facts(Game game, Artwork? art, string source)
    {
        var parts = new List<string> { source };
        var info = art?.IsMatched == true ? art.Info ?? new GameInfo() : new GameInfo();
        if (info.IsEmpty)
        {
            // A game matched before the facts were kept still has its plain details.
            if (art?.IsMatched == true) parts.AddRange(art.Details.Where(detail => detail.Length > 0));
        }
        else
        {
            parts.Add(info.Genre);
            parts.Add(info.Year);
            parts.Add(info.Platform.Length > 0 ? ShortPlatform(info.Platform) : "");
            parts.Add(info.Developer);
            if (int.TryParse(info.Players, out var players) && players > 0)
                parts.Add(players == 1 ? Strings.Get("OnePlayer") : Strings.Format("UpToPlayers", players));
        }
        return string.Join("  ·  ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
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
            Width = size * 1.9, Height = size * 1.9, CornerRadius = new CornerRadius(size * 0.35), Margin = new Thickness(0, 0, size * 0.6, 0),
            Background = new SolidColorBrush(PegiColor(age)), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
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

    /// <summary>"Sony Playstation 5" → "PlayStation 5", "Microsoft Xbox Series X/S" → "Xbox Series X/S", "Windows" → "PC".</summary>
    public static string ShortPlatform(string platform)
    {
        var name = platform.Trim();
        foreach (var maker in new[] { "Sony ", "Microsoft " })
            if (name.StartsWith(maker, StringComparison.OrdinalIgnoreCase)) name = name[maker.Length..];
        return name.Replace("Playstation", "PlayStation", StringComparison.Ordinal).Replace("Windows", "PC", StringComparison.Ordinal);
    }
}
