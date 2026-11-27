using System.Globalization;

namespace C0n50le.Core.Theming;

/// <summary>How the app bar names a screen: short, so the bar stays compact.</summary>
public static class DisplayText
{
    /// <summary>"4K", "1440p", "1080p"… or the exact size when it is not a common one.</summary>
    public static string Resolution(int width, int height) => (width, height) switch
    {
        ( >= 7680, >= 4320) => "8K",
        ( >= 3840, >= 2160) => "4K",
        (_, 1440) => "1440p",
        (_, 1080) => "1080p",
        (_, 720) => "720p",
        _ => $"{width}×{height}"
    };

    public static string Label(string name, int width, int height, double hertz)
    {
        var parts = new List<string> { name.Length > 40 ? name[..39].TrimEnd() + "…" : name };
        if (width > 0 && height > 0) parts.Add(Resolution(width, height));
        if (hertz >= 1) parts.Add(Math.Round(hertz).ToString("0", CultureInfo.InvariantCulture) + " Hz");
        return string.Join(" · ", parts);
    }
}
