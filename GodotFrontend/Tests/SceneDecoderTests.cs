using System.IO.Compression;
using GodotFrontend.Core.Color;
using GodotFrontend.Core.Geometry;
using GodotFrontend.Core.Scene;
using Xunit;

namespace GodotFrontend.Tests;

public static class Fixtures
{
    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", name);
    public static byte[] Bytes(string name) => File.ReadAllBytes(Path(name));

    /// <summary>Run-store frames are stored as zlib streams (.g); inflate them as the client does.</summary>
    public static byte[] Inflate(byte[] packed)
    {
        using var input = new MemoryStream(packed);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }
}

public class SceneDecoderTests
{
    private static DecodedScene Decode(byte[] bytes, GeometryLibrary? lib = null)
    {
        lib ??= new GeometryLibrary();
        return new SceneDecoder(lib).Decode(bytes);
    }

    [Fact]
    public void EmptyV7FrameDecodes()
    {
        var s = Decode(Fixtures.Bytes("v7-frame-0.bin"));
        Assert.Equal(7, s.Version);
        Assert.True(s.Analytics);
        Assert.False(s.RootsDownloaded);
        Assert.Equal(0, s.PlantCount);
        Assert.Equal(0, s.Count);
    }

    [Fact]
    public void V7FixtureFrameDecodesWithPlausibleData()
    {
        var s = Decode(Fixtures.Bytes("v7-frame-1.bin"));
        Assert.Equal(7, s.Version);
        Assert.Equal(1, s.PlantCount);
        Assert.True(s.Count > 50, $"primitives: {s.Count}");
        Assert.Equal(s.Count, s.PlantDrawCount[0]);
        Assert.Single(s.Species);
        Assert.NotNull(s.Stats);
        AssertPlausible(s);
        // every lamina and cylinder in V7+ carries a colour; the fixture has no roots
        for (var i = 0; i < s.Count; ++i)
        {
            Assert.True(s.HasColor(i));
            Assert.NotEqual(PrimitiveType.Box, s.Kind[i]);
        }
    }

    [Theory]
    [InlineData("v8-single-first.g", 0)]   // step 0: nothing has germinated, only the asset table travels
    [InlineData("v8-single-mid.g", 1)]
    [InlineData("v8-single-last.g", 1)]
    [InlineData("v8-facade-first.g", 0)]
    [InlineData("v8-facade-early.g", -1)]  // a few hundred kilobytes of a 4,134-plant stand; count unknown but large
    public void V8StoredFramesDecode(string name, int plants)
    {
        var lib = new GeometryLibrary();
        var bytes = Fixtures.Inflate(Fixtures.Bytes(name));
        var s = Decode(bytes, lib);
        Assert.Equal(8, s.Version);
        Assert.True(s.Analytics);
        if (plants >= 0) Assert.Equal(plants, s.PlantCount);
        else Assert.True(s.PlantCount > 1000, $"plants: {s.PlantCount}");
        if (s.PlantCount > 0)
        {
            Assert.True(s.Count > 0);
            Assert.NotEmpty(s.Assets);
        }
        Assert.True(lib.Count > Geom.StaticCount, "recorded assets were registered");
        AssertPlausible(s);
        // decoding again with the same library skips the asset table and gives the same frame
        var again = Decode(bytes, lib);
        Assert.Equal(s.Count, again.Count);
        Assert.Equal(s.Transform, again.Transform);
        Assert.Equal(s.Stats, again.Stats);
    }

    [Fact]
    public void LeafChainsGetIncreasingSlices()
    {
        var s = Decode(Fixtures.Inflate(Fixtures.Bytes("v8-single-last.g")));
        var leaves = 0;
        var maxSlice = 0;
        for (var i = 0; i < s.Count; ++i)
            if (s.Organ[i] == OrganType.Leaf)
            {
                leaves++;
                maxSlice = System.Math.Max(maxSlice, s.LaminaSlice[i]);
            }
        Assert.True(leaves > 0);
        Assert.True(maxSlice >= 0);
    }

    [Fact]
    public void TruncatedStreamThrows()
    {
        var bytes = Fixtures.Bytes("v7-frame-1.bin");
        Assert.Throws<InvalidDataException>(() => Decode(bytes.AsSpan(0, bytes.Length - 7).ToArray()));
        var longer = bytes.Concat(new byte[] { 1, 2, 3 }).ToArray();
        Assert.Throws<InvalidDataException>(() => Decode(longer));
    }

