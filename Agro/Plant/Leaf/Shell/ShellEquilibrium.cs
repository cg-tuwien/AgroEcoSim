namespace Agro.Mechanics;

public enum ShellSolveStatus : byte { Converged, Unconverged, InadmissibleStart }
public readonly record struct ShellSolveResult(ShellSolveStatus Status, int NewtonIterations,
    int LinearIterations, double Energy, double ForceResidual, double RelativeDisplacement);

/// <summary>
/// Quasistatic shell equilibrium. Work limits report nonconvergence, never confinement.
/// Local exact Hessians are projected to PSD and applied as element blocks; IC(0)
/// supplies a topology-aware preconditioner. All solve storage is reusable workspace.
/// </summary>
public sealed class ShellEquilibrium
{
    readonly BacShell shell;
    internal BacShell Shell => shell;
    /// <summary>
    /// Owned array payloads, including the direct numeric factor and serial
    /// element scratch. Excludes object headers, IC dictionary storage, shared
    /// symbolic patterns and thread-local parallel element scratch. This is a
    /// measurement of reusable solver storage, never a per-organ state size.
    /// </summary>
    public long ArrayPayloadBytes => sizeof(double) * ((long)gradient.Length + direction.Length + trial.Length
        + diagonal.Length + residual.Length + z.Length + p.Length + ap.Length + blocks.Length + scaling.Length
        + factor.Length + unfactored.Length + evaluationState.Length + elementGradients.Length + elementEnergies.Length
        + contactBlocks.Length + Forces.Length + SpringTargets.Length + SpringStiffness.Length)
        + sizeof(int) * ((long)rowStarts.Length + columns.Length) + Fixed.Length + BacShell.WorkspacePayloadBytes
        + (direct?.NumericPayloadBytes ?? 0) + (productWork?.ArrayPayloadBytes ?? 0);
    public long SharedFactorPatternPayloadBytes => direct?.SymbolicPayloadBytes ?? 0;
    readonly BacShell.Workspace element = new();
    readonly double[] gradient, direction, trial, diagonal, residual, z, p, ap, blocks, scaling;
    readonly int[] rowStarts, columns;
    readonly double[] factor, unfactored;
    readonly Dictionary<long, int> slots;
    readonly double[] evaluationState, elementGradients, elementEnergies;
    readonly double[] contactBlocks;
    readonly Action<int> evaluateFace, projectFace;
    readonly ParallelOptions parallelOptions = new();
    sealed class FaceWorkspace
    {
        internal readonly BacShell.Workspace Element = new();
        internal readonly double[] Eigenvectors = new double[BacShell.ElementDofs * BacShell.ElementDofs];
        internal readonly double[] Eigenmatrix = new double[BacShell.ElementDofs * BacShell.ElementDofs];
    }
    [ThreadStatic] static FaceWorkspace? worker;
    readonly ShellSparseFactor? direct;
    readonly ShellFactorProfile? factorProfile;
    bool directReady;
    ShellParallelWork? productWork;
    double[]? productInput;
    int productWorkers;
    /// <summary>Zero follows MaximumWorkers; one selects the serial product.
    /// Expensive element batches use disjoint scratch and a fixed-order scatter.</summary>
    public int MaximumProductWorkers { get; set; }
    public int ParallelMatrixProducts { get; private set; }
    public int PeakProductWorkers { get; private set; } = 1;
    public int MaximumNewtonIterations { get; set; } = 80;
    public double RelativeForceTolerance { get; set; } = 1e-5;
    public double AbsoluteForceTolerance { get; set; } = 1e-9;
    public double RelativeDisplacementTolerance { get; set; } = 1e-6;
    public double LinearRelativeTolerance { get; set; } = 1e-6;
    /// <summary>
    /// For direct systems above 3000 DOFs, try the previous positive factor as a
    /// preconditioner for this many PCG iterations before rebuilding. Zero disables
    /// reuse. The current tangent is always applied, and its true residual must
    /// pass the ordinary linear tolerance. Reuse never crosses Solve calls.
    /// </summary>
    public int MaximumFactorReuseIterations { get; set; } = 16;
    public int FactorizationAttempts { get; private set; }
    public int ReusedFactorSolves { get; private set; }
    public int RejectedFactorReuses { get; private set; }
    bool linearBreakdown;
    /// <summary>
    /// Absolute objective uncertainty in joules for the guarded near-equilibrium
    /// line-search fallback. The conservative default is 1e-19 J; zero disables
    /// it for comparisons. This is a numerical allowance, not an error estimate
    /// for every energy scale, and changes neither material nor force tolerances.
    /// </summary>
    public double RoundoffEnergyAllowance { get; set; } = 1e-19;
    public int RoundoffLineSearchSteps { get; private set; }
    public int MaximumWorkers { get; set; } = Environment.ProcessorCount;
    /// <summary>Zero follows MaximumWorkers; one selects the serial reference path.
    /// Only expensive columns dispatch, with at most 32 active workers.</summary>
    public int MaximumFactorWorkers { get; set; }
    /// <summary>Diagnostics for the most recent direct factorization.</summary>
    public int ParallelFactorColumns => direct?.ParallelColumns ?? 0;
    public int PeakFactorWorkers => direct?.PeakParallelWorkers ?? 1;
    public long DerivativeTicks { get; private set; }
    public long ProjectionTicks { get; private set; }
    public long FactorTicks { get; private set; }
    public long LinearTicks { get; private set; }
    public long LineSearchTicks { get; private set; }
    public double CharacteristicLength { get; }
    public double[] Forces { get; }
    public bool[] Fixed { get; }
    public double[] SpringTargets { get; }
    public double[] SpringStiffness { get; }
    public ShellPlaneContact[] Planes { get; set; } = [];
    public double MinimumPlaneGap { get; private set; } = double.PositiveInfinity;
    public double MaximumPlaneReactionNewtons { get; private set; }
    public int ActivePlaneVertexContacts { get; private set; }

