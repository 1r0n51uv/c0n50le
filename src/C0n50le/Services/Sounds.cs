using System.Runtime.InteropServices;
using C0n50le.Core.Audio;
using Microsoft.UI.Dispatching;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace C0n50le.Services;

/// <summary>
/// Plays the interface sounds from memory with winmm (a new sound cuts the one playing, so quick moves keep up).
/// Shipped files first (Assets\Sounds), else the synthesized ones. A sound that can't play is skipped silently.
/// Adapted from Console Mode.
/// </summary>
public static class Sounds
{
    private const uint SndAsync = 0x0001, SndNoDefault = 0x0002, SndMemory = 0x0004;
    private static readonly Dictionary<UiSound, GCHandle> Buffers = [];
    private static bool _failed;

    public static bool Enabled { get; set; } = true;

    public static void Play(UiSound sound, bool evenWhenOff = false)
    {
        if ((!Enabled && !evenWhenOff) || _failed) return;
        try
        {
            if (!Buffers.TryGetValue(sound, out var handle))
            {
                handle = GCHandle.Alloc(Embedded(sound) ?? UiSoundSynth.Wav(sound), GCHandleType.Pinned);
                Buffers[sound] = handle;
            }
            PlaySound(handle.AddrOfPinnedObject(), 0, SndMemory | SndAsync | SndNoDefault);
        }
        catch (Exception ex)
        {
            _failed = true;
            AppLog.Write($"Suoni disattivati: {ex.Message}");
        }
    }

    private static byte[]? Embedded(UiSound sound)
    {
        using var stream = typeof(Sounds).Assembly.GetManifestResourceStream($"C0n50le.Sounds.{sound.ToString().ToLowerInvariant()}.wav");
        if (stream is null) return null;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    private static extern bool PlaySound(nint sound, nint module, uint flags);
}

/// <summary>
/// Plays the ambient music in a loop under the home, fading in and out, on its own player so the interface sounds never
/// cut it. Anything that fails (no audio device) stays silent. Adapted from Console Mode.
/// </summary>
public static class HomeMusic
{
    private const double Volume = 0.22;
    private static MediaPlayer? _player;
    private static DispatcherQueueTimer? _fade;
    private static bool _wanted, _failed;

    public static void Set(bool play, DispatcherQueue dispatcher)
    {
        if (_failed || play == _wanted) return;
        _wanted = play;
        try
        {
            if (play && _player is null) _player = Create();
            if (_player is null) return;
            if (play && _player.PlaybackSession.PlaybackState != MediaPlaybackState.Playing)
            {
                _player.Volume = 0;
                _player.Play();
            }
            Fade(dispatcher, play ? Volume : 0, pauseAtEnd: !play);
        }
        catch (Exception ex)
        {
            _failed = true;
            AppLog.Write($"Musica disattivata: {ex.Message}");
        }
    }

    private static MediaPlayer Create()
    {
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(AmbientMusic.Wav());
            writer.StoreAsync().AsTask().GetAwaiter().GetResult();
            writer.DetachStream();
        }
        stream.Seek(0);
        return new MediaPlayer
        {
            Source = MediaSource.CreateFromStream(stream, "audio/wav"),
            IsLoopingEnabled = true,
            Volume = 0,
            AudioCategory = MediaPlayerAudioCategory.GameMedia
        };
    }

    private static void Fade(DispatcherQueue dispatcher, double target, bool pauseAtEnd)
    {
        _fade?.Stop();
        _fade = dispatcher.CreateTimer();
        _fade.Interval = TimeSpan.FromMilliseconds(50);
        _fade.Tick += (timer, _) =>
        {
            if (_player is null) { timer.Stop(); return; }
            var step = Volume / 30;
            var volume = _player.Volume;
            volume = target > volume ? Math.Min(target, volume + step) : Math.Max(target, volume - step);
            _player.Volume = volume;
            if (Math.Abs(volume - target) > 0.0001) return;
            timer.Stop();
            if (pauseAtEnd) _player.Pause();
        };
        _fade.Start();
    }
}
