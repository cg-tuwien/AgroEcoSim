using System.Numerics;

namespace Agro.Collisions;

internal readonly record struct ContactShape(OrganPose Pose, LaminaTemplate? Lamina, int Feature = -1)
{
    public Bvh.BoundingBox Bounds
    {
        get
        {
            if (Lamina != null)
            {
                if (Feature < 0) return Lamina.Bounds(Pose);
                Lamina.Triangle(Pose, Feature, out var a, out var b, out var c);
                var skin = new Vector3(Pose.Radius);
                return new(Vector3.Min(a, Vector3.Min(b, c)) - skin, Vector3.Max(a, Vector3.Max(b, c)) + skin);
            }
            var pad = new Vector3(Pose.Radius);
            return new(Vector3.Min(Pose.Base, Pose.Tip) - pad, Vector3.Max(Pose.Base, Pose.Tip) + pad);
        }
    }
    public float MotionBound(in ContactShape other)
    {
        if (Lamina == null) return Pose.MotionBound(other.Pose);
        if (Feature < 0) return Lamina.MotionBound(Pose, other.Pose);
        Lamina.Triangle(Pose, Feature, out var a, out var b, out var c);
        Lamina.Triangle(other.Pose, Feature, out var x, out var y, out var z);
        return MathF.Max(Vector3.Distance(a, x), MathF.Max(Vector3.Distance(b, y), Vector3.Distance(c, z)))
            + ArcBound(other) + MathF.Abs(Pose.Radius - other.Pose.Radius);
    }
    public ContactShape Interpolate(in ContactShape other, float t) => new(Pose.Interpolate(other.Pose, t), Lamina, Feature);
    public Vector3 Support(Vector3 direction)
    {
        if (Lamina == null) return Vector3.Dot(Pose.Tip - Pose.Base, direction) < 0 ? Pose.Tip : Pose.Base;
        if (Feature >= 0)
        {
            Lamina.Triangle(Pose, Feature, out var a, out var b, out var c);
            var result = Vector3.Dot(a, direction) < Vector3.Dot(b, direction) ? a : b;
            return Vector3.Dot(result, direction) < Vector3.Dot(c, direction) ? result : c;
        }
        return Lamina.Support(Pose, direction);
    }
    public float ArcBound(in ContactShape other)
    {
        var delta = Quaternion.Normalize(other.Pose.Rotation * Quaternion.Conjugate(Pose.Rotation));
        var sine = new Vector3(delta.X, delta.Y, delta.Z).Length();
        var reach = Lamina == null ? MathF.Max(Pose.Length, other.Pose.Length)
            : Lamina.LongReach * MathF.Max(Pose.Length, other.Pose.Length)
                + Lamina.WideReach * MathF.Max(Pose.Width, other.Pose.Width)
                + MathF.Max(Lamina.ReliefAt(Pose),Lamina.ReliefAt(other.Pose));
        var growth = (Lamina?.LongReach ?? 1) * MathF.Abs(Pose.Length - other.Pose.Length)
            + (Lamina?.WideReach ?? 0) * MathF.Abs(Pose.Width - other.Pose.Width)
            + (Lamina?.ReliefChange(Pose,other.Pose) ?? 0);
        if (Lamina != null && Feature >= 0)
        {
            var a = Lamina.Vertices[Lamina.Indices[Feature * 3]];
            var b = Lamina.Vertices[Lamina.Indices[Feature * 3 + 1]];
            var c = Lamina.Vertices[Lamina.Indices[Feature * 3 + 2]];
            var longitudinal = MathF.Max(MathF.Abs(a.X + 1), MathF.Max(MathF.Abs(b.X + 1), MathF.Abs(c.X + 1))) * 0.5f;
            var width = MathF.Max(MathF.Abs(a.Y), MathF.Max(MathF.Abs(b.Y), MathF.Abs(c.Y)));
            var relief = MathF.Max(MathF.Abs(a.Z), MathF.Max(MathF.Abs(b.Z), MathF.Abs(c.Z)));
            reach = longitudinal * MathF.Max(Pose.Length, other.Pose.Length) + width * MathF.Max(Pose.Width, other.Pose.Width)
                + relief * MathF.Max(Lamina.ReliefScale(Pose.Width),Lamina.ReliefScale(other.Pose.Width));
            growth = longitudinal * MathF.Abs(Pose.Length - other.Pose.Length) + width * MathF.Abs(Pose.Width - other.Pose.Width)
                + relief * MathF.Abs(Lamina.ReliefScale(Pose.Width)-Lamina.ReliefScale(other.Pose.Width));
        }
        return reach * MathF.Max(0, 1f - MathF.Abs(delta.W)) + growth * sine * 0.5f;
    }
    public Bvh.BoundingBox SweptBounds(in ContactShape other)
    {
        var a = Bounds; var b = other.Bounds; var pad = new Vector3(ArcBound(other));
        return new(Vector3.Min(a.Min, b.Min) - pad, Vector3.Max(a.Max, b.Max) + pad);
    }
}

