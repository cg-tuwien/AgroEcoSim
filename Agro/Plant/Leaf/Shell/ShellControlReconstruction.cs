namespace Agro.Mechanics;

public enum ShellReconstructionStatus : byte
{
    Converged, InvalidInput, InadmissiblePredictor, NumericalNonconvergence, ResidualFailure
}

/// <summary>FullForceResidual is an equivalent force in newtons: director torques
/// are divided by the shell characteristic length, as in ShellEquilibrium.</summary>
public readonly record struct ShellReconstructionResult(ShellReconstructionStatus Status,
    ShellSolveResult? Solve, double FullForceResidual, double MinimumStretch, double MaximumStretch);

/// <summary>
/// Experimental static reconstruction from retained free vertex coordinates.
/// Fine coordinates and directors are active-worker scratch, not saved organ
/// state. Convergence includes forces on the retained vertices: an equilibrated
/// interior alone is insufficient. This does not certify self-contact, general
/// obstacles, a growth trajectory, branch uniqueness or checkpoint continuation.
/// The immutable fixed-rest profile is shared; do not instantiate its full rest
/// arrays per organ. Changing biological rest geometry needs another profile or
/// a future normalized worker representation, not reuse with mismatched assets.
/// </summary>
public sealed class ShellControlReconstruction
{
    readonly BacShell shell;
    readonly ShellControlPredictor predictor;
    readonly double[] rest;
    readonly bool[] physicalFixed, reconstructionFixed;
    readonly int[] savedDofs;
    readonly Lazy<ShellFactorProfile> factorProfile;
    public int SavedCoordinateCount => savedDofs.Length;
    public int FineDofCount => shell.DofCount;
    public int SavedCoordinatePayloadBytes => checked(sizeof(double) * SavedCoordinateCount);
    /// <summary>Additional shared arrays; shell, predictor and optional factor
    /// profile assets are separate. The solver reports the latter's payload.</summary>
    public long ProfileArrayPayloadBytes => (long)rest.Length * sizeof(double)
        + physicalFixed.Length + reconstructionFixed.Length + (long)savedDofs.Length * sizeof(int);

    public ShellControlReconstruction(BacShell shell, ShellControlPredictor predictor, ReadOnlySpan<bool> fixedDofs)
    {
        ArgumentNullException.ThrowIfNull(shell); ArgumentNullException.ThrowIfNull(predictor);
        if (fixedDofs.Length != shell.DofCount || predictor.VertexCount != shell.VertexCount
            || !predictor.RestPositions.SequenceEqual(shell.RestVertices))
            throw new ArgumentException("Reconstruction requires the same rest surface and a complete clamp mask.");
        this.shell = shell; this.predictor = predictor; rest = shell.CreateRestState();
        physicalFixed = fixedDofs.ToArray(); reconstructionFixed = physicalFixed.ToArray();
        var retained = new List<int>();
        foreach (var vertex in predictor.ControlVertices)
            for (var axis = 0; axis < 3; ++axis)
            {
                var i = 3 * vertex + axis;
                reconstructionFixed[i] = true;
                if (!physicalFixed[i]) retained.Add(i);
            }
        if (retained.Count == 0) throw new ArgumentException("At least one free control coordinate is required.");
        savedDofs = retained.ToArray();
        factorProfile = new(() => new ShellFactorProfile(shell, reconstructionFixed));
    }

    /// <summary>
    /// Create a direct worker using this profile's shared reconstruction clamps.
    /// After Reconstruct, Fixed is restored to physical clamps for the complete
    /// force check. Use this worker through Reconstruct, which reinstates the
    /// reconstruction mask before solving; a direct Solve with released controls
    /// is rejected. Numeric arrays remain worker-owned and reusable.
    /// </summary>
    public ShellEquilibrium CreateSolver() => new(shell, useDirectReference: true, factorProfile: factorProfile.Value);

