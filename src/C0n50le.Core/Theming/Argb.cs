namespace C0n50le.Core.Theming;

/// <summary>A color as alpha, red, green, blue bytes. Kept free of WinUI so the themes are tested anywhere.</summary>
public readonly record struct Argb(byte A, byte R, byte G, byte B)
{
    public static Argb FromHex(string hex)
    {
        var value = hex.TrimStart('#');
        if (value.Length == 6) value = "FF" + value;
        if (value.Length != 8) throw new FormatException($"Not a color: {hex}");
        var number = Convert.ToUInt32(value, 16);
        return new Argb((byte)(number >> 24), (byte)(number >> 16), (byte)(number >> 8), (byte)number);
    }

    public Argb WithAlpha(byte alpha) => this with { A = alpha };

    public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";

    /// <summary>Mixes towards <paramref name="other"/> by <paramref name="amount"/> (0 keeps this, 1 gives the other).</summary>
    public Argb Blend(Argb other, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        static byte Mix(byte a, byte b, double t) => (byte)Math.Round(a + (b - a) * t);
        return new Argb(Mix(A, other.A, amount), Mix(R, other.R, amount), Mix(G, other.G, amount), Mix(B, other.B, amount));
    }

    /// <summary>This color laid over an opaque <paramref name="below"/>.</summary>
    public Argb Over(Argb below) => below.Blend(this with { A = 255 }, A / 255.0) with { A = 255 };

    /// <summary>Relative luminance (WCAG 2), of the opaque color.</summary>
    public double Luminance
    {
        get
        {
            static double Channel(byte value)
            {
                var c = value / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(R) + 0.7152 * Channel(G) + 0.0722 * Channel(B);
        }
    }

    /// <summary>WCAG contrast ratio between two opaque colors, 1 to 21.</summary>
    public static double Contrast(Argb a, Argb b)
    {
        var (light, dark) = a.Luminance > b.Luminance ? (a.Luminance, b.Luminance) : (b.Luminance, a.Luminance);
        return (light + 0.05) / (dark + 0.05);
    }

    /// <summary>Hue 0-360, saturation and lightness 0-1.</summary>
    public (double H, double S, double L) ToHsl()
    {
        double r = R / 255.0, g = G / 255.0, b = B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max == min) return (0, 0, l);
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    public static Argb FromHsl(double h, double s, double l, byte alpha = 255)
    {
        h = ((h % 360) + 360) % 360 / 360;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        if (s == 0)
        {
            var gray = (byte)Math.Round(l * 255);
            return new Argb(alpha, gray, gray, gray);
        }
        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
        return new Argb(alpha, (byte)Math.Round(Hue(p, q, h + 1.0 / 3) * 255),
            (byte)Math.Round(Hue(p, q, h) * 255), (byte)Math.Round(Hue(p, q, h - 1.0 / 3) * 255));
    }
}