internal readonly record struct FoliageContact(Vector3 A, Vector3 B, Vector3 Normal, float Distance, int FeatureA, int FeatureB);

/// <summary>Thin-sheet distance and conservative continuous collision checks. No world meshes are retained.</summary>
internal static partial class FoliageGeometry
{
    [ThreadStatic] internal static long IntervalCount;
    [ThreadStatic] internal static long TriangleCount;
    internal const float Skin = 0.00001f;
    [ThreadStatic] static List<int>? TrianglesA;
    [ThreadStatic] static List<int>? TrianglesB;
    [ThreadStatic] static List<int>? SweepTriangles;
    [ThreadStatic] static List<int>? SweepFirstTriangles;
    [ThreadStatic] static List<(int A, int B)>? TemplatePairs;
    [ThreadStatic] static Vector3[]? TransformedA;
    [ThreadStatic] static Vector3[]? TransformedB;
    static Bvh.BoundingBox Pad(Bvh.BoundingBox b, float p) => new(b.Min - new Vector3(p), b.Max + new Vector3(p));
    static Bvh.BoundingBox Bounds(Vector3 a, Vector3 b, Vector3 c) => new(Vector3.Min(a, Vector3.Min(b, c)), Vector3.Max(a, Vector3.Max(b, c)));

    static FoliageContact DistanceUncached(in ContactShape a, in ContactShape b, float search)
    {
        if (a.Feature >= 0 && b.Feature >= 0 && a.Lamina != null && b.Lamina != null)
        {
            // Temporal subdivision asks about individual triangle features millions of
            // times. Prune their current boxes before distance arithmetic, and avoid
            // constructing single-item query lists for this direct narrow-phase case.
            a.Lamina.Triangle(a.Pose, a.Feature, out var p, out var q, out var r);
            b.Lamina.Triangle(b.Pose, b.Feature, out var x, out var y, out var z);
            if (!Bounds(p,q,r).Intersects(Pad(Bounds(x,y,z), search)))
                return new(a.Pose.Base,b.Pose.Base,Vector3.UnitY,search,-1,-1);
            var d = TriangleDistance(p,q,r,x,y,z,out var onA,out var onB,search);
            return d < search ? Make(onA,onB,x,y,z,d,a.Feature,b.Feature)
                : new(a.Pose.Base,b.Pose.Base,Vector3.UnitY,search,-1,-1);
        }
        if(a.Feature<0 && b.Feature<0 && BoundsSeparate(a,b,search))
            return new(a.Pose.Base,b.Pose.Base,Vector3.UnitY,search,-1,-1);
        if(a.Feature<0 && b.Feature<0 && a.Lamina!=null && b.Lamina!=null
            && ConvexSeparation(a,b,search))
            return new(a.Pose.Base,b.Pose.Base,Vector3.UnitY,search,-1,-1);
        if (a.Lamina == null && b.Lamina == null)
        {
            StaticCollider.ClosestPointsBetweenSegments(a.Pose.Base, a.Pose.Tip, b.Pose.Base, b.Pose.Tip, out var x, out var y, out var d2);
            var normal = x - y;
            if (normal.LengthSquared() < 1e-16f) normal = Vector3.Cross(a.Pose.Tip - a.Pose.Base, b.Pose.Tip - b.Pose.Base);
            if (normal.LengthSquared() < 1e-16f) normal = Vector3.Cross(a.Pose.Tip - a.Pose.Base, Vector3.UnitY);
            if (normal.LengthSquared() < 1e-16f) normal = Vector3.UnitX;
            return new(x, y, Vector3.Normalize(normal), MathF.Sqrt(d2), -1, -1);
        }
        if (b.Lamina == null)
        {
            var reverse = Distance(b, a, search);
            return new(reverse.B, reverse.A, -reverse.Normal, reverse.Distance, reverse.FeatureB, reverse.FeatureA);
        }
        var hitsB = TrianglesB ??= [];
        var best = new FoliageContact(a.Pose.Base, b.Pose.Base, Vector3.UnitY, search, -1, -1);
        if (a.Lamina == null)
        {
            if (b.Feature >= 0) { hitsB.Clear(); hitsB.Add(b.Feature); }
            else b.Lamina.Query(b.Pose, Pad(a.Bounds, search), hitsB);
            foreach (var j in hitsB)
            {
                b.Lamina.Triangle(b.Pose, j, out var p, out var q, out var r);
                ++TriangleCount;
                var d = StaticCollider.SegmentTriangleDistance(a.Pose.Base, a.Pose.Tip, p, q - p, r - p, out var onB, out var onA);
                if (d < best.Distance) best = Make(onA, onB, p, q, r, d, -1, j);
                if (d <= 1e-9f || d < a.Pose.Radius + b.Pose.Radius - Skin) return best;
            }
            return best;
        }
        if (a.Feature < 0 && b.Feature < 0)
        {
            var pairs = TemplatePairs ??= [];
            a.Lamina.QueryPairs(a.Pose, b.Lamina, b.Pose, search, pairs);
            // In a dense query the same vertex belongs to many candidate facet pairs.
            // Transform once for this query; no pose hashing occurs in the inner loop.
            // Sparse queries keep direct transforms so preparing an entire mesh cannot
            // cost more than the triangle work it replaces.
            var va = pairs.Count * 3 > a.Lamina.Vertices.Length
                ? TransformVertices(a, ref TransformedA) : ReadOnlySpan<Vector3>.Empty;
            var vb = pairs.Count * 3 > b.Lamina.Vertices.Length
                ? TransformVertices(b, ref TransformedB) : ReadOnlySpan<Vector3>.Empty;
            foreach (var (i, j) in pairs)
            {
                Triangle(a, i, va, out var p, out var q, out var r);
                Triangle(b, j, vb, out var x, out var y, out var z);
                var d = TriangleDistance(p, q, r, x, y, z, out var onA, out var onB,best.Distance);
                if (d < best.Distance) best = Make(onA, onB, x, y, z, d, i, j);
                if (d <= 1e-9f || d < a.Pose.Radius + b.Pose.Radius - Skin) return best;
            }
            return best;
        }
        var hitsA = TrianglesA ??= [];
        if (a.Feature >= 0) { hitsA.Clear(); hitsA.Add(a.Feature); }
        else a.Lamina.Query(a.Pose, Pad(b.Bounds, search), hitsA);
        foreach (var i in hitsA)
        {
            a.Lamina.Triangle(a.Pose, i, out var p, out var q, out var r);
            if (b.Feature >= 0) { hitsB.Clear(); hitsB.Add(b.Feature); }
            else b.Lamina.Query(b.Pose, Pad(Bounds(p, q, r), best.Distance), hitsB);
            foreach (var j in hitsB)
            {
                b.Lamina.Triangle(b.Pose, j, out var x, out var y, out var z);
                var d = TriangleDistance(p, q, r, x, y, z, out var onA, out var onB,best.Distance);
                if (d < best.Distance) best = Make(onA, onB, x, y, z, d, i, j);
                if (d <= 1e-9f || d < a.Pose.Radius + b.Pose.Radius - Skin) return best;
            }
        }
        return best;
    }

