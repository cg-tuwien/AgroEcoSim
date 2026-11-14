using System.Numerics;

namespace Agro.Collisions;

public sealed partial class StaticCollider
{
    [ThreadStatic] static List<int>? LaminaCandidates;

    internal bool ShapeContactUncached(in ContactShape shape, Vector3 trusted, StaticSurface surface,
        out Vector3 point, out Vector3 normal, out float depth)
    {
        if (shape.Lamina == null)
        {
            if (!shape.Pose.CurvedRod) return Contact(shape.Pose, trusted, surface, out point, out normal, out depth, out _);
            point=normal=default; depth=0;
            for(var span=0;span<3;++span)
                if(Contact(shape.Pose.RodSpan(span),trusted,surface,out var p,out var n,out var d,out _) && d>depth)
                { point=p; normal=n; depth=d; }
            return depth>FoliageGeometry.Skin;
        }
        point = normal = default; depth = 0;
        if (NodeCount == 0) return false;
        var bounds = shape.Bounds;
        var stack = NodeStack ??= new int[128]; int count = 0; stack[count++] = 0;
        var candidates = LaminaCandidates ??= [];
        while (count > 0)
        {
            var nodeIndex = stack[--count]; var node = Nodes[nodeIndex];
            if ((SurfaceMasks[nodeIndex] & (1 << (int)surface)) == 0 || BoxDistanceSquared(node, bounds.Min, bounds.Max) > 0) continue;
            if (node.Count == 0) { stack[count++] = node.Left; stack[count++] = node.Left + 1; continue; }
            for (int t = node.Left; t < node.Left + node.Count; ++t)
            {
                if (Kind[t] != surface) continue;
                var p = A[t]; var q = p + U[t]; var r = p + V[t];
                var pad = new Vector3(shape.Pose.Radius + FoliageGeometry.Skin);
                shape.Lamina.Query(shape.Pose, new(Vector3.Min(p, Vector3.Min(q, r)) - pad, Vector3.Max(p, Vector3.Max(q, r)) + pad), candidates);
                foreach (var triangle in candidates)
                {
                    shape.Lamina.Triangle(shape.Pose, triangle, out var a, out var b, out var c);
                    var distance = FoliageGeometry.TriangleDistance(a, b, c, p, q, r, out var onLeaf, out var onSurface,
                        shape.Pose.Radius + FoliageGeometry.Skin);
                    if (distance >= shape.Pose.Radius - FoliageGeometry.Skin) continue;
                    var n = N[t];
                    if (surface == StaticSurface.Obstacle && Vector3.Dot(trusted - p, n) < 0) n = -n;
                    var support = a;
                    if (Vector3.Dot(b, n) < Vector3.Dot(support, n)) support = b;
                    if (Vector3.Dot(c, n) < Vector3.Dot(support, n)) support = c;
                    var penetration = distance > 1e-7f ? shape.Pose.Radius - distance : shape.Pose.Radius - Vector3.Dot(support - p, n);
                    if (penetration <= depth) continue;
                    point = distance > 1e-7f ? onLeaf : support;
                    normal = distance > 1e-7f ? Vector3.Normalize(onLeaf - onSurface) : n;
                    depth = penetration;
                }
            }
        }
        return depth > FoliageGeometry.Skin;
    }

    bool ShapeSweepClearUncached(in ContactShape from, in ContactShape to, StaticSurface surface)
    {
        if (from.Lamina == null && !from.Pose.CurvedRod && !to.Pose.CurvedRod) return SweepClear(from.Pose, to.Pose, surface);
        if (NodeCount == 0 || from.MotionBound(to) < FoliageGeometry.Skin * 0.25f) return true;
        var bounds = from.SweptBounds(to);
        var stack = NodeStack ??= new int[128]; int count = 0; stack[count++] = 0;
        // Every static triangle is swept over the same interval, so what belongs to the
        // interval alone is worked out for the first triangle that needs it.
        var whole = default(SweepInterval);
        while (count > 0)
        {
            var nodeIndex = stack[--count]; var node = Nodes[nodeIndex];
            if ((SurfaceMasks[nodeIndex] & (1 << (int)surface)) == 0 || BoxDistanceSquared(node, bounds.Min, bounds.Max) > 0) continue;
            if (node.Count == 0) { stack[count++] = node.Left; stack[count++] = node.Left + 1; continue; }
            for (int t = node.Left; t < node.Left + node.Count; ++t)
                if (Kind[t] == surface)
                {
                    var normal = N[t];
                    if (Vector3.Dot(from.Pose.Base - A[t], normal) < 0) normal = -normal;
                    var intervals = 256;
                    if (!SweepLaminaTriangle(from, to, t, normal, 0, ref intervals, ref whole)) return false;
                }
        }
        return true;
    }

