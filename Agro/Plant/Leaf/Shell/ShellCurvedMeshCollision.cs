using I = Agro.Mechanics.ShellContinuousCollision.Interval;
using V = Agro.Mechanics.ShellContinuousCollision.VectorInterval;

namespace Agro.Mechanics;

/// <summary>
/// Feature BVHs over enclosed curved trajectories. Scratch is per active worker,
/// not per leaf. Mesh bounds are refitted on every call; the CCD worker may reuse
/// support transforms, never clearance results. No force is inferred from an
/// unresolved enclosure. Shared rigid transport cancels before broad phase.
/// </summary>
public static class ShellCurvedMeshCollision
{
    public sealed class Workspace
    {
        internal readonly ShellCollisionMesh.Workspace Tree;
        internal readonly ShellCollisionMesh.Bounds[] Vertices;
        public int AccountedBytes => checked(64 + Vertices.Length * 48 + Tree.AccountedBytes);
        public Workspace(ShellCollisionMesh mesh)
        { Tree = new(mesh); Vertices = new ShellCollisionMesh.Bounds[mesh.VertexCount]; }

        internal bool Refit(ReadOnlySpan<ShellVertexMotion> local, in ShellSupportMotion support)
        {
            if (local.Length != Vertices.Length) throw new ArgumentException("Motion does not match mesh.");
            var time = new I(0, 1);
            if (!support.TryTransform(time, out var transform)) return false;
            for (var i = 0; i < local.Length; ++i)
            {
                var p = transform.Point(V.Lerp(local[i], time));
                if (!p.Finite) return false;
                Vertices[i] = new(new(p.X.Low, p.Y.Low, p.Z.Low), new(p.X.High, p.Y.High, p.Z.High));
            }
            Tree.RefitBounds(Vertices); return true;
        }
    }

    public static ShellMeshCcdResult Self(ReadOnlySpan<ShellVertexMotion> local, in ShellSupportMotion support,
        Workspace mesh, ShellCurvedCollision.Workspace ccd, ShellCcdSettings settings, int maximumFeatureQueries = 100000)
    {
        settings.Validate();
        if (!support.Valid) return Invalid;
        // Blade coordinates deform locally; tip transport is common to all features.
        return ShellMeshCollision.Self(local, mesh.Tree, ccd.Linear, settings, maximumFeatureQueries);
    }

    static ShellMeshCcdResult Invalid => new(new(ShellCcdStatus.Uncertified, 0, 1, 0,
        ShellCcdLimit.NumericalResolution), ShellContactFeature.None, -1, -1, false, 0, 0);

    public static ShellMeshCcdResult Pair(ReadOnlySpan<ShellVertexMotion> a, ShellSupportMotion supportA, Workspace wa,
        ReadOnlySpan<ShellVertexMotion> b, ShellSupportMotion supportB, Workspace wb,
        ShellCurvedCollision.Workspace ccd, ShellCcdSettings settings, int maximumFeatureQueries = 100000)
    {
        settings.Validate();
        if (ReferenceEquals(wa, wb)) throw new ArgumentException("A mesh pair requires distinct worker bounds.");
        if (maximumFeatureQueries < 1) throw new ArgumentOutOfRangeException(nameof(maximumFeatureQueries));
        if (!supportA.Valid || !supportB.Valid) return Invalid;
        if (supportA == supportB)
            return ShellMeshCollision.Pair(a, wa.Tree, b, wb.Tree, ccd.Linear, settings, maximumFeatureQueries);
        ShellSupportMotion.RemoveCommonBase(ref supportA, ref supportB);
        if (!wa.Refit(a, supportA) || !wb.Refit(b, supportB)) return Invalid;
        var result = new Accumulator { Motion = new(ShellCcdStatus.Clear, 1, 1, 0, ShellCcdLimit.None), A = -1, B = -1 };
        if (InitialEdges(a, supportA, wa, b, supportB, wb, ccd, settings, maximumFeatureQueries, false, ref result)
            && InitialEdges(b, supportB, wb, a, supportA, wa, ccd, settings, maximumFeatureQueries, true, ref result)
            && Vertices(a, supportA, wa, b, supportB, wb, ccd, settings, maximumFeatureQueries, false, ref result)
            && Vertices(b, supportB, wb, a, supportA, wa, ccd, settings, maximumFeatureQueries, true, ref result))
            Edges(a, supportA, wa, b, supportB, wb, ccd, settings, maximumFeatureQueries, ref result);
        return new(result.Motion, result.Feature, result.A, result.B, result.Reversed, result.Queries, result.BoundsTests);
    }

    struct Accumulator
    {
        internal ShellCcdResult Motion;
        internal ShellContactFeature Feature;
        internal int A, B, Queries, BoundsTests;
        internal bool Reversed;
        internal bool Before(int budget)
        {
            if (Queries < budget) { ++Queries; return true; }
            Motion = new(ShellCcdStatus.Uncertified, 0, 1, Motion.VisitedBoxes, ShellCcdLimit.WorkBudget);
            Feature = ShellContactFeature.None; A = B = -1; Reversed = false; return false;
        }
        internal bool Include(ShellCcdResult r, ShellContactFeature feature, int a, int b, bool reversed)
        {
            var count = checked(Motion.VisitedBoxes + r.VisitedBoxes);
            if (r.SafeFraction < Motion.SafeFraction) { Motion = r; Feature = feature; A = a; B = b; Reversed = reversed; }
            Motion = Motion with { VisitedBoxes = count }; return Motion.SafeFraction > 0;
        }
    }

