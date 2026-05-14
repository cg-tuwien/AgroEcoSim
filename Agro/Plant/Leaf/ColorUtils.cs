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
    private static float SrgbToLinear(float x)
    {
        if (x <= 0.04045f)
            return x / 12.92f;

        return MathF.Pow((x + 0.055f) / 1.055f, 2.4f);
    }


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
    public static RGB8 OklabToLinearRgb8(Vector3 lab)
    {
        var lms = new Vector3(
            Vector3.Dot(lab, new Vector3(1f,  0.3963377774f,  0.2158037573f)),
            Vector3.Dot(lab, new Vector3(1f, -0.1055613458f, -0.0638541728f)),
            Vector3.Dot(lab, new Vector3(1f, -0.0894841775f, -1.2914855480f)));

        lms = lms * lms * lms;
        return new RGB8(
            (byte)Math.Round(Math.Clamp(Vector3.Dot(lms, new Vector3(255f * 4.0767416621f, 255f * -3.3077115913f, 255f * 0.2309699292f)), 0, 255)),
            (byte)Math.Round(Math.Clamp(Vector3.Dot(lms, new Vector3(255f * -1.2684380046f, 255f * 2.6097574011f, 255f * -0.3413193965f)), 0, 255)),
            (byte)Math.Round(Math.Clamp(Vector3.Dot(lms, new Vector3(255f * -0.0041960863f, 255f * -0.7034186147f, 255f * 1.7076147010f)), 0, 255))
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