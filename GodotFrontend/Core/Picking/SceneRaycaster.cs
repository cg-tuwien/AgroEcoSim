using GodotFrontend.Core.Geometry;
using GodotFrontend.Core.Math;
using GodotFrontend.Core.Scene;

namespace GodotFrontend.Core.Picking;

public enum PickKind { None, Plant, Seed, Terrain, Obstacle }

/// <summary>The nearest thing under the cursor.</summary>
public readonly record struct PickHit(PickKind Kind, int Index, int Plant, int PrimitiveInPlant, double Distance);

/// <summary>
/// CPU picking for the viewport (ThreeRenderer.pick: nearest of seeds, obstacles, plants and terrain, each gated by
/// its visibility toggle). Plants: an AABB per plant in entry order, a bounding sphere per primitive from the drawn
/// transform, then an exact double-sided triangle test in mesh space. Statics use a BVH built once per import.
/// </summary>
public sealed class SceneRaycaster
{
    private readonly GeometryLibrary lib;
    private readonly Dictionary<(int id, int version), (Vec3 c, double r)> spheres = new();

    public SceneRaycaster(GeometryLibrary lib) => this.lib = lib;

    public bool ShowLeaves = true, ShowRoots = true, ShowSeeds = true, ShowTerrain = true, ShowObstacles = true;
    public DecodedScene? Scene;
    public int[]? GeometryOf;
    public readonly List<(Vec3 position, double radius)> Seeds = new();
    public readonly List<MeshBvh> Trays = new();
    public MeshBvh? Obstacles;

    public PickHit Pick(Ray ray)
    {
        var best = new PickHit(PickKind.None, -1, -1, -1, double.PositiveInfinity);
        if (ShowSeeds)
            for (var i = 0; i < Seeds.Count; ++i)
                if (RayMath.Sphere(ray.Origin, ray.Direction, Seeds[i].position, Seeds[i].radius) is double t && t < best.Distance)
                    best = new PickHit(PickKind.Seed, i, -1, -1, t);
        if (ShowObstacles && Obstacles?.Raycast(ray, best.Distance) is double to)
            best = new PickHit(PickKind.Obstacle, 0, -1, -1, to);
        if (ShowTerrain)
            for (var i = 0; i < Trays.Count; ++i)
                if (Trays[i].Raycast(ray, best.Distance) is double tt)
                    best = new PickHit(PickKind.Terrain, i, -1, -1, tt);
        PickPlants(ray, ref best);
        return best;
    }

    private void PickPlants(Ray ray, ref PickHit best)
    {
        var s = Scene;
        var geom = GeometryOf;
        if (s is null || geom is null || s.Count == 0) return;
        var o = ray.Origin;
        var d = ray.Direction;
        double ix = 1 / d.X, iy = 1 / d.Y, iz = 1 / d.Z;
        var order = new List<(double t, int plant)>();
        for (var p = 0; p < s.PlantCount; ++p)
        {
            if (s.PlantDrawCount[p] == 0) continue;
            var t = RayMath.Slab(o, ix, iy, iz,
                s.PlantBoundsMin[p * 3] - 0.01, s.PlantBoundsMin[p * 3 + 1] - 0.01, s.PlantBoundsMin[p * 3 + 2] - 0.01,
                s.PlantBoundsMax[p * 3] + 0.01, s.PlantBoundsMax[p * 3 + 1] + 0.01, s.PlantBoundsMax[p * 3 + 2] + 0.01, best.Distance);
            if (t is double tv) order.Add((tv, p));
        }
        order.Sort((a, b) => a.t.CompareTo(b.t));
        Span<double> m = stackalloc double[12];
        foreach (var (entry, plant) in order)
        {
            if (entry > best.Distance) break;
            var first = s.PlantFirst[plant];
            var end = first + s.PlantDrawCount[plant];
            for (var i = first; i < end; ++i)
            {
                var kind = s.Kind[i];
                if (kind == PrimitiveType.Rectangle && s.Organ[i] != OrganType.FruitWing && !ShowLeaves) continue;
                if (kind == PrimitiveType.Box && !ShowRoots) continue;
                DrawnTransform(s, i, m);
                var mesh = lib.Get(geom[i]);
                var (c, r) = Sphere(geom[i], mesh);
                var center = new Vec3(m[0] * c.X + m[1] * c.Y + m[2] * c.Z + m[3], m[4] * c.X + m[5] * c.Y + m[6] * c.Z + m[7], m[8] * c.X + m[9] * c.Y + m[10] * c.Z + m[11]);
                var scale = System.Math.Sqrt(System.Math.Max(m[0] * m[0] + m[4] * m[4] + m[8] * m[8],
                    System.Math.Max(m[1] * m[1] + m[5] * m[5] + m[9] * m[9], m[2] * m[2] + m[6] * m[6] + m[10] * m[10])));
                if (RayMath.Sphere(o, d, center, r * scale + 1e-6) is not double ts || ts > best.Distance) continue;
                if (Exact(m, mesh, o, d, best.Distance) is double th)
                    best = new PickHit(PickKind.Plant, i, plant, i - first, th);
            }
        }
    }