    /// <summary>
    /// Gather finite retained coordinates from an already accepted full state.
    /// This performs no equilibrium test and is not a versioned archive format.
    /// Output is unchanged on false; input and output may not alias.
    /// </summary>
    public bool TryCapture(ReadOnlySpan<double> acceptedState, Span<double> savedCoordinates)
    {
        if (acceptedState.Length != shell.DofCount || savedCoordinates.Length != SavedCoordinateCount
            || acceptedState.Overlaps(savedCoordinates)) throw new ArgumentException("Incorrect or aliased capture buffers.");
        foreach (var i in savedDofs) if (!double.IsFinite(acceptedState[i])) return false;
        for (var j = 0; j < savedDofs.Length; ++j) savedCoordinates[j] = acceptedState[savedDofs[j]];
        return true;
    }

    /// <summary>
    /// Own one per active reconstruction worker, and reuse it across organs.
    /// The supplied solver must belong to this exact shell. The explicit budget
    /// covers additional reconstruction arrays only; report Solver.ArrayPayloadBytes
    /// and its shared pattern separately. No previous fine configuration is used.
    /// </summary>
    public sealed class Workspace
    {
        internal readonly ShellControlReconstruction Owner;
        internal readonly double[] State, Controls, CapturedControls;
        internal readonly ShellControlPredictor.Workspace Predictor;
        public ShellEquilibrium Solver { get; }
        public long AdditionalArrayPayloadBytes => ((long)State.Length + Controls.Length + CapturedControls.Length) * sizeof(double)
            + Predictor.ArrayPayloadBytes;
        public Workspace(ShellControlReconstruction owner, ShellEquilibrium solver, long maximumAdditionalArrayPayloadBytes)
        {
            ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(solver);
            if (!ReferenceEquals(solver.Shell, owner.shell)) throw new ArgumentException("Incorrect reconstruction solver.");
            solver.ValidateFixedMask(owner.reconstructionFixed);
            var required = sizeof(double) * ((long)owner.shell.DofCount + 6L * owner.predictor.ControlCount + owner.SavedCoordinateCount);
            if (maximumAdditionalArrayPayloadBytes < required)
                throw new ArgumentException("Reconstruction exceeds its additional worker array budget.");
            Owner = owner; Solver = solver; State = new double[owner.shell.DofCount];
            Controls = new double[3 * owner.predictor.ControlCount]; Predictor = new(owner.predictor);
            CapturedControls = new double[owner.SavedCoordinateCount];
        }
    }

    // Capture is certified by decoding from rest, not by retaining a previous
    // fine snapshot. The comparison rejects an alternative equilibrium branch.
    internal ShellControlStorageResult CertifyCapture(ReadOnlySpan<double> state, ReadOnlySpan<double> forces,
        ShellControlCaptureTolerance tolerance, Workspace w)
    {
        tolerance.Validate();
        if (w.Owner != this || state.Length != shell.DofCount || (!forces.IsEmpty && forces.Length != shell.DofCount)
            || state.Overlaps(w.State) || state.Overlaps(w.CapturedControls) || forces.Overlaps(w.State)
            || state.Overlaps(w.Solver.Forces) || state.Overlaps(w.Solver.SpringTargets)
            || state.Overlaps(w.Solver.SpringStiffness))
            throw new ArgumentException("Incorrect or aliased capture buffers.");
        foreach (var x in state) if (!double.IsFinite(x)) return new(ShellControlStorageStatus.InvalidInput);
        for (var i = 0; i < state.Length; ++i)
            if (physicalFixed[i] && state[i] != rest[i]) return new(ShellControlStorageStatus.ShapeMismatch);
        TryCapture(state, w.CapturedControls);
        var decoded = Reconstruct(w.CapturedControls, forces, tolerance.ForceNewtons, w.State, w);
        if (decoded.Status != ShellReconstructionStatus.Converged)
            return new(ShellControlStorageStatus.ReconstructionFailed, decoded);
        var positionError = 0.0; var directorError = 0.0;
        for (var i = 0; i < shell.VertexCount; ++i)
            positionError = Math.Max(positionError, new ShellVector(state[3 * i] - w.State[3 * i],
                state[3 * i + 1] - w.State[3 * i + 1], state[3 * i + 2] - w.State[3 * i + 2]).Norm);
        for (var i = 3 * shell.VertexCount; i < state.Length; ++i)
            directorError = Math.Max(directorError, Math.Abs(state[i] - w.State[i]));
        return new(positionError <= tolerance.PositionMetres && directorError <= tolerance.DirectorRadians
            ? ShellControlStorageStatus.Stored : ShellControlStorageStatus.ShapeMismatch,
            decoded, positionError, directorError);
    }