    /// <param name="shell">Immutable shell profile.</param>
    /// <param name="useDirectReference">Use a direct factor as the preconditioner.</param>
    /// <param name="factorProfile">Optional shared prescribed-DOF pattern for the
    /// direct solver. Its prescribed DOFs must remain fixed during every Solve;
    /// freeing them requires another profile or the unrestricted constructor.
    /// Force evaluation may still include their reactions.</param>
    public ShellEquilibrium(BacShell shell, bool useDirectReference = false, ShellFactorProfile? factorProfile = null)
    {
        if (factorProfile != null && (!useDirectReference || !ReferenceEquals(factorProfile.Shell, shell)))
            throw new ArgumentException("A factor profile requires the direct solver and its owning shell.", nameof(factorProfile));
        this.factorProfile = factorProfile;
        this.shell = shell; var n = shell.DofCount;
        gradient = new double[n]; direction = new double[n]; trial = new double[n]; diagonal = new double[n];
        residual = new double[n]; z = new double[n]; p = new double[n]; ap = new double[n];
        Forces = new double[n]; Fixed = new bool[n]; SpringTargets = new double[n]; SpringStiffness = new double[n];
        factorProfile?.Prescribed.CopyTo(Fixed, 0);
        scaling = new double[n]; blocks = new double[shell.FaceCount * BacShell.ElementDofs * BacShell.ElementDofs];
        evaluationState = new double[n]; elementGradients = new double[shell.FaceCount * BacShell.ElementDofs];
        contactBlocks = new double[9 * shell.VertexCount];
        elementEnergies = new double[shell.FaceCount]; evaluateFace = EvaluateFace; projectFace = ProjectFace;
        var min = shell.RestVertices[0]; var max = min;
        foreach (var v in shell.RestVertices)
        {
            min = new(Math.Min(min.X, v.X), Math.Min(min.Y, v.Y), Math.Min(min.Z, v.Z));
            max = new(Math.Max(max.X, v.X), Math.Max(max.Y, v.Y), Math.Max(max.Z, v.Z));
        }
        CharacteristicLength = Math.Max(1e-12, (max - min).Norm);
        for (var i = 0; i < n; ++i) scaling[i] = i < 3 * shell.VertexCount ? CharacteristicLength : 1;
        if (useDirectReference)
        {
            // This path uses a profile-shared sparse factor pattern. Do not also
            // build a second, unused IC(0) graph for every reference workspace.
            rowStarts = columns = []; factor = unfactored = []; slots = [];
            direct = new(shell, profile: factorProfile);
            return;
        }
        var pattern = new SortedSet<int>[n];
        for (var i = 0; i < n; ++i) pattern[i] = [i];
        for (var f = 0; f < shell.FaceCount; ++f)
        {
            var map = shell.ElementMap(f);
            foreach (var i in map) foreach (var j in map) if (i >= j) pattern[i].Add(j);
        }
        rowStarts = new int[n + 1];
        for (var i = 0; i < n; ++i) rowStarts[i + 1] = rowStarts[i] + pattern[i].Count;
        columns = new int[rowStarts[n]]; slots = new(columns.Length);
        for (var i = 0; i < n; ++i)
        {
            var k = rowStarts[i];
            foreach (var j in pattern[i]) { columns[k] = j; slots[((long)i << 32) | (uint)j] = k++; }
        }
        factor = new double[columns.Length]; unfactored = new double[columns.Length];
    }

