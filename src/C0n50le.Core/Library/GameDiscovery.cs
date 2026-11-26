using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace C0n50le.Core.Library;

/// <summary>Where to look for installed games. Each root can be empty (that store is skipped).</summary>
public sealed record DiscoveryRoots(string GamesFolder, string SteamRoot, string EpicManifests)
{
    /// <summary>The roots of this PC: the chosen folder, plus Steam and Epic from the registry when they are on.</summary>
    public static DiscoveryRoots FromSystem(string gamesFolder, bool steam, bool epic) =>
        new(gamesFolder, steam ? GameDiscovery.FindSteamRoot() : "", epic ? GameDiscovery.FindEpicManifests() : "");
}

/// <summary>
/// Finds games: one per subfolder of the games folder, the installed Steam apps and the installed Epic games.
/// A folder or manifest that can't be read is skipped, never an error. Adapted from RON1N Mode.
/// </summary>
public static class GameDiscovery
{
    public static List<Game> Scan(DiscoveryRoots roots)
    {
        var games = new List<Game>();
        Collect(() => ScanFolder(roots.GamesFolder, games));
        Collect(() => ScanSteam(roots.SteamRoot, games));
        Collect(() => ScanEpic(roots.EpicManifests, games));
        return games.OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    internal static void ScanFolder(string root, List<Game> games)
    {
        foreach (var folder in Directories(root))
            Collect(() =>
            {
                var name = System.IO.Path.GetFileName(folder);
                if (FindExecutable(folder, name) is { } exe)
                    games.Add(Entry(GameSource.Folder, folder.ToUpperInvariant(), name, exe));
            });
    }

    internal static void ScanSteam(string root, List<Game> games)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        var config = System.IO.Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (File.Exists(config))
            foreach (Match match in Regex.Matches(File.ReadAllText(config), "\"path\"\\s+\"([^\"]+)\""))
                libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));

        foreach (var library in libraries)
            foreach (var manifest in Files(System.IO.Path.Combine(library, "steamapps"), "appmanifest_*.acf"))
                Collect(() =>
                {
                    var document = File.ReadAllText(manifest);
                    string Read(string name) => Regex.Match(document, $"\"{name}\"\\s+\"([^\"]+)\"").Groups[1].Value;
                    var id = Read("appid");
                    var name = Read("name");
                    var install = Read("installdir");
                    if (!uint.TryParse(id, out _) || name.Length == 0 || install.Length == 0
                        || name.StartsWith("Steamworks Common", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase)) return;
                    var location = System.IO.Path.Combine(library, "steamapps", "common", install);
                    if (FindExecutable(location, name) is { } exe)
                        games.Add(Entry(GameSource.Steam, id, name, exe, $"steam://rungameid/{id}"));
                });
    }

    internal static void ScanEpic(string manifests, List<Game> games)
    {
        foreach (var file in Files(manifests, "*.item"))
            Collect(() =>
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                var item = document.RootElement;
                string Read(string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? "" : "";
                var app = Read("AppName");
                var name = Read("DisplayName");
                var launcher = Read("LaunchExecutable");
                var location = Read("InstallLocation");
                if (app.Length == 0 || name.Length == 0 || launcher.Length == 0 || location.Length == 0) return;
                if (item.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True) return;
                var exe = System.IO.Path.Combine(location, launcher);
                if (!File.Exists(exe)) return;
                var identity = string.Join(":", Read("CatalogNamespace"), Read("CatalogItemId"), app);
                games.Add(Entry(GameSource.Epic, identity, name, exe,
                    $"com.epicgames.launcher://apps/{Uri.EscapeDataString(identity)}?action=launch&silent=true"));
            });
    }

    /// <summary>A stable id for a found game, so its pictures and last play survive a rescan.</summary>
    public static string StableId(string source, string key) =>
        "auto-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source + ":" + key)))[..24];

    private static Game Entry(string source, string key, string name, string exe, string uri = "") => new()
    {
        Id = StableId(source, key),
        Name = name,
        Path = exe,
        Source = source,
        SteamId = source == GameSource.Steam && uint.TryParse(key, out var id) ? id : null,
        LaunchUri = uri
    };

    /// <summary>
    /// The exe that most likely starts the game in <paramref name="root"/>: one named like the game, then a shipping
    /// build, then the shallowest. Installers, uninstallers, redistributables and crash reporters never count.
    /// </summary>
    public static string? FindExecutable(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 7,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        var hint = Normalize(name);
        return Directory.EnumerateFiles(root, "*.exe", options)
            .Select(path => (Path: path, Name: Normalize(System.IO.Path.GetFileNameWithoutExtension(path))))
            .Where(file => !Regex.IsMatch(file.Name,
                "^(setup|unins|install|redist|crash|vcredist|dxsetup|easyanticheat|unitycrash|ueprereq|dotnet|vc_redist)|crashreport|helper$"))
            .OrderByDescending(file => file.Name == hint)
            .ThenByDescending(file => file.Name.EndsWith("shipping", StringComparison.Ordinal))
            .ThenBy(file => file.Path.Count(c => c == System.IO.Path.DirectorySeparatorChar))
            .ThenBy(file => file.Path.Length)
            .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .Select(file => file.Path).FirstOrDefault();
    }

    public static string FindSteamRoot()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("SteamPath") is string root && File.Exists(System.IO.Path.Combine(root, "steam.exe"))
                ? System.IO.Path.GetFullPath(root) : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return ""; }
    }

    public static string FindEpicManifests()
    {
        try
        {
            using var protocol = Registry.ClassesRoot.OpenSubKey(@"com.epicgames.launcher\shell\open\command");
            if (protocol?.GetValue(null) is not string command || string.IsNullOrWhiteSpace(command)) return "";
            var executable = command.StartsWith('"') ? command.Split('"')[1] : command.Split(' ')[0];
            if (!File.Exists(Environment.ExpandEnvironmentVariables(executable))) return "";
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return ""; }
    }

    internal static string Normalize(string name) => string.Concat(name.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static string[] Directories(string path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) ? Directory.GetDirectories(path) : [];

    private static string[] Files(string path, string pattern) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) ? Directory.GetFiles(path, pattern) : [];

    private static void Collect(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            System.Diagnostics.Trace.WriteLine(ex);
        }
    }
}
