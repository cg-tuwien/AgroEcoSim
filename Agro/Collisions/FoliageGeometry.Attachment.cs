using System.Numerics;
using System.Runtime.InteropServices;

namespace Agro.Collisions;

internal static partial class FoliageGeometry
{
    readonly record struct Part(Vector3 A, Vector3 B, Vector3 C, bool Triangle, int Feature)
    {
        internal Bvh.BoundingBox Box => Triangle ? Bounds(A, B, C) : new(Vector3.Min(A, B), Vector3.Max(A, B));
    }
    [ThreadStatic] static List<Part>? OutsideA;
    [ThreadStatic] static List<Part>? OutsideB;
    [ThreadStatic] static List<int>? AttachmentCandidates;
    [ThreadStatic] static List<int>? AttachmentSweepA;
    [ThreadStatic] static List<int>? AttachmentSweepB;
    [ThreadStatic] static Bvh? AttachmentPartsBvh;
    [ThreadStatic] static List<(int, Bvh.BoundingBox)>? AttachmentPartBounds;
    [ThreadStatic] static List<int>? AttachmentPartHits;
    [ThreadStatic] static List<(int A, int B)>? AttachmentTemplatePairs;
    [ThreadStatic] static List<(int A, int B)>? AttachmentFragmentPairs;
    [ThreadStatic] static (int First, int End)[]? AttachmentRangesA;
    [ThreadStatic] static (int First, int End)[]? AttachmentRangesB;

    // Subtract a small attachment box from BOTH bodies. Merely ignoring their nearest
    // contact at the node also ignored any second crossing farther along the same leaf.
    static FoliageContact OutsideAttachmentUncached(in ContactShape a, in ContactShape b,
        float search, Vector3 attachment, float radius)
    {
        if (radius <= 0) return Distance(a, b, search);
        if(a.Feature<0 && b.Feature<0 && BoundsSeparate(a,b,search))
            return new(a.Pose.Base,b.Pose.Base,Vector3.UnitY,search,-1,-1);
        if (a.Lamina == null && b.Lamina == null)
            return RodsOutsideAttachment(a.Pose, b.Pose, search, attachment, radius);
        var aa = OutsideA ??= []; var bb = OutsideB ??= [];
        Parts(a, attachment, radius, aa, Pad(b.Bounds,search));
        Parts(b, attachment, radius, bb, Pad(a.Bounds,search));
        var dual = a.Lamina != null && b.Lamina != null && a.Feature < 0 && b.Feature < 0
            && (long)aa.Count * bb.Count >= 1024;
        if (dual)
        {
            var templates = AttachmentTemplatePairs ??= [];
            a.Lamina!.QueryPairs(a.Pose, b.Lamina!, b.Pose, search, templates);
            FillRanges(aa, a.Lamina.Indices.Length / 3, ref AttachmentRangesA);
            FillRanges(bb, b.Lamina!.Indices.Length / 3, ref AttachmentRangesB);
            var fragments = AttachmentFragmentPairs ??= []; fragments.Clear();
            foreach (var (fa, fb) in templates)
            {
                var ra = AttachmentRangesA![fa]; var rb = AttachmentRangesB![fb];
                for (int i = ra.First; i < ra.End; ++i)
                    for (int j = rb.First; j < rb.End; ++j) fragments.Add((i, j));
            }
            fragments.Sort(static (x,y) => x.A != y.A ? x.A.CompareTo(y.A) : x.B.CompareTo(y.B));
        }
        var indexed = !dual && bb.Count >= 32 && (long)aa.Count * bb.Count >= 1024;
        var hits = AttachmentPartHits ??= [];
        if (indexed)
        {
            var leaves = AttachmentPartBounds ??= [];
            leaves.Clear();
            for (int i = 0; i < bb.Count; ++i) leaves.Add((i, bb[i].Box));
            (AttachmentPartsBvh ??= new()).Build(leaves);
        }
        var best = new FoliageContact(a.Pose.Base, b.Pose.Base, Vector3.UnitY, search, -1, -1);
        var fragmentCursor = 0;
        for (int first = 0; first < aa.Count; ++first)
        {
            ref readonly var x = ref CollectionsMarshal.AsSpan(aa)[first];
            var box = Pad(x.Box, search);
            if (dual)
            {
                hits.Clear();
                var fragments = AttachmentFragmentPairs!;
                while (fragmentCursor < fragments.Count && fragments[fragmentCursor].A == first)
                    hits.Add(fragments[fragmentCursor++].B);
            }
            else if (indexed)
            {
                hits.Clear(); AttachmentPartsBvh!.QueryOverlaps(box, hits);
                // Keep the exact existing first-contact/tie behavior independent of tree layout.
                hits.Sort();
            }
            for (int part = 0, count = indexed || dual ? hits.Count : bb.Count; part < count; ++part)
            {
                ref readonly var y = ref CollectionsMarshal.AsSpan(bb)[indexed || dual ? hits[part] : part];
                if (!box.Intersects(y.Box)) continue;
                Vector3 onA, onB; float distance;
                if (x.Triangle && y.Triangle)
                    distance = TriangleDistance(x.A, x.B, x.C, y.A, y.B, y.C, out onA, out onB,best.Distance);
                else if (x.Triangle)
                {
                    ++TriangleCount;
                    distance = StaticCollider.SegmentTriangleDistance(y.A, y.B, x.A, x.B-x.A, x.C-x.A, out onA, out onB);
                }
                else if (y.Triangle)
                {
                    ++TriangleCount;
                    distance = StaticCollider.SegmentTriangleDistance(x.A, x.B, y.A, y.B-y.A, y.C-y.A, out onB, out onA);
                }
                else
                {
                    StaticCollider.ClosestPointsBetweenSegments(x.A, x.B, y.A, y.B, out onA, out onB, out var d2);
                    distance = MathF.Sqrt(d2);
                }
                if (distance >= best.Distance) continue;
                var normal = onA-onB;
                if (normal.LengthSquared() < 1e-16f)
                    normal = y.Triangle ? Vector3.Cross(y.B-y.A, y.C-y.A)
                        : x.Triangle ? Vector3.Cross(x.B-x.A, x.C-x.A) : Vector3.Cross(x.B-x.A, y.B-y.A);
                if (normal.LengthSquared() < 1e-20f) normal = Vector3.UnitY;
                best = new(onA, onB, Vector3.Normalize(normal), distance, x.Feature, y.Feature);
                if (distance <= 1e-9f || distance < a.Pose.Radius + b.Pose.Radius - Skin) return best;
            }
        }
        return best;
    }

