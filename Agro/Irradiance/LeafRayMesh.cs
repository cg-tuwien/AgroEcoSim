using System.Numerics;
using System.Runtime.CompilerServices;
using Agro.Collisions;

namespace Agro;

/// <summary>A shared ray hierarchy over the exact, low-resolution lamina template.</summary>
/// <remarks>Ordinary blades only transform the ray. Nonlinear contact deformation uses the
/// template's actual deformed facets; it never deforms the shared hierarchy.</remarks>
internal sealed class LeafRayMesh
{
    const float MinCosine = 0.05f;
    static readonly ConditionalWeakTable<LaminaTemplate, LeafRayMesh> Cache = new();
    [ThreadStatic] static QueryScratch? Scratch;

    internal LaminaTemplate Template { get; }
    readonly Triangle[] Triangles;
    readonly BvhNode[] Nodes;
    readonly int[] Order;

    readonly struct Triangle
    {
        public readonly Vector3 A, B, C, Normal;
        public readonly int VertexA, VertexB, VertexC;

        public Triangle(Vector3 a, Vector3 b, Vector3 c, int vertexA, int vertexB, int vertexC)
        {
            A = a; B = b; C = c;
            // Authored outlines contain very narrow facets. Subtracting/crossing
            // their edges in floats can rotate the normal by several percent.
            double ux = (double)b.X - a.X, uy = (double)b.Y - a.Y, uz = (double)b.Z - a.Z;
            double vx = (double)c.X - a.X, vy = (double)c.Y - a.Y, vz = (double)c.Z - a.Z;
            Normal = new((float)(uy * vz - uz * vy), (float)(uz * vx - ux * vz), (float)(ux * vy - uy * vx));
            VertexA = vertexA; VertexB = vertexB; VertexC = vertexC;
        }
    }

    readonly record struct Feature(int A, int B);
    struct FeatureHit
    {
        public Feature Feature;
        public int Triangle;
        public float Weight;
    }

    sealed class QueryScratch
    {
        // The binned builder has a maximum depth of 48. This stack is separate from
        // RayScene's outer traversal, which is still active while this one runs.
        public readonly int[] Stack = new int[128];
        public FeatureHit[] Features = [];
        public void EnsureFeatures(int count)
        {
            if (Features.Length < count) Features = new FeatureHit[count];
        }
    }

    readonly struct Ray
    {
        public readonly Vector3 Origin, Direction, Inverse;
        public readonly int X, Y, Z;
        public readonly float ShearX, ShearY, ScaleZ;
        public readonly float Min, Max;

        public Ray(Vector3 origin, Vector3 direction, float min, float max)
        {
            Origin = origin; Direction = direction; Min = min; Max = max;
            Inverse = new(Reciprocal(direction.X), Reciprocal(direction.Y), Reciprocal(direction.Z));
            var absolute = Vector3.Abs(direction);
            Z = absolute.X > absolute.Y ? (absolute.X > absolute.Z ? 0 : 2) : (absolute.Y > absolute.Z ? 1 : 2);
            X = (Z + 1) % 3; Y = (X + 1) % 3;
            if (Component(direction, Z) < 0) (X, Y) = (Y, X);
            ScaleZ = 1f / Component(direction, Z);
            ShearX = Component(direction, X) * ScaleZ;
            ShearY = Component(direction, Y) * ScaleZ;
        }

        static float Reciprocal(float value) => value == 0 ? 0 : 1f / value;
    }

    internal static LeafRayMesh For(LaminaTemplate template) => Cache.GetValue(template, static t => new(t));

    LeafRayMesh(LaminaTemplate template)
    {
        Template = template;
        var count = template.Indices.Length / 3;
        Triangles = new Triangle[count];
        Order = new int[count];
        if (count == 0) { Nodes = []; return; }

        // Seam refinement can duplicate indexed vertices without changing their position.
        // Exact aliases share ownership; nearby but distinct material is never welded.
        var aliases = new Dictionary<Vector3, int>();
        var vertices = new int[template.Vertices.Length];
        for (int i = 0; i < vertices.Length; ++i)
        {
            var point = template.Vertices[i];
            if (!aliases.TryGetValue(point, out var id))
            { id = aliases.Count; aliases.Add(point, id); }
            vertices[i] = id;
        }

        var items = new BvhItem[count];
        for (int i = 0; i < count; ++i)
        {
            var a = template.Indices[i * 3]; var b = template.Indices[i * 3 + 1]; var c = template.Indices[i * 3 + 2];
            var triangle = Triangles[i] = new(template.Vertices[a], template.Vertices[b], template.Vertices[c],
                vertices[a], vertices[b], vertices[c]);
            var min = Vector3.Min(triangle.A, Vector3.Min(triangle.B, triangle.C));
            var max = Vector3.Max(triangle.A, Vector3.Max(triangle.B, triangle.C));
            var padding = Vector3.Max(Vector3.Abs(min), Vector3.Abs(max)) * 4e-7f + new Vector3(1e-8f);
            items[i] = new() { Min = min - padding, Max = max + padding, Payload = i };
        }

        var nodes = new BvhNode[BvhBuilder.NodeBudgetFor(count, int.MaxValue)];
        new BvhBuilder().Build(items, 0, count, nodes, 0, nodes.Length, 4);
        Nodes = BvhBuilder.Compact(nodes);
        for (int i = 0; i < count; ++i) Order[i] = items[i].Payload;
    }

