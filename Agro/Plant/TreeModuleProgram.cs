using System.Numerics;

namespace Agro;

/// <summary>Reference-local geometry of a newly developing module; no plant or clock state.</summary>
internal static class TreeModuleProgram
{
    /// <summary>Mean of the saturated smoothstep over the complete, ordered interval.</summary>
    internal static float MeanSaturatedSmoothstep(float start, float end)
    {
        if (!float.IsFinite(start) || !float.IsFinite(end) || end < start)
            throw new ArgumentOutOfRangeException(nameof(end), "A finite ordered interval is required.");
        return (float)Mean(start, end);
    }

    // I(x)=0 below zero, x^3-x^4/2 inside the transition, and x-1/2 above one.
    // Keep the zero/one tails in the denominator: clamping both endpoints before
    // integration incorrectly changes the mean of an interval crossing a boundary.
    static double Mean(double start, double end)
    {
        if (end <= 0d) return 0d;
        if (start >= 1d) return 1d;
        if (start == end) return Smooth(start);
        static double Integral(double x) => x * x * x * (1d - .5d * x);
        var lower = Math.Max(0d, start);
        var upper = Math.Min(1d, end);
        var integral = upper > lower ? Integral(upper) - Integral(lower) : 0d;
        if (end > 1d) integral += end - Math.Max(1d, start);
        return integral / (end - start);
    }

    static double Smooth(double value)
        => value <= 0d ? 0d : value >= 1d ? 1d : value * value * (3d - 2d * value);

    /// <summary>
    /// Absolute programme in the frozen reference frame. The mean scalar opening
    /// and arch phases approximate a finite internode; they are not a mean of
    /// quaternions or an exact integral of a curved centreline. Point phases apply
    /// at the tip or at a zero-width interval. The caller handles old==new carry
    /// as exact Identity and composes its frozen reference on the left.
    /// </summary>
    internal static Quaternion LocalFrame(Quaternion openingStartLocal, float openingLength,
        float allowance, float archRadians, float startExtension, float extension, bool atTip = false)
    {
        if (!float.IsFinite(openingLength) || !float.IsFinite(allowance)
            || !(openingLength > 0f && openingLength < allowance))
            throw new ArgumentOutOfRangeException(nameof(openingLength), "Require finite0<openingLength<allowance.");
        if (!float.IsFinite(archRadians))
            throw new ArgumentOutOfRangeException(nameof(archRadians));
        if (!float.IsFinite(startExtension) || !float.IsFinite(extension) || extension < startExtension)
            throw new ArgumentOutOfRangeException(nameof(extension), "A finite ordered accepted interval is required.");
        if (!float.IsFinite(openingStartLocal.X) || !float.IsFinite(openingStartLocal.Y)
            || !float.IsFinite(openingStartLocal.Z) || !float.IsFinite(openingStartLocal.W)
            || Math.Abs((double)openingStartLocal.X * openingStartLocal.X
                + (double)openingStartLocal.Y * openingStartLocal.Y
                + (double)openingStartLocal.Z * openingStartLocal.Z
                + (double)openingStartLocal.W * openingStartLocal.W - 1d) > 1e-4d)
            throw new ArgumentException("A finite unit opening frame is required.", nameof(openingStartLocal));

        // Double ratios also keep finite float inputs well-defined when L is tiny.
        var start = atTip ? extension : startExtension;
        var opening = (float)Mean((double)start / openingLength, (double)extension / openingLength);
        var archLength = (double)allowance - openingLength;
        var arch = (float)Mean(((double)start - openingLength) / archLength,
            ((double)extension - openingLength) / archLength);
        var openingFrame = opening == 0f ? openingStartLocal
            : opening == 1f || openingStartLocal == Quaternion.Identity ? Quaternion.Identity
            : Quaternion.Slerp(openingStartLocal, Quaternion.Identity, opening);
        if (arch == 0f || archRadians == 0f) return openingFrame;
        var archFrame = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -archRadians * arch);
        return openingFrame == Quaternion.Identity ? archFrame : archFrame * openingFrame;
    }
}
