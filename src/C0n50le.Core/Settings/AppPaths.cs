namespace C0n50le.Core.Settings;

/// <summary>
/// Where c0n50le keeps its data: %LOCALAPPDATA%\c0n50le when installed (the installer leaves C0n50le.installed next to
/// the exe), otherwise a c0n50le_Data folder next to the portable exe.
/// </summary>
public sealed class AppPaths
{
    public const string InstalledMarker = "C0n50le.installed";

    public AppPaths(string dataFolder, bool installed)
    {
        Data = dataFolder;
        Installed = installed;
    }

    public string Data { get; }
    public bool Installed { get; }
    public string PreferencesFile => Path.Combine(Data, "preferences.json");
    public string ArtFolder => Path.Combine(Data, "art");
    public string LogFile => Path.Combine(Data, "c0n50le.log");

    public static string ExecutableDirectory =>
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    public static AppPaths ForThisApp()
    {
        var installed = File.Exists(Path.Combine(ExecutableDirectory, InstalledMarker));
        var data = installed
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "c0n50le")
            : Path.Combine(ExecutableDirectory, "c0n50le_Data");
        return new AppPaths(data, installed);
    }
}
