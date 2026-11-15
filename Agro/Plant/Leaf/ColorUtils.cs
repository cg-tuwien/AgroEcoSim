using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Agro;

public readonly struct RGB8
{
    public readonly byte Red;
    public readonly byte Green;
    public readonly byte Blue;
    public RGB8(byte red, byte green, byte blue)
    {
        Red = red;
        Green = green;
        Blue = blue;
    }
}

public static class ColorUtils
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 OkLabFromSrgb8(byte r, byte g, byte b)
    {
        float rl = SrgbToLinear(r / 255f);
        float gl = SrgbToLinear(g / 255f);
        float bl = SrgbToLinear(b / 255f);
        return LinearRgbToOklab(new Vector3(rl, gl, bl));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SrgbToLinear(float x)
    {
        if (x <= 0.04045f)
            return x / 12.92f;

        return MathF.Pow((x + 0.055f) / 1.055f, 2.4f);
    }

    /// <summary>An sRGB colour with channels from 0 to 1, as presets author it, in linear light.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 SrgbToLinear(Vector3 srgb)
        => new(SrgbToLinear(srgb.X), SrgbToLinear(srgb.Y), SrgbToLinear(srgb.Z));

    /// <summary>
    /// OKLab of a CIELAB colour (D65, as a colorimeter or a Munsell conversion reports it).
    /// </summary>
    /// <remarks>
    /// Leaf, petal and dead-tissue colours are measured in CIELAB, so presets author them that
    /// way and the value can be checked against the measurement it came from. Out-of-gamut
    /// values are kept as they are; the wire encoding clamps.
    /// </remarks>
    public static Vector3 OkLabFromCieLab(float L, float a, float b)
    {
        static float Inverse(float t) => t * t * t > 216f / 24389f ? t * t * t : (116f * t - 16f) * (27f / 24389f);
        var fy = (L + 16f) / 116f;
        var x = 0.95047f * Inverse(fy + a / 500f);
        var y = Inverse(fy);
        var z = 1.08883f * Inverse(fy - b / 200f);
        var linear = new Vector3(
            3.2404542f * x - 1.5371385f * y - 0.4985314f * z,
            -0.9692660f * x + 1.8760108f * y + 0.0415560f * z,
            0.0556434f * x - 0.2040259f * y + 1.0572252f * z);
        return LinearRgbToOklab(Vector3.Max(linear, Vector3.Zero));
    }

    /// <summary>CIELAB (D65) of an OKLab colour: the inverse of <see cref="OkLabFromCieLab"/>, for checks.</summary>
    public static Vector3 CieLabFromOkLab(Vector3 lab)
    {
        var rgb = Vector3.Max(OklabToLinearRgb(lab), Vector3.Zero);
        var x = (0.4124564f * rgb.X + 0.3575761f * rgb.Y + 0.1804375f * rgb.Z) / 0.95047f;
        var y = 0.2126729f * rgb.X + 0.7151522f * rgb.Y + 0.0721750f * rgb.Z;
        var z = (0.0193339f * rgb.X + 0.1191920f * rgb.Y + 0.9503041f * rgb.Z) / 1.08883f;
        static float F(float t) => t > 216f / 24389f ? MathF.Cbrt(t) : (24389f / 27f * t + 16f) / 116f;
        return new Vector3(116f * F(y) - 16f, 500f * (F(x) - F(y)), 200f * (F(y) - F(z)));
    }

    /// <summary>OKLab of an sRGB colour with channels from 0 to 1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 OkLabFromSrgb(Vector3 srgb) => LinearRgbToOklab(SrgbToLinear(srgb));

    #region Wire colour
    // Every colour a frame carries is an sRGB-encoded ALBEDO, and every colour a preset authors
    // is an APPEARANCE: what the organ should look like in the viewport. The two differ by the
    // light. The viewers shade with a hemisphere of intensity 3.175 (white sky, #707070 ground:
    // HEMI_* in ThreeFrontend/src/render/core/Lighting.ts, mirrored by the Godot viewer), so an
    // albedo of x is shown as 3.175 x on a sky-facing sheet and as 1.845 x averaged over the
    // normals of a cylinder or an ellipsoid. Dividing the authored colour by that exposure makes
    // the screen show what the preset says. See Agro/ORGAN_COLORS.md.

    /// <summary>Hemisphere irradiance on a sky-facing lamina; the viewers' HEMI_INTENSITY.</summary>
    public const float LaminaExposure = 3.175f;

    /// <summary>Mean hemisphere irradiance over the normals of a cylinder or an ellipsoid:
    /// 3.175 (1 + linear(#70)) / 2.</summary>
    public const float RoundOrganExposure = 1.845f;

    const int SrgbTableSize = 4096;
    static readonly byte[] LinearToSrgb8Table = BuildLinearToSrgb8();

    static byte[] BuildLinearToSrgb8()
    {
        var table = new byte[SrgbTableSize];
        for (int i = 0; i < SrgbTableSize; ++i)
        {
            var x = i / (float)(SrgbTableSize - 1);
            var encoded = x <= 0.0031308f ? x * 12.92f : 1.055f * MathF.Pow(x, 1f / 2.4f) - 0.055f;
            table[i] = (byte)Math.Clamp((int)MathF.Round(encoded * 255f), 0, 255);
        }
        return table;
    }

    /// <summary>sRGB byte of a linear value, by table: the export encodes three channels per organ.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte LinearToSrgb8(float linear)
        => LinearToSrgb8Table[(int)(Math.Clamp(linear, 0f, 1f) * (SrgbTableSize - 1) + 0.5f)];

    /// <summary>The wire bytes of a linear appearance colour lit at <paramref name="exposure"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RGB8 AppearanceToWire(Vector3 linearAppearance, float exposure)
    {
        var scale = 1f / exposure;
        return new RGB8(
            LinearToSrgb8(linearAppearance.X * scale),
            LinearToSrgb8(linearAppearance.Y * scale),
            LinearToSrgb8(linearAppearance.Z * scale));
    }

    /// <summary>The wire bytes of an OKLab appearance colour lit at <paramref name="exposure"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RGB8 OklabToWire(Vector3 lab, float exposure)
        => AppearanceToWire(OklabToLinearRgb(lab), exposure);
    #endregion


    // Assumes RGB is LINEAR sRGB in [0,1].
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 LinearRgbToOklab(Vector3 rgb)
    {
        var r = rgb.X;
        var g = rgb.Y;
        var b = rgb.Z;

        var l = 0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b;
        var m = 0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b;
        var s = 0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b;

        var l_ = Cbrt(l);
        var m_ = Cbrt(m);
        var s_ = Cbrt(s);

        var L = 0.2104542553f * l_ + 0.7936177850f * m_ - 0.0040720468f * s_;
        var A = 1.9779984951f * l_ - 2.4285922050f * m_ + 0.4505937099f * s_;
        var B = 0.0259040371f * l_ + 0.7827717662f * m_ - 0.8086757660f * s_;

        return new Vector3(L, A, B);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 OklabToLinearRgb(Vector3 lab)
    {
        var lms = new Vector3(
            Vector3.Dot(lab, new Vector3(1f,  0.3963377774f,  0.2158037573f)),
            Vector3.Dot(lab, new Vector3(1f, -0.1055613458f, -0.0638541728f)),
            Vector3.Dot(lab, new Vector3(1f, -0.0894841775f, -1.2914855480f)));

        lms = lms * lms * lms;
        return new Vector3(
            Vector3.Dot(lms, new Vector3(4.0767416621f, -3.3077115913f, 0.2309699292f)),
            Vector3.Dot(lms, new Vector3(-1.2684380046f, 2.6097574011f, -0.3413193965f)),
            Vector3.Dot(lms, new Vector3(-0.0041960863f, -0.7034186147f, 1.7076147010f))
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 OklabToOklch(Vector3 lab)
    {
        var L = lab.X;
        var a = lab.Y;
        var b = lab.Z;

        var C = MathF.Sqrt(a * a + b * b);
        var h = MathF.Atan2(b, a) * (180f / MathF.PI);
        if (h < 0f) h += 360f;

        return new Vector3(L, C, h);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 OklchToOklab(Vector3 lch)
    {
        var L = lch.X;
        var C = lch.Y;
        var hRad = lch.Z * (MathF.PI / 180f);

        var a = C * MathF.Cos(hRad);
        var b = C * MathF.Sin(hRad);

        return new Vector3(L, a, b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Clamp01(Vector3 v) => Vector3.Clamp(v, Vector3.Zero, Vector3.One);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Saturate(float x) => Math.Clamp(x, 0f, 1f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Saturate((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Cbrt(float x)
    {
        // inputs should be non-negative in linear RGB workflows,
        // but this keeps it safe.
        return x >= 0f ? MathF.Pow(x, 1f / 3f) : -MathF.Pow(-x, 1f / 3f);
    }
}