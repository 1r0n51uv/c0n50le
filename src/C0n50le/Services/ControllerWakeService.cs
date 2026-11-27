using System.ComponentModel;
using System.Diagnostics;
using C0n50le.Core.Input;

namespace C0n50le.Services;

/// <summary>
/// Reads and turns on "Allow this device to wake the computer" for the controller's adapters, as consolemode does.
/// Reading needs no rights; turning it on runs an elevated PowerShell, so Windows asks first.
/// </summary>
public static class ControllerWakeService
{
    private const string PowerShell = "powershell.exe";

    public enum EnableResult { Done, Canceled, Failed, NotFound }

    /// <summary>The controller-related devices that can wake the PC, or null when they couldn't be read.</summary>
    public static List<WakeDevice>? Read()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(PowerShell)
            {
                ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", ControllerWake.Encode(ControllerWake.QueryScript) },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            });
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(20000))
            {
                try { process.Kill(); } catch { }
                AppLog.Write("Riattivazione col controller: lettura oltre 20 s");
                return null;
            }
            if (process.ExitCode != 0)
            {
                AppLog.Write($"Riattivazione col controller: lettura fallita ({process.ExitCode})");
                return null;
            }
            return ControllerWake.Relevant(ControllerWake.Parse(output.Result));
        }
        catch (Exception ex)
        {
            AppLog.Write($"Riattivazione col controller: lettura fallita: {ex.Message}");
            return null;
        }
    }

    /// <summary>Lets the given devices wake the PC. Shows the UAC prompt; blocks until PowerShell exits.</summary>
    public static EnableResult Enable(IReadOnlyCollection<WakeDevice> devices)
    {
        if (devices.Count == 0) return EnableResult.Done;
        var script = ControllerWake.EnableScript(devices.Select(d => d.InstanceName));
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = PowerShell,
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {ControllerWake.Encode(script)}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process is null) return EnableResult.Failed;
            if (!process.WaitForExit(30000))
            {
                AppLog.Write("Riattivazione col controller: PowerShell non ha finito in 30 s");
                return EnableResult.Failed;
            }
            AppLog.Write($"Riattivazione col controller: {string.Join(", ", devices.Select(d => d.Name))} => {process.ExitCode}");
            return process.ExitCode switch
            {
                0 => EnableResult.Done,
                ControllerWake.NotFoundExitCode => EnableResult.NotFound,
                _ => EnableResult.Failed
            };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            AppLog.Write("Riattivazione col controller: permesso di amministratore negato");
            return EnableResult.Canceled;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Riattivazione col controller: {ex.Message}");
            return EnableResult.Failed;
        }
    }
}