    [Fact]
    public void NaturalColoursFollowTheWebRules()
    {
        var s = Decode(Fixtures.Inflate(Fixtures.Bytes("v8-single-mid.g")));
        var sawLeaf = false;
        var sawStem = false;
        for (var i = 0; i < s.Count && !(sawLeaf && sawStem); ++i)
        {
            var c = PlantColors.Of(s, i, VisualMapping.Natural);
            Assert.Equal(RGB.Black, c.Emissive);
            if (s.Organ[i] == OrganType.Leaf)
            {
                sawLeaf = true;
                Assert.Equal(ColorMath.FromSrgb8(s.Rgb[i * 3], s.Rgb[i * 3 + 1], s.Rgb[i * 3 + 2]), c.Color);
            }
            if (s.Organ[i] == OrganType.Stem)
            {
                sawStem = true;
                var wood = s.Stats![i * Stat.Count + Stat.IrradianceOrWood];
                Assert.Equal(ColorMath.LerpHSL(Ramps.Green, Ramps.Wood, wood), c.Color);
            }
        }
        Assert.True(sawLeaf && sawStem);
        // a data mapping is unlit: black colour, ramp in emissive
        var w = PlantColors.Of(s, 0, VisualMapping.Water);
        Assert.Equal(RGB.Black, w.Color);
        Assert.Equal(Ramps.Water(s.Stats![Stat.Water]), w.Emissive);
    }

    private static void AssertPlausible(DecodedScene s)
    {
        Assert.Equal(s.Count * 12, s.Transform.Length);
        for (var i = 0; i < s.Transform.Length; ++i) Assert.True(float.IsFinite(s.Transform[i]));
        for (var i = 0; i < s.Count; ++i)
        {
            Assert.True(s.Length[i] >= 0 && s.Radius[i] >= 0);
            if (s.Kind[i] == PrimitiveType.Cylinder) Assert.True(s.Length[i] > 0 && s.Radius[i] > 0, $"cylinder {i} has length {s.Length[i]} radius {s.Radius[i]}");
            Assert.InRange(s.Plant[i], 0, s.PlantCount - 1);
            var st = s.StatsOf(i);
            Assert.InRange(st[Stat.Water], -1e-3, 1.5);
            Assert.InRange(st[Stat.RelResource], 0, 1 + 1e-5);
            Assert.InRange(st[Stat.RelProduction], 0, 1 + 1e-5);
            Assert.True(float.IsFinite(st[Stat.IrradianceOrWood]));
        }
        for (var p = 0; p < s.PlantCount; ++p)
        {
            Assert.InRange(s.PlantFirst[p], 0, s.Count);
            Assert.Equal(p == s.PlantCount - 1 ? s.Count : s.PlantFirst[p + 1], s.PlantFirst[p] + s.PlantDrawCount[p]);
            for (var c = 0; c < 3; ++c) Assert.True(s.PlantBoundsMin[p * 3 + c] <= s.PlantBoundsMax[p * 3 + c]);
        }
        Assert.True(s.SceneMax[1] - s.SceneMin[1] < 50, "scene extent is plausible");
    }
}

public class TerrainDecoderTests
{
    [Fact]
    public void FacadeFixtureDecodes()
    {
        var t = TerrainDecoder.Decode(Fixtures.Bytes("terrain-facade.bin"));
        Assert.True(t.Items.Count > 0);
        Assert.NotNull(t.ObstacleMesh);
        Assert.True(t.ObstacleMesh!.VertexCount > 0);
        Assert.Equal(t.ObstacleMesh.Positions.Length, t.ObstacleMesh.Normals.Length);
        if (t.Version >= 1)
        {
            Assert.NotEmpty(t.Groups);
            foreach (var g in t.Groups)
                Assert.True(g.VertexFirst + g.VertexCount <= t.ObstacleMesh.VertexCount);
        }
    }

    [Fact]
    public void LaminaMeshReaderRoundTrips()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
        {
            w.Write((byte)2);
            for (var s = 0; s < 2; ++s)
            {
                w.Write(3);
                foreach (var f in new float[] { -1, -1, 0, 1, -1, 0, 0, 1, 0 }) w.Write(f);
                w.Write(3);
                w.Write(0); w.Write(1); w.Write(2);
            }
        }
        var slices = LaminaMeshReader.ReadSlices(ms.ToArray(), "leaf:test");
        Assert.Equal(2, slices.Length);
        Assert.Equal(3, slices[0].VertexCount);
        Assert.Equal(1, slices[0].TriangleCount);
        Assert.Equal(1f, slices[0].Normals[2], 6); // +Z normal for a counter-clockwise triangle in the xy plane
    }
}

public class StaticGeometryTests
{
    private static readonly GeometryLibrary Lib = new();

    [Theory]
    [InlineData(Geom.BoxCentered, 24, 36)]
    [InlineData(Geom.BoxCorner, 24, 36)]
    [InlineData(Geom.BoxBottomCentered, 24, 36)]
    [InlineData(Geom.Cylinder, 52, 96)]
    [InlineData(Geom.Sphere, 81, 336)]
    [InlineData(Geom.Plane, 4, 6)]
    [InlineData(Geom.CircleHalf, 14, 36)]
    [InlineData(Geom.CircleUnit, 34, 96)]
    [InlineData(Geom.Dodecahedron, 108, 108)]
    [InlineData(Geom.DebugBox, 24, 36)]
    public void CountsMatchThree(int id, int vertices, int indices)
    {
        var m = Lib.Get(id);
        Assert.Equal(vertices, m.VertexCount);
        Assert.Equal(indices, m.Indices.Length);
        Assert.Equal(m.Positions.Length, m.Normals.Length);
    }

