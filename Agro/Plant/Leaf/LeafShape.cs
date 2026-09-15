using System.Numerics;
using System.Text.Json.Serialization;

namespace Agro;

/// <summary>Leaf shape strategy. Supports area computation + outline sampling for mesh generation.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$shape")]
[JsonDerivedType(typeof(EllipticLeafShape), "elliptic")]
[JsonDerivedType(typeof(LanceolateLeafShape), "lanceolate")]
[JsonDerivedType(typeof(OvateLeafShape), "ovate")]
[JsonDerivedType(typeof(MapleLikeLeafShape), "mapleLike")]
[JsonDerivedType(typeof(LinearLeafShape), "linear")]
public interface ILeafShape
{
    /// <summary>Area in m^2 based on dimensions. Should match outline generator roughly.</summary>
    float AreaM2(in LeafDimensions dims);

    /// <summary>
    /// Outline in local 2D plane (x across width, y along length), normalized in meters.
    /// t in [0,1) goes around the perimeter (CCW).
    /// </summary>
    Vector2 OutlinePoint(in LeafDimensions dims, float t);

    /// <summary>
    /// Outline in local 2D plane (x across width, y along length), normalized to [-1, 1].
    /// t in [0,1) goes around the perimeter (CCW).
    /// </summary>
    Vector2 OutlinePointInvariant(float t);

    /// <summary>
    /// Optional: recommended tessellation resolution for this shape.
    /// </summary>
    int RecommendedSegments { get; }
}

public sealed record EllipticLeafShape : ILeafShape
{
    public int RecommendedSegments => 48;

    public float AreaM2(in LeafDimensions dims)
    {
        // Ellipse area = pi * a * b with a=length/2, b=width/2
        var a = dims.LengthM * 0.5f;
        var b = dims.MaxWidthM * 0.5f;
        return MathF.PI * a * b;
    }

    public Vector2 OutlinePoint(in LeafDimensions dims, float t)
    {
        var theta = t * MathF.Tau;
        var a = dims.MaxWidthM * 0.5f;
        var b = dims.LengthM * 0.5f;
        // x across width, y along length
        return new Vector2(a * MathF.Cos(theta), b * MathF.Sin(theta));
    }

    public Vector2 OutlinePointInvariant(float t)
    {
        var theta = t * MathF.Tau;
        // x across width, y along length
        return new Vector2(MathF.Cos(theta), MathF.Sin(theta));
    }
}

public sealed record LanceolateLeafShape : ILeafShape
{
    public int RecommendedSegments => 64;

    public float AreaM2(in LeafDimensions dims)
    {
        // Approx. lanceolate as ellipse * factor (narrower tips)
        var ellipse = new EllipticLeafShape().AreaM2(dims);
        return ellipse * 0.82f;
    }

    public Vector2 OutlinePoint(in LeafDimensions dims, float t)
    {
        // A simple superellipse-ish profile along length for pointy ends
        var theta = t * MathF.Tau;
        var a = dims.MaxWidthM * 0.5f;
        var b = dims.LengthM * 0.5f;

        var x = MathF.Cos(theta);
        var y = MathF.Sin(theta);

        // Sharpen tips by increasing exponent on y
        var p = 1.7f;
        var yp = MathF.Sign(y) * MathF.Pow(MathF.Abs(y), p);

        return new Vector2(a * x * (0.9f + 0.1f * (1f - MathF.Abs(yp))), b * yp);
    }

    public Vector2 OutlinePointInvariant(float t)
    {
        // A simple superellipse-ish profile along length for pointy ends
        var theta = t * MathF.Tau;

        var x = MathF.Cos(theta);
        var y = MathF.Sin(theta);

        // Sharpen tips by increasing exponent on y
        var p = 1.7f;
        var yp = MathF.Sign(y) * MathF.Pow(MathF.Abs(y), p);

        return new Vector2(x * (0.9f + 0.1f * (1f - MathF.Abs(yp))), yp);
    }
}

public sealed record OvateLeafShape : ILeafShape
{
    public int RecommendedSegments => 64;

    public float AreaM2(in LeafDimensions dims)
    {
        // Ovate: slightly broader near base
        var ellipse = new EllipticLeafShape().AreaM2(dims);
        return ellipse * 0.92f;
    }

    public Vector2 OutlinePoint(in LeafDimensions dims, float t)
    {
        var theta = t * MathF.Tau;
        var a = dims.MaxWidthM * 0.5f;
        var b = dims.LengthM * 0.5f;

        var x = MathF.Cos(theta);
        var y = MathF.Sin(theta);

        // Bias width toward base (negative y if we treat y up as apex; define base at y=-b)
        var baseBias = 0.25f;
        var widthScale = 1f + baseBias * (-y); // when y negative -> wider
        return new Vector2(a * x * widthScale, b * y);
    }