    public double Energy(ReadOnlySpan<double> q)
    {
        var value = shell.Energy(q, [], element);
        for (var i = 0; i < q.Length; ++i)
        { var d = q[i] - SpringTargets[i]; value += .5 * SpringStiffness[i] * d * d - Forces[i] * q[i]; }
        return value + PlaneEnergy(q, [], false);
    }

    /// <summary>
    /// Recheck force balance on an exact restored/packed state. No factorization
    /// or allocation; nonfinite or inadmissible configurations return infinity.
    /// Uses the same equivalent-newton norm as Solve, including plane reactions.
    /// </summary>
    public double EvaluateForceResidual(ReadOnlySpan<double> q)
    {
        if (q.Length != shell.DofCount) throw new ArgumentException("Incorrect shell state size.");
        var energy = shell.Energy(q, gradient, element) + PlaneEnergy(q, gradient, false);
        if (!double.IsFinite(energy)) return double.PositiveInfinity;
        var force = 0.0;
        for (var i = 0; i < q.Length; ++i)
        {
            var d = q[i] - SpringTargets[i];
            energy += .5 * SpringStiffness[i] * d * d - Forces[i] * q[i];
            if (Fixed[i]) continue;
            var g = gradient[i] + SpringStiffness[i] * d - Forces[i];
            if (!double.IsFinite(g)) return double.PositiveInfinity;
            force = Math.Max(force, Math.Abs(g * scaling[i] / CharacteristicLength));
        }
        return double.IsFinite(energy) ? force : double.PositiveInfinity;
    }

    internal void ValidateFixedMask(ReadOnlySpan<bool> fixedDofs) => factorProfile?.ValidateMask(fixedDofs);

    double PlaneEnergy(ReadOnlySpan<double> q, Span<double> gradient, bool hessian)
    {
        if (hessian) Array.Clear(contactBlocks);
        if (!gradient.IsEmpty)
        { MinimumPlaneGap = double.PositiveInfinity; MaximumPlaneReactionNewtons = 0; ActivePlaneVertexContacts = 0; }
        var energy = 0.0;
        foreach (var plane in Planes)
            for (var vertex = 0; vertex < shell.VertexCount; ++vertex)
            {
                var weight = plane.UsesRestArea ? shell.RestVertexAreas[vertex] : 1.0;
                if (weight == 0) continue; // Unused vertices carry no material surface.
                var point = new ShellVector(q[3 * vertex], q[3 * vertex + 1], q[3 * vertex + 2]);
                var gap = plane.Normal.Dot(point) - plane.Offset;
                var value = plane.Evaluate(gap, out var first, out var second);
                if (!gradient.IsEmpty) MinimumPlaneGap = Math.Min(MinimumPlaneGap, gap);
                if (!double.IsFinite(value)) return value;
                energy += weight * value;
                first *= weight; second *= weight;
                if (!gradient.IsEmpty)
                {
                    MaximumPlaneReactionNewtons = Math.Max(MaximumPlaneReactionNewtons, -first);
                    if (first != 0) ++ActivePlaneVertexContacts;
                    gradient[3 * vertex] += first * plane.Normal.X;
                    gradient[3 * vertex + 1] += first * plane.Normal.Y;
                    gradient[3 * vertex + 2] += first * plane.Normal.Z;
                }
                if (!hessian || second == 0) continue;
                // Same-vertex normal outer product; no contact object allocation.
                for (var i = 0; i < 3; ++i) for (var j = 0; j < 3; ++j)
                {
                    var ni = i == 0 ? plane.Normal.X : i == 1 ? plane.Normal.Y : plane.Normal.Z;
                    var nj = j == 0 ? plane.Normal.X : j == 1 ? plane.Normal.Y : plane.Normal.Z;
                    contactBlocks[9 * vertex + 3 * i + j] += second * ni * nj * scaling[3 * vertex + i] * scaling[3 * vertex + j];
                }
            }
        return energy;
    }

