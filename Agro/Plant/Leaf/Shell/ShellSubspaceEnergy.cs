using System.Numerics;

namespace Agro.Mechanics;

/// <summary>
/// Exact BAC energy restricted to q = qRest + U z. The immutable row-major basis
/// includes positions AND edge directors. It is supplied by offline mechanics,
/// not inferred from display geometry. No cubature or material approximation is
/// applied here: all fine elements and their strain barriers are evaluated.
/// A useful subspace and converged reduced equations do not certify full force
/// balance, large folds, contact, storage or production performance.
/// </summary>
public sealed class ShellSubspaceEnergy
{
    readonly BacShell shell;
    readonly double[] basis, rest;
    const int TangentEntries = BacShell.ElementDofs * (BacShell.ElementDofs + 1) / 2;
    public int DofCount { get; }
    public int FineDofCount => shell.DofCount;
    public long BasisPayloadBytes => (long)basis.Length * sizeof(double);
    public int CoefficientPayloadBytes => checked(DofCount * sizeof(double));
    /// <summary>Optional worker cache payload; never charged to or retained by an organ.</summary>
    public long TangentCachePayloadBytes => checked((long)shell.FaceCount * TangentEntries * sizeof(double));

    public ShellSubspaceEnergy(BacShell shell, int coordinates, ReadOnlySpan<double> rowMajorBasis)
    {
        ArgumentNullException.ThrowIfNull(shell);
        if (coordinates <= 0 || coordinates > shell.DofCount
            || rowMajorBasis.Length != checked(shell.DofCount * coordinates))
            throw new ArgumentException("Incorrect shell subspace dimensions.");
        foreach (var value in rowMajorBasis)
            if (!double.IsFinite(value)) throw new ArgumentException("Nonfinite subspace basis.");
        // Dependence/conditioning and force accuracy are preprocessing gates,
        // not something this evaluation kernel can establish from column norms.
        for (var j = 0; j < coordinates; ++j)
        {
            var nonzero = false;
            for (var i = 0; i < shell.DofCount; ++i) nonzero |= rowMajorBasis[i * coordinates + j] != 0;
            if (!nonzero) throw new ArgumentException("Empty subspace coordinate.");
        }
        this.shell = shell; DofCount = coordinates;
        basis = rowMajorBasis.ToArray(); rest = shell.CreateRestState();
    }

    /// <summary>Fine arrays live on bounded workers, never implicitly on organs.</summary>
    public sealed class Workspace
    {
        internal readonly ShellSubspaceEnergy Owner;
        internal readonly BacShell.Workspace Element = new();
        internal readonly double[] Fine, Gradient, Direction, Product;
        internal readonly double[] LocalDirection = new double[BacShell.ElementDofs];
        internal readonly double[] LocalProduct = new double[BacShell.ElementDofs];
        internal readonly double[] ElementValues, ElementVectors;
        internal readonly double[] TangentBlocks;
        internal readonly ElementWorker[] Workers;
        internal readonly ParallelOptions ParallelOptions;
        internal readonly Action<int> RunWorker;
        internal byte EvaluationKind;
        internal bool Prepared;
        internal bool TangentsReady;
        public int WorkerCount => Workers.Length;
        public long CachedTangentPayloadBytes => (long)TangentBlocks.Length * sizeof(double);
        /// <summary>
        /// Zero keeps direct directional differentiation. A positive budget opts
        /// into lazy exact element tangents and must cover the entire payload.
        /// This bounds cache arrays only, in addition to normal worker scratch.
        /// Allocate on bounded solver workers, never per leaf or contact.
        /// </summary>
        public Workspace(ShellSubspaceEnergy owner, int maximumWorkers = 1, long tangentCacheBudgetBytes = 0)
        {
            ArgumentNullException.ThrowIfNull(owner); Owner = owner;
            if (maximumWorkers < 1) throw new ArgumentOutOfRangeException(nameof(maximumWorkers));
            if (tangentCacheBudgetBytes < 0) throw new ArgumentOutOfRangeException(nameof(tangentCacheBudgetBytes));
            if (tangentCacheBudgetBytes > 0 && tangentCacheBudgetBytes < owner.TangentCachePayloadBytes)
                throw new ArgumentException("Tangent cache exceeds the explicit worker payload budget.", nameof(tangentCacheBudgetBytes));
            TangentBlocks = tangentCacheBudgetBytes == 0 ? []
                : new double[checked((int)(owner.TangentCachePayloadBytes / sizeof(double)))];
            Fine = new double[owner.FineDofCount]; Gradient = new double[owner.FineDofCount];
            Direction = new double[owner.FineDofCount]; Product = new double[owner.FineDofCount];
            var count = Math.Min(maximumWorkers, Math.Max(1, owner.shell.FaceCount / 256));
            Workers = new ElementWorker[count];
            for (var i = 0; i < count; ++i) Workers[i] = new();
            ElementValues = count > 1 ? new double[owner.shell.FaceCount] : [];
            ElementVectors = count > 1 ? new double[owner.shell.FaceCount * BacShell.ElementDofs] : [];
            ParallelOptions = new() { MaxDegreeOfParallelism = count };
            RunWorker = EvaluateBatch;
        }

