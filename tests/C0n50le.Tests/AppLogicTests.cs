using System.Text.RegularExpressions;
using C0n50le.Core.Audio;
using C0n50le.Core.Diagnostics;
using C0n50le.Core.Library;
using C0n50le.Core.Localization;
using C0n50le.Core.Settings;
using C0n50le.Core.Startup;
using C0n50le.Core.Updates;

namespace C0n50le.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void Both_catalogs_have_the_same_keys()
    {
        var italian = Strings.Catalog(Strings.Italian);
        var english = Strings.Catalog(Strings.English);
        Assert.Empty(italian.Keys.Except(english.Keys));
        Assert.Empty(english.Keys.Except(italian.Keys));
    }

    [Fact]
    public void Translations_keep_the_placeholders()
    {
        var italian = Strings.Catalog(Strings.Italian);
        foreach (var (key, english) in Strings.Catalog(Strings.English))
            Assert.True(Strings.Placeholders(english).SetEquals(Strings.Placeholders(italian[key])), key);
    }

    [Fact]
    public void Every_key_the_app_uses_exists()
    {
        var root = FindRepository();
        var code = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText);
        const string pattern = """"Strings\.(?:Get|Format)\("([A-Za-z_]+)"|(?:Toggle|Choice|ActionRow|Info)\("([A-Za-z]+)"(?:, "([A-Za-z]*)")?"""";
        var used = code.SelectMany(text => Regex.Matches(text, pattern)
            .SelectMany(match => match.Groups.Values.Skip(1).Where(group => group.Success && group.Value.Length > 0).Select(group => group.Value)))
            .ToHashSet();
        var keys = Strings.Catalog(Strings.Italian).Keys.ToHashSet();
        // Keys built at run time ("Theme_" + id, "Glass_" + depth) are checked by ThemeTests.
        Assert.Empty(used.Where(key => !key.EndsWith('_') && !keys.Contains(key)));
        foreach (var stop in Tour.Stops)
        {
            Assert.Contains(Tour.TitleKey(stop), keys);
            Assert.Contains(Tour.BodyKey(stop), keys);
        }
    }

    [Fact]
    public void Italian_texts_are_not_left_in_english()
    {
        var english = Strings.Catalog(Strings.English);
        var same = Strings.Catalog(Strings.Italian)
            .Where(pair => pair.Value == english[pair.Key] && pair.Value.Contains(' ') && !pair.Key.StartsWith("Credits") && pair.Key != "LiquidGlass")
            .Select(pair => pair.Key);
        Assert.Empty(same);
    }

    [Fact]
    public void Language_follows_the_choice_then_windows()
    {
        Assert.Equal("en-US", Strings.Resolve("en-US", new System.Globalization.CultureInfo("it-IT")));
        Assert.Equal("it-IT", Strings.Resolve("", new System.Globalization.CultureInfo("it-CH")));
        Assert.Equal("en-US", Strings.Resolve(null, new System.Globalization.CultureInfo("de-DE")));
    }

    private static string FindRepository()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "C0n50le.sln"))) return folder.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
}

public sealed class UpdateTests
{
    private static string Release(string tag, bool prerelease = false, bool draft = false, string asset = ReleaseFeed.SetupAsset,
        string digest = "sha256:0000000000000000000000000000000000000000000000000000000000000000",
        string url = "https://github.com/1r0n51uv/c0n50le/releases/download/{0}/{1}") => $$"""
        {"tag_name":"{{tag}}","draft":{{draft.ToString().ToLowerInvariant()}},"prerelease":{{prerelease.ToString().ToLowerInvariant()}},
         "assets":[{"name":"{{asset}}","digest":"{{digest}}","browser_download_url":"{{string.Format(url, tag, asset)}}"}]}
        """;

    private static string Feed(params string[] releases) => "[" + string.Join(",", releases) + "]";

    [Fact]
    public void Picks_the_newest_stable_release_above_the_current_one()
    {
        var feed = Feed(Release("v0.2.0"), Release("v0.3.0-beta.1", prerelease: true), Release("v0.1.0"), Release("v0.4.0", draft: true));
        Assert.Equal("v0.2.0", ReleaseFeed.Pick(feed, "0.1.0", beta: false, installed: true)?.Tag);
        Assert.Equal("v0.3.0-beta.1", ReleaseFeed.Pick(feed, "0.1.0", beta: true, installed: true)?.Tag);
        Assert.Null(ReleaseFeed.Pick(feed, "0.3.0", beta: true, installed: true));
    }

    [Fact]
    public void A_beta_build_keeps_getting_betas()
    {
        var feed = Feed(Release("v0.2.0-beta.2", prerelease: true));
        Assert.Equal("v0.2.0-beta.2", ReleaseFeed.Pick(feed, "0.2.0-beta.1", beta: false, installed: true)?.Tag);
    }

