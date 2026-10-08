using I = Agro.Mechanics.ShellContinuousCollision.Interval;
using V = Agro.Mechanics.ShellContinuousCollision.VectorInterval;

namespace Agro.Mechanics;

public readonly record struct ShellPlaneCcdResult(ShellCcdResult Motion, int Vertex, int Plane, long GapTests);

/// <summary>
/// Full-path half-space check for the standalone coupled shell/petiole objective.
/// A triangle stays in a half-space when all its vertices do. This checks the
/// composed support path, not just endpoint plane energy. It does not solve forces,
/// self-contact, surface strain, rod-radius contact or arbitrary support chains.
/// </summary>
public static class ShellSupportPlaneCollision
{
    public static ShellPlaneCcdResult Query(ReadOnlySpan<ShellVertexMotion> vertices, in ShellSupportMotion support,
        ReadOnlySpan<ShellPlaneContact> planes, ShellCurvedCollision.Workspace w, ShellCcdSettings settings)
    {
        settings.Validate();
        if (settings.MinimumSeparation != 0) throw new ArgumentException("Plane clearance belongs in ShellPlaneContact.Offset.");
        w.Count = w.PeakBoxes = w.TransformEvaluations = w.TransformCacheHits = 0; w.Sequence = 0;
        if (!support.Valid) return Stop(0, 1, 0, ShellCcdLimit.NumericalResolution, -1, -1, 0);
        foreach (var plane in planes)
            if (!(plane.Normal.Norm > 0) || !double.IsFinite(plane.Normal.Norm + plane.Offset))
                return Stop(0, 1, 0, ShellCcdLimit.NumericalResolution, -1, -1, 0);
        w.Push(new() { T1 = 1 }); var visited = 0; long tests = 0;
        while (w.Count > 0)
        {
            if (visited == settings.MaximumBoxes)
                return Stop(w.Heap[0].T0, w.Heap[0].T1, visited, ShellCcdLimit.WorkBudget, -1, -1, tests);
            var box = w.Pop(); ++visited; var time = new I(box.T0, box.T1);
            var transformed = w.Transform(support, default, box.T0, box.T1, out var pose, out _);
            var foundVertex = -1; var foundPlane = -1; var gapWidth = 0.0; var definiteViolation = false;
            if (transformed)
            {
                for (var i = 0; i < vertices.Length; ++i)
                {
                    var point = pose.Point(V.Lerp(vertices[i], time));
                    if (!point.Finite) return Stop(box.T0, box.T1, visited, ShellCcdLimit.NumericalResolution, i, -1, tests);
                    for (var j = 0; j < planes.Length; ++j)
                    {
                        ++tests; var plane = planes[j];
                        var gap = point.X * I.Point(plane.Normal.X) + point.Y * I.Point(plane.Normal.Y)
                            + point.Z * I.Point(plane.Normal.Z) - I.Point(plane.Offset);
                        if (!gap.Finite) return Stop(box.T0, box.T1, visited, ShellCcdLimit.NumericalResolution, i, j, tests);
                        if (gap.Low > 0) continue;
                        if (foundVertex < 0) { foundVertex = i; foundPlane = j; }
                        gapWidth = Math.Max(gapWidth, gap.Width); definiteViolation |= gap.High <= 0;
                    }
                }
                if (foundVertex < 0) continue;
                if (box.T1 - box.T0 <= settings.TimeTolerance && (definiteViolation || gapWidth <= settings.SpatialTolerance))
                    return Stop(box.T0, box.T1, visited, ShellCcdLimit.None, foundVertex, foundPlane, tests);
            }
            else if (box.T1 - box.T0 <= settings.TimeTolerance)
                return Stop(box.T0, box.T1, visited, ShellCcdLimit.NumericalResolution, -1, -1, tests);
            var middle = (box.T0 + box.T1) * .5;
            if (!(middle > box.T0 && middle < box.T1)) return Stop(box.T0, box.T1, visited, ShellCcdLimit.NumericalResolution, foundVertex, foundPlane, tests);
            if (w.Count + 2 > w.Capacity) return Stop(box.T0, box.T1, visited, ShellCcdLimit.WorkspaceCapacity, foundVertex, foundPlane, tests);
            w.Push(new() { T0 = box.T0, T1 = middle }); w.Push(new() { T0 = middle, T1 = box.T1 });
        }
        return new(new(ShellCcdStatus.Clear, 1, 1, visited, ShellCcdLimit.None), -1, -1, tests);
    }

    static ShellPlaneCcdResult Stop(double from, double to, int visits, ShellCcdLimit limit, int vertex, int plane, long tests)
        => new(new(limit == ShellCcdLimit.None ? ShellCcdStatus.PotentialContact : ShellCcdStatus.Uncertified,
            from <= 0 ? 0 : Math.BitDecrement(from), to, visits, limit), vertex, plane, tests);
}
