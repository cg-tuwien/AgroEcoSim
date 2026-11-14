using System.Numerics;

namespace Agro;

/// <summary>Immutable fruit body shared by contacts, shadows and recorded playback.</summary>
internal static class FruitGeometry
{
    // Eight sides and eight latitude bands. The axial extrema are exact, and the
    // same triangles travel with the frame: a fruit never casts a different silhouette.
    internal static readonly LaminaTemplate Body = Build();

    static LaminaTemplate Build()
    {
        const int sides = 8, bands = 8;
        var vertices = new List<Vector3>((bands + 1) * (sides + 1));
        var indices = new List<int>(sides * (bands - 1) * 6);
        for (var band = 0; band <= bands; ++band)
        {
            var angle = MathF.PI * band / bands;
            var x = MathF.Cos(angle);
            var r = band is 0 or bands ? 0f : MathF.Sin(angle);
            for (var side = 0; side <= sides; ++side)
            {
                var azimuth = MathF.Tau * side / sides;
                vertices.Add(new(x, r * MathF.Cos(azimuth), r * MathF.Sin(azimuth)));
            }
        }
        for (var band = 0; band < bands; ++band)
        for (var side = 0; side < sides; ++side)
        {
            var a = band * (sides + 1) + side;
            var b = a + sides + 1;
            if (band > 0) { indices.Add(a); indices.Add(b); indices.Add(a + 1); }
            if (band < bands - 1) { indices.Add(a + 1); indices.Add(b); indices.Add(b + 1); }
        }
        // Template X maps [-1,1] to [base,tip], Y to half-width and Z to relief.
        // A unit reference half-width makes both transverse semiaxes equal Radius.
        return new(new MeshData { Positions = vertices, Indices = indices }, referenceHalfWidth: 1f);
    }
}
