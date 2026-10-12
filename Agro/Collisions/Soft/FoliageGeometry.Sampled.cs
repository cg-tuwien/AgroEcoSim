using System.Numerics;

namespace Agro.Collisions;

internal static partial class FoliageGeometry
{
    [ThreadStatic] static List<int>? SampleHitsA;
    [ThreadStatic] static List<int>? SampleHitsB;

    /// <summary>
    /// The soft engine's blade-to-blade distance. Exact while the overlap is small; sampled when
    /// it is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The exact query gathers every triangle pair whose padded boxes overlap and takes the
    /// minimum over them. Two blades lying along each other - a sedge tussock is made of little
    /// else - put every triangle of the overlap within the band of every triangle opposite, so
    /// the candidate count is the product of the two triangle counts: measured, one such pair
    /// cost 10-16 ms and a single Carex at a hundred days spent 95 % of its contact time there.
    /// </para>
    /// <para>
    /// Past <paramref name="exactPairBudget"/> candidates the query switches to sampling: each
    /// vertex of either blade against the other blade's triangle tree, pruned by the best
    /// distance so far, then an exact triangle-pair refinement around the best vertex. For two
    /// sheets lying along each other the nearest points are at a vertex up to the relief, and for
    /// two sheets crossing at an angle the vertex nearest the crossing is within an edge of it
    /// and its incident triangles reach the crossing, so the refinement finds the zero. The
    /// answer is an upper bound on the true distance, within an edge length times the crossing
    /// angle in the worst case, which is well inside what a soft, banded response can tell. The
    /// certified engine never comes here.
    /// </para>
    /// </remarks>
    internal static FoliageContact DistanceSampled(in ContactShape a, in ContactShape b, float search, int exactPairBudget)
    {
        if (exactPairBudget <= 0 || a.Lamina == null || b.Lamina == null || a.Feature >= 0 || b.Feature >= 0)
            return DistanceUncached(a, b, search);
        if (BoundsSeparate(a, b, search) || ConvexSeparation(a, b, search))
            return new(a.Pose.Base, b.Pose.Base, Vector3.UnitY, search, -1, -1);
        var best = new FoliageContact(a.Pose.Base, b.Pose.Base, Vector3.UnitY, search, -1, -1);
        // Sampling costs about a tree query per vertex of both blades; the exact query costs a
        // triangle test per candidate pair. Stay exact until the candidates outnumber the
        // vertices several times over, whatever the configured floor.
        var limit = Math.Max(exactPairBudget, 4 * (a.Lamina.Vertices.Length + b.Lamina.Vertices.Length));
        var pairs = TemplatePairs ??= [];
        if (a.Lamina.QueryPairs(a.Pose, b.Lamina, b.Pose, search, pairs, limit))
            return ExactPairs(a, b, pairs, best);
        var hits = SampleHitsB ??= [];
        var bestVertex = -1; var bestOnA = true;
        SampleVertices(a, b, true, hits, ref best, ref bestVertex, ref bestOnA);
        SampleVertices(b, a, false, hits, ref best, ref bestVertex, ref bestOnA);
        if (bestVertex < 0) return best;
        // Refinement: the triangles incident to the best vertex against everything of the other
        // blade within the best distance of them. Edge-edge minima next to the best vertex, and
        // a crossing, are found exactly here.
        ref readonly var from = ref bestOnA ? ref a : ref b;
        ref readonly var to = ref bestOnA ? ref b : ref a;
        var vertex = from.Lamina!.TransformVertex(from.Pose, bestVertex);
        var incident = SampleHitsA ??= [];
        var eps = new Vector3(1e-6f);
        from.Lamina.Query(from.Pose, new(vertex - eps, vertex + eps), incident);
        foreach (var i in incident)
        {
            from.Lamina.Triangle(from.Pose, i, out var x, out var y, out var z);
            to.Lamina!.Query(to.Pose, Pad(Bounds(x, y, z), best.Distance), hits);
            foreach (var j in hits)
            {
                to.Lamina.Triangle(to.Pose, j, out var p, out var q, out var r);
                if (!Bounds(p, q, r).Intersects(Pad(Bounds(x, y, z), best.Distance))) continue;
                var d = TriangleDistance(x, y, z, p, q, r, out var onFrom, out var onTo, best.Distance);
                if (d < best.Distance)
                    best = bestOnA ? Make(onFrom, onTo, p, q, r, d, i, j) : Make(onTo, onFrom, x, y, z, d, j, i);
                if (d <= 1e-9f) return best;
            }
        }
        // And the other way round: the triangle the best vertex landed on, against everything of
        // the first blade within the best distance of it. A crossing that runs between the vertices
        // of a long narrow blade is caught from the other sheet's side.
        var landed = bestOnA ? best.FeatureB : best.FeatureA;
        if (landed >= 0)
        {
            to.Lamina!.Triangle(to.Pose, landed, out var p, out var q, out var r);
            from.Lamina.Query(from.Pose, Pad(Bounds(p, q, r), best.Distance), incident);
            foreach (var i in incident)
            {
                from.Lamina.Triangle(from.Pose, i, out var x, out var y, out var z);
                if (!Bounds(x, y, z).Intersects(Pad(Bounds(p, q, r), best.Distance))) continue;
                var d = TriangleDistance(x, y, z, p, q, r, out var onFrom, out var onTo, best.Distance);
                if (d < best.Distance)
                    best = bestOnA ? Make(onFrom, onTo, p, q, r, d, i, landed) : Make(onTo, onFrom, x, y, z, d, landed, i);
                if (d <= 1e-9f) return best;
            }
        }
        return best;
    }

    /// <summary>Every vertex of <paramref name="from"/> against the triangles of <paramref name="to"/>
    /// within the best distance so far.</summary>
    static void SampleVertices(in ContactShape from, in ContactShape to, bool fromIsA, List<int> hits,
        ref FoliageContact best, ref int bestVertex, ref bool bestOnA)
    {
        var lamina = from.Lamina!; var other = to.Lamina!;
        var toBounds = to.Bounds;
        for (int v = 0; v < lamina.Vertices.Length; ++v)
        {
            var p = lamina.TransformVertex(from.Pose, v);
            var pad = new Vector3(best.Distance);
            var box = new Bvh.BoundingBox(p - pad, p + pad);
            if (!box.Intersects(toBounds)) continue;
            other.Query(to.Pose, box, hits);
            foreach (var j in hits)
            {
                other.Triangle(to.Pose, j, out var x, out var y, out var z);
                var c = StaticCollider.ClosestPointOnTriangle(p, x, y - x, z - x);
                var d = Vector3.Distance(p, c);
                if (d < best.Distance)
                {
                    best = fromIsA ? Make(p, c, x, y, z, d, -1, j) : Make(c, p, x, y, z, d, j, -1);
                    bestVertex = v; bestOnA = fromIsA;
                }
            }
        }
    }
}