    public ShellSolveResult Solve(Span<double> q)
    {
        if (q.Length != shell.DofCount) throw new ArgumentException("Incorrect shell state size.");
        ValidateFixedMask(Fixed);
        if (MaximumProductWorkers < 0) throw new ArgumentOutOfRangeException(nameof(MaximumProductWorkers));
        ParallelMatrixProducts = 0; PeakProductWorkers = 1;
        if (MaximumFactorWorkers < 0) throw new ArgumentOutOfRangeException(nameof(MaximumFactorWorkers));
        DerivativeTicks = ProjectionTicks = FactorTicks = LinearTicks = LineSearchTicks = 0;
        directReady = false;
        FactorizationAttempts = ReusedFactorSolves = RejectedFactorReuses = 0;
        if (MaximumFactorReuseIterations < 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumFactorReuseIterations));
        if (!double.IsFinite(RoundoffEnergyAllowance) || RoundoffEnergyAllowance < 0)
            throw new ArgumentOutOfRangeException(nameof(RoundoffEnergyAllowance));
        RoundoffLineSearchSteps = 0;
        var initial = 0.0; var displacement = 0.0; var linearIterations = 0; var energy = double.PositiveInfinity; var force = double.PositiveInfinity;
        var iteration = 0;
        for (; iteration <= MaximumNewtonIterations; ++iteration)
        {
            // Sleeping/settled configurations need only a force check. Building exact
            // Hessians and factorizations before this check dominated no-op solves.
            energy = shell.Energy(q, gradient, element);
            energy += PlaneEnergy(q, gradient, false);
            for (var i = 0; i < q.Length; ++i)
            {
                var d = q[i] - SpringTargets[i];
                energy += .5 * SpringStiffness[i] * d * d - Forces[i] * q[i];
                gradient[i] = Fixed[i] ? 0 : (gradient[i] + SpringStiffness[i] * d - Forces[i]) * scaling[i];
            }
            if (!double.IsFinite(energy)) return new(iteration == 0 ? ShellSolveStatus.InadmissibleStart : ShellSolveStatus.Unconverged,
                iteration, linearIterations, energy, force, displacement);
            force = 0;
            for (var i = 0; i < q.Length; ++i) if (!Fixed[i]) force = Math.Max(force, Math.Abs(gradient[i]) / CharacteristicLength);
            if (iteration == 0) initial = force;
            if (force <= AbsoluteForceTolerance + RelativeForceTolerance * initial && displacement <= RelativeDisplacementTolerance)
                return new(ShellSolveStatus.Converged, iteration, linearIterations, energy, force, displacement);
            if (iteration == MaximumNewtonIterations) break;
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            Assemble(q);
            DerivativeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
            var reused = false;
            if (directReady && gradient.Length > 3000 && MaximumFactorReuseIterations > 0)
            {
                started = System.Diagnostics.Stopwatch.GetTimestamp();
                linearIterations += LinearSolve(MaximumFactorReuseIterations);
                reused = !linearBreakdown && AccurateLinearDirection();
                LinearTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
                if (reused) ++ReusedFactorSolves; else ++RejectedFactorReuses;
            }
            if (!reused)
            {
                started = System.Diagnostics.Stopwatch.GetTimestamp();
                BuildPreconditioner();
                FactorTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
                started = System.Diagnostics.Stopwatch.GetTimestamp();
                linearIterations += LinearSolve();
                LinearTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
            }
            // A large preceding step can arrive at equilibrium. Test the next
            // correction before demanding an energy decrease below roundoff;
            // otherwise a settled state is falsely reported as nonconverged.
            var correction = 0.0;
            for (var i = 0; i < direction.Length; ++i)
                if (!Fixed[i]) correction = Math.Max(correction, Math.Abs(direction[i]));
            if (force <= AbsoluteForceTolerance + RelativeForceTolerance * initial
                && correction <= RelativeDisplacementTolerance)
                return new(ShellSolveStatus.Converged, iteration, linearIterations, energy, force, correction);
            var slope = Dot(gradient, direction);
            if (!double.IsFinite(slope) || slope >= 0)
            {
                for (var i = 0; i < q.Length; ++i) direction[i] = Fixed[i] ? 0 : -gradient[i] / Math.Max(1e-20, diagonal[i]);
                slope = Dot(gradient, direction);
                correction = 0;
                for (var i = 0; i < direction.Length; ++i)
                    if (!Fixed[i]) correction = Math.Max(correction, Math.Abs(direction[i]));
            }
            var step = 1.0; var accepted = false;
            started = System.Diagnostics.Stopwatch.GetTimestamp();
            for (var backtrack = 0; backtrack < 40; ++backtrack, step *= .5)
            {
                for (var i = 0; i < q.Length; ++i) trial[i] = q[i] + step * direction[i] * scaling[i];
                var candidate = Energy(trial);
                if (double.IsFinite(candidate) && candidate <= energy + 1e-4 * step * slope
                    && shell.TryCertifyLinearStrainPath(q, trial))
                { accepted = true; break; }
                if (RoundoffEnergyAllowance > 0 && slope < 0 && double.IsFinite(candidate)
                    && candidate - energy <= RoundoffEnergyAllowance
                    // A step that improves equilibrium need not itself meet the
                    // final stopping tolerance. Keep the tiny-step safeguard
                    // independent, then recheck force AND displacement after it.
                    && step * correction <= ShellLineSearch.MaximumRoundoffStep
                    && shell.TryCertifyLinearStrainPath(q, trial))
                {
                    var candidateForce = EvaluateForceResidual(trial);
                    var candidateSlope = 0.0;
                    for (var i = 0; i < q.Length; ++i)
                        if (!Fixed[i]) candidateSlope += (gradient[i]
                            + SpringStiffness[i] * (trial[i] - SpringTargets[i]) - Forces[i])
                            * direction[i] * scaling[i];
                    if (ShellLineSearch.AcceptRoundoffStep(energy, candidate, RoundoffEnergyAllowance,
                        slope, candidateSlope, force, candidateForce, step * correction))
                    { ++RoundoffLineSearchSteps; accepted = true; break; }
                }
            }
            LineSearchTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
            if (!accepted) break;
            displacement = 0;
            for (var i = 0; i < q.Length; ++i)
            { displacement = Math.Max(displacement, Math.Abs(step * direction[i])); q[i] = trial[i]; }
        }
        return new(ShellSolveStatus.Unconverged, iteration, linearIterations, energy, force, displacement);
    }

    double Assemble(ReadOnlySpan<double> q)
    {
        Array.Clear(gradient); Array.Clear(diagonal); var energy = 0.0;
        const int m = BacShell.ElementDofs;
        q.CopyTo(evaluationState);
        var workers = Math.Min(Math.Max(1, MaximumWorkers), Math.Max(1, shell.FaceCount / 32));
        if (workers > 1)
        {
            parallelOptions.MaxDegreeOfParallelism = workers;
            Parallel.For(0, shell.FaceCount, parallelOptions, evaluateFace);
        }
        else for (var f = 0; f < shell.FaceCount; ++f) EvaluateFace(f);
        // Workers write disjoint blocks. Scatter and reduction stay in stable face
        // order, so changing worker count cannot change the floating-point result.
        for (var f = 0; f < shell.FaceCount; ++f)
        {
            var block = blocks.AsSpan(f * m * m, m * m);
            var value = elementEnergies[f];
            if (!double.IsFinite(value)) return value;
            energy += value; var map = shell.ElementMap(f);
            for (var i = 0; i < m; ++i)
            {
                gradient[map[i]] += elementGradients[f * m + i];
                diagonal[map[i]] += block[i * m + i];
            }
        }
        for (var i = 0; i < q.Length; ++i)
        {
            var d = q[i] - SpringTargets[i];
            energy += .5 * SpringStiffness[i] * d * d - Forces[i] * q[i];
            gradient[i] += (SpringStiffness[i] * d - Forces[i]) * scaling[i];
            diagonal[i] += SpringStiffness[i] * scaling[i] * scaling[i];
            if (Fixed[i]) gradient[i] = 0;
        }
        // The force check above already used physical plane gradients. Assembly
        // supplies their scaled contributions and PSD normal Hessian blocks.
        Array.Clear(residual);
        energy += PlaneEnergy(q, residual, true);
        for (var i = 0; i < 3 * shell.VertexCount; ++i)
        {
            if (!Fixed[i]) gradient[i] += residual[i] * scaling[i];
            diagonal[i] += contactBlocks[9 * (i / 3) + 4 * (i % 3)];
        }
        return energy;
    }

    void EvaluateFace(int face)
    {
        var scratch = worker ??= new(); const int m = BacShell.ElementDofs;
        var block = blocks.AsSpan(face * m * m, m * m);
        var energy = shell.EvaluateElement(face, evaluationState, scratch.Element.Gradient, block, scratch.Element);
        elementEnergies[face] = energy;
        if (!double.IsFinite(energy)) return;
        var map = shell.ElementMap(face);
        for (var i = 0; i < m; ++i)
        {
            elementGradients[face * m + i] = scratch.Element.Gradient[i] * scaling[map[i]];
            for (var j = 0; j < m; ++j) block[i * m + j] *= scaling[map[i]] * scaling[map[j]];
        }
        // Preserve exact Newton convergence when the assembled Hessian is SPD.
        if (direct == null) ProjectPositive(block, m, scratch.Eigenmatrix, scratch.Eigenvectors);
    }

    void ProjectFace(int face)
    {
        var scratch = worker ??= new(); const int m = BacShell.ElementDofs;
        ProjectPositive(blocks.AsSpan(face * m * m, m * m), m, scratch.Eigenmatrix, scratch.Eigenvectors);
    }

    // Cyclic Jacobi eigendecomposition of the small symmetric element Hessian.
    // Negative eigenvalues are projected out; no artificial spring is added to E.
    static void ProjectPositive(Span<double> block, int n, double[] eigenmatrix, double[] eigenvectors)
    {
        block.CopyTo(eigenmatrix); Array.Clear(eigenvectors);
        for (var i = 0; i < n; ++i) eigenvectors[i * n + i] = 1;
        for (var sweep = 0; sweep < 24; ++sweep)
        {
            var changed = false;
            for (var p0 = 0; p0 < n; ++p0) for (var q0 = p0 + 1; q0 < n; ++q0)
            {
                var a = eigenmatrix[p0 * n + p0]; var b = eigenmatrix[q0 * n + q0]; var off = eigenmatrix[p0 * n + q0];
                if (Math.Abs(off) <= 1e-13 * (Math.Abs(a) + Math.Abs(b)) + 1e-30) continue;
                changed = true; var tau = (b - a) / (2 * off);
                var t = Math.CopySign(1, tau) / (Math.Abs(tau) + Math.Sqrt(1 + tau * tau));
                var c = 1 / Math.Sqrt(1 + t * t); var s = t * c;
                eigenmatrix[p0 * n + p0] = a - t * off; eigenmatrix[q0 * n + q0] = b + t * off;
                eigenmatrix[p0 * n + q0] = eigenmatrix[q0 * n + p0] = 0;
                for (var k = 0; k < n; ++k)
                {
                    if (k != p0 && k != q0)
                    {
                        var kp = eigenmatrix[k * n + p0]; var kq = eigenmatrix[k * n + q0];
                        eigenmatrix[k * n + p0] = eigenmatrix[p0 * n + k] = c * kp - s * kq;
                        eigenmatrix[k * n + q0] = eigenmatrix[q0 * n + k] = s * kp + c * kq;
                    }
                    var vp = eigenvectors[k * n + p0]; var vq = eigenvectors[k * n + q0];
                    eigenvectors[k * n + p0] = c * vp - s * vq; eigenvectors[k * n + q0] = s * vp + c * vq;
                }
            }
            if (!changed) break;
        }
        block.Clear();
        for (var k = 0; k < n; ++k)
        {
            var eigenvalue = Math.Max(0, eigenmatrix[k * n + k]);
            for (var i = 0; i < n; ++i) for (var j = 0; j < n; ++j)
                block[i * n + j] += eigenvalue * eigenvectors[i * n + k] * eigenvectors[j * n + k];
        }
    }

    void Multiply(double[] x, double[] y)
    {
        Array.Clear(y); const int m = BacShell.ElementDofs;
        var limit = MaximumProductWorkers == 0 ? Math.Max(1, MaximumWorkers) : MaximumProductWorkers;
        var workers = Math.Min(32, Math.Min(limit, shell.FaceCount / 512));
        if (workers > 1 && shell.FaceCount >= 2048)
        {
            productWork ??= new ShellParallelWork(32, MultiplyFaceBatch);
            productInput = x; productWorkers = workers;
            try { productWork.Run(workers); }
            finally { productInput = null; }
            ++ParallelMatrixProducts; PeakProductWorkers = Math.Max(PeakProductWorkers, workers);
            // Element gradients are dead after assembly. Reuse their storage for
            // products, then scatter in the same face/local-row order as serial.
            for (var f = 0; f < shell.FaceCount; ++f)
            {
                var map = shell.ElementMap(f);
                for (var i = 0; i < m; ++i)
                    if (!Fixed[map[i]]) y[map[i]] += elementGradients[f * m + i];
            }
        }
        else for (var f = 0; f < shell.FaceCount; ++f)
        {
            var map = shell.ElementMap(f); var block = blocks.AsSpan(f * m * m, m * m);
            for (var i = 0; i < m; ++i)
            {
                if (Fixed[map[i]]) continue;
                double sum = 0;
                for (var j = 0; j < m; ++j) if (!Fixed[map[j]]) sum += block[i * m + j] * x[map[j]];
                y[map[i]] += sum;
            }
        }
        for (var i = 0; i < x.Length; ++i)
            y[i] = Fixed[i] ? x[i] : y[i] + (SpringStiffness[i] * scaling[i] * scaling[i] + Math.Max(1e-30, diagonal[i] * 1e-12)) * x[i];
        for (var v = 0; v < shell.VertexCount; ++v)
            for (var i = 0; i < 3; ++i) if (!Fixed[3 * v + i])
                for (var j = 0; j < 3; ++j) if (!Fixed[3 * v + j])
                    y[3 * v + i] += contactBlocks[9 * v + 3 * i + j] * x[3 * v + j];
    }

    void MultiplyFaceBatch(int workerIndex)
    {
        const int m = BacShell.ElementDofs;
        var x = productInput!;
        var start = (int)((long)shell.FaceCount * workerIndex / productWorkers);
        var end = (int)((long)shell.FaceCount * (workerIndex + 1) / productWorkers);
        for (var f = start; f < end; ++f)
        {
            var map = shell.ElementMap(f); var block = blocks.AsSpan(f * m * m, m * m);
            for (var i = 0; i < m; ++i)
            {
                if (Fixed[map[i]]) continue;
                double sum = 0;
                for (var j = 0; j < m; ++j) if (!Fixed[map[j]]) sum += block[i * m + j] * x[map[j]];
                elementGradients[f * m + i] = sum;
            }
        }
    }

    void BuildPreconditioner()
    {
        directReady = false;
        if (direct != null)
        {
            for (var pass = 0; pass < 2; ++pass)
            {
                direct.Clear(); const int size = BacShell.ElementDofs;
                for (var f = 0; f < shell.FaceCount; ++f)
                {
                    direct.AddElement(f, blocks.AsSpan(f * size * size, size * size), Fixed);
                }
                for (var i = 0; i < gradient.Length; ++i)
                    direct.Add(i, i, Fixed[i] ? 1 : SpringStiffness[i] * scaling[i] * scaling[i] + Math.Max(1e-30, diagonal[i] * 1e-12));
                for (var v = 0; v < shell.VertexCount; ++v)
                    for (var i = 0; i < 3; ++i) if (!Fixed[3 * v + i])
                        for (var j = 0; j <= i; ++j) if (!Fixed[3 * v + j])
                            if (contactBlocks[9 * v + 3 * i + j] != 0)
                                direct.Add(3 * v + i, 3 * v + j, contactBlocks[9 * v + 3 * i + j]);
                ++FactorizationAttempts;
                if (direct.Factor(MaximumFactorWorkers == 0 ? Math.Max(1, MaximumWorkers) : MaximumFactorWorkers))
                { directReady = true; return; }
                if (pass == 0)
                {
                    var started = System.Diagnostics.Stopwatch.GetTimestamp();
                    if (Math.Min(Math.Max(1, MaximumWorkers), Math.Max(1, shell.FaceCount / 32)) > 1)
                        Parallel.For(0, shell.FaceCount, parallelOptions, projectFace);
                    else for (var f = 0; f < shell.FaceCount; ++f) ProjectFace(f);
                    ProjectionTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
                    Array.Clear(diagonal);
                    for (var f = 0; f < shell.FaceCount; ++f)
                    {
                        var block = blocks.AsSpan(f * size * size, size * size);
                        var map = shell.ElementMap(f);
                        for (var i = 0; i < size; ++i) diagonal[map[i]] += block[i * size + i];
                    }
                    for (var i = 0; i < gradient.Length; ++i) diagonal[i] += SpringStiffness[i] * scaling[i] * scaling[i];
                    for (var i = 0; i < 3 * shell.VertexCount; ++i) diagonal[i] += contactBlocks[9 * (i / 3) + 4 * (i % 3)];
                }
            }
            // A failed factorization does not authorize a state. A positive
            // diagonal preconditioner allows bounded descent/line-search work;
            // only the ordinary equilibrium criteria can accept its result.
            return;
        }
        Array.Clear(factor); const int m = BacShell.ElementDofs;
        for (var f = 0; f < shell.FaceCount; ++f)
        {
            var map = shell.ElementMap(f); var block = blocks.AsSpan(f * m * m, m * m);
            for (var i = 0; i < m; ++i) for (var j = 0; j < m; ++j)
                if (map[i] >= map[j] && !Fixed[map[i]] && !Fixed[map[j]])
                    factor[slots[((long)map[i] << 32) | (uint)map[j]]] += block[i * m + j];
        }
        for (var i = 0; i < gradient.Length; ++i)
        {
            var last = rowStarts[i + 1] - 1;
            factor[last] += Fixed[i] ? 1 : SpringStiffness[i] * scaling[i] * scaling[i] + Math.Max(1e-30, diagonal[i] * 1e-8);
        }
        for (var v = 0; v < shell.VertexCount; ++v)
            for (var i = 0; i < 3; ++i) if (!Fixed[3 * v + i])
                for (var j = 0; j <= i; ++j) if (!Fixed[3 * v + j])
                    if (contactBlocks[9 * v + 3 * i + j] != 0)
                        factor[slots[((long)(3 * v + i) << 32) | (uint)(3 * v + j)]] += contactBlocks[9 * v + 3 * i + j];
        factor.CopyTo(unfactored, 0);
        for (var attempt = 0; attempt < 10; ++attempt)
        {
            unfactored.CopyTo(factor, 0);
            var shift = attempt == 0 ? 0 : Math.Pow(10, attempt - 6);
            var valid = true;
            for (var i = 0; i < gradient.Length; ++i)
            {
                var last = rowStarts[i + 1] - 1;
                factor[last] += shift * Math.Max(diagonal[i], 1e-30);
                for (var k = rowStarts[i]; k < last; ++k)
                {
                    var j = columns[k]; var sum = factor[k]; var a = rowStarts[i]; var b = rowStarts[j];
                    while (a < k && b < rowStarts[j + 1] - 1)
                    {
                        if (columns[a] == columns[b]) { sum -= factor[a++] * factor[b++]; }
                        else if (columns[a] < columns[b]) ++a; else ++b;
                    }
                    factor[k] = sum / factor[rowStarts[j + 1] - 1];
                }
                var d = factor[last];
                for (var k = rowStarts[i]; k < last; ++k) d -= factor[k] * factor[k];
                if (!double.IsFinite(d) || d <= Math.Max(1e-30, diagonal[i] * 1e-12)) { valid = false; break; }
                factor[last] = Math.Sqrt(d);
            }
            if (valid) return;
        }
        // Shift only the preconditioner, never the physical objective. A diagonal
        // fallback is preferable to propagating a failed IC pivot into a false solve.
        Array.Clear(factor);
        for (var i = 0; i < gradient.Length; ++i)
            factor[rowStarts[i + 1] - 1] = Math.Sqrt(Math.Max(1e-30, unfactored[rowStarts[i + 1] - 1]));
    }

    void Precondition(ReadOnlySpan<double> rhs, Span<double> result)
    {
        if (directReady) { direct!.Solve(rhs, result); return; }
        if (direct != null)
        {
            for (var i = 0; i < rhs.Length; ++i)
                result[i] = rhs[i] / (Fixed[i] ? 1 : Math.Max(1e-30, diagonal[i]));
            return;
        }
        for (var i = 0; i < rhs.Length; ++i)
        {
            var sum = rhs[i]; var last = rowStarts[i + 1] - 1;
            for (var k = rowStarts[i]; k < last; ++k) sum -= factor[k] * result[columns[k]];
            result[i] = sum / factor[last];
        }
        for (var i = rhs.Length - 1; i >= 0; --i)
        {
            var last = rowStarts[i + 1] - 1; result[i] /= factor[last];
            for (var k = rowStarts[i]; k < last; ++k) result[columns[k]] -= factor[k] * result[i];
        }
    }

    bool AccurateLinearDirection()
    {
        // Recursive PCG residuals can drift on ill-conditioned shell tangents.
        // A retained factor is only an accelerator, never an approximate model:
        // certify against a fresh product with the current matrix before use.
        Multiply(direction, ap);
        var error = 0.0;
        for (var i = 0; i < ap.Length; ++i)
        { var r = ap[i] + gradient[i]; error += r * r; }
        var target = Dot(gradient, gradient) * LinearRelativeTolerance * LinearRelativeTolerance;
        return double.IsFinite(error) && error <= target && Dot(gradient, direction) < 0;
    }

    int LinearSolve(int maximumIterations = int.MaxValue)
    {
        linearBreakdown = false;
        Array.Clear(direction);
        for (var i = 0; i < residual.Length; ++i) residual[i] = -gradient[i];
        Precondition(residual, z); z.CopyTo(p, 0); var rz = Dot(residual, z);
        var target = Dot(residual, residual) * LinearRelativeTolerance * LinearRelativeTolerance;
        var iteration = 0;
        for (; iteration < Math.Min(maximumIterations, 4 * gradient.Length); ++iteration)
        {
            if (Dot(residual, residual) <= target) break;
            Multiply(p, ap); var denominator = Dot(p, ap);
            if (denominator <= 0 || !double.IsFinite(denominator + rz))
            { linearBreakdown = true; break; }
            var alpha = rz / denominator;
            for (var i = 0; i < residual.Length; ++i) { direction[i] += alpha * p[i]; residual[i] -= alpha * ap[i]; }
            Precondition(residual, z); var next = Dot(residual, z); var beta = next / rz; rz = next;
            for (var i = 0; i < residual.Length; ++i) p[i] = z[i] + beta * p[i];
        }
        return iteration;
    }

    static double Dot(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
    { double sum = 0; for (var i = 0; i < a.Length; ++i) sum += a[i] * b[i]; return sum; }
}