    static void FillRanges(List<Part> parts, int features, ref (int First, int End)[]? ranges)
    {
        if (ranges == null || ranges.Length < features) ranges = new (int, int)[features];
        else Array.Clear(ranges, 0, features);
        for (int first = 0; first < parts.Count;)
        {
            var feature = parts[first].Feature; var end = first + 1;
            while (end < parts.Count && parts[end].Feature == feature) ++end;
            ranges[feature] = (first, end); first = end;
        }
    }

    static MotionResult AttachmentSweepUncached(in ContactShape a0, in ContactShape a1,
        in ContactShape b0, in ContactShape b1, Vector3 attachment0, Vector3 attachment1, float radius)
    {
        if (!a0.SweptBounds(a1).Intersects(b0.SweptBounds(b1))) return MotionResult.Clear;
        if (AttachmentContained(attachment0,attachment1,radius,a0,a1)
            || AttachmentContained(attachment0,attachment1,radius,b0,b1)) return MotionResult.Clear;
        var movement=a0.MotionBound(a1)+b0.MotionBound(b1);
        if (SweptPlanesSeparate(a0,a1,b0,b1)) return MotionResult.Clear;
        var reach=MathF.Max(a0.Pose.Radius,a1.Pose.Radius)+MathF.Max(b0.Pose.Radius,b1.Pose.Radius);
        var gap=OutsideAttachment(a0,b0,reach+movement+Skin,attachment0,
            radius-movement-Vector3.Distance(attachment0,attachment1)).Distance;
        if (DistanceBoundsMotion(gap,reach,movement)) return MotionResult.Clear;
        // Split sheets before temporal subdivision: a small triangle's motion bound is
        // much tighter than that of the complete blade, especially near an attachment.
        var budget = 256;
        var first=AttachmentSweepA??=[];var second=AttachmentSweepB??=[];
        first.Clear();
        if(a0.Lamina==null) first.Add(-1);
        else a0.Lamina.Query(a0.Pose,Pad(b0.SweptBounds(b1),a0.MotionBound(a1)),first);
        foreach(var i in first)
        {
            var x0=a0.Lamina==null?a0:a0 with {Feature=i};
            var x1=a1.Lamina==null?a1:a1 with {Feature=i};
            var core=AttachmentBounds(x0,x1);
            if (AttachmentBoxContains(attachment0,attachment1,radius,core)) continue;
            var bounds=Pad(core,MathF.Max(x0.Pose.Radius,x1.Pose.Radius));
            second.Clear();
            if(b0.Lamina==null) second.Add(-1);
            else b0.Lamina.Query(b0.Pose,Pad(bounds,b0.MotionBound(b1)),second);
            foreach(var j in second)
            {
                var y0=b0.Lamina==null?b0:b0 with {Feature=j};
                var y1=b1.Lamina==null?b1:b1 with {Feature=j};
                var otherCore=AttachmentBounds(y0,y1);
                if (AttachmentBoxContains(attachment0,attachment1,radius,otherCore)) continue;
                var other=Pad(otherCore,MathF.Max(y0.Pose.Radius,y1.Pose.Radius));
                if (!bounds.Intersects(other)) continue;
                var result = AttachmentInterval(x0,x1,y0,y1,attachment0,attachment1,radius,ref budget);
                if (result != MotionResult.Clear) return result;
            }
        }
        return MotionResult.Clear;
    }