        void EvaluateBatch(int worker)
        {
            var s = Owner.shell; var scratch = Workers[worker];
            Span<double> denseTangent = stackalloc double[EvaluationKind == 3 ? BacShell.ElementDofs * BacShell.ElementDofs : 0];
            var begin = s.FaceCount * (long)worker / Workers.Length;
            var end = s.FaceCount * (long)(worker + 1) / Workers.Length;
            for (var f = (int)begin; f < end; ++f)
            {
                if (EvaluationKind == 3)
                {
                    ElementValues[f] = s.EvaluateElement(f, Fine, [], denseTangent, scratch.Element);
                    PackTangent(denseTangent, TangentBlocks.AsSpan(f * TangentEntries, TangentEntries));
                    continue;
                }
                var output = ElementVectors.AsSpan(f * BacShell.ElementDofs, BacShell.ElementDofs);
                if (EvaluationKind == 2)
                {
                    var map = s.ElementMap(f);
                    for (var j = 0; j < map.Length; ++j) scratch.Direction[j] = Direction[map[j]];
                    if (TangentsReady)
                    {
                        MultiplyElement(TangentBlocks.AsSpan(f * TangentEntries, TangentEntries), scratch.Direction, output);
                        ElementValues[f] = 0;
                    }
                    else ElementValues[f] = s.EvaluateElementProduct(f, Fine, scratch.Direction, [], output, scratch.Element);
                }
                else ElementValues[f] = s.EvaluateElement(f, Fine, EvaluationKind == 1 ? output : [], [], scratch.Element);
            }
        }
    }

    internal sealed class ElementWorker
    {
        internal readonly BacShell.Workspace Element = new();
        internal readonly double[] Direction = new double[BacShell.ElementDofs];
    }

    // Parallel work writes independent face slots. All scatters/sums retain the
    // same face order as the serial path, independent of scheduling and worker count.
    // TPL dispatch may allocate per batch; no task is created per face/contact.
    double EvaluateFine(Workspace w, bool gradient)
    {
        if (w.WorkerCount == 1) return shell.Energy(w.Fine, gradient ? w.Gradient : [], w.Element);
        w.EvaluationKind = gradient ? (byte)1 : (byte)0;
        Parallel.For(0, w.WorkerCount, w.ParallelOptions, w.RunWorker);
        if (gradient) Array.Clear(w.Gradient);
        var energy = 0.0;
        for (var f = 0; f < shell.FaceCount; ++f)
        {
            if (!double.IsFinite(w.ElementValues[f])) return double.PositiveInfinity;
            energy += w.ElementValues[f];
            if (!gradient) continue;
            var map = shell.ElementMap(f);
            for (var j = 0; j < map.Length; ++j) w.Gradient[map[j]] += w.ElementVectors[f * BacShell.ElementDofs + j];
        }
        return energy;
    }

