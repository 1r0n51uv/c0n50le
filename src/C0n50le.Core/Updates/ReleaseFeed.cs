using System.Text.Json;
using System.Text.RegularExpressions;

namespace C0n50le.Core.Updates;

/// <summary>A newer release to install: its tag, the asset for this kind of install and the asset's SHA-256.</summary>
public sealed record UpdateOffer(string Tag, Uri Asset, string Sha256);

/// <summary>A version as c0n50le numbers them: 0.1.0, 0.2.0-beta.1, 1.0.0-alpha.2.</summary>
public readonly record struct ReleaseVersion(Version Number, int Level, int Serial) : IComparable<ReleaseVersion>
{
    public const int Alpha = 0, Beta = 1, Stable = 2;

    public bool IsStable => Level == Stable;

    public static ReleaseVersion? Parse(string? text)
    {
        var value = (text ?? "").Trim().TrimStart('v').Split('+')[0];
        var match = Regex.Match(value, @"^(\d+\.\d+\.\d+)(?:-(alpha|beta)\.(\d+))?$");
        if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var version)) return null;
        var level = match.Groups[2].Value switch { "alpha" => Alpha, "beta" => Beta, _ => Stable };
        return new ReleaseVersion(version, level, int.TryParse(match.Groups[3].Value, out var serial) ? serial : 0);
    }

    public int CompareTo(ReleaseVersion other)
    {
        var number = Number.CompareTo(other.Number);
        if (number != 0) return number;
        var level = Level.CompareTo(other.Level);
        return level != 0 ? level : Serial.CompareTo(other.Serial);
    }
}

/// <summary>
/// Picks the update from GitHub's release list. Pure, so it is tested on saved answers; the app downloads the list and
/// the asset, and checks the asset's SHA-256 before running it.
/// </summary>
public static class ReleaseFeed
{
    public const string Repository = "1r0n51uv/c0n50le";
    public const string SetupAsset = "C0n50le-Setup-x64.exe";
    public const string PortableAsset = "C0n50le-Portable-x64.exe";

    public static string ReleasesUrl => $"https://api.github.com/repos/{Repository}/releases?per_page=20";

    /// <summary>
    /// The newest release above <paramref name="current"/> that carries the asset with a sha256 digest, from this
    /// repository's download URLs. Pre-releases count only with <paramref name="beta"/> on, or when the current build is
    /// itself a pre-release. Drafts never count.
    /// </summary>
    public static UpdateOffer? Pick(string releasesJson, string current, bool beta, bool installed)
    {
        var running = ReleaseVersion.Parse(current) ?? new ReleaseVersion(new Version(0, 0, 0), ReleaseVersion.Stable, 0);
        var acceptPrerelease = beta || !running.IsStable;
        var assetName = installed ? SetupAsset : PortableAsset;
        using var releases = JsonDocument.Parse(releasesJson);
        if (releases.RootElement.ValueKind != JsonValueKind.Array) return null;
        UpdateOffer? best = null;
        var bestVersion = running;
        foreach (var release in releases.RootElement.EnumerateArray())
        {
            var tag = release.TryGetProperty("tag_name", out var tagValue) ? tagValue.GetString() ?? "" : "";
            if (Flag(release, "draft") || (Flag(release, "prerelease") && !acceptPrerelease)) continue;
            if (ReleaseVersion.Parse(tag) is not { } version || version.CompareTo(bestVersion) <= 0) continue;
            if (!version.IsStable && !acceptPrerelease) continue;
            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
            foreach (var asset in assets.EnumerateArray())
            {
                if (Text(asset, "name") != assetName) continue;
                var digest = Text(asset, "digest");
                if (!Regex.IsMatch(digest, "^sha256:[0-9a-fA-F]{64}$")) continue;
                if (!Uri.TryCreate(Text(asset, "browser_download_url"), UriKind.Absolute, out var uri)
                    || uri.Scheme != "https" || uri.Host != "github.com"
                    || !uri.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.OrdinalIgnoreCase)) continue;
                best = new UpdateOffer(tag, uri, digest[7..]);
                bestVersion = version;
            }
        }
        return best;
    }

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
