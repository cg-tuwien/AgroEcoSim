namespace Agro.Mechanics;

/// <summary>
/// Immutable collision topology and balanced feature trees, shared by a morphology.
/// Trees split rest-space centroids; accepted/trial motion bounds live only in worker
/// scratch. No geometry, contact history or per-organ cache is retained here.
/// </summary>
public sealed class ShellCollisionMesh
{
    internal readonly record struct Edge(int A, int B);
    internal readonly record struct Node(int Left, int Right, int Feature);
    internal readonly int[] Faces;
    internal readonly Edge[] Edges;
    internal readonly Node[] FaceTree, EdgeTree;
    public int VertexCount { get; }
    public int FaceCount => Faces.Length / 3;
    public int EdgeCount => Edges.Length;

    public ShellCollisionMesh(ReadOnlySpan<ShellVector> rest, ReadOnlySpan<int> triangles)
    {
        if (rest.Length == 0 || triangles.Length == 0 || triangles.Length % 3 != 0)
            throw new ArgumentException("A nonempty indexed triangle mesh is required.");
        VertexCount = rest.Length; Faces = triangles.ToArray();
        var unique = new SortedSet<(int A, int B)>();
        foreach (var v in rest)
            if (!double.IsFinite(v.X) || !double.IsFinite(v.Y) || !double.IsFinite(v.Z))
                throw new ArgumentException("Rest coordinates must be finite.");
        foreach (var i in triangles)
            if ((uint)i >= (uint)VertexCount) throw new ArgumentException("Triangle index outside mesh.");
        for (var f = 0; f < FaceCount; ++f)
            for (var j = 0; j < 3; ++j)
            {
                var a = Faces[3 * f + j]; var b = Faces[3 * f + (j + 1) % 3];
                unique.Add((Math.Min(a, b), Math.Max(a, b)));
            }
        Edges = unique.Select(e => new Edge(e.A, e.B)).ToArray();
        var centers = new ShellVector[FaceCount];
        for (var f = 0; f < FaceCount; ++f)
            centers[f] = rest[Faces[3 * f]] * (1.0 / 3) + rest[Faces[3 * f + 1]] * (1.0 / 3) + rest[Faces[3 * f + 2]] * (1.0 / 3);
        FaceTree = Build(centers);
        centers = new ShellVector[EdgeCount];
        for (var e = 0; e < EdgeCount; ++e) centers[e] = rest[Edges[e].A] * .5 + rest[Edges[e].B] * .5;
        EdgeTree = Build(centers);
    }

