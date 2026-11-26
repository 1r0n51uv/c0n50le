using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using C0n50le.Core.Settings;
using C0n50le.Core.Updates;

namespace C0n50le.Services;

/// <summary>
/// Over-the-air updates from GitHub Releases: finds a newer release, downloads the asset for this kind of install,
/// checks its SHA-256 and runs it (the setup silently; the portable exe swapped in place). Adapted from RON1N Mode.
/// </summary>
public static class Updater
{
    private static readonly HttpClient Http = CreateClient();

    public static string CurrentVersion =>
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];

    public static async Task<UpdateOffer?> FindAsync(bool beta, bool installed, CancellationToken token = default)
    {
        var json = await Http.GetStringAsync(ReleaseFeed.ReleasesUrl, token);
        return ReleaseFeed.Pick(json, CurrentVersion, beta, installed);
    }

    public static async Task InstallAsync(UpdateOffer update, bool installed)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown app path.");
        var directory = Path.Combine(installed ? Path.GetTempPath() : AppPaths.ExecutableDirectory, ".c0n50le-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, installed ? "update.exe" : "update.zip");
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(6));
            using var response = await Http.GetAsync(update.Asset, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            await using (var download = await response.Content.ReadAsStreamAsync(deadline.Token))
            await using (var target = File.Create(file))
                await download.CopyToAsync(target, deadline.Token);
            await using (var completed = File.OpenRead(file))
            {
                var digest = Convert.ToHexString(await SHA256.HashDataAsync(completed, deadline.Token));
                if (!string.Equals(update.Sha256, digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256 mismatch.");
            }
            AppLog.Write($"Aggiornamento verificato: {update.Tag}");
            if (installed)
            {
                Process.Start(new ProcessStartInfo(file, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });
                return;
            }
            // Portable: the zip holds the whole app folder. Once c0n50le has closed, its files are replaced (the data folder
            // c0n50le_Data is not in the zip, so it stays) and the app starts again; on failure the old files come back.
            static string Quote(string path) => "'" + path.Replace("'", "''") + "'";
            var appFolder = AppPaths.ExecutableDirectory;
            var unpacked = Path.Combine(directory, "app");
            var backup = Path.Combine(directory, "previous");
            var script = $$"""
                $ErrorActionPreference = 'Stop'
                Wait-Process -Id {{Environment.ProcessId}} -Timeout 90 -ErrorAction SilentlyContinue
                $app = {{Quote(appFolder)}}
                $new = {{Quote(unpacked)}}
                $old = {{Quote(backup)}}
                Expand-Archive -LiteralPath {{Quote(file)}} -DestinationPath $new -Force
                if (-not (Test-Path (Join-Path $new 'C0n50le.exe'))) { throw 'Update archive without C0n50le.exe' }
                New-Item -ItemType Directory -Force $old | Out-Null
                Get-ChildItem -LiteralPath $app -Force | Where-Object Name -ne 'c0n50le_Data' | Where-Object Name -notlike '.c0n50le-update-*' |
                    Move-Item -Destination $old -Force
                try {
                    Get-ChildItem -LiteralPath $new -Force | Move-Item -Destination $app -Force
                } catch {
                    Get-ChildItem -LiteralPath $old -Force | Move-Item -Destination $app -Force
                    Start-Process -FilePath (Join-Path $app 'C0n50le.exe')
                    throw
                }
                Start-Process -FilePath (Join-Path $app 'C0n50le.exe')
                """;
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + encoded)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            try { Directory.Delete(directory, true); } catch (IOException) { }
            throw;
        }
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("c0n50le");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }
}
