using System.Numerics;

namespace Agro.Mechanics;

public readonly record struct ShellMeshEdge(int A, int B);

public sealed partial class ShellMesh
{
    /// <summary>
    /// Offline conforming refinement of selected existing edges. Both incident
    /// faces share one midpoint, including attachment boundaries. Existing
    /// vertices and projected boundary segments remain exact; new heights sample
    /// the same original rest field as uniform refinement. No per-organ state is
    /// created and no load-dependent adaptation occurs in the live simulation.
    /// Input order and duplicate/reversed edges do not affect the profile.
    /// The explicit vertex ceiling is a preprocessing budget, not the state budget.
    /// </summary>
    public ShellMesh RefineEdges(ReadOnlySpan<ShellMeshEdge> selected, int maximumVertices)
    {
        if (maximumVertices < vertices.Length) throw new ArgumentOutOfRangeException(nameof(maximumVertices));
        if (selected.IsEmpty) return this;
        var requested = new SortedSet<(int A, int B)>();
        foreach (var edge in selected)
        {
            if ((uint)edge.A >= vertices.Length || (uint)edge.B >= vertices.Length || edge.A == edge.B)
                throw new ArgumentException("Invalid refinement edge.", nameof(selected));
            requested.Add((Math.Min(edge.A, edge.B), Math.Max(edge.A, edge.B)));
        }
        if (requested.Count > maximumVertices - vertices.Length)
            throw new ArgumentException("Refinement exceeds the explicit vertex budget.", nameof(maximumVertices));
        var existing = new HashSet<(int, int)>();
        for (var f = 0; f < triangles.Length; f += 3)
            for (var j = 0; j < 3; ++j)
            {
                var a = triangles[f + j]; var b = triangles[f + (j + 1) % 3];
                existing.Add((Math.Min(a, b), Math.Max(a, b)));
            }
        foreach (var edge in requested)
            if (!existing.Contains(edge)) throw new ArgumentException("Refinement edge is absent from the mesh.", nameof(selected));
        return new(this, requested);
    }

    ShellMesh(ShellMesh source, SortedSet<(int A, int B)> selected)
    {
        OriginalVertexCount = source.OriginalVertexCount;
        RefinementLevel = source.RefinementLevel;
        AdaptiveRefinementSteps = checked(source.AdaptiveRefinementSteps + 1);
        Interpolation = source.Interpolation; restSurface = source.restSurface;
        var points = new List<Vector3>(source.vertices);
        var lineage = new List<int>(source.vertexParents);
        var midpoints = new Dictionary<(int, int), int>(selected.Count);
        foreach (var edge in selected)
        {
            var point = (points[edge.A] + points[edge.B]) * .5f;
            if (restSurface != null) point.Y = (float)restSurface.Height(point.X, point.Z);
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
                throw new ArgumentException("Nonfinite refinement point.", nameof(selected));
            midpoints.Add(edge, points.Count); points.Add(point);
            lineage.Add(edge.A); lineage.Add(edge.B);
        }
        var faces = new List<int>(); var ancestry = new List<int>();
        void Face(int a, int b, int c, int parent)
        { faces.Add(a); faces.Add(b); faces.Add(c); ancestry.Add(parent); }
        int Mid(int a, int b) => midpoints.GetValueOrDefault((Math.Min(a, b), Math.Max(a, b)), -1);
        for (var f = 0; f < source.triangles.Length; f += 3)
        {
            var a = source.triangles[f]; var b = source.triangles[f + 1]; var c = source.triangles[f + 2];
            var ab = Mid(a, b); var bc = Mid(b, c); var ca = Mid(c, a);
            var count = (ab >= 0 ? 1 : 0) + (bc >= 0 ? 1 : 0) + (ca >= 0 ? 1 : 0);
            var parent = source.parents[f / 3];
            if (count == 0) { Face(a, b, c, parent); continue; }
            if (count == 3)
            {
                Face(a, ab, ca, parent); Face(ab, b, bc, parent);
                Face(ca, bc, c, parent); Face(ab, bc, ca, parent); continue;
            }
            // Rotate the face, preserving winding, into the one-edge or
            // two-consecutive-edge pattern. Red/green splits create no T-junctions.
            while (count == 1 ? ab < 0 : ca >= 0)
            { (a, b, c) = (b, c, a); (ab, bc, ca) = (bc, ca, ab); }
            if (count == 1)
            { Face(a, ab, c, parent); Face(ab, b, c, parent); continue; }
            Face(b, bc, ab, parent);
            // Both choices cover the remaining convex projected quadrilateral.
            // Prefer its shorter diagonal with stable tie-breaking.
            if (Vector3.DistanceSquared(points[a], points[bc]) < Vector3.DistanceSquared(points[ab], points[c]))
            { Face(a, ab, bc, parent); Face(a, bc, c, parent); }
            else
            { Face(a, ab, c, parent); Face(ab, bc, c, parent); }
        }
        vertices = points.ToArray(); triangles = faces.ToArray(); parents = ancestry.ToArray(); vertexParents = lineage.ToArray();
        EdgeCount = CountEdges(triangles, vertices);
        // Reconstruct the original faceted reference from immutable lineage;
        // scratch is discarded, rather than retained for each profile or organ.
        var faceted = new Vector3[vertices.Length];
        for (var i = 0; i < vertices.Length; ++i)
        {
            var a = vertexParents[2 * i]; var b = vertexParents[2 * i + 1];
            faceted[i] = a < 0 ? vertices[i] : (faceted[a] + faceted[b]) * .5f;
            MaximumRestOffset = Math.Max(MaximumRestOffset, Vector3.Distance(vertices[i], faceted[i]));
        }
        Identity = ComputeIdentity();
    }
}
