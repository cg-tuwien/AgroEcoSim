namespace Agro.Mechanics;

/// <summary>Separate stationarity diagnostics; director residuals are torques, not forces or pressure.</summary>
public readonly record struct ShellProlongedResiduals(double CoarseForceNewtons,
    double FineForceNewtons, double DirectorTorqueNewtonMetres);

/// <summary>
/// Reference objective E(P(c),d)-f dot (P(c),d): coarse positions c, independently
/// variable fine BAC directors d, and the unchanged fine shell constitutive law.
/// Exact chain rules include director/position coupling and geometric stiffness.
/// This kernel deliberately exposes every fine director. It is not an accepted
/// compact state format or a claim that director state can be discarded. Geometry,
/// equilibrium, storage and contact gates must pass before production use.
/// </summary>
public sealed class ShellProlongedEnergy
{
    readonly BacShell shell;
    readonly ShellSurfaceProlongation transfer;
    readonly double[] coarseRest;
    public int CoarsePositionDofs => 3 * transfer.SourceVertexCount;
    public int DirectorDofs => shell.EdgeCount;
    public int DofCount => CoarsePositionDofs + DirectorDofs;
    public int FineDofCount => shell.DofCount;
    int FinePositionDofs => 3 * shell.VertexCount;

    public ShellProlongedEnergy(BacShell shell, ShellSurfaceProlongation transfer, ReadOnlySpan<double> coarseRest)
    {
        ArgumentNullException.ThrowIfNull(shell); ArgumentNullException.ThrowIfNull(transfer);
        if (transfer.SampleCount != shell.VertexCount || coarseRest.Length != 3 * transfer.SourceVertexCount)
            throw new ArgumentException("Incompatible coarse/fine geometry.");
        this.shell = shell; this.transfer = transfer; this.coarseRest = coarseRest.ToArray();
        var w = new ShellSurfaceProlongation.Workspace(transfer);
        var positions = new double[FinePositionDofs];
        if (!transfer.TryProlong(coarseRest, positions, w)) throw new ArgumentException("Invalid coarse rest state.");
        var scale = 1e-12;
        foreach (var point in shell.RestVertices) scale = Math.Max(scale, point.Norm);
        const double epsilon = 2.2204460492503131e-16;
        for (var i = 0; i < shell.VertexCount; ++i)
        {
            var p = new ShellVector(positions[3 * i], positions[3 * i + 1], positions[3 * i + 2]);
            if ((p - shell.RestVertices[i]).Norm > 64 * epsilon * scale)
                throw new ArgumentException("Transfer does not recover the fine rest surface.");
        }
    }

    public double[] CreateRestState()
    {
        var state = new double[DofCount]; coarseRest.CopyTo(state, 0);
        shell.RestDirectors.CopyTo(state.AsSpan(CoarsePositionDofs)); return state;
    }

    /// <summary>
    /// Reusable per-worker storage, sized to the explicit fine reference. None of
    /// these arrays are persistent organ state or hidden per-organ caches.
    /// A prepared Hessian refers to the last successful Evaluate call; Energy
    /// and failed evaluations invalidate it. Concurrent use is unsupported.
    /// </summary>
    public sealed class Workspace
    {
        internal readonly ShellProlongedEnergy Owner;
        internal readonly ShellSurfaceProlongation.Workspace Transfer;
        internal readonly BacShell.Workspace Shell = new();
        internal readonly double[] Coarse, Fine, FineGradient, FineDirection, FineProduct, Jacobians, CoarseGradient;
        internal readonly double[] LocalDirection = new double[BacShell.ElementDofs], LocalProduct = new double[BacShell.ElementDofs];
        internal readonly double[] GeometricDirection = new double[9], GeometricProduct = new double[9];
        internal bool Prepared;
        public Workspace(ShellProlongedEnergy energy)
        {
            ArgumentNullException.ThrowIfNull(energy); Owner = energy;
            Transfer = new(energy.transfer); Coarse = new double[energy.CoarsePositionDofs];
            CoarseGradient = new double[energy.CoarsePositionDofs];
            Fine = new double[energy.FineDofCount]; FineGradient = new double[energy.FineDofCount];
            FineDirection = new double[energy.FineDofCount]; FineProduct = new double[energy.FineDofCount];
            Jacobians = new double[27 * energy.shell.VertexCount];
        }
    }

