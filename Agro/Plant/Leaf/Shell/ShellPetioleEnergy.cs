namespace Agro.Mechanics;

/// <summary>
/// Experimental coupled BAC shell / three-span petiole objective. Blade vertices
/// stay in the unloaded tip frame; petiole kinematics transports them for loads
/// and plane contacts. No extra leaf hinge spring is added. A caller must fix the
/// appropriate local attachment patch, enforce bend limits, certify motion and
/// solve equilibrium. This kernel is not a production contact solver or CCD.
/// </summary>
public sealed class ShellPetioleEnergy(BacShell shell, PetioleBeam petiole)
{
    public BacShell Shell { get; } = shell;
    public PetioleBeam Petiole { get; } = petiole;
    public int DofCount => Shell.DofCount + PetioleBeam.DofCount;
    const int PointDofs = 3 + PetioleBeam.DofCount;

    /// <summary>Bounded active-worker scratch; never retain one on each organ.</summary>
    public sealed class Workspace
    {
        internal readonly ShellPetioleEnergy Owner;
        internal readonly BacShell.Workspace Element = new();
        internal readonly PetioleBeam.Workspace Rod = new();
        public PetioleBeam.AttachmentWorkspace Attachment { get; } = new();
        internal readonly double[] ElementBlocks, PointBlocks, RodBlock = new double[36];
        internal bool HessianReady;
        public double MinimumPlaneGap { get; internal set; }
        public double MaximumPlaneReactionNewtons { get; internal set; }
        public int ActivePlaneVertexContacts { get; internal set; }
        public Workspace(ShellPetioleEnergy owner)
        {
            Owner = owner;
            ElementBlocks = new double[owner.Shell.FaceCount * BacShell.ElementDofs * BacShell.ElementDofs];
            PointBlocks = new double[owner.Shell.VertexCount * PointDofs * PointDofs];
        }
    }

    /// <summary>
    /// q contains local shell coordinates/directors followed by six rod angles.
    /// Vertex forces are in N in the rod base frame; the rod's distributed force
    /// is in N/m. Plane offsets include collision clearance, not tissue thickness.
    /// Pass intrinsic-area-weighted gravity forces once per leaf vertex, never
    /// separate biological loads for mechanical sections. Empty forces mean zero.
    /// </summary>
    public double Evaluate(ReadOnlySpan<double> q, ReadOnlySpan<ShellVector> vertexForces,
        ShellVector rodDistributedForce, ReadOnlySpan<ShellPlaneContact> planes,
        Span<double> gradient, bool buildHessian, Workspace w)
    {
        if (q.Length != DofCount || (!gradient.IsEmpty && gradient.Length != DofCount)
            || (!vertexForces.IsEmpty && vertexForces.Length != Shell.VertexCount)
            || (buildHessian && gradient.IsEmpty) || !ReferenceEquals(w.Owner, this))
            throw new ArgumentException("Incorrect coupled state, derivative or workspace size.");
        w.HessianReady = false; gradient.Clear();
        w.MinimumPlaneGap = double.PositiveInfinity;
        w.MaximumPlaneReactionNewtons = 0; w.ActivePlaneVertexContacts = 0;
        var local = q[..Shell.DofCount]; var angles = q[Shell.DofCount..];
        var energy = 0.0;
        for (var f = 0; f < Shell.FaceCount; ++f)
        {
            var value = Shell.EvaluateElement(f, local, gradient.IsEmpty ? [] : w.Element.Gradient,
                buildHessian ? w.ElementBlocks.AsSpan(f * BacShell.ElementDofs * BacShell.ElementDofs,
                    BacShell.ElementDofs * BacShell.ElementDofs) : [], w.Element);
            if (!double.IsFinite(value)) return value;
            energy += value;
            if (gradient.IsEmpty) continue;
            var map = Shell.ElementMap(f);
            for (var i = 0; i < map.Length; ++i) gradient[map[i]] += w.Element.Gradient[i];
        }
        energy += Petiole.Evaluate(angles, new(), rodDistributedForce,
            gradient.IsEmpty ? [] : gradient[Shell.DofCount..], buildHessian ? w.RodBlock : [], w.Rod);
        if (!double.IsFinite(energy)) return energy;
        Petiole.PrepareAttachment(angles, w.Attachment, w.Rod, buildHessian ? 2 : gradient.IsEmpty ? 0 : 1);
        Span<double> pointHessian = stackalloc double[9];
        Span<double> pointGradient = stackalloc double[PointDofs];
        for (var vertex = 0; vertex < Shell.VertexCount; ++vertex)
        {
            var point = new ShellVector(local[3 * vertex], local[3 * vertex + 1], local[3 * vertex + 2]);
            var transported = w.Attachment.Point(point);
            var force = vertexForces.IsEmpty ? new ShellVector() : vertexForces[vertex];
            energy -= force.Dot(transported);
            var g = force * -1; pointHessian.Clear();
            foreach (var plane in planes)
            {
                var weight = plane.UsesRestArea ? Shell.RestVertexAreas[vertex] : 1.0;
                if (weight == 0) continue;
                var gap = plane.Normal.Dot(transported) - plane.Offset;
                w.MinimumPlaneGap = Math.Min(w.MinimumPlaneGap, gap);
                var value = plane.Evaluate(gap, out var first, out var second);
                if (!double.IsFinite(value)) return value;
                energy += weight * value;
                first *= weight; second *= weight;
                w.MaximumPlaneReactionNewtons = Math.Max(w.MaximumPlaneReactionNewtons, -first);
                if (first == 0) continue;
                ++w.ActivePlaneVertexContacts; g += plane.Normal * first;
                if (!buildHessian) continue;
                for (var i = 0; i < 3; ++i) for (var j = 0; j < 3; ++j)
                    pointHessian[3 * i + j] += second * Component(plane.Normal, i) * Component(plane.Normal, j);
            }
            if (gradient.IsEmpty) continue;
            w.Attachment.PullBack(point, g, pointHessian, pointGradient,
                buildHessian ? w.PointBlocks.AsSpan(vertex * PointDofs * PointDofs, PointDofs * PointDofs) : []);
            for (var i = 0; i < PointDofs; ++i) gradient[PointIndex(vertex, i)] += pointGradient[i];
        }
        w.HessianReady = buildHessian && double.IsFinite(energy);
        return energy;
    }