    static MotionResult AttachmentInterval(in ContactShape a0, in ContactShape a1, in ContactShape b0, in ContactShape b1,
        Vector3 p0, Vector3 p1, float radius, ref int budget)
    {
        ++IntervalCount;
        if (--budget < 0) return MotionResult.Uncertified;
        // Clipped parts are subsets of these facets. A separating plane for the
        // complete finite-thickness facets therefore certifies their clipped
        // motion too, including rotation/deformation through the arc bound. Use
        // the facet's actual rest-curved normal, not only the organ's midrib frame.
        // Test this before transforming candidate
        // vertices again for attachment bounds and temporal subdivision.
        if (FacetPlaneSeparates(a0,a1,b0,b1) || FacetPlaneSeparates(b0,b1,a0,a1)) return MotionResult.Clear;
        // If either whole body remains inside the moving attachment neighbourhood,
        // its outside portion is empty throughout the interval. This proof is especially
        // useful for new blades; distance subdivision alone needlessly erodes the joint.
        if (AttachmentContained(p0,p1,radius,a0,a1)
            || AttachmentContained(p0,p1,radius,b0,b1)) return MotionResult.Clear;
        var am = a0.Interpolate(a1, .5f); var bm = b0.Interpolate(b1, .5f);
        var motion = MathF.Max(a0.MotionBound(am), am.MotionBound(a1)) + MathF.Max(b0.MotionBound(bm), bm.MotionBound(b1));
        var reach = MathF.Max(a0.Pose.Radius, a1.Pose.Radius) + MathF.Max(b0.Pose.Radius, b1.Pose.Radius);
        var pm = (p0+p1)*.5f;
        // Erode the excluded region by the entire motion bound. A contact leaving the
        // attachment during this interval must therefore participate in the distance proof.
        var hit = OutsideAttachment(am, bm, reach+motion+Skin, pm, radius-motion-Vector3.Distance(p0,pm));
        if (DistanceBoundsMotion(hit.Distance, reach, motion)) return MotionResult.Clear;
        // The eroded attachment region and full motion bound are required even
        // below the skin; a clear midpoint does not certify its endpoints/path.
        // Continue bounded subdivision rather than treating small motion as clear.
        // Use actual midpoint thickness to identify penetration; the larger
        // endpoint thickness remains the conservative separation-proof bound.
        var midpointReach = am.Pose.Radius + bm.Pose.Radius;
        if (OutsideAttachment(am, bm, midpointReach+Skin, pm, radius).Distance < midpointReach-Skin) return MotionResult.Intersecting;
        var left = AttachmentInterval(a0,am,b0,bm,p0,pm,radius,ref budget);
        return left == MotionResult.Clear ? AttachmentInterval(am,a1,bm,b1,pm,p1,radius,ref budget) : left;
    }

