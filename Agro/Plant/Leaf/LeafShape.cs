using System.Numerics;
using System.Text.Json.Serialization;

namespace Agro;

/// <summary>Leaf shape strategy. Supports area computation + outline sampling for mesh generation.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$shape")]
[JsonDerivedType(typeof(EllipticLeafShape), "elliptic")]
[JsonDerivedType(typeof(LanceolateLeafShape), "lanceolate")]
[JsonDerivedType(typeof(OvateLeafShape), "ovate")]
[JsonDerivedType(typeof(MapleLikeLeafShape), "mapleLike")]
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