    static ReadOnlySpan<Vector3> TransformVertices(in ContactShape shape, ref Vector3[]? buffer)
    {
        var vertices = shape.Lamina!.Vertices;
        if (buffer == null || buffer.Length < vertices.Length) buffer = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; ++i) buffer[i] = shape.Lamina.Transform(shape.Pose, vertices[i]);
        return buffer.AsSpan(0, vertices.Length);
    }

    static void Triangle(in ContactShape shape, int feature, ReadOnlySpan<Vector3> vertices,
        out Vector3 a, out Vector3 b, out Vector3 c)
    {
        if (vertices.IsEmpty) { shape.Lamina!.Triangle(shape.Pose,feature,out a,out b,out c); return; }
        var indices = shape.Lamina!.Indices;
        a = vertices[indices[feature*3]]; b = vertices[indices[feature*3+1]]; c = vertices[indices[feature*3+2]];
    }

    static bool BoundsSeparate(in ContactShape a, in ContactShape b, float clearance)
    {
        // World AABBs overlap heavily in a crown. Oriented template bounds often prove
        // that its oblique blades remain separated without a quadratic triangle walk.
        Span<Vector3> axes=stackalloc Vector3[6];
        axes[0]=Vector3.Transform(Vector3.UnitX,a.Pose.Rotation);
        axes[1]=Vector3.Transform(Vector3.UnitY,a.Pose.Rotation);
        axes[2]=Vector3.Transform(Vector3.UnitZ,a.Pose.Rotation);
        axes[3]=Vector3.Transform(Vector3.UnitX,b.Pose.Rotation);
        axes[4]=Vector3.Transform(Vector3.UnitY,b.Pose.Rotation);
        axes[5]=Vector3.Transform(Vector3.UnitZ,b.Pose.Rotation);
        for(int i=0;i<6;++i) if(Separated(a,b,axes[i],clearance)) return true;
        for(int i=0;i<3;++i)for(int j=3;j<6;++j)
        {
            var axis=Vector3.Cross(axes[i],axes[j]);var length=axis.Length();
            if(length>1e-6f && Separated(a,b,axis/length,clearance)) return true;
        }
        return false;
    }

    static bool ConvexSeparation(in ContactShape a,in ContactShape b,float clearance)
    {
        // Search for a separating plane of the complete displayed vertex sets. Closest
        // points on successive support segments suggest directions; they never authorize
        // separation themselves. Only the actual extrema projected on a unit axis do so.
        // Concavities and intersecting hulls fall through to the triangle narrow phase.
        var closest=(a.Bounds.Min+a.Bounds.Max-b.Bounds.Min-b.Bounds.Max)*.5f;
        for(int iteration=0;iteration<10;++iteration)
        {
            var size=closest.LengthSquared();
            if(size<1e-16f) return false;
            var direction=closest/MathF.Sqrt(size);
            var support=a.Support(direction)-b.Support(-direction);
            if(Vector3.Dot(support,direction)>=clearance+Skin) return true;
            if(iteration==0) {closest=support;continue;}
            var delta=support-closest;
            var d2=delta.LengthSquared();
            if(d2<1e-16f) return false;
            var fraction=Math.Clamp(-Vector3.Dot(closest,delta)/d2,0f,1f);
            if(fraction<1e-4f) return false;
            closest+=delta*fraction;
        }
        return false;
    }

    static bool Separated(in ContactShape a,in ContactShape b,Vector3 axis,float clearance)
    {
        Project(a,axis,out var al,out var ah);Project(b,axis,out var bl,out var bh);
        return al-bh>=clearance+1e-7f || bl-ah>=clearance+1e-7f;
    }

    static void Project(in ContactShape shape,Vector3 axis,out float low,out float high)
    {
        if(shape.Lamina!=null){shape.Lamina.ProjectBounds(shape.Pose,axis,out low,out high);return;}
        var a=Vector3.Dot(shape.Pose.Base,axis);var b=Vector3.Dot(shape.Pose.Tip,axis);
        low=MathF.Min(a,b);high=MathF.Max(a,b);
    }

    static FoliageContact Make(Vector3 a, Vector3 b, Vector3 p, Vector3 q, Vector3 r, float distance, int fa, int fb)
    {
        var normal = distance > 1e-7f ? a - b : Vector3.Cross(q - p, r - p);
        return new(a, b, normal.LengthSquared() > 1e-20f ? Vector3.Normalize(normal) : Vector3.UnitY, distance, fa, fb);
    }

    internal static float TriangleDistance(Vector3 a, Vector3 b, Vector3 c, Vector3 p, Vector3 q, Vector3 r,
        out Vector3 onA, out Vector3 onB, float search = float.PositiveInfinity)
    {
        ++TriangleCount;
        var ab = b - a; var ac = c - a; var pq = q - p; var pr = r - p;
        var na = Vector3.Cross(ab, ac); var nb = Vector3.Cross(pq, pr);
        // Most candidate facets are on separate planes even when their boxes overlap.
        // A projection lower bound avoids the 6 vertex/face and 9 edge/edge distances.
        // Returning the search ceiling cannot replace a closer feature or create contact.
        if (float.IsFinite(search) && (PlaneSeparates(na,ab,ac,p-a,q-a,r-a,search)
            || PlaneSeparates(nb,pq,pr,a-p,b-p,c-p,search)))
        { onA = a; onB = p; return search; }
        if (Pierces(a,b,p,pq,pr,nb,out var hit) || Pierces(b,c,p,pq,pr,nb,out hit)
            || Pierces(c,a,p,pq,pr,nb,out hit) || Pierces(p,q,a,ab,ac,na,out hit)
            || Pierces(q,r,a,ab,ac,na,out hit) || Pierces(r,p,a,ab,ac,na,out hit))
        { onA = onB = hit; return 0; }
        // Six segment/triangle calls repeated every vertex/face and edge/edge
        // distance twice, with six square roots. Evaluate each feature once instead.
        Span<Vector3> aa = stackalloc Vector3[3] { a, b, c };
        Span<Vector3> bb = stackalloc Vector3[3] { p, q, r };
        float best = float.PositiveInfinity; onA = a; onB = p;
        for (int i = 0; i < 3; ++i)
        {
            Consider(aa[i], StaticCollider.ClosestPointOnTriangle(aa[i], p, pq, pr), ref best, ref onA, ref onB);
            Consider(StaticCollider.ClosestPointOnTriangle(bb[i], a, ab, ac), bb[i], ref best, ref onA, ref onB);
            for (int j = 0; j < 3; ++j)
            {
                StaticCollider.ClosestPointsBetweenSegments(aa[i], aa[(i+1)%3], bb[j], bb[(j+1)%3], out var x, out var y, out var d2);
                if (d2 < best) { best = d2; onA = x; onB = y; }
            }
        }
        return MathF.Sqrt(best);
    }
    static bool PlaneSeparates(Vector3 normal, Vector3 ab, Vector3 ac, Vector3 p, Vector3 q, Vector3 r, float search)
    {
        var size = normal.LengthSquared();
        if (size < 1e-30f) return false;
        var b = Vector3.Dot(normal, ab); var c = Vector3.Dot(normal, ac);
        var x = Vector3.Dot(normal, p); var y = Vector3.Dot(normal, q); var z = Vector3.Dot(normal, r);
        var lowA = MathF.Min(0,MathF.Min(b,c)); var highA = MathF.Max(0,MathF.Max(b,c));
        var lowB = MathF.Min(x,MathF.Min(y,z)); var highB = MathF.Max(x,MathF.Max(y,z));
        var gap = MathF.Max(lowA-highB,lowB-highA);
        var padded = search + Skin * .01f;
        return gap > 0 && gap * gap > padded * padded * size;
    }
    static void Consider(Vector3 a, Vector3 b, ref float best, ref Vector3 onA, ref Vector3 onB)
    {
        var d2 = Vector3.DistanceSquared(a,b);
        if (d2 < best) { best = d2; onA = a; onB = b; }
    }
    static bool Pierces(Vector3 start, Vector3 end, Vector3 a, Vector3 ab, Vector3 ac, Vector3 normal, out Vector3 hit)
    {
        hit = default;
        var direction = end - start;
        if (direction.LengthSquared() <= 1e-16f) return false;
        var denominator = Vector3.Dot(normal, direction);
        if (MathF.Abs(denominator) <= 1e-20f) return false;
        var t = Vector3.Dot(normal, a-start) / denominator;
        if (t < 0 || t > 1) return false;
        hit = start + direction*t;
        return Vector3.DistanceSquared(hit, StaticCollider.ClosestPointOnTriangle(hit,a,ab,ac)) <= 1e-12f;
    }

    static bool SweepUncached(in ContactShape fromA, in ContactShape toA, in ContactShape fromB, in ContactShape toB, int depth = 0)
    {
        var intervals = 256;
        return SweepInterval(fromA, toA, fromB, toB, depth, ref intervals);
    }

    static bool SweepInterval(in ContactShape fromA, in ContactShape toA, in ContactShape fromB, in ContactShape toB, int depth, ref int intervals)
    {
        ++IntervalCount;
        if (--intervals < 0) return false; // Suspend uncertified motion; never skip a required check.
        if (!fromA.SweptBounds(toA).Intersects(fromB.SweptBounds(toB))) return true;
        if (depth == 0 && (fromA.Feature < 0 || fromB.Feature < 0))
        {
            // A whole-sheet plane certificate is much cheaper than the triangle distance
            // queried below. It bounds the entire path, including curvature and rotation.
            if (SweptPlanesSeparate(fromA,toA,fromB,toB)) return true;
            // Certify small whole-organ motion before splitting a lamina into triangles.
            // Daily growth usually moves only a fraction of the gap to its neighbours;
            // a distance bound proves all vertex/face and edge/edge paths at once.
            var movement = fromA.MotionBound(toA) + fromB.MotionBound(toB);
            var padding = MathF.Max(fromA.Pose.Radius, toA.Pose.Radius)
                + MathF.Max(fromB.Pose.Radius, toB.Pose.Radius);
            var separation = Distance(fromA, fromB, padding + movement + Skin).Distance;
            if (separation >= padding + movement - Skin) return true;
        }
        if (fromA.Lamina != null && fromA.Feature < 0)
        {
            // This local list must survive nested queries against the other lamina.
            var candidates = SweepFirstTriangles ??= [];
            fromA.Lamina.Query(fromA.Pose, Pad(fromB.SweptBounds(toB), fromA.MotionBound(toA)), candidates);
            foreach (var i in candidates)
                if (!SweepInterval(fromA with { Feature = i }, toA with { Feature = i }, fromB, toB, depth, ref intervals)) return false;
            return true;
        }
        if (fromB.Lamina != null && fromB.Feature < 0)
        {
            var candidates = SweepTriangles ??= [];
            var relative = fromA.SweptBounds(toA);
            fromB.Lamina.Query(fromB.Pose, Pad(relative, fromB.MotionBound(toB) + fromB.Pose.Radius), candidates);
            foreach (var j in candidates)
                if (!SweepInterval(fromA, toA, fromB with { Feature = j }, toB with { Feature = j }, depth, ref intervals)) return false;
            return true;
        }
        var midA = fromA.Interpolate(toA, 0.5f); var midB = fromB.Interpolate(toB, 0.5f);
        var reach = MathF.Max(fromA.Pose.Radius, toA.Pose.Radius) + MathF.Max(fromB.Pose.Radius, toB.Pose.Radius);
        var motion = MathF.Max(fromA.MotionBound(midA), midA.MotionBound(toA)) + MathF.Max(fromB.MotionBound(midB), midB.MotionBound(toB));
        var hit = Distance(midA, midB, reach + motion + Skin);
        if (hit.Distance >= reach + motion + Skin * 0.5f) return true;
        if (hit.Distance < reach - Skin) return false;
        // A separating plane for the WHOLE finite bodies certifies sliding without recursively
        // rejecting every persistent touch. Arc bounds cover quaternion rotation and growth.
        var normal = hit.Normal;
        if (Vector3.Dot(midA.Pose.Base - midB.Pose.Base, normal) < 0) normal = -normal;
        var gap0 = Vector3.Dot(fromA.Support(normal) - fromB.Support(-normal), normal);
        var gap1 = Vector3.Dot(toA.Support(normal) - toB.Support(-normal), normal);
        if (MathF.Min(gap0, gap1) - fromA.ArcBound(toA) - fromB.ArcBound(toB) >= reach - Skin) return true;
        if (motion < Skin * 0.25f) return hit.Distance >= reach - Skin;
        if (depth >= 16) return false; // Uncertified motion is never accepted on budget exhaustion.
        return SweepInterval(fromA, midA, fromB, midB, depth + 1, ref intervals)
            && SweepInterval(midA, toA, midB, toB, depth + 1, ref intervals);
    }

    static bool SweptPlanesSeparate(in ContactShape a0, in ContactShape a1, in ContactShape b0, in ContactShape b1)
    {
        if (a0.Lamina != null && (SeparatingPlane(a0,a1,b0,b1,Vector3.Transform(Vector3.UnitY,a0.Pose.Rotation))
            || a0.Pose.Rotation != a1.Pose.Rotation && SeparatingPlane(a0,a1,b0,b1,Vector3.Transform(Vector3.UnitY,a1.Pose.Rotation)))) return true;
        return b0.Lamina != null && (SeparatingPlane(a0,a1,b0,b1,Vector3.Transform(Vector3.UnitY,b0.Pose.Rotation))
            || b0.Pose.Rotation != b1.Pose.Rotation && SeparatingPlane(a0,a1,b0,b1,Vector3.Transform(Vector3.UnitY,b1.Pose.Rotation)));
    }

    static bool SeparatingPlane(in ContactShape a0, in ContactShape a1, in ContactShape b0, in ContactShape b1, Vector3 normal)
    {
        var reach = MathF.Max(a0.Pose.Radius, a1.Pose.Radius) + MathF.Max(b0.Pose.Radius, b1.Pose.Radius)
            + a0.ArcBound(a1) + b0.ArcBound(b1) - Skin;
        var gap0 = Vector3.Dot(a0.Support(normal) - b0.Support(-normal), normal);
        var gap1 = Vector3.Dot(a1.Support(normal) - b1.Support(-normal), normal);
        if (MathF.Min(gap0, gap1) >= reach) return true;
        normal = -normal;
        gap0 = Vector3.Dot(a0.Support(normal) - b0.Support(-normal), normal);
        gap1 = Vector3.Dot(a1.Support(normal) - b1.Support(-normal), normal);
        return MathF.Min(gap0, gap1) >= reach;
    }
}