    /// <summary>
    /// What one interval of a sweep works out about the moving shape alone, whichever static
    /// triangle it is tested against: its support along the last normal asked for, the bound on
    /// its arc, its pose half way and how far it moves to either side of that, and the stamp
    /// under which the blade's corners at that pose are kept (<see cref="BladeVertices"/>).
    /// Each is the value the sweep computed per triangle before; a blade over a tray meets some
    /// eighteen triangles of it, most of them sharing a handful of normals.
    /// </summary>
    struct SweepInterval
    {
        public bool HasSupport, HasArc, HasMiddle;
        public Vector3 Normal, FromSupport, ToSupport;
        public float Arc, Motion;
        public ContactShape Middle;
        public int Stamp;
    }

    // World positions of a blade's vertices at the pose a stamp stands for, per thread. A
    // candidate facet shares its corners with its neighbours, and an interval's blade is
    // tested against one static triangle after another, so most corners are asked for many
    // times over; each is transformed once.
    [ThreadStatic] static Vector3[]? BladeVertices;
    [ThreadStatic] static int[]? BladeStamps;
    [ThreadStatic] static int BladeStamp;

    static int NextBladeStamp()
    {
        if (BladeStamp == int.MaxValue) { BladeStamp = 0; if (BladeStamps != null) Array.Clear(BladeStamps); }
        return ++BladeStamp;
    }

    /// <summary>
    /// The distance from a blade to one static triangle, as far as a sweep interval needs it:
    /// below <paramref name="lo"/> (the interval collides), at or above <paramref name="hi"/>
    /// (it is clear), or in between (it is subdivided). The value returned falls in the same
    /// one of the three as the least distance over all the blade's candidate facets, which is
    /// what the plain loop (<paramref name="plain"/>) returns.
    /// </summary>
    /// <remarks>
    /// The plain loop measures every candidate facet that the projection tests do not put
    /// beyond the least distance so far, in full: six piercing tests and fifteen closest
    /// features. A blade lying along a tray wall has all its facets at about that least
    /// distance, so every one was measured, at every level of the subdivision; on the planted
    /// facade these measurements were the largest single cost of a tick. But once one facet is
    /// known to lie in between, all that is left to learn about the others is whether any is
    /// below <c>lo</c>, and a facet the projections put beyond <c>lo</c> need not be measured.
    ///
    /// The decisions are the plain loop's. Up to the first facet nearer than <c>hi</c> the two
    /// are the same calls, and a first facet below <c>lo</c> ends both the same way. After that
    /// a facet is passed over only when a projection separates it by more than <c>lo</c> plus a
    /// guard, and counts as colliding only when measured below <c>lo</c> minus the guard; one
    /// measured inside the guard is too close to call and the plain loop decides the interval.
    /// A projection bound and a measured distance of the same pair can differ by the piercing
    /// test's own micrometre, by the rounding of a closest point at world coordinates (two
    /// tenths of a micrometre per metre from the origin) and by the rounding of the local
    /// arithmetic (a tenth of a micrometre per metre of facet); the guard is 3 um plus a
    /// micrometre per metre from the origin plus ten per metre of facet, at least twice each
    /// term, so neither shortcut can decide differently from the plain loop.
    /// </remarks>
    float BladeDistance(in ContactShape blade, List<int> candidates, Vector3 p, Vector3 q, Vector3 r, float lo, float hi, int stamp, bool plain)
    {
        var lamina = blade.Lamina!;
        // The distance test's projection onto the static triangle's own normal needs nothing
        // of a candidate but its corners, and separates most of them.
        var surfaceAxis = new FoliageGeometry.FacetAxis(Vector3.Cross(q - p, r - p), q - p, r - p);
        var rigid = blade.Pose.Deformation == Vector4.Zero;
        var vertices = BladeVertices; var stamps = BladeStamps; var indices = lamina.Indices;
        if (rigid && (vertices == null || vertices.Length < lamina.Vertices.Length))
        {
            BladeVertices = vertices = new Vector3[Math.Max(256, lamina.Vertices.Length * 2)];
            BladeStamps = stamps = new int[vertices.Length];
        }
        var distance = float.PositiveInfinity; var between = false;
        var surfaceReach = Vector3.Max(Vector3.Abs(p), Vector3.Max(Vector3.Abs(q - p), Vector3.Abs(r - p)));
        foreach (var triangle in candidates)
        {
            Vector3 a, b, c;
            if (rigid)
            {
                int ia = indices[triangle * 3], ib = indices[triangle * 3 + 1], ic = indices[triangle * 3 + 2];
                if (stamps![ia] != stamp) { vertices![ia] = lamina.TransformVertex(blade.Pose, ia); stamps[ia] = stamp; }
                if (stamps[ib] != stamp) { vertices![ib] = lamina.TransformVertex(blade.Pose, ib); stamps[ib] = stamp; }
                if (stamps[ic] != stamp) { vertices![ic] = lamina.TransformVertex(blade.Pose, ic); stamps[ic] = stamp; }
                a = vertices![ia]; b = vertices[ib]; c = vertices[ic];
            }
            else lamina.Triangle(blade.Pose, triangle, out a, out b, out c);
            if (!between)
            {
                var search = MathF.Min(distance, hi);
                // The measurement returns its search ceiling when a projection separates the pair.
                distance = MathF.Min(distance, float.IsFinite(search) && surfaceAxis.Separates(a - p, b - p, c - p, search) ? search
                    : FoliageGeometry.TriangleDistance(a, b, c, p, q, r, out _, out _, search));
                if (plain || !(distance < hi)) continue;
                if (distance < lo) break;
                between = true;
                continue;
            }
            var reach = Vector3.Max(Vector3.Max(surfaceReach, Vector3.Abs(a)), Vector3.Max(Vector3.Abs(b - a), Vector3.Abs(c - a)));
            var facet = Vector3.Max(Vector3.Max(Vector3.Abs(q - p), Vector3.Abs(r - p)), Vector3.Max(Vector3.Abs(b - a), Vector3.Max(Vector3.Abs(c - a), Vector3.Abs(a - p))));
            var guard = 3e-6f + 1e-6f * MathF.Max(reach.X, MathF.Max(reach.Y, reach.Z)) + 1e-5f * MathF.Max(facet.X, MathF.Max(facet.Y, facet.Z));
            if (surfaceAxis.Separates(a - p, b - p, c - p, lo + guard)) continue;
            var measured = FoliageGeometry.TriangleDistance(a, b, c, p, q, r, out _, out _, lo + guard);
            if (measured >= lo) continue;
            return measured < lo - guard ? measured : BladeDistance(blade, candidates, p, q, r, lo, hi, stamp, plain: true);
        }
        return distance;
    }