    bool PrepareState(ReadOnlySpan<double> state, ReadOnlySpan<double> forces, Workspace w)
    {
        if (w.Owner == this) w.Prepared = false;
        if (w.Owner != this || state.Length != DofCount || (!forces.IsEmpty && forces.Length != FineDofCount))
            throw new ArgumentException("Incorrect reference workspace or state/load size.");
        w.Prepared = false;
        foreach (var v in state) if (!double.IsFinite(v)) return false;
        foreach (var v in forces) if (!double.IsFinite(v)) return false;
        state[..CoarsePositionDofs].CopyTo(w.Coarse);
        if (!transfer.TryProlong(w.Coarse, w.Fine.AsSpan(0, FinePositionDofs), w.Transfer)) return false;
        state[CoarsePositionDofs..].CopyTo(w.Fine.AsSpan(FinePositionDofs)); return true;
    }

    /// <summary>Energy-only trial; checks fine-surface strain, not contact or a motion path.</summary>
    public double Energy(ReadOnlySpan<double> state, ReadOnlySpan<double> fineForces, Workspace w)
    {
        if (!PrepareState(state, fineForces, w)) return double.PositiveInfinity;
        var energy = shell.Energy(w.Fine, [], w.Shell);
        for (var i = 0; i < fineForces.Length; ++i) energy -= fineForces[i] * w.Fine[i];
        return double.IsFinite(energy) ? energy : double.PositiveInfinity;
    }

    /// <summary>
    /// Evaluate energy/gradient and prepare exact matrix-free Hessian products.
    /// Fine forces are fixed conservative generalized loads, not contact barriers.
    /// Empty gradient still prepares the Hessian. All arrays are caller/worker-owned.
    /// </summary>
    public double Evaluate(ReadOnlySpan<double> state, ReadOnlySpan<double> fineForces, Span<double> gradient, Workspace w)
    {
        if (w.Owner == this) w.Prepared = false;
        if ((!gradient.IsEmpty && gradient.Length != DofCount) || gradient.Overlaps(state) || gradient.Overlaps(fineForces))
            throw new ArgumentException("Incorrect or aliased reference gradient.");
        gradient.Clear();
        if (!PrepareState(state, fineForces, w)) return double.PositiveInfinity;
        var energy = shell.Energy(w.Fine, w.FineGradient, w.Shell);
        if (!double.IsFinite(energy)) return double.PositiveInfinity;
        for (var i = 0; i < fineForces.Length; ++i)
        { energy -= fineForces[i] * w.Fine[i]; w.FineGradient[i] -= fineForces[i]; }
        Span<int> map = stackalloc int[9];
        for (var i = 0; i < shell.VertexCount; ++i)
        {
            var jacobian = w.Jacobians.AsSpan(27 * i, 27);
            if (!transfer.TryEvaluate(i, w.Coarse, out _, jacobian, [], w.Transfer))
            { gradient.Clear(); return double.PositiveInfinity; }
            if (gradient.IsEmpty) continue;
            transfer.CopyElementMap(i, map);
            for (var j = 0; j < 9; ++j)
                for (var k = 0; k < 3; ++k) gradient[map[j]] += jacobian[9 * k + j] * w.FineGradient[3 * i + k];
        }
        if (!gradient.IsEmpty) w.FineGradient.AsSpan(FinePositionDofs).CopyTo(gradient[CoarsePositionDofs..]);
        if (!double.IsFinite(energy) || !AllFinite(w.FineGradient) || !AllFinite(gradient))
        { gradient.Clear(); return double.PositiveInfinity; }
        w.Prepared = true; return energy;
    }