    /// <summary>
    /// Reconstruct the fine state, including directors. This is an endpoint map,
    /// not a strain/contact certificate. False clears the complete output.
    /// </summary>
    public bool TryExpand(ReadOnlySpan<double> coordinates, Span<double> state)
    {
        if (coordinates.Length != DofCount || state.Length != FineDofCount || coordinates.Overlaps(state))
            throw new ArgumentException("Incorrect or aliased subspace state.");
        state.Clear();
        if (!Finite(coordinates)) return false;
        Multiply(coordinates, state);
        for (var i = 0; i < state.Length; ++i) state[i] += rest[i];
        if (Finite(state)) return true;
        state.Clear(); return false;
    }

    bool Prepare(ReadOnlySpan<double> coordinates, ReadOnlySpan<double> forces, Workspace w)
    {
        if (w.Owner == this) { w.Prepared = false; w.TangentsReady = false; }
        if (w.Owner != this || (!forces.IsEmpty && forces.Length != FineDofCount))
            throw new ArgumentException("Incorrect subspace workspace or load.");
        return TryExpand(coordinates, w.Fine) && Finite(forces);
    }

    /// <summary>Energy trial; invalidates prepared products even if rejected.</summary>
    public double Energy(ReadOnlySpan<double> coordinates, ReadOnlySpan<double> forces, Workspace w)
    {
        if (!Prepare(coordinates, forces, w)) return double.PositiveInfinity;
        var energy = EvaluateFine(w, false);
        for (var i = 0; i < forces.Length; ++i) energy -= forces[i] * w.Fine[i];
        return double.IsFinite(energy) ? energy : double.PositiveInfinity;
    }

    /// <summary>
    /// Exact restricted energy and gradient, with fixed conservative fine loads.
    /// Reduced gradient units depend on U; do not report its norm as newtons.
    /// Empty gradient still prepares a Hessian product and physical residuals.
    /// </summary>
    public double Evaluate(ReadOnlySpan<double> coordinates, ReadOnlySpan<double> forces,
        Span<double> gradient, Workspace w)
    {
        if (w.Owner == this) { w.Prepared = false; w.TangentsReady = false; }
        if ((!gradient.IsEmpty && gradient.Length != DofCount) || gradient.Overlaps(coordinates) || gradient.Overlaps(forces))
            throw new ArgumentException("Incorrect or aliased subspace gradient.");
        gradient.Clear();
        if (!Prepare(coordinates, forces, w)) return double.PositiveInfinity;
        var energy = EvaluateFine(w, true);
        if (!double.IsFinite(energy)) return double.PositiveInfinity;
        for (var i = 0; i < forces.Length; ++i)
        { energy -= forces[i] * w.Fine[i]; w.Gradient[i] -= forces[i]; }
        if (!gradient.IsEmpty) Project(w.Gradient, gradient);
        if (!double.IsFinite(energy) || !Finite(w.Gradient) || !Finite(gradient))
        { gradient.Clear(); return double.PositiveInfinity; }
        w.Prepared = true; return energy;
    }

    /// <summary>Exact U^T H U product at the snapshotted state; no assembled global Hessian.</summary>
    public bool TryMultiplyHessian(ReadOnlySpan<double> direction, Span<double> product, Workspace w)
    {
        if (w.Owner != this || direction.Length != DofCount || product.Length != DofCount || direction.Overlaps(product))
            throw new ArgumentException("Incorrect or aliased subspace product.");
        product.Clear();
        if (!w.Prepared || !Finite(direction)) return false;
        Multiply(direction, w.Direction);
        if (!MultiplyFineHessian(w)) return false;
        Project(w.Product, product);
        if (Finite(product)) return true;
        product.Clear(); return false;
    }