    public Vector2 OutlinePointInvariant(float t)
    {
        var theta = t * MathF.Tau;

        var x = MathF.Cos(theta);
        var y = MathF.Sin(theta);

        // Bias width toward base (negative y if we treat y up as apex; define base at y=-b)
        var baseBias = 0.25f;
        var widthScale = 1f + baseBias * (-y); // when y negative -> wider
        return new Vector2(x * widthScale, y);
    }
}

/// <summary>
/// Placeholder for lobed shapes like maple/grape. Replace with your own parametric lobe function.
/// </summary>
public sealed record MapleLikeLeafShape : ILeafShape
{
    public int Lobes { get; init; } = 5;
    public float LobeDepth01 { get; init; } = 0.35f;
    public int RecommendedSegments => 128;

    public float AreaM2(in LeafDimensions dims)
    {
        // Rough estimate: ellipse scaled down by lobe cutouts
        var ellipse = new EllipticLeafShape().AreaM2(dims);
        return ellipse * (1f - 0.12f * Math.Clamp(LobeDepth01, 0f, 1f));
    }

    public Vector2 OutlinePoint(in LeafDimensions dims, float t)
    {
        var theta = t * MathF.Tau;
        var a = dims.MaxWidthM * 0.5f;
        var b = dims.LengthM * 0.5f;

        // Radial modulation for lobes
        var lobeWave = 0.5f * (1f + MathF.Cos(Lobes * theta));
        var r = 1f - LobeDepth01 * lobeWave;

        return new Vector2(a * r * MathF.Cos(theta), b * r * MathF.Sin(theta));
    }

    public Vector2 OutlinePointInvariant(float t)
    {
        var theta = t * MathF.Tau;

        // Radial modulation for lobes
        var lobeWave = 0.5f * (1f + MathF.Cos(Lobes * theta));
        var r = 1f - LobeDepth01 * lobeWave;

        return new Vector2(r * MathF.Cos(theta), r * MathF.Sin(theta));
    }
}

/// <summary>
/// A strap: parallel sided over most of its length, tapering only into an acuminate tip.
/// </summary>
/// <remarks>
/// The grass and sedge outline. An ellipse is the wrong silhouette for one of these - at a
/// 50:1 aspect ratio it reads as a very long lens, at full width only at its midpoint - and
/// it is doubly wrong for a blade drawn as a chain of segments, because a segment near the
/// tip then comes out a fraction of the width of a segment near the middle.
/// </remarks>
public sealed record LinearLeafShape : ILeafShape
{
    /// <summary>Where the tip taper begins, measured from the base (0) to the tip (1).</summary>
    public float TipStart { get; init; } = 0.75f;

    /// <summary>How sharply the tip draws in. Above one the taper is concave, i.e. acuminate.</summary>
    public float TipSharpness { get; init; } = 1.6f;

    /// <summary>Half width where the blade leaves the sheath, as a fraction of the maximum.</summary>
    public float BaseWidth01 { get; init; } = 0.6f;

    /// <summary>How far the basal widening reaches, measured from the base.</summary>
    public float BaseRun { get; init; } = 0.06f;

    //A strap is nearly all straight edge, but the tip taper and the sheath both need enough
    //samples not to read as facets - and a segmented blade divides whatever it is given.
    public int RecommendedSegments => 128;

    /// <summary>Half width at <paramref name="s"/>, the normalized distance from base to tip.</summary>
    float HalfWidth(float s)
    {
        s = Math.Clamp(s, 0f, 1f);
        if (s < BaseRun)
            return BaseWidth01 + (1f - BaseWidth01) * (s / MathF.Max(BaseRun, 1e-6f));
        if (s <= TipStart)
            return 1f;
        var t = (s - TipStart) / MathF.Max(1f - TipStart, 1e-6f);
        return MathF.Pow(MathF.Max(0f, 1f - t), TipSharpness);
    }

    public float AreaM2(in LeafDimensions dims)
    {
        //Area is the width profile integrated along the length; the profile is piecewise and
        //cheap to sample, and this is only ever called to size leaflets.
        const int samples = 64;
        var sum = 0f;
        for (int i = 0; i < samples; ++i)
            sum += HalfWidth((i + 0.5f) / samples);
        return dims.MaxWidthM * dims.LengthM * sum / samples;
    }

    public Vector2 OutlinePoint(in LeafDimensions dims, float t)
    {
        var p = OutlinePointInvariant(t);
        return new(p.X * dims.MaxWidthM * 0.5f, p.Y * dims.LengthM * 0.5f);
    }

    public Vector2 OutlinePointInvariant(float t)
    {
        //Up one margin and back down the other, so the loop runs counter-clockwise in (across,
        //along) exactly as the conic shapes above do.
        t -= MathF.Floor(t);
        var side = t < 0.5f ? 1f : -1f;
        var s = t < 0.5f ? t * 2f : (1f - t) * 2f;
        return new(side * HalfWidth(s), s * 2f - 1f);
    }
}