    [Fact]
    public void Needs_the_right_asset_a_digest_and_this_repository()
    {
        Assert.Null(ReleaseFeed.Pick(Feed(Release("v0.2.0", asset: "Other.exe")), "0.1.0", false, true));
        Assert.Null(ReleaseFeed.Pick(Feed(Release("v0.2.0", digest: "md5:abc")), "0.1.0", false, true));
        Assert.Null(ReleaseFeed.Pick(Feed(Release("v0.2.0", url: "https://github.com/someone/else/releases/download/{0}/{1}")), "0.1.0", false, true));
        Assert.Null(ReleaseFeed.Pick(Feed(Release("v0.2.0")), "0.1.0", false, installed: false));
        Assert.NotNull(ReleaseFeed.Pick(Feed(Release("v0.2.0", asset: ReleaseFeed.PortableAsset)), "0.1.0", false, installed: false));
    }

    [Theory]
    [InlineData("0.1.0", "0.1.0-beta.3", 1)]
    [InlineData("0.1.0-beta.2", "0.1.0-beta.10", -1)]
    [InlineData("0.1.0-alpha.9", "0.1.0-beta.1", -1)]
    [InlineData("v1.0.0+abc", "1.0.0", 0)]
    public void Versions_compare_like_releases(string a, string b, int sign) =>
        Assert.Equal(sign, Math.Sign(ReleaseVersion.Parse(a)!.Value.CompareTo(ReleaseVersion.Parse(b)!.Value)));

    [Fact]
    public void Odd_tags_are_not_versions() => Assert.Null(ReleaseVersion.Parse("latest"));
}

public sealed class StartupAndSoundTests
{
    [Fact]
    public void Tour_has_its_stops_in_order_and_steps_within_bounds()
    {
        Assert.Equal(TourStop.Welcome, Tour.Stops[0]);
        Assert.Equal(TourStop.Done, Tour.Stops[^1]);
        Assert.Null(Tour.Step(0, -1));
        Assert.Null(Tour.Step(Tour.Stops.Count - 1, 1));
        Assert.Equal(1, Tour.Step(0, 1));
        Assert.True(Tour.ShouldOffer(tourDone: false, gameRunning: false));
        Assert.False(Tour.ShouldOffer(tourDone: false, gameRunning: true));
        Assert.InRange(Tour.DwellTime("x", "y").TotalSeconds, 7, 16);
        Assert.Equal(16, Tour.DwellTime("x", new string('a', 1000)).TotalSeconds);
    }

    [Fact]
    public void Intro_shows_only_when_wanted_and_lasts_at_least_its_minimum()
    {
        Assert.True(Intro.ShouldShow(true, true, false));
        Assert.False(Intro.ShouldShow(true, false, false));
        Assert.False(Intro.ShouldShow(true, true, true));
        Assert.Equal(Intro.Minimum - TimeSpan.FromSeconds(1), Intro.StillToShow(TimeSpan.FromSeconds(1)));
        Assert.Equal(TimeSpan.Zero, Intro.StillToShow(TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData(UiSound.Move)]
    [InlineData(UiSound.Confirm)]
    [InlineData(UiSound.Back)]
    [InlineData(UiSound.Open)]
    [InlineData(UiSound.Boot)]
    public void Synthesized_sounds_are_short_quiet_wavs(UiSound sound)
    {
        var wav = UiSoundSynth.Wav(sound);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        var samples = UiSoundSynth.Samples(sound);
        Assert.InRange(samples.Length / (double)UiSoundSynth.SampleRate, 0.03, 1.0);
        Assert.All(samples, sample => Assert.InRange(sample, -0.5f, 0.5f));
    }

    [Fact]
    public void Ambient_music_loops_without_a_click()
    {
        var samples = AmbientMusic.Samples();
        Assert.Equal((int)(AmbientMusic.LoopSeconds * AmbientMusic.SampleRate), samples.Length);
        var largestStep = samples.Zip(samples.Skip(1), (a, b) => Math.Abs(b - a)).Max();
        Assert.True(Math.Abs(samples[^1] - samples[0]) <= largestStep, "the loop jumps where it wraps");
        Assert.All(samples, sample => Assert.InRange(sample, -1f, 1f));
    }

    [Fact]
    public void Diagnostics_report_has_no_game_names_or_user_folders()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var preferences = new Preferences { GamesFolder = Path.Combine(profile, "Games") };
        var library = new[] { new Game { Id = "a", Name = "Secret Game", Path = Path.Combine(profile, "Games", "x.exe"), Source = GameSource.Folder } };
        var report = DiagnosticsReport.Build("0.1.0", "Windows", true, preferences, library, "Xbox",
            [$"Avvio: {Path.Combine(profile, "Games", "x.exe")}"]);
        Assert.DoesNotContain("Secret Game", report);
        Assert.DoesNotContain(profile, report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("folder 1", report);
    }
}
