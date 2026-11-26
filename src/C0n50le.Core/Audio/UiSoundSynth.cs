namespace C0n50le.Core.Audio;

/// <summary>The interface sounds.</summary>
public enum UiSound
{
    /// <summary>Focus moved to another item.</summary>
    Move,
    /// <summary>Something was picked, opened or started.</summary>
    Confirm,
    /// <summary>Went back or closed a panel.</summary>
    Back,
    /// <summary>A panel opened over the home (System, a card's options).</summary>
    Open,
    /// <summary>The opening, under the name.</summary>
    Boot
}

/// <summary>
/// Builds a sound in code when its file isn't shipped: short, soft blips (a sine plus a quiet octave, a fast attack and a
/// decay). Returns a 16-bit mono PCM WAV. Adapted from Console Mode.
/// </summary>
public static class UiSoundSynth
{
    public const int SampleRate = 44100;

    private readonly record struct Note(double From, double To, int Ms, double Gain);

    private static Note[] Notes(UiSound sound) => sound switch
    {
        UiSound.Move => [new(1150, 1000, 45, 0.30)],
        UiSound.Open => [new(980, 1180, 55, 0.30)],
        UiSound.Confirm => [new(740, 740, 55, 0.32), new(1110, 1110, 95, 0.34)],
        UiSound.Boot => [new(523, 523, 110, 0.26), new(659, 659, 110, 0.28), new(784, 784, 140, 0.30), new(1047, 1047, 420, 0.30)],
        _ => [new(820, 820, 50, 0.30), new(560, 540, 85, 0.30)]
    };

    public static float[] Samples(UiSound sound)
    {
        var samples = new List<float>();
        double phase = 0;
        foreach (var note in Notes(sound))
        {
            var count = SampleRate * note.Ms / 1000;
            var attack = SampleRate * 3 / 1000;
            var release = SampleRate * 6 / 1000;
            for (var i = 0; i < count; i++)
            {
                var t = (double)i / count;
                phase += 2 * Math.PI * (note.From + (note.To - note.From) * t) / SampleRate;
                var tone = Math.Sin(phase) + 0.25 * Math.Sin(2 * phase);
                var envelope = Math.Exp(-3.2 * t) * Math.Min(1.0, (double)i / attack) * Math.Min(1.0, (double)(count - i) / release);
                samples.Add((float)(tone / 1.25 * note.Gain * envelope));
            }
        }
        return [.. samples];
    }

    public static byte[] Wav(UiSound sound) => Wave.Encode(Samples(sound), SampleRate);
}

/// <summary>Writes samples (-1 to 1) as a 16-bit mono PCM WAV.</summary>
public static class Wave
{
    public static byte[] Encode(float[] samples, int sampleRate)
    {
        var dataLength = samples.Length * 2;
        using var stream = new MemoryStream(44 + dataLength);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataLength);
        foreach (var sample in samples)
            writer.Write((short)Math.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue));
        writer.Flush();
        return stream.ToArray();
    }
}
