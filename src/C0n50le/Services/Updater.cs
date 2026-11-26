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
        var file = Path.Combine(directory, "update.exe");
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
            static string Quote(string path) => "'" + path.Replace("'", "''") + "'";
            var script = $$"""
                $ErrorActionPreference = 'Stop'
                Wait-Process -Id {{Environment.ProcessId}} -Timeout 90 -ErrorAction SilentlyContinue
                $exe = {{Quote(exe)}}
                $backup = {{Quote(exe + ".previous")}}
                Move-Item -LiteralPath $exe -Destination $backup -Force
                try {
                    Move-Item -LiteralPath {{Quote(file)}} -Destination $exe
                    Start-Process -FilePath $exe
                    Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
                } catch {
                    Move-Item -LiteralPath $backup -Destination $exe -Force
                    Start-Process -FilePath $exe
                    throw
                }
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