    [Fact]
    public void BoxCornerSpansTheUnitCube()
    {
        var (min, max) = Lib.Get(Geom.BoxCorner).Bounds();
        Assert.Equal(new float[] { 0, 0, 0 }, min);
        Assert.Equal(new float[] { 1, 1, 1 }, max);
        var (bmin, bmax) = Lib.Get(Geom.BoxBottomCentered).Bounds();
        Assert.Equal(new float[] { -0.5f, 0, -0.5f }, bmin);
        Assert.Equal(new float[] { 0.5f, 1, 0.5f }, bmax);
    }

    [Fact]
    public void CylinderIsUnitRadiusAndCentred()
    {
        var (min, max) = Lib.Get(Geom.Cylinder).Bounds();
        Assert.Equal(-0.5f, min[1]);
        Assert.Equal(0.5f, max[1]);
        Assert.Equal(1f, max[0], 5);
        Assert.Equal(-1f, min[2], 5);
    }

    [Fact]
    public void CirclesLieInTheGroundPlaneAfterRotation()
    {
        var c = Lib.Get(Geom.CircleUnit);
        for (var i = 0; i < c.VertexCount; ++i)
        {
            Assert.Equal(0f, c.Positions[i * 3 + 1], 5);
            Assert.Equal(1f, System.Math.Abs(c.Normals[i * 3 + 1]), 5);
        }
        var (min, max) = Lib.Get(Geom.CircleHalf).Bounds();
        Assert.Equal(0.5f, max[0], 5);
        Assert.Equal(-0.5f, min[0], 5);
    }

    [Fact]
    public void DodecahedronIsOnTheSphere()
    {
        var d = Lib.Get(Geom.Dodecahedron);
        for (var i = 0; i < d.VertexCount; ++i)
        {
            double x = d.Positions[i * 3], y = d.Positions[i * 3 + 1], z = d.Positions[i * 3 + 2];
            Assert.Equal(0.03, System.Math.Sqrt(x * x + y * y + z * z), 5);
        }
    }
}

public class ColorTests
{
    [Fact]
    public void HexDecodesToLinear()
    {
        var c = ColorMath.FromHex("#909090");
        Assert.Equal(0.27889, c.R, 4);
        Assert.Equal(c.R, c.G);
        Assert.Equal("#909090", ColorMath.ToHex(c));
        Assert.Equal(ColorMath.FromHex("#999999"), ColorMath.FromHex("#999"));
    }

    [Fact]
    public void LerpHslEndpointsAndHeatRamp()
    {
        var start = ColorMath.LerpHSL(Ramps.Green, Ramps.Wood, 0);
        Assert.Equal(Ramps.Green.R, start.R, 6);
        Assert.Equal(Ramps.Green.G, start.G, 6);
        Assert.Equal(Ramps.Green.B, start.B, 6);
        var end = ColorMath.LerpHSL(Ramps.Green, Ramps.Wood, 1);
        Assert.Equal(Ramps.Wood.R, end.R, 5);
        Assert.Equal(Ramps.Wood.G, end.G, 5);
        Assert.Equal(Ramps.Wood.B, end.B, 5);
        Assert.Equal(ColorMath.FromHex("#ffa600"), Ramps.Heat(1));
        Assert.Equal(ColorMath.FromHex("#003f5c"), Ramps.Heat(0));
        Assert.Equal(Ramps.WaterFull, Ramps.Water(1));
    }

    [Fact]
    public void PaletteMatchesTheJavaScriptShuffle()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Fixtures.Path("palette-expected.json")));
        var rnd = Palette.Mulberry32(Palette.GreenPaletteSeed);
        var first = doc.RootElement.GetProperty("firstRandoms").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        foreach (var expected in first) Assert.Equal(expected, rnd(), 15);
        var greens = doc.RootElement.GetProperty("greens").EnumerateArray().Select(e => e.GetString()!).ToArray();
        for (var i = 0; i < greens.Length; ++i) Assert.Equal(greens[i], ColorMath.ToHex(Palette.GreenColors[i]));
        Assert.Equal(doc.RootElement.GetProperty("green799").GetString(), ColorMath.ToHex(Palette.GreenColors[799]));
    }

    [Fact]
    public void NeutralIsTheHslBlendOfForegroundAndBackground()
    {
        var n = Theme.Neutral;
        Assert.True(n.R > 0.05f && n.R < 0.95f);
        Assert.Equal(n.R, n.G, 6);
    }
}