    static bool AttachmentContained(Vector3 center0, Vector3 center1, float radius,
        in ContactShape start, in ContactShape end)
        => AttachmentBoxContains(center0,center1,radius,AttachmentBounds(start,end));

    static bool AttachmentBoxContains(Vector3 center0, Vector3 center1, float radius, Bvh.BoundingBox bounds)
    {
        // Test the intersection of the two exemption boxes without rounding their
        // faces to world floats. A rounded-out box can otherwise certify an entire
        // rod as exempt although its double-clipped parameter range is nonempty.
        for (var axis = 0; axis < 3; ++axis)
            if ((double)bounds.Min[axis] - center0[axis] < -radius
                || (double)bounds.Min[axis] - center1[axis] < -radius
                || (double)bounds.Max[axis] - center0[axis] > radius
                || (double)bounds.Max[axis] - center1[axis] > radius) return false;
        return true;
    }

    static Bvh.BoundingBox AttachmentBounds(in ContactShape start, in ContactShape end)
    {
        // Parts clips centre lines / blade surfaces, then the distance test applies
        // collision thickness to the remaining parts. Use that same geometry for
        // the empty-outside proof: a thickness-expanded box could straddle the seam
        // even though no clipped part exists at any instant. Keep curvature, growth
        // and the rotational arc bound; do not enlarge the attachment exemption.
        var a = start with { Pose = start.Pose with { Radius = 0f } };
        var b = end with { Pose = end.Pose with { Radius = 0f } };
        return a.SweptBounds(b);
    }

    static bool FacetPlaneSeparates(in ContactShape start, in ContactShape end,
        in ContactShape otherStart, in ContactShape otherEnd)
    {
        if (start.Lamina == null || start.Feature < 0) return false;
        start.Lamina.Triangle(start.Pose, start.Feature, out var a, out var b, out var c);
        var normal = Vector3.Cross(b-a,c-a);
        var size = normal.LengthSquared();
        if (size <= 1e-30f) return false;
        normal /= MathF.Sqrt(size);
        // Project each endpoint once for both faces, reusing the start vertices.
        // A local origin avoids cancellation of large world-coordinate projections.
        var p = Vector3.Dot(b-a,normal); var q = Vector3.Dot(c-a,normal);
        var al0 = MathF.Min(0,MathF.Min(p,q)); var ah0 = MathF.Max(0,MathF.Max(p,q));
        ProjectAttachmentFeature(otherStart,normal,a,out var bl0,out var bh0);
        var offset0 = start.Pose.Radius + otherStart.Pose.Radius;
        var forward0 = al0-bh0-offset0; var reverse0 = bl0-ah0-offset0;
        // A path certificate also needs separation at its start. Avoid endpoint
        // transforms and arc calculations when neither face can meet that condition.
        if (forward0 < -Skin && reverse0 < -Skin) return false;
        var bound = start.ArcBound(end) + otherStart.ArcBound(otherEnd) - Skin;
        if (forward0 < bound && reverse0 < bound) return false;
        ProjectAttachmentFeature(end,normal,a,out var al1,out var ah1);
        ProjectAttachmentFeature(otherEnd,normal,a,out var bl1,out var bh1);
        var offset1 = end.Pose.Radius + otherEnd.Pose.Radius;
        return MathF.Min(forward0,al1-bh1-offset1) >= bound
            || MathF.Min(reverse0,bl1-ah1-offset1) >= bound;
    }

    static void ProjectAttachmentFeature(in ContactShape shape, Vector3 axis, Vector3 origin,
        out float low, out float high)
    {
        if (shape.Lamina != null && shape.Feature >= 0)
        {
            shape.Lamina.Triangle(shape.Pose,shape.Feature,out var a,out var b,out var c);
            var x = Vector3.Dot(a-origin,axis); var y = Vector3.Dot(b-origin,axis); var z = Vector3.Dot(c-origin,axis);
            low = MathF.Min(x,MathF.Min(y,z)); high = MathF.Max(x,MathF.Max(y,z));
            return;
        }
        // Whole-sheet bounds remain conservative; rods include every internal node.
        Project(shape with { Pose = shape.Pose with { Base = shape.Pose.Base-origin } },axis,out low,out high);
    }

