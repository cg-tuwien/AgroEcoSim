using System.Numerics;

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

    // Subtract a small attachment box from BOTH bodies. Merely ignoring their nearest
    // contact at the node also ignored any second crossing farther along the same leaf.
    static FoliageContact OutsideAttachmentUncached(in ContactShape a, in ContactShape b,
        float search, Vector3 attachment, float radius)
    {
        if (radius <= 0) return Distance(a, b, search);
        if(a.Feature<0 && b.Feature<0 && BoundsSeparate(a,b,search))
            return new(a.Pose.Base,b.Pose.Base,Vector3.UnitY,search,-1,-1);
        var aa = OutsideA ??= []; var bb = OutsideB ??= [];
        Parts(a, attachment, radius, aa, Pad(b.Bounds,search));
        Parts(b, attachment, radius, bb, Pad(a.Bounds,search));
        var best = new FoliageContact(a.Pose.Base, b.Pose.Base, Vector3.UnitY, search, -1, -1);
        foreach (var x in aa)
        {
            var box = Pad(x.Box, search);
            foreach (var y in bb)
            {
                if (!box.Intersects(y.Box)) continue;
                Vector3 onA, onB; float distance;
                if (x.Triangle && y.Triangle)
                    distance = TriangleDistance(x.A, x.B, x.C, y.A, y.B, y.C, out onA, out onB);
                else if (x.Triangle)
                    distance = StaticCollider.SegmentTriangleDistance(y.A, y.B, x.A, x.B-x.A, x.C-x.A, out onA, out onB);
                else if (y.Triangle)
                    distance = StaticCollider.SegmentTriangleDistance(x.A, x.B, y.A, y.B-y.A, y.C-y.A, out onB, out onA);
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

    static bool AttachmentSweepUncached(in ContactShape a0, in ContactShape a1,
        in ContactShape b0, in ContactShape b1, Vector3 attachment0, Vector3 attachment1, float radius)
    {
        if (!a0.SweptBounds(a1).Intersects(b0.SweptBounds(b1))) return true;
        var low=Vector3.Max(attachment0,attachment1)-new Vector3(radius);
        var high=Vector3.Min(attachment0,attachment1)+new Vector3(radius);
        if (Contains(low,high,a0.SweptBounds(a1)) || Contains(low,high,b0.SweptBounds(b1))) return true;
        var movement=a0.MotionBound(a1)+b0.MotionBound(b1);
        // Endpoints are checked independently. Below a quarter skin, the whole path stays
        // within the same contact tolerance; do not exhaust a triangle budget on roundoff.
        if (movement < Skin * .25f) return true;
        var reach=MathF.Max(a0.Pose.Radius,a1.Pose.Radius)+MathF.Max(b0.Pose.Radius,b1.Pose.Radius);
        var gap=OutsideAttachment(a0,b0,reach+movement+Skin,attachment0,
            radius-movement-Vector3.Distance(attachment0,attachment1)).Distance;
        if (gap>=reach+movement-Skin) return true;
        if (a0.Lamina != null && (SeparatingPlane(a0,a1,b0,b1,Vector3.Transform(Vector3.UnitY,a0.Pose.Rotation))
            || SeparatingPlane(a0,a1,b0,b1,Vector3.Transform(Vector3.UnitY,a1.Pose.Rotation)))) return true;
        if (b0.Lamina != null && (SeparatingPlane(a0,a1,b0,b1,Vector3.Transform(Vector3.UnitY,b0.Pose.Rotation))
            || SeparatingPlane(a0,a1,b0,b1,Vector3.Transform(Vector3.UnitY,b1.Pose.Rotation)))) return true;
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
            var bounds=x0.SweptBounds(x1);
            if (Contains(low,high,bounds)) continue;
            second.Clear();
            if(b0.Lamina==null) second.Add(-1);
            else b0.Lamina.Query(b0.Pose,Pad(bounds,b0.MotionBound(b1)),second);
            foreach(var j in second)
            {
                var y0=b0.Lamina==null?b0:b0 with {Feature=j};
                var y1=b1.Lamina==null?b1:b1 with {Feature=j};
                var other=y0.SweptBounds(y1);
                if (!bounds.Intersects(other) || Contains(low,high,other)) continue;
                if (!AttachmentInterval(x0,x1,y0,y1,attachment0,attachment1,radius,ref budget)) return false;
            }
        }
        return true;
    }

    static bool AttachmentInterval(in ContactShape a0, in ContactShape a1, in ContactShape b0, in ContactShape b1,
        Vector3 p0, Vector3 p1, float radius, ref int budget)
    {
        if (--budget < 0) return false;
        // If either whole body remains inside the moving attachment neighbourhood,
        // its outside portion is empty throughout the interval. This proof is especially
        // useful for new blades; distance subdivision alone needlessly erodes the joint.
        var low = Vector3.Max(p0,p1)-new Vector3(radius);
        var high = Vector3.Min(p0,p1)+new Vector3(radius);
        if (Contains(low,high,a0.SweptBounds(a1)) || Contains(low,high,b0.SweptBounds(b1))) return true;
        var am = a0.Interpolate(a1, .5f); var bm = b0.Interpolate(b1, .5f);
        var motion = MathF.Max(a0.MotionBound(am), am.MotionBound(a1)) + MathF.Max(b0.MotionBound(bm), bm.MotionBound(b1));
        var reach = MathF.Max(a0.Pose.Radius, a1.Pose.Radius) + MathF.Max(b0.Pose.Radius, b1.Pose.Radius);
        var pm = (p0+p1)*.5f;
        // Erode the excluded region by the entire motion bound. A contact leaving the
        // attachment during this interval must therefore participate in the distance proof.
        var hit = OutsideAttachment(am, bm, reach+motion+Skin, pm, radius-motion-Vector3.Distance(p0,pm));
        if (hit.Distance >= reach+motion+Skin*.5f) return true;
        if (motion < Skin*.25f) return hit.Distance >= reach-Skin;
        if (OutsideAttachment(am, bm, reach+Skin, pm, radius).Distance < reach-Skin) return false;
        return AttachmentInterval(a0,am,b0,bm,p0,pm,radius,ref budget)
            && AttachmentInterval(am,a1,bm,b1,pm,p1,radius,ref budget);
    }

    static bool Contains(Vector3 low, Vector3 high, Bvh.BoundingBox box)
        => box.Min.X>=low.X && box.Min.Y>=low.Y && box.Min.Z>=low.Z
            && box.Max.X<=high.X && box.Max.Y<=high.Y && box.Max.Z<=high.Z;

    static void Parts(in ContactShape shape, Vector3 center, float radius, List<Part> result, Bvh.BoundingBox against)
    {
        result.Clear();
        if (shape.Lamina == null)
        {
            var a = shape.Pose.Base; var b = shape.Pose.Tip; var direction = b-a;
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
