namespace Agro.Mechanics;

public enum ShellCondensationStatus : byte
{
    Prepared,
    InadmissibleState,
    InteriorNotPositiveDefinite,
    InaccurateInteriorSolve
}

/// <summary>
/// Exact, state-dependent Newton condensation of a BAC shell. Retained coordinates
/// are selected fine DOFs, not a fixed deformation subspace. Interior coordinates
/// and directors remain free to relax. Re-prepare after EVERY state/load change.
/// This is an experimental reference kernel, not a compressed-state codec,
/// contact solver or equilibrium certificate. No timestep enters its equations.
/// </summary>
public sealed class ShellStaticCondensation
{
    readonly BacShell shell;
    readonly int[] retained;
    readonly bool[] fixedDofs, excluded;
    readonly double[] rest;
    public int DofCount => retained.Length;
    public int FineDofCount => shell.DofCount;
    public int CoordinatePayloadBytes => checked(DofCount * sizeof(double));

    /// <param name="shell">Immutable mechanical surface and constitutive data.</param>
    /// <param name="retainedDofs">Unique nonfixed indices, in caller-defined order.</param>
    /// <param name="fixedDofs">Prescribed boundary conditions; empty means none.</param>
    public ShellStaticCondensation(BacShell shell, ReadOnlySpan<int> retainedDofs, ReadOnlySpan<bool> fixedDofs)
    {
        ArgumentNullException.ThrowIfNull(shell);
        if (retainedDofs.IsEmpty || (!fixedDofs.IsEmpty && fixedDofs.Length != shell.DofCount))
            throw new ArgumentException("Incorrect condensation partition.");
        this.shell = shell;
        this.fixedDofs = fixedDofs.IsEmpty ? new bool[shell.DofCount] : fixedDofs.ToArray();
        excluded = this.fixedDofs.ToArray(); retained = retainedDofs.ToArray();
        foreach (var i in retained)
        {
            if ((uint)i >= (uint)shell.DofCount || excluded[i])
                throw new ArgumentException("Retained coordinates must be unique, in range and nonfixed.");
            excluded[i] = true;
        }
        rest = shell.CreateRestState();
    }

    /// <summary>
    /// Full arrays and numeric factors belong to a bounded solver worker, never an
    /// organ. Budget covers array payloads, including derivative scratch. CLR
    /// overhead and shared immutable patterns are separate. Pattern discovery may
    /// run before budget rejection. Calls on one workspace must be sequential.
    /// </summary>
    public sealed class Workspace
    {
        internal readonly ShellStaticCondensation Owner;
        internal readonly BacShell.Workspace Element;
        internal readonly ShellSparseFactor Factor;
        internal readonly double[] State, Gradient, Scale, Offset, Direction, Product, Right, Solution, Residual;
        internal readonly double[] LocalGradient, LocalHessian, LocalDirection, LocalProduct;
        internal bool Prepared;
        public double Energy { get; internal set; } = double.PositiveInfinity;
        public double LastLinearRelativeResidual { get; internal set; } = double.PositiveInfinity;
        public long ArrayPayloadBytes { get; }
        public long SharedFactorPatternPayloadBytes => Factor.SymbolicPayloadBytes;

        public Workspace(ShellStaticCondensation owner, long maximumArrayPayloadBytes)
        {
            ArgumentNullException.ThrowIfNull(owner); Owner = owner;
            var n = owner.FineDofCount;
            var arrays = (9L * n + 3 * BacShell.ElementDofs + BacShell.ElementDofs * BacShell.ElementDofs) * sizeof(double)
                + BacShell.WorkspacePayloadBytes;
            if (maximumArrayPayloadBytes < arrays)
                throw new ArgumentException("Condensation exceeds the explicit worker payload budget.", nameof(maximumArrayPayloadBytes));
            Factor = new(owner.shell, maximumArrayPayloadBytes - arrays);
            State = new double[n]; Gradient = new double[n]; Scale = new double[n]; Offset = new double[n];
            Direction = new double[n]; Product = new double[n]; Right = new double[n]; Solution = new double[n]; Residual = new double[n];
            Element = new(); LocalGradient = new double[BacShell.ElementDofs];
            LocalHessian = new double[BacShell.ElementDofs * BacShell.ElementDofs];
            LocalDirection = new double[BacShell.ElementDofs]; LocalProduct = new double[BacShell.ElementDofs];
            ArrayPayloadBytes = arrays + Factor.NumericPayloadBytes;
        }
    }

