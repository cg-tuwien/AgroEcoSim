using GodotFrontend.Core.Color;
using GodotFrontend.Core.Geometry;
using GodotFrontend.Core.Packing;
using GodotFrontend.Core.Scene;
using Xunit;

namespace GodotFrontend.Tests;

public class FramePackerTests
{
    [Fact]
    public void PacksEveryPrimitiveOnceWithTheRightEncoding()
    {
        var lib = new GeometryLibrary();
        var s = new SceneDecoder(lib).Decode(Fixtures.Inflate(Fixtures.Bytes("v8-single-mid.g")));
        var f = new FramePacker(lib).Pack(s);
        Assert.Equal(s.Count, f.InstanceCount);
        Assert.True(f.TriangleCount > 0);
        Assert.Equal(s.SceneMin, f.SceneMin);

        var seen = new HashSet<int>();
        foreach (var b in f.Buckets.Values)
        {
            Assert.True(b.Count <= b.Capacity);
            for (var slot = 0; slot < b.Count; ++slot)
            {
                var i = b.PrimitiveOf[slot];
                Assert.True(seen.Add(i), "primitive packed twice");
                var d = b.Data.AsSpan(slot * Bucket.Stride, Bucket.Stride);
                var w = s.TransformOf(i);
                if (s.Kind[i] == PrimitiveType.Cylinder)
                {
                    Assert.Equal(Geom.Cylinder, b.Key.GeometryId);
                    Assert.Equal(BucketVariant.Opaque, b.Key.Variant);
                    Assert.Equal(Category.Stems, b.Key.Category);
                    // scaled columns and the half-length recentring
                    Assert.Equal(w[1] * s.Length[i], d[1], 6);
                    Assert.Equal(w[0] * s.Radius[i], d[0], 6);
                    Assert.Equal(w[3] + 0.5f * w[1] * s.Length[i], d[3], 5);
                    Assert.Equal(w[7] + 0.5f * w[5] * s.Length[i], d[7], 5);
                    Assert.Equal(w[11] + 0.5f * w[9] * s.Length[i], d[11], 5);
                }
                else if (s.Kind[i] == PrimitiveType.Rectangle)
                {
                    Assert.Equal(Category.Leaves, b.Key.Category);
                    Assert.True(b.Key.GeometryId >= Geom.FirstDynamic, "recorded asset mesh resolved");
                    for (var k = 0; k < 12; ++k) Assert.Equal(w[k], d[k]);
                }
                var c = PlantColors.Natural(s, i);
                Assert.Equal(c.R, d[InstanceEncoding.OffColor], 6);
                Assert.Equal(1f, d[InstanceEncoding.OffColor + 3]);
                // custom data is exact integers below 2^24, except the raw irradiance/wood slot
                Assert.Equal(System.Math.Round(d[16]), d[16]);
                Assert.Equal(System.Math.Round(d[18]), d[18]);
                Assert.Equal(System.Math.Round(d[19]), d[19]);
                Assert.True(d[16] < 16777216 && d[19] < 16777216);
            }
        }
        Assert.Equal(s.Count, seen.Count);

        // repacking into the same frame reuses the buckets and gives the same counts
        var again = new FramePacker(lib).Pack(s, f);
        Assert.Same(f, again);
        Assert.Equal(s.Count, again.InstanceCount);
    }

    [Fact]
    public void Pack12RoundTrips()
    {
        var v = InstanceEncoding.Pack12(0.25f, 0.75f);
        var ia = (int)v / 4096;
        var ib = (int)v % 4096;
        Assert.Equal(0.25, ia / 4095.0, 3);
        Assert.Equal(0.75, ib / 4095.0, 3);
        Assert.Equal(4095 * 4096 + 4095, InstanceEncoding.Pack12(5f, 9f));
    }
}
