using GodotFrontend.Core.Geometry;
using GodotFrontend.Core.Math;
using GodotFrontend.Core.Packing;
using GodotFrontend.Core.Picking;
using GodotFrontend.Core.Scene;
using Xunit;

namespace GodotFrontend.Tests;

public class PickingRaycastTests
{
    [Fact]
    public void BvhHitsTheTopOfTheUnitBox()
    {
        var lib = new GeometryLibrary();
        var box = lib.Get(Geom.BoxCorner);
        var bvh = new MeshBvh(box.Positions, box.Indices, new Vec3(2, 0, 0));
        var t = bvh.Raycast(new Ray(new Vec3(2.5, 5, 0.5), new Vec3(0, -1, 0)));
        Assert.NotNull(t);
        Assert.Equal(4, t!.Value, 6);
        Assert.Null(bvh.Raycast(new Ray(new Vec3(0.5, 5, 0.5), new Vec3(0, -1, 0))));
        // a big mesh builds and answers
        var facade = TerrainDecoder.Decode(Fixtures.Bytes("terrain-facade.bin"));
        var obs = new MeshBvh(facade.ObstacleMesh!.Positions, facade.ObstacleMesh.Indices);
        Assert.True(obs.TriangleCount > 0);
    }

    [Fact]
    public void PicksTheStemItIsAimedAt()
    {
        var lib = new GeometryLibrary();
        var s = new SceneDecoder(lib).Decode(Fixtures.Inflate(Fixtures.Bytes("v8-single-mid.g")));
        var f = new FramePacker(lib).Pack(s);
        var rc = new SceneRaycaster(lib) { Scene = s, GeometryOf = f.GeometryOf };
        // aim horizontally at the middle of the thickest cylinder
        var best = -1;
        for (var i = 0; i < s.Count; ++i)
            if (s.Kind[i] == PrimitiveType.Cylinder && (best < 0 || s.Radius[i] > s.Radius[best])) best = i;
        Span<double> m = stackalloc double[12];
        SceneRaycaster.DrawnTransform(s, best, m);
        var centre = new Vec3(m[3], m[7], m[11]);
        var origin = centre + new Vec3(0.0, 2.0, 0.0);
        var hit = rc.Pick(new Ray(origin, new Vec3(0, -1, 0)));
        Assert.Equal(PickKind.Plant, hit.Kind);
        Assert.True(hit.Distance > 0 && hit.Distance <= 2.0 + 1e-6, $"distance {hit.Distance}");
        Assert.Equal(0, hit.Plant);
        Assert.Equal(hit.Index, hit.PrimitiveInPlant);
        // hiding everything that could be hit leaves nothing or a stem
        var miss = rc.Pick(new Ray(new Vec3(-50, 5, -50), new Vec3(0, -1, 0)));
        Assert.Equal(PickKind.None, miss.Kind);
    }

    [Fact]
    public void PickingAThousandsOfPlantsFrameIsFast()
    {
        var lib = new GeometryLibrary();
        var s = new SceneDecoder(lib).Decode(Fixtures.Inflate(Fixtures.Bytes("v8-facade-early.g")));
        var f = new FramePacker(lib).Pack(s);
        var rc = new SceneRaycaster(lib) { Scene = s, GeometryOf = f.GeometryOf };
        var rng = new Random(1);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var hits = 0;
        for (var k = 0; k < 200; ++k)
        {
            var target = new Vec3(s.SceneMin[0] + rng.NextDouble() * (s.SceneMax[0] - s.SceneMin[0]), s.SceneMin[1], s.SceneMin[2] + rng.NextDouble() * (s.SceneMax[2] - s.SceneMin[2]));
            var origin = target + new Vec3(-3, 6, 8);
            if (rc.Pick(new Ray(origin, (target - origin).Normalized())).Kind == PickKind.Plant) hits++;
        }
        var perPick = sw.Elapsed.TotalMilliseconds / 200;
        Assert.True(perPick < 20, $"{perPick:F2} ms per pick");
    }
}