    /// <summary>The 3x4 the primitive is drawn with: wire order, cylinders and roots scaled and recentred.</summary>
    public static void DrawnTransform(DecodedScene s, int i, Span<double> m)
    {
        var w = s.TransformOf(i);
        for (var k = 0; k < 12; ++k) m[k] = w[k];
        if (s.Kind[i] is PrimitiveType.Cylinder or PrimitiveType.Box)
        {
            double r = s.Radius[i], l = s.Length[i];
            m[0] *= r; m[4] *= r; m[8] *= r;
            m[1] *= l; m[5] *= l; m[9] *= l;
            m[2] *= r; m[6] *= r; m[10] *= r;
            m[3] += 0.5 * m[1]; m[7] += 0.5 * m[5]; m[11] += 0.5 * m[9];
        }
    }

    private (Vec3 c, double r) Sphere(int id, MeshData mesh)
    {
        var key = (id, lib.VersionOf(id));
        if (spheres.TryGetValue(key, out var v)) return v;
        var (cx, cy, cz, r) = mesh.BoundingSphere();
        v = (new Vec3(cx, cy, cz), r);
        spheres[key] = v;
        return v;
    }

    /// <summary>Ray into mesh space through the inverse of the 3x4, then the triangles; t stays in world units.</summary>
    private static double? Exact(ReadOnlySpan<double> m, MeshData mesh, Vec3 o, Vec3 d, double maxT)
    {
        double a = m[0], b = m[1], c = m[2], dd = m[4], e = m[5], f = m[6], g = m[8], h = m[9], k = m[10];
        var det = a * (e * k - f * h) - b * (dd * k - f * g) + c * (dd * h - e * g);
        if (System.Math.Abs(det) < 1e-24) return null;
        var inv = 1 / det;
        double i00 = (e * k - f * h) * inv, i01 = (c * h - b * k) * inv, i02 = (b * f - c * e) * inv;
        double i10 = (f * g - dd * k) * inv, i11 = (a * k - c * g) * inv, i12 = (c * dd - a * f) * inv;
        double i20 = (dd * h - e * g) * inv, i21 = (b * g - a * h) * inv, i22 = (a * e - b * dd) * inv;
        var po = new Vec3(o.X - m[3], o.Y - m[7], o.Z - m[11]);
        var lo = new Vec3(i00 * po.X + i01 * po.Y + i02 * po.Z, i10 * po.X + i11 * po.Y + i12 * po.Z, i20 * po.X + i21 * po.Y + i22 * po.Z);
        var ld = new Vec3(i00 * d.X + i01 * d.Y + i02 * d.Z, i10 * d.X + i11 * d.Y + i12 * d.Z, i20 * d.X + i21 * d.Y + i22 * d.Z);
        var pos = mesh.Positions;
        var idx = mesh.Indices;
        double? best = null;
        var bestT = maxT;
        for (var t = 0; t < idx.Length; t += 3)
        {
            int ia = idx[t] * 3, ib = idx[t + 1] * 3, ic = idx[t + 2] * 3;
            var hit = RayMath.Triangle(lo, ld,
                new Vec3(pos[ia], pos[ia + 1], pos[ia + 2]), new Vec3(pos[ib], pos[ib + 1], pos[ib + 2]), new Vec3(pos[ic], pos[ic + 1], pos[ic + 2]));
            if (hit is double th && th < bestT) { bestT = th; best = th; }
        }
        return best;
    }
}