    /// <summary>Counts distinct lamina surface crossings and their inverse-cosine area weights.</summary>
    internal int CountCrossings(in OrganPose pose, Vector3 origin, Vector3 direction,
        float tMin, float tMax, out float totalAreaSum)
        => CountCrossings(pose, PrepareInverseScale(pose), origin, direction, tMin, tMax, out totalAreaSum);

    /// <summary>Prepared once per accepted leaf pose, rather than for every candidate ray.</summary>
    internal Vector3 PrepareInverseScale(in OrganPose pose)
        => new(2f / pose.Length, 1f / pose.Width, 1f / Template.ReliefScale(pose.Width));

    internal int CountCrossings(in OrganPose pose, Vector3 inverseScale, Vector3 origin, Vector3 direction,
        float tMin, float tMax, out float totalAreaSum)
    {
        totalAreaSum = 0;
        if (Triangles.Length == 0 || !(pose.Length > 0) || !(pose.Width > 0)
            || !float.IsFinite(pose.Length) || !float.IsFinite(pose.Width)
            || !Finite(origin) || !Finite(direction) || direction == Vector3.Zero || !(tMax >= tMin)) return 0;

        var scratch = Scratch ??= new();
        scratch.EnsureFeatures(Triangles.Length);
        int featureCount = 0, crossings = 0;
        double area = 0;
        var directionLength = Length(direction);

        if (pose.Deformation != Vector4.Zero)
        {
            // A nonlinear fold cannot be undone by transforming a straight ray. The
            // existing helper returns the same facets that are rendered/exported.
            var worldRay = new Ray(origin, direction, tMin, tMax);
            for (int i = 0; i < Triangles.Length; ++i)
            {
                Template.Triangle(pose, i, out var a, out var b, out var c);
                ref readonly var source = ref Triangles[i];
                var triangle = new Triangle(a, b, c, source.VertexA, source.VertexB, source.VertexC);
                if (Intersect(triangle, worldRay, out var zeros))
                {
                    var weight = AreaWeight(triangle.Normal, direction, Vector3.One, directionLength);
                    RecordHit(triangle, i, zeros, weight, scratch.Features, ref featureCount, ref crossings, ref area);
                }
            }
        }
        else
        {
            if (!(inverseScale.X > 0) || !(inverseScale.Y > 0) || !(inverseScale.Z > 0) || !Finite(inverseScale)) return 0;
            var inverse = Quaternion.Conjugate(pose.Rotation);
            var o = Vector3.Transform(origin - pose.Base, inverse);
            var d = Vector3.Transform(direction, inverse);
            o = new(o.X * inverseScale.X - 1f, -o.Z * inverseScale.Y, o.Y * inverseScale.Z);
            d = new(d.X * inverseScale.X, -d.Z * inverseScale.Y, d.Y * inverseScale.Z);
            if (!Finite(o) || !Finite(d) || d == Vector3.Zero) return 0;
            // Do not normalize d: the ray parameter and its world-space clipping
            // interval are unchanged by this inverse affine transform.
            var ray = new Ray(o, d, tMin, tMax);
            var stack = scratch.Stack;
            int top = 0;
            stack[top++] = 0;
            while (top > 0)
            {
                ref readonly var node = ref Nodes[stack[--top]];
                if (!HitsBox(node, ray)) continue;
                if (node.Count == 0)
                { stack[top++] = node.Left; stack[top++] = node.Left + 1; continue; }
                for (int k = 0; k < node.Count; ++k)
                {
                    var triangleIndex = Order[node.Left + k];
                    ref readonly var triangle = ref Triangles[triangleIndex];
                    if (!Intersect(triangle, ray, out var zeros)) continue;
                    // A^-T n, in organ coordinates, is (2nx/L, nz/S, -ny/W).
                    // Its dot with the world ray equals n dot d in template space.
                    var weight = AreaWeight(triangle.Normal, d, inverseScale, directionLength);
                    RecordHit(triangle, triangleIndex, zeros, weight, scratch.Features,
                        ref featureCount, ref crossings, ref area);
                }
            }
        }
        totalAreaSum = (float)area;
        return crossings;
    }

    static float AreaWeight(Vector3 normal, Vector3 ray, Vector3 inverseScale, double directionLength)
    {
        // The permutation/sign of the organ-frame normal does not affect its length.
        var x = (double)normal.X * inverseScale.X;
        var y = (double)normal.Y * inverseScale.Y;
        var z = (double)normal.Z * inverseScale.Z;
        var length = Math.Sqrt(x * x + y * y + z * z) * directionLength;
        var dot = Math.Abs((double)normal.X * ray.X + (double)normal.Y * ray.Y + (double)normal.Z * ray.Z);
        return length > 0 ? (float)(length / Math.Max(dot, MinCosine * length)) : 0;
    }

