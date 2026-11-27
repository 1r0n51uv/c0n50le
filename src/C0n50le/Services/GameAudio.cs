using System.Runtime.InteropServices;

namespace C0n50le.Services;

/// <summary>
/// Silences a running game while the home is in front, so only the home's music plays, and gives it its sound back when
/// it returns. It works on the game's own audio sessions (the per-app volume of the Windows mixer), on every output.
/// Windows remembers a session's mute, so everything muted here is always unmuted again (return, game end, exit).
/// </summary>
public static class GameAudio
{
    private static readonly HashSet<int> Muted = [];

    /// <summary>Mutes or unmutes the audio of these processes; false leaves nothing muted.</summary>
    public static void Set(IReadOnlyCollection<int> processes, bool mute)
    {
        if (mute && processes.Count == 0) return;
        try
        {
            var targets = mute ? processes.ToHashSet() : [.. Muted];
            foreach (var session in Sessions())
            {
                try
                {
                    if (session.Control.GetProcessId(out var pid) != 0 || !targets.Contains((int)pid)) continue;
                    var volume = (ISimpleAudioVolume)session.Control;
                    var context = Guid.Empty;
                    volume.SetMute(mute, ref context);
                    if (mute) Muted.Add((int)pid);
                }
                finally { Marshal.ReleaseComObject(session.Control); }
            }
            if (!mute) Muted.Clear();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            AppLog.Write($"Audio del gioco: {ex.Message}");
        }
    }

    /// <summary>Gives back the sound to everything this app muted.</summary>
    public static void RestoreAll() => Set([], mute: false);

    private readonly record struct Session(IAudioSessionControl2 Control);

    private static IEnumerable<Session> Sessions()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        // Render endpoints that are active: a game may play on headphones while the TV shows it.
        Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(0, 1, out var devices));
        devices.GetCount(out var count);
        for (uint index = 0; index < count; index++)
        {
            if (devices.Item(index, out var device) != 0) continue;
            var iid = typeof(IAudioSessionManager2).GUID;
            if (device.Activate(ref iid, 23, 0, out var manager) != 0) continue;
            if (((IAudioSessionManager2)manager).GetSessionEnumerator(out var sessions) != 0) continue;
            sessions.GetCount(out var total);
            for (var item = 0; item < total; item++)
                if (sessions.GetSession(item, out var session) == 0) yield return new Session(session);
        }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(nint group, int flags, out nint control);
        [PreserveSig] int GetSimpleAudioVolume(nint group, int flags, out nint volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // IAudioSessionControl, unused here.
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out nint name);
        [PreserveSig] int SetDisplayName(nint name, ref Guid context);
        [PreserveSig] int GetIconPath(out nint path);
        [PreserveSig] int SetIconPath(nint path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid group);
        [PreserveSig] int SetGroupingParam(ref Guid group, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(nint client);
        [PreserveSig] int UnregisterAudioSessionNotification(nint client);
        // IAudioSessionControl2.
        [PreserveSig] int GetSessionIdentifier(out nint id);
        [PreserveSig] int GetSessionInstanceIdentifier(out nint id);
        [PreserveSig] int GetProcessId(out uint pid);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