    static bool InitialEdges(ReadOnlySpan<ShellVertexMotion> a, in ShellSupportMotion sa, Workspace wa,
        ReadOnlySpan<ShellVertexMotion> b, in ShellSupportMotion sb, Workspace wb,
        ShellCurvedCollision.Workspace ccd, ShellCcdSettings settings, int budget, bool reversed, ref Accumulator r)
    {
        Span<int> stack = stackalloc int[64]; var initialA = sa.AtStart; var initialB = sb.AtStart;
        for (var e = 0; e < wa.Tree.Mesh.EdgeCount; ++e)
        {
            var edge = wa.Tree.Mesh.Edges[e]; var bounds = wa.Vertices[edge.A].Union(wa.Vertices[edge.B]);
            var segment = new ShellVertexMotion(a[edge.A].Start, a[edge.B].Start); var count = 1; stack[0] = 0;
            while (count > 0)
            {
                var n = stack[--count]; ++r.BoundsTests;
                if (!bounds.Intersects(wb.Tree.FaceBounds[n], settings.MinimumSeparation)) continue;
                var node = wb.Tree.Mesh.FaceTree[n];
                if (node.Feature < 0) { stack[count++] = node.Right; stack[count++] = node.Left; continue; }
                var f = node.Feature; var ids = wb.Tree.Mesh.Faces;
                var x = b[ids[3 * f]].Start; var y = b[ids[3 * f + 1]].Start; var z = b[ids[3 * f + 2]].Start;
                if (!r.Before(budget)) return false;
                var test = ShellCurvedCollision.VertexFace(segment, initialA, new(x, x), new(y, y), new(z, z), initialB, ccd, settings);
                if (test.Status != ShellCcdStatus.Clear) test = test with { SafeFraction = 0, CandidateTimeUpperBound = 0 };
                if (!r.Include(test, ShellContactFeature.EdgeFaceAtStart, e, f, reversed)) return false;
            }
        }
        return true;
    }

    static bool Vertices(ReadOnlySpan<ShellVertexMotion> a, in ShellSupportMotion sa, Workspace wa,
        ReadOnlySpan<ShellVertexMotion> b, in ShellSupportMotion sb, Workspace wb,
        ShellCurvedCollision.Workspace ccd, ShellCcdSettings settings, int budget, bool reversed, ref Accumulator r)
    {
        Span<int> stack = stackalloc int[64];
        for (var v = 0; v < a.Length; ++v)
        {
            var bounds = wa.Vertices[v]; var count = 1; stack[0] = 0;
            while (count > 0)
            {
                var n = stack[--count]; ++r.BoundsTests;
                if (!bounds.Intersects(wb.Tree.FaceBounds[n], settings.MinimumSeparation)) continue;
                var node = wb.Tree.Mesh.FaceTree[n];
                if (node.Feature < 0) { stack[count++] = node.Right; stack[count++] = node.Left; continue; }
                var f = node.Feature; var ids = wb.Tree.Mesh.Faces;
                if (!r.Before(budget)) return false;
                var test = ShellCurvedCollision.VertexFace(a[v], sa, b[ids[3 * f]], b[ids[3 * f + 1]], b[ids[3 * f + 2]], sb, ccd, settings);
                if (!r.Include(test, ShellContactFeature.VertexFace, v, f, reversed)) return false;
            }
        }
        return true;
    }

    static bool Edges(ReadOnlySpan<ShellVertexMotion> a, in ShellSupportMotion sa, Workspace wa,
        ReadOnlySpan<ShellVertexMotion> b, in ShellSupportMotion sb, Workspace wb,
        ShellCurvedCollision.Workspace ccd, ShellCcdSettings settings, int budget, ref Accumulator r)
    {
        Span<int> stack = stackalloc int[64];
        for (var e = 0; e < wa.Tree.Mesh.EdgeCount; ++e)
        {
            var edge = wa.Tree.Mesh.Edges[e]; var bounds = wa.Vertices[edge.A].Union(wa.Vertices[edge.B]);
            var count = 1; stack[0] = 0;
            while (count > 0)
            {
                var n = stack[--count]; ++r.BoundsTests;
                if (!bounds.Intersects(wb.Tree.EdgeBounds[n], settings.MinimumSeparation)) continue;
                var node = wb.Tree.Mesh.EdgeTree[n];
                if (node.Feature < 0) { stack[count++] = node.Right; stack[count++] = node.Left; continue; }
                var f = node.Feature; var other = wb.Tree.Mesh.Edges[f];
                if (!r.Before(budget)) return false;
                var test = ShellCurvedCollision.EdgeEdge(a[edge.A], a[edge.B], sa, b[other.A], b[other.B], sb, ccd, settings);
                if (!r.Include(test, ShellContactFeature.EdgeEdge, e, f, false)) return false;
            }
        }
        return true;
    }
}