    static void RecordHit(in Triangle triangle, int triangleIndex, byte zeros, float weight,
        FeatureHit[] features, ref int featureCount, ref int crossings, ref double area)
    {
        if (zeros == 0) { ++crossings; area += weight; return; }

        Feature feature;
        if ((zeros & 6) == 6) feature = new(triangle.VertexA, -1);
        else if ((zeros & 5) == 5) feature = new(triangle.VertexB, -1);
        else if ((zeros & 3) == 3) feature = new(triangle.VertexC, -1);
        else
        {
            var a = (zeros & 1) != 0 ? triangle.VertexB : triangle.VertexA;
            var b = (zeros & 4) != 0 ? triangle.VertexB : triangle.VertexC;
            feature = new(Math.Min(a, b), Math.Max(a, b));
        }
        // Only exact boundary hits use this normally tiny list. Interior crossings,
        // including distinct folded layers at nearly equal distances, remain independent.
        for (int i = 0; i < featureCount; ++i)
        {
            ref var previous = ref features[i];
            if (previous.Feature != feature) continue;
            if (triangleIndex < previous.Triangle)
            { area += weight - previous.Weight; previous.Triangle = triangleIndex; previous.Weight = weight; }
            return;
        }
        features[featureCount++] = new() { Feature = feature, Triangle = triangleIndex, Weight = weight };
        ++crossings; area += weight;
    }

    static bool Intersect(in Triangle triangle, in Ray ray, out byte zeros)
    {
        zeros = 0;
        var a = triangle.A - ray.Origin; var b = triangle.B - ray.Origin; var c = triangle.C - ray.Origin;
        var ax = Component(a, ray.X) - ray.ShearX * Component(a, ray.Z);
        var ay = Component(a, ray.Y) - ray.ShearY * Component(a, ray.Z);
        var bx = Component(b, ray.X) - ray.ShearX * Component(b, ray.Z);
        var by = Component(b, ray.Y) - ray.ShearY * Component(b, ray.Z);
        var cx = Component(c, ray.X) - ray.ShearX * Component(c, ray.Z);
        var cy = Component(c, ray.Y) - ray.ShearY * Component(c, ray.Z);

        double e0 = bx * cy - by * cx, e1 = cx * ay - cy * ax, e2 = ax * by - ay * bx;
        // Reversed shared edges use the very same projected vertex coordinates.
        // Reevaluate uncertain edge products in double, as in the watertight test.
        if (e0 == 0 || e1 == 0 || e2 == 0)
        {
            e0 = (double)bx * cy - (double)by * cx;
            e1 = (double)cx * ay - (double)cy * ax;
            e2 = (double)ax * by - (double)ay * bx;
        }
        if ((e0 < 0 || e1 < 0 || e2 < 0) && (e0 > 0 || e1 > 0 || e2 > 0)) return false;
        var determinant = e0 + e1 + e2;
        if (determinant == 0) return false;
        var t = (e0 * Component(a, ray.Z) + e1 * Component(b, ray.Z) + e2 * Component(c, ray.Z))
            * ray.ScaleZ / determinant;
        if (!(t >= ray.Min && t <= ray.Max)) return false;
        zeros = (byte)((e0 == 0 ? 1 : 0) | (e1 == 0 ? 2 : 0) | (e2 == 0 ? 4 : 0));
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool HitsBox(in BvhNode node, in Ray ray)
    {
        var a = (node.Min - ray.Origin) * ray.Inverse;
        var b = (node.Max - ray.Origin) * ray.Inverse;
        var lo = Vector3.Min(a, b); var hi = Vector3.Max(a, b);
        // A parallel ray lying on a slab belongs to it for the entire interval.
        // Replacing zero by a tiny direction would incorrectly clip that interval.
        if (ray.Direction.X == 0)
        { if (ray.Origin.X < node.Min.X || ray.Origin.X > node.Max.X) return false; lo.X = float.NegativeInfinity; hi.X = float.PositiveInfinity; }
        if (ray.Direction.Y == 0)
        { if (ray.Origin.Y < node.Min.Y || ray.Origin.Y > node.Max.Y) return false; lo.Y = float.NegativeInfinity; hi.Y = float.PositiveInfinity; }
        if (ray.Direction.Z == 0)
        { if (ray.Origin.Z < node.Min.Z || ray.Origin.Z > node.Max.Z) return false; lo.Z = float.NegativeInfinity; hi.Z = float.PositiveInfinity; }
        return MathF.Max(MathF.Max(lo.X, lo.Y), MathF.Max(lo.Z, ray.Min))
            <= MathF.Min(MathF.Min(hi.X, hi.Y), MathF.Min(hi.Z, ray.Max));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float Component(Vector3 value, int axis) => axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;
    static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    static double Length(Vector3 value) => Math.Sqrt((double)value.X * value.X + (double)value.Y * value.Y + (double)value.Z * value.Z);
}