    /// <summary>
    /// Exact matrix-free Hessian product at the last successful build in this
    /// workspace. Fixed coordinates and positive-definite search-direction
    /// projection belong to the equilibrium solver, not to the physical energy.
    /// Deterministic serial scatter; does not allocate per contact or multiply.
    /// </summary>
    public void MultiplyHessian(ReadOnlySpan<double> direction, Span<double> result, Workspace w)
    {
        if (direction.Length != DofCount || result.Length != DofCount || direction.Overlaps(result)
            || !ReferenceEquals(w.Owner, this)) throw new ArgumentException("Incorrect Hessian product buffers.");
        if (!w.HessianReady) throw new InvalidOperationException("Build coupled Hessian blocks for the current state first.");
        result.Clear(); const int m = BacShell.ElementDofs;
        for (var f = 0; f < Shell.FaceCount; ++f)
        {
            var map = Shell.ElementMap(f); var block = w.ElementBlocks.AsSpan(f * m * m, m * m);
            for (var i = 0; i < m; ++i)
            {
                double value = 0;
                for (var j = 0; j < m; ++j) value += block[i * m + j] * direction[map[j]];
                result[map[i]] += value;
            }
        }
        for (var v = 0; v < Shell.VertexCount; ++v)
        {
            var block = w.PointBlocks.AsSpan(v * PointDofs * PointDofs, PointDofs * PointDofs);
            for (var i = 0; i < PointDofs; ++i)
            {
                double value = 0;
                for (var j = 0; j < PointDofs; ++j) value += block[i * PointDofs + j] * direction[PointIndex(v, j)];
                result[PointIndex(v, i)] += value;
            }
        }
        for (var i = 0; i < PetioleBeam.DofCount; ++i)
            for (var j = 0; j < PetioleBeam.DofCount; ++j)
                result[Shell.DofCount + i] += w.RodBlock[i * PetioleBeam.DofCount + j] * direction[Shell.DofCount + j];
    }

    int PointIndex(int vertex, int dof) => dof < 3 ? 3 * vertex + dof : Shell.DofCount + dof - 3;
    static double Component(ShellVector p, int axis) => axis == 0 ? p.X : axis == 1 ? p.Y : p.Z;
}
