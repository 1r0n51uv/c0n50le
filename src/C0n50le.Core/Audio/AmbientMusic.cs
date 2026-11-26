namespace C0n50le.Core.Audio;

/// <summary>
/// The music under the home: a slow, warm pad over four chords with a few bell notes on top, made in code. The loop
/// is seamless: every voice fades in and out on a circle of time. Adapted from Console Mode.
/// </summary>
public static class AmbientMusic
{
    public const int SampleRate = 22050;
    public const double ChordSeconds = 9.0;

    // Cmaj9, Am9, Fmaj7(#11), G6/9: open voicings, low and soft.
    private static readonly double[][] Chords =
    [
        [130.81, 196.00, 246.94, 293.66, 329.63],
        [110.00, 164.81, 196.00, 246.94, 261.63],
        [87.31, 130.81, 164.81, 220.00, 246.94],
        [98.00, 146.83, 196.00, 220.00, 293.66]
    ];

    private static readonly double[] Bells = [783.99, 659.25, 587.33, 523.25, 659.25, 783.99, 880.00, 587.33];

    public static double LoopSeconds => Chords.Length * ChordSeconds;

    public static float[] Samples()
    {
        var total = (int)(LoopSeconds * SampleRate);
        var samples = new float[total];
        var chordLength = ChordSeconds * SampleRate;
        for (var i = 0; i < total; i++)
        {
            var t = (double)i / SampleRate;
            double pad = 0;
            for (var c = 0; c < Chords.Length; c++)
            {
                var weight = Swell(i, c * chordLength + chordLength / 2, chordLength * 1.5, total);
                if (weight <= 0) continue;
                foreach (var f in Chords[c])
                    pad += weight * (Math.Sin(2 * Math.PI * Whole(f) * t) + 0.6 * Math.Sin(2 * Math.PI * Whole(f * 1.003) * t + 1.1))
                           * (0.85 + 0.15 * Math.Sin(2 * Math.PI * Whole(0.11) * t + f));
            }
            double bell = 0;
            var bellLength = chordLength * 1.5;
            for (var b = 0; b < Bells.Length; b++)
            {
                var start = b * bellLength % total;
                var age = (i - start + total) % total;
                if (age > SampleRate * 4) continue;
                var since = age / SampleRate;
                bell += Math.Sin(2 * Math.PI * Bells[b] * since) * Math.Exp(-1.6 * since) * Math.Min(1, since * 200)
                        + 0.25 * Math.Sin(2 * Math.PI * Bells[b] * 2.01 * since) * Math.Exp(-3 * since) * Math.Min(1, since * 200);
            }
            samples[i] = (float)(pad * 0.045 + bell * 0.06);
        }
        var smoothed = 0.0;
        for (var pass = 0; pass < 2; pass++)
            for (var i = 0; i < total; i++)
            {
                smoothed += (samples[i] - smoothed) * 0.35;
                if (pass == 1) samples[i] = (float)smoothed;
            }
        return samples;
    }

    /// <summary>The nearest frequency with a whole number of cycles in the loop, so the end meets the start without a click.</summary>
    private static double Whole(double frequency) => Math.Max(1, Math.Round(frequency * LoopSeconds)) / LoopSeconds;

    private static double Swell(int i, double centre, double width, int total)
    {
        var distance = Math.Abs(i - centre);
        distance = Math.Min(distance, total - distance);
        return distance >= width / 2 ? 0 : 0.5 + 0.5 * Math.Cos(Math.PI * distance / (width / 2));
    }

    public static byte[] Wav() => Wave.Encode(Samples(), SampleRate);
}
