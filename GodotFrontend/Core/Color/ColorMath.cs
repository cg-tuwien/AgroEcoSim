using System.Globalization;

namespace GodotFrontend.Core.Color;

/// <summary>Linear-sRGB colour. Every colour that crosses a module boundary in the core is linear (render/core/Color.ts).</summary>
public readonly record struct RGB(float R, float G, float B)
{
    public static readonly RGB Black = new(0, 0, 0);
    public static RGB operator *(RGB c, float s) => new(c.R * s, c.G * s, c.B * s);
}

public readonly record struct HSL(double H, double S, double L);

/// <summary>A port of three r152's colour maths as used by ThreeFrontend, including the truncated 0.41666 encode exponent.</summary>
public static class ColorMath
{
    public static double SrgbToLinear(double c) => c < 0.04045 ? c * 0.0773993808 : System.Math.Pow(c * 0.9478672986 + 0.0521327014, 2.4);
    public static double LinearToSrgb(double c) => c < 0.0031308 ? c * 12.92 : 1.055 * System.Math.Pow(c, 0.41666) - 0.055;

    /// <summary>sRGB bytes (the wire colour) to linear RGB, as the web's fromHex(readHexRGB()) does.</summary>
    public static RGB FromSrgb8(byte r, byte g, byte b) => new((float)SrgbToLinear(r / 255.0), (float)SrgbToLinear(g / 255.0), (float)SrgbToLinear(b / 255.0));

    /// <summary>"#rgb" or "#rrggbb" to linear RGB (new THREE.Color(hex) under r152's colour management).</summary>
    public static RGB FromHex(string hex)
    {
        var s = hex.Trim().TrimStart('#');
        int r, g, b;
        if (s.Length == 3)
        {
            r = int.Parse(new string(s[0], 2), NumberStyles.HexNumber);
            g = int.Parse(new string(s[1], 2), NumberStyles.HexNumber);
            b = int.Parse(new string(s[2], 2), NumberStyles.HexNumber);
        }
        else if (s.Length == 6)
        {
            r = int.Parse(s.Substring(0, 2), NumberStyles.HexNumber);
            g = int.Parse(s.Substring(2, 2), NumberStyles.HexNumber);
            b = int.Parse(s.Substring(4, 2), NumberStyles.HexNumber);
        }
        else throw new ArgumentException($"unsupported hex colour '{hex}'");
        return FromSrgb8((byte)r, (byte)g, (byte)b);
    }

    public static string ToHex(RGB c)
    {
        static int Ch(double v) => (int)System.Math.Round(System.Math.Clamp(LinearToSrgb(v), 0, 1) * 255);
        return $"#{Ch(c.R):x2}{Ch(c.G):x2}{Ch(c.B):x2}";
    }

    private static double EuclideanModulo(double n, double m) => ((n % m) + m) % m;

    private static double Hue2Rgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * 6 * (2.0 / 3 - t);
        return p;
    }

    /// <summary>Color.getHSL over the (linear) components.</summary>
    public static HSL RgbToHsl(RGB c)
    {
        double r = c.R, g = c.G, b = c.B;
        var max = System.Math.Max(r, System.Math.Max(g, b));
        var min = System.Math.Min(r, System.Math.Min(g, b));
        double hue = 0, saturation = 0;
        var lightness = (min + max) / 2.0;
        if (min != max)
        {
            var delta = max - min;
            saturation = lightness <= 0.5 ? delta / (max + min) : delta / (2 - max - min);
            if (max == r) hue = (g - b) / delta + (g < b ? 6 : 0);
            else if (max == g) hue = (b - r) / delta + 2;
            else hue = (r - g) / delta + 4;
            hue /= 6;
        }
        return new HSL(hue, saturation, lightness);
    }

    /// <summary>Color.setHSL, with three's (q, p, t) argument order.</summary>
    public static RGB HslToRgb(double h, double s, double l)
    {
        h = EuclideanModulo(h, 1);
        s = System.Math.Clamp(s, 0, 1);
        l = System.Math.Clamp(l, 0, 1);
        if (s == 0) return new RGB((float)l, (float)l, (float)l);
        var p = l <= 0.5 ? l * (1 + s) : l + s - l * s;
        var q = 2 * l - p;
        return new RGB((float)Hue2Rgb(q, p, h + 1.0 / 3), (float)Hue2Rgb(q, p, h), (float)Hue2Rgb(q, p, h - 1.0 / 3));
    }

    public static RGB Lerp(RGB a, RGB b, double t) => new((float)(a.R + (b.R - a.R) * t), (float)(a.G + (b.G - a.G) * t), (float)(a.B + (b.B - a.B) * t));

    /// <summary>three's Color.lerpHSL: hue lerped plainly, not along the shortest arc (relied on by the wood ramps).</summary>
    public static RGB LerpHSL(RGB a, RGB b, double t)
    {
        var ha = RgbToHsl(a);
        var hb = RgbToHsl(b);
        return HslToRgb(ha.H + (hb.H - ha.H) * t, ha.S + (hb.S - ha.S) * t, ha.L + (hb.L - ha.L) * t);
    }
}

/// <summary>sRGB byte to linear float through a 256-entry table: the packer converts millions of wire colours per frame.</summary>
public static class SrgbLut
{
    private static readonly float[] Table = BuildTable();

    private static float[] BuildTable()
    {
        var t = new float[256];
        for (var i = 0; i < 256; ++i) t[i] = (float)ColorMath.SrgbToLinear(i / 255.0);
        return t;
    }

    public static float ToLinear(byte v) => Table[v];
}