    static Node[] Build(ShellVector[] centers)
    {
        var nodes = new Node[checked(centers.Length * 2 - 1)];
        var ids = Enumerable.Range(0, centers.Length).ToArray(); var next = 0;
        int Split(int first, int count)
        {
            var id = next++;
            if (count == 1) { nodes[id] = new(-1, -1, ids[first]); return id; }
            var min = centers[ids[first]]; var max = min;
            for (var i = first + 1; i < first + count; ++i)
            {
                var p = centers[ids[i]];
                min = new(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                max = new(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
            }
            var extent = max - min;
            var axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
            double Coordinate(int i) => axis == 0 ? centers[i].X : axis == 1 ? centers[i].Y : centers[i].Z;
            Array.Sort(ids, first, count, Comparer<int>.Create((a, b) =>
            { var c = Coordinate(a).CompareTo(Coordinate(b)); return c != 0 ? c : a.CompareTo(b); }));
            var half = count / 2;
            var left = Split(first, half); var right = Split(first + half, count - half);
            nodes[id] = new(left, right, -1); return id;
        }
        Split(0, ids.Length); return nodes;
    }

    internal readonly record struct Bounds(ShellVector Min, ShellVector Max)
    {
        internal static Bounds Motion(in ShellVertexMotion p) => new(
            new(Math.Min(p.Start.X, p.End.X), Math.Min(p.Start.Y, p.End.Y), Math.Min(p.Start.Z, p.End.Z)),
            new(Math.Max(p.Start.X, p.End.X), Math.Max(p.Start.Y, p.End.Y), Math.Max(p.Start.Z, p.End.Z)));
        internal Bounds Union(in Bounds b) => new(
            new(Math.Min(Min.X, b.Min.X), Math.Min(Min.Y, b.Min.Y), Math.Min(Min.Z, b.Min.Z)),
            new(Math.Max(Max.X, b.Max.X), Math.Max(Max.Y, b.Max.Y), Math.Max(Max.Z, b.Max.Z)));
        internal bool Intersects(in Bounds b, double separation)
            => Math.BitDecrement(b.Min.X - Max.X) <= separation && Math.BitDecrement(Min.X - b.Max.X) <= separation
            && Math.BitDecrement(b.Min.Y - Max.Y) <= separation && Math.BitDecrement(Min.Y - b.Max.Y) <= separation
            && Math.BitDecrement(b.Min.Z - Max.Z) <= separation && Math.BitDecrement(Min.Z - b.Max.Z) <= separation;
    }

    /// <summary>
    /// Reusable bounds for one mesh being queried by a worker. Refit explicitly on
    /// each supplied trial, so stale revision IDs cannot certify changed geometry.
    /// This is proportional worker scratch, not persistent leaf state.
    /// </summary>
    public sealed class Workspace
    {
        internal readonly ShellCollisionMesh Mesh;
        internal readonly Bounds[] FaceBounds, EdgeBounds;
        public int AccountedBytes => checked(112 + 48 * (FaceBounds.Length + EdgeBounds.Length));
        public Workspace(ShellCollisionMesh mesh)
        {
            Mesh = mesh; FaceBounds = new Bounds[mesh.FaceTree.Length]; EdgeBounds = new Bounds[mesh.EdgeTree.Length];
        }
        internal bool Refit(ReadOnlySpan<ShellVertexMotion> motion)
        {
            if (motion.Length != Mesh.VertexCount) throw new ArgumentException("Motion does not match mesh.");
            foreach (var p in motion)
                if (!double.IsFinite(p.Start.X) || !double.IsFinite(p.Start.Y) || !double.IsFinite(p.Start.Z)
                    || !double.IsFinite(p.End.X) || !double.IsFinite(p.End.Y) || !double.IsFinite(p.End.Z)) return false;
            for (var n = FaceBounds.Length - 1; n >= 0; --n)
            {
                var node = Mesh.FaceTree[n];
                FaceBounds[n] = node.Feature < 0 ? FaceBounds[node.Left].Union(FaceBounds[node.Right])
                    : Bounds.Motion(motion[Mesh.Faces[3 * node.Feature]])
                        .Union(Bounds.Motion(motion[Mesh.Faces[3 * node.Feature + 1]]))
                        .Union(Bounds.Motion(motion[Mesh.Faces[3 * node.Feature + 2]]));
            }
            for (var n = EdgeBounds.Length - 1; n >= 0; --n)
            {
                var node = Mesh.EdgeTree[n]; var edge = node.Feature < 0 ? default : Mesh.Edges[node.Feature];
                EdgeBounds[n] = node.Feature < 0 ? EdgeBounds[node.Left].Union(EdgeBounds[node.Right])
                    : Bounds.Motion(motion[edge.A]).Union(Bounds.Motion(motion[edge.B]));
            }
            return true;
        }

        internal void RefitBounds(ReadOnlySpan<Bounds> vertices)
        {
            if (vertices.Length != Mesh.VertexCount) throw new ArgumentException("Bounds do not match mesh.");
            for (var n = FaceBounds.Length - 1; n >= 0; --n)
            {
                var node = Mesh.FaceTree[n];
                FaceBounds[n] = node.Feature < 0 ? FaceBounds[node.Left].Union(FaceBounds[node.Right])
                    : vertices[Mesh.Faces[3 * node.Feature]].Union(vertices[Mesh.Faces[3 * node.Feature + 1]])
                        .Union(vertices[Mesh.Faces[3 * node.Feature + 2]]);
            }
            for (var n = EdgeBounds.Length - 1; n >= 0; --n)
            {
                var node = Mesh.EdgeTree[n]; var edge = node.Feature < 0 ? default : Mesh.Edges[node.Feature];
                EdgeBounds[n] = node.Feature < 0 ? EdgeBounds[node.Left].Union(EdgeBounds[node.Right])
                    : vertices[edge.A].Union(vertices[edge.B]);
            }
        }
    }
}

public enum ShellContactFeature : byte { None, VertexFace, EdgeEdge, EdgeFaceAtStart }
public readonly record struct ShellMeshCcdResult(ShellCcdResult Motion, ShellContactFeature Feature,
    int FeatureA, int FeatureB, bool Reversed, int FeatureQueries, int BoundsTests);

/// <summary>
/// Whole-surface linear motion certification with unique edges, feature BVHs and
/// connected-feature exclusions. Initial edge/face piercing is checked explicitly.
/// It supplies a safe prefix, not contact reactions or a manifold. Curved support
/// paths must be enclosed separately before use.
/// </summary>
public static class ShellMeshCollision
{
    public static ShellMeshCcdResult Self(ReadOnlySpan<ShellVertexMotion> motion, ShellCollisionMesh.Workspace mesh,
        ShellContinuousCollision.Workspace ccd, ShellCcdSettings settings, int maximumFeatureQueries = 100000)
        => Query(motion, mesh, motion, mesh, ccd, settings, maximumFeatureQueries, true);

    public static ShellMeshCcdResult Pair(ReadOnlySpan<ShellVertexMotion> a, ShellCollisionMesh.Workspace meshA,
        ReadOnlySpan<ShellVertexMotion> b, ShellCollisionMesh.Workspace meshB,
        ShellContinuousCollision.Workspace ccd, ShellCcdSettings settings, int maximumFeatureQueries = 100000)
    {
        if (ReferenceEquals(meshA, meshB)) throw new ArgumentException("A mesh pair requires distinct worker bounds.");
        return Query(a, meshA, b, meshB, ccd, settings, maximumFeatureQueries, false);
    }

    static ShellMeshCcdResult Query(ReadOnlySpan<ShellVertexMotion> a, ShellCollisionMesh.Workspace wa,
        ReadOnlySpan<ShellVertexMotion> b, ShellCollisionMesh.Workspace wb, ShellContinuousCollision.Workspace ccd,
        ShellCcdSettings settings, int budget, bool self)
    {
        settings.Validate(); if (budget < 1) throw new ArgumentOutOfRangeException(nameof(budget));
        var result = new Accumulator { Result = new(ShellCcdStatus.Clear, 1, 1, 0, ShellCcdLimit.None), A = -1, B = -1 };
        if (!wa.Refit(a) || (!self && !wb.Refit(b)))
            result.Result = new(ShellCcdStatus.Uncertified, 0, 1, 0, ShellCcdLimit.NumericalResolution);
        // VF/EE events characterize first contact from a disjoint start. They
        // alone do not detect an edge already piercing a face at t=0. Check
        // static segment/triangle intersections before certifying motion.
        else if (InitialEdges(a, wa, b, wb, ccd, settings, budget, self, false, ref result)
            && (self || InitialEdges(b, wb, a, wa, ccd, settings, budget, false, true, ref result))
            && Vertices(a, b, wb, ccd, settings, budget, self, false, ref result)
            && (self || Vertices(b, a, wa, ccd, settings, budget, false, true, ref result)))
            Edges(a, wa, b, wb, ccd, settings, budget, self, ref result);
        return new(result.Result, result.Feature, result.A, result.B, result.Reversed, result.Queries, result.BoundsTests);
    }

    struct Accumulator
    {
        internal ShellCcdResult Result;
        internal ShellContactFeature Feature;
        internal int A, B, Queries, BoundsTests;
        internal bool Reversed;
        internal bool BeforeQuery(int budget)
        {
            if (Queries++ < budget) return true;
            --Queries; Result = new(ShellCcdStatus.Uncertified, 0, 1, Result.VisitedBoxes, ShellCcdLimit.WorkBudget);
            Feature = ShellContactFeature.None; A = B = -1; Reversed = false; return false;
        }
        internal bool Include(ShellCcdResult r, ShellContactFeature feature, int a, int b, bool reversed)
        {
            var visits = checked(Result.VisitedBoxes + r.VisitedBoxes);
            if (r.SafeFraction < Result.SafeFraction)
            { Result = r; Feature = feature; A = a; B = b; Reversed = reversed; }
            Result = Result with { VisitedBoxes = visits };
            return Result.SafeFraction > 0;
        }
    }

    static bool InitialEdges(ReadOnlySpan<ShellVertexMotion> a, ShellCollisionMesh.Workspace wa,
        ReadOnlySpan<ShellVertexMotion> b, ShellCollisionMesh.Workspace wb, ShellContinuousCollision.Workspace ccd,
        ShellCcdSettings settings, int budget, bool self, bool reversed, ref Accumulator result)
    {
        Span<int> stack = stackalloc int[64];
        for (var e = 0; e < wa.Mesh.EdgeCount; ++e)
        {
            var edge = wa.Mesh.Edges[e];
            var segment = new ShellVertexMotion(a[edge.A].Start, a[edge.B].Start);
            var bound = ShellCollisionMesh.Bounds.Motion(segment); var count = 1; stack[0] = 0;
            while (count > 0)
            {
                var n = stack[--count]; ++result.BoundsTests;
                if (!bound.Intersects(wb.FaceBounds[n], settings.MinimumSeparation)) continue;
                var node = wb.Mesh.FaceTree[n];
                if (node.Feature < 0) { stack[count++] = node.Right; stack[count++] = node.Left; continue; }
                var f = node.Feature; var i = wb.Mesh.Faces[3 * f]; var j = wb.Mesh.Faces[3 * f + 1]; var k = wb.Mesh.Faces[3 * f + 2];
                if (self && (edge.A == i || edge.A == j || edge.A == k || edge.B == i || edge.B == j || edge.B == k)) continue;
                var x = new ShellVertexMotion(b[i].Start, b[i].Start);
                var y = new ShellVertexMotion(b[j].Start, b[j].Start);
                var z = new ShellVertexMotion(b[k].Start, b[k].Start);
                ++result.BoundsTests;
                if (!bound.Intersects(ShellCollisionMesh.Bounds.Motion(x).Union(ShellCollisionMesh.Bounds.Motion(y))
                    .Union(ShellCollisionMesh.Bounds.Motion(z)), settings.MinimumSeparation)) continue;
                if (!result.BeforeQuery(budget)) return false;
                var r = ShellContinuousCollision.VertexFace(segment, x, y, z, ccd, settings);
                if (r.Status != ShellCcdStatus.Clear) r = r with { SafeFraction = 0, CandidateTimeUpperBound = 0 };
                if (!result.Include(r, ShellContactFeature.EdgeFaceAtStart, e, f, reversed)) return false;
            }
        }
        return true;
    }

    static bool Vertices(ReadOnlySpan<ShellVertexMotion> a, ReadOnlySpan<ShellVertexMotion> b,
        ShellCollisionMesh.Workspace wb, ShellContinuousCollision.Workspace ccd, ShellCcdSettings settings,
        int budget, bool self, bool reversed, ref Accumulator result)
    {
        Span<int> stack = stackalloc int[64];
        for (var v = 0; v < a.Length; ++v)
        {
            var bound = ShellCollisionMesh.Bounds.Motion(a[v]); var count = 1; stack[0] = 0;
            while (count > 0)
            {
                var n = stack[--count]; ++result.BoundsTests;
                if (!bound.Intersects(wb.FaceBounds[n], settings.MinimumSeparation)) continue;
                var node = wb.Mesh.FaceTree[n];
                if (node.Feature < 0) { stack[count++] = node.Right; stack[count++] = node.Left; continue; }
                var f = node.Feature; var i = wb.Mesh.Faces[3 * f]; var j = wb.Mesh.Faces[3 * f + 1]; var k = wb.Mesh.Faces[3 * f + 2];
                if (self && (v == i || v == j || v == k)) continue;
                if (!result.BeforeQuery(budget)) return false;
                var r = ShellContinuousCollision.VertexFace(a[v], b[i], b[j], b[k], ccd, settings);
                if (!result.Include(r, ShellContactFeature.VertexFace, v, f, reversed)) return false;
            }
        }
        return true;
    }

    static bool Edges(ReadOnlySpan<ShellVertexMotion> a, ShellCollisionMesh.Workspace wa,
        ReadOnlySpan<ShellVertexMotion> b, ShellCollisionMesh.Workspace wb,
        ShellContinuousCollision.Workspace ccd, ShellCcdSettings settings, int budget, bool self, ref Accumulator result)
    {
        Span<int> stack = stackalloc int[64];
        for (var e = 0; e < wa.Mesh.EdgeCount; ++e)
        {
            var edge = wa.Mesh.Edges[e]; var bound = ShellCollisionMesh.Bounds.Motion(a[edge.A]).Union(ShellCollisionMesh.Bounds.Motion(a[edge.B]));
            var count = 1; stack[0] = 0;
            while (count > 0)
            {
                var n = stack[--count]; ++result.BoundsTests;
                if (!bound.Intersects(wb.EdgeBounds[n], settings.MinimumSeparation)) continue;
                var node = wb.Mesh.EdgeTree[n];
                if (node.Feature < 0) { stack[count++] = node.Right; stack[count++] = node.Left; continue; }
                var f = node.Feature; var other = wb.Mesh.Edges[f];
                if (self && (f <= e || edge.A == other.A || edge.A == other.B || edge.B == other.A || edge.B == other.B)) continue;
                if (!result.BeforeQuery(budget)) return false;
                var r = ShellContinuousCollision.EdgeEdge(a[edge.A], a[edge.B], b[other.A], b[other.B], ccd, settings);
                if (!result.Include(r, ShellContactFeature.EdgeEdge, e, f, false)) return false;
            }
        }
        return true;
    }
}