    /// <summary>
    /// Full fine-coordinate tangent at the prepared state, before basis
    /// projection or clamp elimination. This exposes missing directions for
    /// residual correction and independent reference checks. Optional element
    /// tangents use bounded worker storage, never persistent organ state.
    /// False clears the supplied output.
    /// </summary>
    public bool TryMultiplyFineHessian(ReadOnlySpan<double> direction, Span<double> product, Workspace w)
    {
        if (w.Owner != this || direction.Length != FineDofCount || product.Length != FineDofCount || direction.Overlaps(product))
            throw new ArgumentException("Incorrect or aliased fine product.");
        product.Clear();
        if (!w.Prepared || !Finite(direction)) return false;
        direction.CopyTo(w.Direction);
        if (!MultiplyFineHessian(w)) return false;
        w.Product.CopyTo(product); return true;
    }

    /// <summary>
    /// Copy the complete prepared residual, including fixed-coordinate reactions.
    /// Position entries are N and director entries N m. Consumers must apply
    /// explicit boundary masks, and may not reinterpret reduced convergence as
    /// full equilibrium. False clears output; the prepared state is unchanged.
    /// </summary>
    public bool TryCopyFineResidual(Span<double> residual, Workspace w)
    {
        if (w.Owner != this || residual.Length != FineDofCount)
            throw new ArgumentException("Incorrect fine residual output.");
        residual.Clear();
        if (!w.Prepared) return false;
        w.Gradient.CopyTo(residual); return true;
    }

    bool MultiplyFineHessian(Workspace w)
    {
        Array.Clear(w.Product);
        if (!Finite(w.Direction)) return false;
        if (w.TangentBlocks.Length > 0 && !EnsureTangents(w)) return false;
        if (w.WorkerCount > 1)
        {
            w.EvaluationKind = 2;
            Parallel.For(0, w.WorkerCount, w.ParallelOptions, w.RunWorker);
        }
        for (var face = 0; face < shell.FaceCount; ++face)
        {
            var map = shell.ElementMap(face);
            ReadOnlySpan<double> localProduct;
            if (w.WorkerCount == 1)
            {
                for (var j = 0; j < map.Length; ++j) w.LocalDirection[j] = w.Direction[map[j]];
                if (w.TangentsReady)
                    MultiplyElement(w.TangentBlocks.AsSpan(face * TangentEntries, TangentEntries), w.LocalDirection, w.LocalProduct);
                else if (!double.IsFinite(shell.EvaluateElementProduct(face, w.Fine, w.LocalDirection, [], w.LocalProduct, w.Element)))
                    return false;
                localProduct = w.LocalProduct;
            }
            else
            {
                if (!double.IsFinite(w.ElementValues[face])) return false;
                localProduct = w.ElementVectors.AsSpan(face * BacShell.ElementDofs, BacShell.ElementDofs);
            }
            for (var j = 0; j < map.Length; ++j) w.Product[map[j]] += localProduct[j];
        }
        return Finite(w.Product);
    }

    bool EnsureTangents(Workspace w)
    {
        if (w.TangentsReady) return true;
        if (w.WorkerCount > 1)
        {
            w.EvaluationKind = 3;
            Parallel.For(0, w.WorkerCount, w.ParallelOptions, w.RunWorker);
            if (!Finite(w.ElementValues)) return false;
        }
        else
        {
            Span<double> dense = stackalloc double[BacShell.ElementDofs * BacShell.ElementDofs];
            for (var face = 0; face < shell.FaceCount; ++face)
            {
                if (!double.IsFinite(shell.EvaluateElement(face, w.Fine, [],
                    dense, w.Element))) return false;
                PackTangent(dense, w.TangentBlocks.AsSpan(face * TangentEntries, TangentEntries));
            }
        }
        w.TangentsReady = Finite(w.TangentBlocks);
        return w.TangentsReady;
    }