    /// <summary>
    /// Snapshot the state/load and factor the exact interior Hessian. Negative or
    /// singular interior tangents are rejected; no stiffness shift is accepted as
    /// physical equilibrium. External loads are conservative fine-coordinate forces
    /// (N on positions, N m on directors). Energy is relative to rest load potential.
    /// </summary>
    public ShellCondensationStatus Prepare(ReadOnlySpan<double> state, ReadOnlySpan<double> forces, Workspace w)
    {
        if (w.Owner == this) w.Prepared = false;
        if (w.Owner != this || state.Length != FineDofCount || (!forces.IsEmpty && forces.Length != FineDofCount))
            throw new ArgumentException("Incorrect condensation state, load or workspace.");
        w.Energy = double.PositiveInfinity; w.LastLinearRelativeResidual = double.PositiveInfinity;
        if (!Finite(state) || !Finite(forces)) return ShellCondensationStatus.InadmissibleState;
        state.CopyTo(w.State); Array.Clear(w.Gradient); w.Factor.Clear();
        var energy = 0.0; const int m = BacShell.ElementDofs;
        for (var face = 0; face < shell.FaceCount; ++face)
        {
            energy += shell.EvaluateElement(face, w.State, w.LocalGradient, w.LocalHessian, w.Element);
            if (!double.IsFinite(energy)) return ShellCondensationStatus.InadmissibleState;
            var map = shell.ElementMap(face);
            for (var i = 0; i < m; ++i)
            {
                w.Gradient[map[i]] += w.LocalGradient[i];
                for (var j = 0; j < i; ++j)
                {
                    var symmetric = .5 * w.LocalHessian[i * m + j] + .5 * w.LocalHessian[j * m + i];
                    w.LocalHessian[i * m + j] = w.LocalHessian[j * m + i] = symmetric;
                }
            }
            w.Factor.AddElement(face, w.LocalHessian, excluded);
        }
        for (var i = 0; i < forces.Length; ++i)
        { energy -= forces[i] * (w.State[i] - rest[i]); w.Gradient[i] -= forces[i]; }
        if (!double.IsFinite(energy) || !Finite(w.Gradient)) return ShellCondensationStatus.InadmissibleState;
        // Unit rows eliminate fixed/retained coordinates only. They are never
        // added to the physical tangent or energy.
        for (var i = 0; i < FineDofCount; ++i) if (excluded[i]) w.Factor.Add(i, i, 1);
        if (!w.Factor.TryEquilibrate(w.Scale) || !w.Factor.Factor())
            return ShellCondensationStatus.InteriorNotPositiveDefinite;
        for (var i = 0; i < FineDofCount; ++i) w.Direction[i] = -w.Gradient[i];
        if (!SolveInterior(w.Direction, w)) return ShellCondensationStatus.InaccurateInteriorSolve;
        w.Solution.CopyTo(w.Offset, 0); w.Energy = energy; w.Prepared = true;
        return ShellCondensationStatus.Prepared;
    }

    /// <summary>
    /// gR - HRI HII^-1 gI. The interior residual MUST be included before it reaches
    /// equilibrium; omitting it creates spurious forces at retained coordinates.
    /// </summary>
    public bool TryCopyReducedResidual(Span<double> residual, Workspace w)
    {
        CheckOutput(residual, DofCount, w); residual.Clear();
        if (!w.Prepared || !Multiply(w.Offset, w.Product, w)) return false;
        for (var j = 0; j < retained.Length; ++j) residual[j] = w.Gradient[retained[j]] + w.Product[retained[j]];
        if (Finite(residual)) return true;
        residual.Clear(); return false;
    }

    /// <summary>Exact Schur product (HRR - HRI HII^-1 HIR) d at the snapshot.</summary>
    public bool TryMultiply(ReadOnlySpan<double> direction, Span<double> product, Workspace w)
        => Lift(direction, product, w, false);

    /// <summary>
    /// Full Newton correction: dI = -HII^-1 (gI + HIR dR). Fixed coordinates are
    /// zero. A zero retained correction still relaxes the interior. Caller must
    /// certify the ENTIRE strain/contact path and select an admissible step size.
    /// </summary>
    public bool TryLiftCorrection(ReadOnlySpan<double> retainedCorrection, Span<double> fineCorrection, Workspace w)
        => Lift(retainedCorrection, fineCorrection, w, true);