    static void ClipRod(Vector3 a, Vector3 b, Vector3 center, float radius, List<Part> result)
    {
        var direction=b-a;
            float enter = 0, exit = 1;
            for (int axis=0; axis<3; ++axis)
            {
                if (MathF.Abs(direction[axis]) < 1e-15f)
                {
                    if (MathF.Abs(a[axis]-center[axis]) > radius) { result.Add(new(a,b,b,false,-1)); return; }
                    continue;
                }
                var lo = (center[axis]-radius-a[axis])/direction[axis];
                var hi = (center[axis]+radius-a[axis])/direction[axis];
                enter = MathF.Max(enter, MathF.Min(lo,hi)); exit = MathF.Min(exit, MathF.Max(lo,hi));
            }
            if (enter >= exit) result.Add(new(a,b,b,false,-1));
            else
            {
                if (enter > 0) result.Add(new(a,a+direction*enter,a,false,-1));
                if (exit < 1) result.Add(new(a+direction*exit,b,b,false,-1));
            }
    }

    static void Parts(in ContactShape shape, Vector3 center, float radius, List<Part> result, Bvh.BoundingBox against)
    {
        result.Clear();
        if (shape.Lamina == null)
        {
            for(var span=0;span<(shape.Pose.CurvedRod?3:1);++span)
            {
                var rod=shape.Pose.CurvedRod?shape.Pose.RodSpan(span):shape.Pose;
                ClipRod(rod.Base,rod.Tip,center,radius,result);
            }
            return;
        }
        Span<Vector3> polygon = stackalloc Vector3[12];
        Span<Vector3> inside = stackalloc Vector3[12];
        Span<Vector3> outside = stackalloc Vector3[12];
        var candidates = AttachmentCandidates ??= [];
        if (shape.Feature >= 0) { candidates.Clear(); candidates.Add(shape.Feature); }
        else shape.Lamina.Query(shape.Pose,against,candidates);
        var exclusion = new Bvh.BoundingBox(center-new Vector3(radius),center+new Vector3(radius));
        foreach (var feature in candidates)
        {
            shape.Lamina.Triangle(shape.Pose, feature, out polygon[0], out polygon[1], out polygon[2]);
            var bounds = Bounds(polygon[0],polygon[1],polygon[2]);
            if (!bounds.Intersects(exclusion))
            { result.Add(new(polygon[0],polygon[1],polygon[2],true,feature)); continue; }
            if (bounds.Min.X>=exclusion.Min.X && bounds.Min.Y>=exclusion.Min.Y && bounds.Min.Z>=exclusion.Min.Z
                && bounds.Max.X<=exclusion.Max.X && bounds.Max.Y<=exclusion.Max.Y && bounds.Max.Z<=exclusion.Max.Z) continue;
            var count = 3;
            for (int plane=0; plane<6 && count>0; ++plane)
            {
                int ni=0, no=0; var axis=plane/2; var sign=plane%2==0?1f:-1f;
                var previous=polygon[count-1]; var dp=sign*(previous[axis]-center[axis])-radius;
                for (int vertex=0; vertex<count; ++vertex)
                {
                    var current=polygon[vertex]; var dc=sign*(current[axis]-center[axis])-radius;
                    if ((dc>0)!=(dp>0))
                    {
                        var intersection=Vector3.Lerp(previous,current,dp/(dp-dc));
                        inside[ni++]=intersection; outside[no++]=intersection;
                    }
                    if (dc>0) outside[no++]=current; else inside[ni++]=current;
                    previous=current; dp=dc;
                }
                for (int triangle=1; triangle+1<no; ++triangle)
                    result.Add(new(outside[0],outside[triangle],outside[triangle+1],true,feature));
                inside[..ni].CopyTo(polygon); count=ni;
            }
        }
    }
}