    bool SweepLaminaTriangle(in ContactShape from, in ContactShape to, int t, Vector3 normal, int depth, ref int intervals, ref SweepInterval interval)
    {
        ++FoliageGeometry.IntervalCount;
        if (--intervals < 0) return false;
        var radius = MathF.Max(from.Pose.Radius, to.Pose.Radius);
        if (!interval.HasSupport || interval.Normal != normal)
        {
            interval.FromSupport = from.Support(normal); interval.ToSupport = to.Support(normal);
            interval.Normal = normal; interval.HasSupport = true;
        }
        var gap = MathF.Min(Vector3.Dot(interval.FromSupport - A[t], normal), Vector3.Dot(interval.ToSupport - A[t], normal));
        if (!interval.HasArc) { interval.Arc = from.ArcBound(to); interval.HasArc = true; }
        if (gap - interval.Arc >= radius - FoliageGeometry.Skin) return true;
        if (!interval.HasMiddle)
        {
            interval.Middle = from.Interpolate(to, 0.5f);
            interval.Motion = MathF.Max(from.MotionBound(interval.Middle), interval.Middle.MotionBound(to));
            interval.HasMiddle = true;
        }
        ref readonly var mid = ref interval.Middle;
        var motion = interval.Motion;
        var distance = float.PositiveInfinity;
        var candidates = LaminaCandidates ??= [];
        var p = A[t]; var q = p + U[t]; var r = p + V[t]; var pad = new Vector3(radius + motion + FoliageGeometry.Skin);
        if(mid.Lamina == null)
        {
            for(var span=0;span<3;++span)
            {
                var rod=mid.Pose.RodSpan(span);
                distance=MathF.Min(distance,SegmentTriangleDistance(rod.Base,rod.Tip,p,q-p,r-p,out _,out _));
            }
        }
        else
        {
            mid.Lamina.Query(mid.Pose, new(Vector3.Min(p, Vector3.Min(q, r)) - pad, Vector3.Max(p, Vector3.Max(q, r)) + pad), candidates);
            if (candidates.Count > 0 && mid.Pose.Deformation == Vector4.Zero && interval.Stamp == 0) interval.Stamp = NextBladeStamp();
            distance = BladeDistance(mid, candidates, p, q, r, radius - FoliageGeometry.Skin, radius + motion + FoliageGeometry.Skin, interval.Stamp, plain: false);
        }
        if (distance >= radius + motion + FoliageGeometry.Skin) return true;
        if (distance < radius - FoliageGeometry.Skin) return false;
        if (motion < FoliageGeometry.Skin * 0.25f) return distance >= radius - FoliageGeometry.Skin;
        if (depth >= 16) return false;
        SweepInterval first = default, second = default;
        return SweepLaminaTriangle(from, mid, t, normal, depth + 1, ref intervals, ref first)
            && SweepLaminaTriangle(mid, to, t, normal, depth + 1, ref intervals, ref second);
    }
}
