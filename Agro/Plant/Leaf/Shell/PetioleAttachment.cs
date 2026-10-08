namespace Agro.Mechanics;

public sealed partial class PetioleBeam
{
    /// <summary>
    /// Reusable active-worker kinematics, not per-organ persistent state. Prepare
    /// once per rod configuration and reuse for all blade vertices/contacts. The
    /// blade is expressed in the unloaded tip frame; its local deformation and
    /// intrinsic shell energy are independent of this rigid transport.
    /// </summary>
    public sealed class AttachmentWorkspace
    {
        // Tip XYZ, then frame columns X/Y/Z. Each output has six angular partials.
        internal readonly double[] Values = new double[12], Jacobians = new double[12 * DofCount],
            Hessians = new double[12 * DofCount * DofCount];
        internal int DerivativeOrder = -1;
        public ShellVector Tip { get { Require(0); return VectorAt(0); } }
        public PetioleTipFrame Frame { get { Require(0); return new(VectorAt(3), VectorAt(6), VectorAt(9)); } }

        ShellVector VectorAt(int i) => new(Values[i], Values[i + 1], Values[i + 2]);
        void Require(int order)
        {
            if (DerivativeOrder < order) throw new InvalidOperationException("Prepare attachment kinematics with the requested derivative order first.");
        }

        /// <summary>
        /// Transport one point to the rod's base frame. The optional 3-by-6
        /// Jacobian and three 6-by-6 Hessians differentiate rod angles while
        /// holding the local blade point fixed. Basal lobes need no special case.
        /// </summary>
        public ShellVector Point(ShellVector local, Span<double> jacobian = default, Span<double> hessians = default)
        {
            if ((!jacobian.IsEmpty && jacobian.Length != 3 * DofCount)
                || (!hessians.IsEmpty && hessians.Length != 3 * DofCount * DofCount))
                throw new ArgumentException("Incorrect attachment derivative size.");
            Require(!hessians.IsEmpty ? 2 : !jacobian.IsEmpty ? 1 : 0);
            for (var i = 0; i < jacobian.Length; ++i)
                jacobian[i] = Jacobians[i] + local.X * Jacobians[3 * DofCount + i]
                    + local.Y * Jacobians[6 * DofCount + i] + local.Z * Jacobians[9 * DofCount + i];
            for (var i = 0; i < hessians.Length; ++i)
                hessians[i] = Hessians[i] + local.X * Hessians[3 * DofCount * DofCount + i]
                    + local.Y * Hessians[6 * DofCount * DofCount + i] + local.Z * Hessians[9 * DofCount * DofCount + i];
            return Tip + Frame.Transform(local);
        }

        /// <summary>
        /// Exact chain rule for a point energy in the rod base frame (e.g. gravity
        /// or a contact barrier). Outputs are ordered [local blade XYZ, six rod
        /// angles]. Includes mixed blade/rod and geometric-stiffness terms; using
        /// only J-transpose H J omits these terms under off-centre contact. These
        /// are energy derivatives: the negative position gradient is force in N
        /// and the negative angular gradient is generalized torque in Nm.
        /// </summary>
        public void PullBack(ShellVector local, ShellVector pointGradient, ReadOnlySpan<double> pointHessian,
            Span<double> gradient, Span<double> hessian = default)
        {
            const int n = 3 + DofCount;
            if (gradient.Length != n || (!hessian.IsEmpty && hessian.Length != n * n)
                || (!hessian.IsEmpty && pointHessian.Length != 9))
                throw new ArgumentException("Incorrect coupled derivative size.");
            Require(hessian.IsEmpty ? 1 : 2);
            Span<double> angular = stackalloc double[3 * DofCount];
            Span<double> angularHessians = stackalloc double[3 * DofCount * DofCount];
            Point(local, angular, hessian.IsEmpty ? [] : angularHessians);
            Span<double> jac = stackalloc double[3 * n];
            Span<double> g = stackalloc double[3] { pointGradient.X, pointGradient.Y, pointGradient.Z };
            for (var axis = 0; axis < 3; ++axis)
            {
                for (var a = 0; a < 3; ++a) jac[axis * n + a] = Values[3 + 3 * a + axis];
                angular.Slice(axis * DofCount, DofCount).CopyTo(jac.Slice(axis * n + 3, DofCount));
            }
            gradient.Clear();
            for (var i = 0; i < n; ++i)
                for (var axis = 0; axis < 3; ++axis) gradient[i] += jac[axis * n + i] * g[axis];
            if (hessian.IsEmpty) return;
            for (var i = 0; i < n; ++i)
                for (var j = 0; j <= i; ++j)
                {
                    double value = 0;
                    for (var a = 0; a < 3; ++a)
                    {
                        for (var b = 0; b < 3; ++b)
                            value += jac[a * n + i] * pointHessian[3 * a + b] * jac[b * n + j];
                        if (i >= 3 && j >= 3)
                            value += g[a] * angularHessians[a * DofCount * DofCount + (i - 3) * DofCount + j - 3];
                        else if (i >= 3)
                            value += g[a] * Jacobians[(3 + 3 * j + a) * DofCount + i - 3];
                    }
                    hessian[i * n + j] = hessian[j * n + i] = value;
                }
        }
    }

    /// <summary>
    /// Evaluate tip position/frame and their angular derivatives once. All graphs
    /// are immutable and shared; both workspaces must be exclusive to this worker.
    /// Call again whenever rod configuration or intrinsic length changes.
    /// </summary>
    public void PrepareAttachment(ReadOnlySpan<double> q, AttachmentWorkspace attachment, Workspace w, int derivativeOrder = 2)
    {
        ValidateState(q);
        if ((uint)derivativeOrder > 2) throw new ArgumentOutOfRangeException(nameof(derivativeOrder));
        attachment.DerivativeOrder = -1;
        q.CopyTo(w.Inputs); w.Inputs[6] = Length; w.Inputs[7] = 1;
        for (var i = 0; i < 12; ++i)
        {
            var graph = i < 3 ? PointGraphs[6 + i] : FrameGraphs[i - 3];
            attachment.Values[i] = graph.Evaluate(w.Inputs, DofCount,
                derivativeOrder >= 1 ? attachment.Jacobians.AsSpan(i * DofCount, DofCount) : [],
                derivativeOrder >= 2 ? attachment.Hessians.AsSpan(i * DofCount * DofCount, DofCount * DofCount) : [], w.Tape);
        }
        attachment.DerivativeOrder = derivativeOrder;
    }
}
