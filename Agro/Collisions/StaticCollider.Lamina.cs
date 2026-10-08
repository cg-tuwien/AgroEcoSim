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
                    if (!SweepLaminaTriangle(from, to, t, normal, 0, ref intervals)) return false;
                }
        }
        return true;
    }

    bool SweepLaminaTriangle(in ContactShape from, in ContactShape to, int t, Vector3 normal, int depth, ref int intervals)
    {
        ++FoliageGeometry.IntervalCount;
        if (--intervals < 0) return false;
        var radius = MathF.Max(from.Pose.Radius, to.Pose.Radius);
        var gap = MathF.Min(Vector3.Dot(from.Support(normal) - A[t], normal), Vector3.Dot(to.Support(normal) - A[t], normal));
        if (gap - from.ArcBound(to) >= radius - FoliageGeometry.Skin) return true;
        var mid = from.Interpolate(to, 0.5f);
        var motion = MathF.Max(from.MotionBound(mid), mid.MotionBound(to));
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
            foreach (var triangle in candidates)
            {
                mid.Lamina.Triangle(mid.Pose, triangle, out var a, out var b, out var c);
                distance = MathF.Min(distance, FoliageGeometry.TriangleDistance(a, b, c, p, q, r, out _, out _,
                    MathF.Min(distance, radius + motion + FoliageGeometry.Skin)));
            }
        }
        if (distance >= radius + motion + FoliageGeometry.Skin) return true;
        if (distance < radius - FoliageGeometry.Skin) return false;
        if (motion < FoliageGeometry.Skin * 0.25f) return distance >= radius - FoliageGeometry.Skin;
        if (depth >= 16) return false;
        return SweepLaminaTriangle(from, mid, t, normal, depth + 1, ref intervals)
            && SweepLaminaTriangle(mid, to, t, normal, depth + 1, ref intervals);
    }
}