    /// <summary>Exact J^T H J v + geometric term, including free fine directors; no dense Hessian.</summary>
    public bool TryMultiplyHessian(ReadOnlySpan<double> direction, Span<double> product, Workspace w)
    {
        if (w.Owner != this || direction.Length != DofCount || product.Length != DofCount || direction.Overlaps(product))
            throw new ArgumentException("Incorrect or aliased reference product.");
        product.Clear();
        if (!w.Prepared || !AllFinite(direction)) return false;
        Array.Clear(w.FineDirection); Array.Clear(w.FineProduct);
        Span<int> map = stackalloc int[9];
        for (var i = 0; i < shell.VertexCount; ++i)
        {
            transfer.CopyElementMap(i, map); var jacobian = w.Jacobians.AsSpan(27 * i, 27);
            for (var k = 0; k < 3; ++k) for (var j = 0; j < 9; ++j)
                w.FineDirection[3 * i + k] += jacobian[9 * k + j] * direction[map[j]];
        }
        direction[CoarsePositionDofs..].CopyTo(w.FineDirection.AsSpan(FinePositionDofs));
        for (var f = 0; f < shell.FaceCount; ++f)
        {
            var elementMap = shell.ElementMap(f);
            for (var j = 0; j < BacShell.ElementDofs; ++j) w.LocalDirection[j] = w.FineDirection[elementMap[j]];
            if (!double.IsFinite(shell.EvaluateElementProduct(f, w.Fine, w.LocalDirection, [], w.LocalProduct, w.Shell))) return false;
            for (var j = 0; j < BacShell.ElementDofs; ++j) w.FineProduct[elementMap[j]] += w.LocalProduct[j];
        }
        for (var i = 0; i < shell.VertexCount; ++i)
        {
            transfer.CopyElementMap(i, map); var jacobian = w.Jacobians.AsSpan(27 * i, 27);
            for (var j = 0; j < 9; ++j) w.GeometricDirection[j] = direction[map[j]];
            if (!transfer.TryContractHessian(i, w.Coarse, w.FineGradient.AsSpan(3 * i, 3),
                w.GeometricDirection, w.GeometricProduct, w.Transfer)) { product.Clear(); return false; }
            for (var j = 0; j < 9; ++j)
            {
                var value = w.GeometricProduct[j];
                for (var k = 0; k < 3; ++k) value += jacobian[9 * k + j] * w.FineProduct[3 * i + k];
                product[map[j]] += value;
            }
        }
        w.FineProduct.AsSpan(FinePositionDofs).CopyTo(product[CoarsePositionDofs..]);
        if (AllFinite(product)) return true;
        product.Clear(); return false;
    }

    /// <summary>
    /// A stationary coarse proxy need not balance forces in the fine space.
    /// Report both, excluding explicitly fixed coordinates. Empty masks mean
    /// no fixed coordinates. The caller must supply consistent clamp masks;
    /// this routine does not infer reactions, equilibrium tolerances or contact.
    /// </summary>
    public bool TryMeasureStationarity(ReadOnlySpan<bool> fixedCoarsePositions, ReadOnlySpan<bool> fixedFinePositions,
        ReadOnlySpan<bool> fixedDirectors, out ShellProlongedResiduals residuals, Workspace w)
    {
        if (w.Owner != this || (!fixedCoarsePositions.IsEmpty && fixedCoarsePositions.Length != CoarsePositionDofs)
            || (!fixedFinePositions.IsEmpty && fixedFinePositions.Length != FinePositionDofs)
            || (!fixedDirectors.IsEmpty && fixedDirectors.Length != DirectorDofs))
            throw new ArgumentException("Incorrect stationarity workspace or masks.");
        residuals = default;
        if (!w.Prepared) return false;
        Array.Clear(w.CoarseGradient);
        Span<int> map = stackalloc int[9];
        for (var i = 0; i < shell.VertexCount; ++i)
        {
            transfer.CopyElementMap(i, map); var jacobian = w.Jacobians.AsSpan(27 * i, 27);
            for (var j = 0; j < 9; ++j) for (var k = 0; k < 3; ++k)
                w.CoarseGradient[map[j]] += jacobian[9 * k + j] * w.FineGradient[3 * i + k];
        }
        var coarseForce = 0.0; var fineForce = 0.0; var torque = 0.0;
        for (var i = 0; i < CoarsePositionDofs; ++i)
            if (fixedCoarsePositions.IsEmpty || !fixedCoarsePositions[i]) coarseForce = Math.Max(coarseForce, Math.Abs(w.CoarseGradient[i]));
        for (var i = 0; i < FinePositionDofs; ++i)
            if (fixedFinePositions.IsEmpty || !fixedFinePositions[i]) fineForce = Math.Max(fineForce, Math.Abs(w.FineGradient[i]));
        for (var i = 0; i < DirectorDofs; ++i)
            if (fixedDirectors.IsEmpty || !fixedDirectors[i]) torque = Math.Max(torque, Math.Abs(w.FineGradient[FinePositionDofs + i]));
        if (!double.IsFinite(coarseForce + fineForce + torque)) return false;
        residuals = new(coarseForce, fineForce, torque); return true;
    }

    static bool AllFinite(ReadOnlySpan<double> values) { foreach (var v in values) if (!double.IsFinite(v)) return false; return true; }
}