    bool Lift(ReadOnlySpan<double> direction, Span<double> output, Workspace w, bool correction)
    {
        CheckOutput(output, correction ? FineDofCount : DofCount, w);
        if (direction.Length != DofCount || direction.Overlaps(output))
            throw new ArgumentException("Incorrect or aliased condensation direction.");
        output.Clear();
        if (!w.Prepared || !Finite(direction)) return false;
        var zero = true;
        foreach (var value in direction) zero &= value == 0;
        if (zero)
        {
            // Prepare already computed the interior-only Newton correction.
            // Reconstruction with prescribed controls need not solve it twice.
            if (correction) w.Offset.CopyTo(output);
            return true;
        }
        Array.Clear(w.Direction);
        for (var j = 0; j < retained.Length; ++j) w.Direction[retained[j]] = direction[j];
        if (!Multiply(w.Direction, w.Product, w)) return false;
        for (var i = 0; i < FineDofCount; ++i)
            w.Direction[i] = -w.Product[i] - (correction ? w.Gradient[i] : 0);
        if (!SolveInterior(w.Direction, w)) return false;
        for (var j = 0; j < retained.Length; ++j) w.Solution[retained[j]] = direction[j];
        if (correction) w.Solution.CopyTo(output);
        else
        {
            if (!Multiply(w.Solution, w.Product, w)) return false;
            for (var j = 0; j < retained.Length; ++j) output[j] = w.Product[retained[j]];
        }
        if (Finite(output)) return true;
        output.Clear(); return false;
    }

    /// <summary>Physical fine residual including clamp reactions, not a reduced norm.</summary>
    public bool TryCopyFineResidual(Span<double> residual, Workspace w)
    {
        CheckOutput(residual, FineDofCount, w); residual.Clear();
        if (!w.Prepared) return false;
        w.Gradient.CopyTo(residual); return true;
    }

    /// <summary>Maximum component norms over ALL nonfixed DOFs, including retained ones.</summary>
    public bool TryMeasureResidual(out double forceNewtons, out double torqueNewtonMetres, Workspace w)
    {
        if (w.Owner != this) throw new ArgumentException("Incorrect condensation workspace.");
        forceNewtons = torqueNewtonMetres = double.PositiveInfinity;
        if (!w.Prepared) return false;
        forceNewtons = torqueNewtonMetres = 0;
        for (var i = 0; i < FineDofCount; ++i) if (!fixedDofs[i])
        {
            if (i < 3 * shell.VertexCount) forceNewtons = Math.Max(forceNewtons, Math.Abs(w.Gradient[i]));
            else torqueNewtonMetres = Math.Max(torqueNewtonMetres, Math.Abs(w.Gradient[i]));
        }
        return true;
    }

    // Up to three iterative refinements. Check the original analytic tangent,
    // not its factorized copy. Exhaustion is numerical failure, not confinement.
    bool SolveInterior(ReadOnlySpan<double> rhs, Workspace w)
    {
        var rhsNorm = 0.0;
        for (var i = 0; i < FineDofCount; ++i)
        { w.Right[i] = excluded[i] ? 0 : rhs[i] * w.Scale[i]; rhsNorm = Math.Max(rhsNorm, Math.Abs(w.Right[i])); }
        Array.Clear(w.Solution);
        if (rhsNorm == 0) { w.LastLinearRelativeResidual = 0; return true; }
        w.Right.CopyTo(w.Residual, 0);
        for (var refine = 0; refine <= 3; ++refine)
        {
            w.Factor.Solve(w.Residual, w.Direction);
            for (var i = 0; i < FineDofCount; ++i) if (!excluded[i]) w.Solution[i] += w.Scale[i] * w.Direction[i];
            if (!Finite(w.Solution) || !Multiply(w.Solution, w.Product, w)) return false;
            var norm = 0.0;
            for (var i = 0; i < FineDofCount; ++i)
            { w.Residual[i] = excluded[i] ? 0 : w.Right[i] - w.Scale[i] * w.Product[i]; norm = Math.Max(norm, Math.Abs(w.Residual[i])); }
            w.LastLinearRelativeResidual = norm / rhsNorm;
            if (w.LastLinearRelativeResidual <= 1e-8) return true;
        }
        return false;
    }

    bool Multiply(ReadOnlySpan<double> direction, Span<double> product, Workspace w)
    {
        product.Clear();
        for (var face = 0; face < shell.FaceCount; ++face)
        {
            var map = shell.ElementMap(face);
            for (var j = 0; j < map.Length; ++j) w.LocalDirection[j] = direction[map[j]];
            if (!double.IsFinite(shell.EvaluateElementProduct(face, w.State, w.LocalDirection, [], w.LocalProduct, w.Element)))
                return false;
            for (var j = 0; j < map.Length; ++j) product[map[j]] += w.LocalProduct[j];
        }
        return Finite(product);
    }

    void CheckOutput(Span<double> output, int count, Workspace w)
    {
        if (w.Owner != this || output.Length != count) throw new ArgumentException("Incorrect condensation workspace or output.");
    }

    static bool Finite(ReadOnlySpan<double> values)
    {
        foreach (var value in values) if (!double.IsFinite(value)) return false;
        return true;
    }
}