    static void MultiplyElement(ReadOnlySpan<double> block, ReadOnlySpan<double> direction, Span<double> product)
    {
        const int m = BacShell.ElementDofs;
        product.Clear(); var entry = 0;
        for (var i = 0; i < m; ++i)
        {
            // Each output still accumulates columns in ascending order.
            for (var j = 0; j < i; ++j)
            {
                var value = block[entry++];
                product[i] += value * direction[j]; product[j] += value * direction[i];
            }
            product[i] += block[entry++] * direction[i];
        }
    }

    static void PackTangent(ReadOnlySpan<double> dense, Span<double> packed)
    {
        const int m = BacShell.ElementDofs; var entry = 0;
        for (var i = 0; i < m; ++i)
        {
            // Analytic Hessians are symmetric; average roundoff in the two
            // directional evaluations. No eigenvalue clipping or regularization.
            for (var j = 0; j < i; ++j) packed[entry++] = .5 * dense[i * m + j] + .5 * dense[j * m + i];
            packed[entry++] = dense[i * m + i];
        }
    }

    /// <summary>
    /// Maximum absolute fine positional residual (N) and director torque (N m),
    /// excluding explicitly fixed coordinates. Empty masks mean all free. This
    /// diagnostic must accompany reduced convergence; no acceptance is inferred.
    /// </summary>
    public bool TryMeasureResidual(ReadOnlySpan<bool> fixedDofs, out double forceNewtons,
        out double torqueNewtonMetres, Workspace w)
    {
        if (w.Owner != this || (!fixedDofs.IsEmpty && fixedDofs.Length != FineDofCount))
            throw new ArgumentException("Incorrect residual workspace or mask.");
        forceNewtons = torqueNewtonMetres = 0;
        if (!w.Prepared) return false;
        for (var i = 0; i < FineDofCount; ++i)
        {
            if (!fixedDofs.IsEmpty && fixedDofs[i]) continue;
            if (i < 3 * shell.VertexCount) forceNewtons = Math.Max(forceNewtons, Math.Abs(w.Gradient[i]));
            else torqueNewtonMetres = Math.Max(torqueNewtonMetres, Math.Abs(w.Gradient[i]));
        }
        return true;
    }

    /// <summary>
    /// Certify surface strain over the complete affine subspace segment. Does not
    /// certify self/solid contact, director admissibility or curved support motion.
    /// Product scratch is reused; the prepared state/loads remain unchanged.
    /// </summary>
    public bool TryCertifyLinearStrainPath(ReadOnlySpan<double> from, ReadOnlySpan<double> to, Workspace w)
    {
        if (w.Owner != this) throw new ArgumentException("Incorrect path workspace.");
        return TryExpand(from, w.Direction) && TryExpand(to, w.Product)
            && shell.TryCertifyLinearStrainPath(w.Direction, w.Product);
    }

    void Multiply(ReadOnlySpan<double> coordinates, Span<double> fine)
    {
        for (var i = 0; i < FineDofCount; ++i)
        {
            var row = basis.AsSpan(i * DofCount, DofCount); var value = 0.0;
            for (var j = 0; j < DofCount; ++j) value += row[j] * coordinates[j];
            fine[i] = value;
        }
    }

    void Project(ReadOnlySpan<double> fine, Span<double> reduced)
    {
        reduced.Clear();
        for (var i = 0; i < FineDofCount; ++i)
        {
            var row = basis.AsSpan(i * DofCount, DofCount); var value = fine[i];
            var j = 0;
            if (Vector.IsHardwareAccelerated)
            {
                // Lanes are independent reduced coordinates. The fine-row
                // summation order is unchanged; no horizontal reduction.
                var scale = new Vector<double>(value);
                for (; j <= DofCount - Vector<double>.Count; j += Vector<double>.Count)
                {
                    var sum = new Vector<double>(reduced.Slice(j)) + new Vector<double>(row.Slice(j)) * scale;
                    sum.CopyTo(reduced.Slice(j));
                }
            }
            for (; j < DofCount; ++j) reduced[j] += row[j] * value;
        }
    }

    static bool Finite(ReadOnlySpan<double> values)
    { foreach (var value in values) if (!double.IsFinite(value)) return false; return true; }
}