    /// <summary>
    /// Rebuild from saved controls and rest geometry, then relax all other free
    /// coordinates. Forces are conservative full-coordinate loads (or empty for
    /// zero load); prescribed DOFs retain their rest values. Only Converged copies
    /// to acceptedState; all failures leave that output unchanged. The absolute
    /// full-force gate is independent of the interior solver's relative tolerance.
    /// Solver settings are caller-owned; fixed masks and loads are set by this call.
    /// Workspace and input/output calls must not overlap in time or memory.
    /// </summary>
    public ShellReconstructionResult Reconstruct(ReadOnlySpan<double> savedCoordinates, ReadOnlySpan<double> forces,
        double maximumForceResidual, Span<double> acceptedState, Workspace w)
    {
        if (w.Owner != this || savedCoordinates.Length != SavedCoordinateCount || acceptedState.Length != shell.DofCount
            || (!forces.IsEmpty && forces.Length != shell.DofCount) || savedCoordinates.Overlaps(acceptedState)
            || forces.Overlaps(acceptedState) || acceptedState.Overlaps(w.Solver.Forces)
            || acceptedState.Overlaps(w.Solver.SpringTargets) || acceptedState.Overlaps(w.Solver.SpringStiffness))
            throw new ArgumentException("Incorrect or aliased reconstruction buffers.");
        if (!(maximumForceResidual > 0) || !double.IsFinite(maximumForceResidual))
            throw new ArgumentOutOfRangeException(nameof(maximumForceResidual));
        static ShellReconstructionResult Failure(ShellReconstructionStatus status) =>
            new(status, default, double.PositiveInfinity, double.NaN, double.NaN);
        foreach (var f in forces) if (!double.IsFinite(f)) return Failure(ShellReconstructionStatus.InvalidInput);
        foreach (var x in savedCoordinates) if (!double.IsFinite(x)) return Failure(ShellReconstructionStatus.InvalidInput);
        var slot = 0; var saved = 0;
        foreach (var vertex in predictor.ControlVertices)
            for (var axis = 0; axis < 3; ++axis)
            {
                var i = 3 * vertex + axis;
                w.Controls[slot++] = physicalFixed[i] ? rest[i] : savedCoordinates[saved++];
            }
        rest.CopyTo(w.State, 0);
        if (!predictor.TryPredict(w.Controls, w.State.AsSpan(0, 3 * shell.VertexCount), w.Predictor))
            return Failure(ShellReconstructionStatus.InvalidInput);
        for (var i = 0; i < rest.Length; ++i) if (physicalFixed[i]) w.State[i] = rest[i];
        if (!shell.StrainRange(w.State, out var low, out var high))
            return new(ShellReconstructionStatus.InadmissiblePredictor, default, double.PositiveInfinity, low, high);
        var solver = w.Solver;
        if (forces.IsEmpty) Array.Clear(solver.Forces); else forces.CopyTo(solver.Forces);
        reconstructionFixed.CopyTo(solver.Fixed, 0);
        ShellSolveResult result;
        try { result = solver.Solve(w.State); }
        finally { physicalFixed.CopyTo(solver.Fixed, 0); }
        var residual = solver.EvaluateForceResidual(w.State);
        var valid = shell.StrainRange(w.State, out low, out high);
        if (result.Status == ShellSolveStatus.InadmissibleStart)
            return new(ShellReconstructionStatus.InadmissiblePredictor, result, residual, low, high);
        if (result.Status != ShellSolveStatus.Converged)
            return new(ShellReconstructionStatus.NumericalNonconvergence, result, residual, low, high);
        if (!valid || !double.IsFinite(residual) || residual > maximumForceResidual)
            return new(ShellReconstructionStatus.ResidualFailure, result, residual, low, high);
        w.State.CopyTo(acceptedState);
        return new(ShellReconstructionStatus.Converged, result, residual, low, high);
    }
}
