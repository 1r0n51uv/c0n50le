using System.Text;
using C0n50le.Core.Library;
using C0n50le.Core.Settings;

namespace C0n50le.Core.Diagnostics;

/// <summary>
/// The text copied by "Copy the diagnostics report": versions, settings and the library's shape, without paths to the
/// user's folders or the names of their games.
/// </summary>
public static class DiagnosticsReport
{
    public static string Build(string version, string os, bool installed, Preferences preferences, IReadOnlyCollection<Game> library,
        string controller, IEnumerable<string> recentLog)
    {
        var text = new StringBuilder();
        text.AppendLine($"c0n50le {version}");
        text.AppendLine($"Windows: {os}");
        text.AppendLine($"Install: {(installed ? "setup" : "portable")}");
        text.AppendLine($"Language: {preferences.Language}");
        text.AppendLine($"Theme: {preferences.Theme}, scale {preferences.UiScale:0.##}, reduce motion {preferences.ReduceMotion}");
        text.AppendLine($"Sounds {preferences.Sounds}, music {preferences.Music}, intro {preferences.Intro}");
        text.AppendLine($"Start full screen {preferences.OpenFullscreen}, close to tray {preferences.CloseToTray}");
        text.AppendLine($"Updates {preferences.CheckUpdates}, beta {preferences.BetaUpdates}");
        text.AppendLine($"Return shortcut: key '{preferences.KeyboardShortcut}', controller 0x{preferences.ControllerShortcut:X4}");
        text.AppendLine($"Controller: {controller}");
        text.AppendLine($"Games folder set: {preferences.GamesFolder.Length > 0}, Steam {preferences.ScanSteam}, Epic {preferences.ScanEpic}");
        var bySource = library.GroupBy(game => game.Source).OrderBy(group => group.Key)
            .Select(group => $"{group.Key} {group.Count()}");
        text.AppendLine($"Library: {library.Count} ({string.Join(", ", bySource)}), hidden {preferences.Hidden.Count}");
        text.AppendLine($"Matched in catalog: {library.Count(game => preferences.Art.GetValueOrDefault(game.Id)?.IsMatched == true)}");
        text.AppendLine();
        text.AppendLine("Log:");
        foreach (var line in recentLog) text.AppendLine(Scrub(line));
        return text.ToString();
    }

    /// <summary>Hides the user's folder in a log line.</summary>
    public static string Scrub(string line)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return profile.Length > 0 ? line.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase) : line;
    }
}
