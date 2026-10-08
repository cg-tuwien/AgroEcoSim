using System.Numerics;

namespace Agro.Collisions;

internal static partial class FoliageGeometry
{
    // Keep the original represented endpoints and clip only their parameter domain.
    // Materialising a clipped endpoint as a world-space float loses the precision
    // gained by the relative-double segment kernel, especially in translated crowns.
    readonly record struct RodRange(Vector3 Start, Vector3 End, double Low, double High);

    static FoliageContact RodsOutsideAttachment(in OrganPose a, in OrganPose b,
        float search, Vector3 center, float radius)
    {
        // Three mechanical spans can each leave the box twice. No per-organ state,
        // cached fragment vertices or per-contact heap allocation is needed.
        Span<RodRange> aa = stackalloc RodRange[6];
        Span<RodRange> bb = stackalloc RodRange[6];
        var countA = RodRanges(a, center, radius, aa);
        var countB = RodRanges(b, center, radius, bb);
        var best = new FoliageContact(a.Base, b.Base, Vector3.UnitY, search, -1, -1);
        for (var i = 0; i < countA; ++i)
            for (var j = 0; j < countB; ++j)
            {
                ref readonly var x = ref aa[i];
                ref readonly var y = ref bb[j];
                var squared = StaticCollider.ClosestPointsBetweenSegmentRanges(
                    x.Start, x.End, x.Low, x.High, y.Start, y.End, y.Low, y.High,
                    out var onA, out var onB);
                var distance = (float)Math.Sqrt(squared);
                if (distance >= best.Distance) continue;
                var normal = onA - onB;
                if (normal.LengthSquared() < 1e-16f)
                    normal = Vector3.Cross(x.End - x.Start, y.End - y.Start);
                if (normal.LengthSquared() < 1e-20f) normal = Vector3.UnitY;
                best = new(onA, onB, Vector3.Normalize(normal), distance, -1, -1);
                if (distance <= 1e-9f || distance < a.Radius + b.Radius - Skin) return best;
            }
        return best;
    }

    static int RodRanges(in OrganPose pose, Vector3 center, float radius, Span<RodRange> ranges)
    {
        var count = 0;
        for (var span = 0; span < (pose.CurvedRod ? 3 : 1); ++span)
        {
            var rod = pose.CurvedRod ? pose.RodSpan(span) : pose;
            var start = rod.Base;
            var end = rod.Tip;
            double enter = 0, exit = 1;
            for (var axis = 0; axis < 3; ++axis)
            {
                double direction = (double)end[axis] - start[axis];
                double offset = (double)start[axis] - center[axis];
                if (direction == 0)
                {
                    if (Math.Abs(offset) > radius) { enter = 1; exit = 0; break; }
                    continue;
                }
                var lo = (-radius - offset) / direction;
                var hi = (radius - offset) / direction;
                enter = Math.Max(enter, Math.Min(lo, hi));
                exit = Math.Min(exit, Math.Max(lo, hi));
                if (enter >= exit) break;
            }
            if (enter >= exit) ranges[count++] = new(start, end, 0, 1);
            else
            {
                if (enter > 0) ranges[count++] = new(start, end, 0, enter);
                if (exit < 1) ranges[count++] = new(start, end, exit, 1);
            }
        }
        return count;
    }
}
