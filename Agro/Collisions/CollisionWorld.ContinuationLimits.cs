using System.Numerics;

namespace Agro.Collisions;

public sealed partial class CollisionWorld
{
    struct ContinuationBendLimit
    {
        public int Joint;
        public Vector3 Normal, Response;
        public double Allowance, Compliance, Force;
        public bool Active;
    }

    // Bounded worker scratch for this continuation component, never per-leaf state.
    readonly List<ContinuationBendLimit> ContinuationBendLimits = new(8);
    double[] ContinuationLimitCoefficients = [], ContinuationRawMatrix = [], ContinuationRawDepth = [];
    public int LastContinuationActiveBendLimits { get; private set; }
    public int LastContinuationDirectSolves { get; private set; }

    bool SolveContinuationBendLimits(int rows, out double residual)
    {
        residual = double.NaN;
        LastContinuationActiveBendLimits = 0;
        ContinuationBendLimits.Clear();
        for (var j = 0; j < ContinuationJoints.Count; ++j)
        {
            var joint = ContinuationJoints[j];
            if (!joint.Plant.AG.ContactBendConstraint(joint.Index, out var normal, out var allowance)) continue;
            var tensor = ContinuationCompliance[j];
            var response = tensor.Response(normal);
            var compliance = tensor.Bilinear(normal, normal);
            if (!(compliance > 1e-20) || !double.IsFinite(compliance)) continue;
            ContinuationBendLimits.Add(new()
            { Joint = j, Normal = normal, Response = response, Allowance = allowance, Compliance = compliance });
        }
        if (ContinuationBendLimits.Count == 0) return SolveContinuationContacts(rows, out residual);
        if (ContinuationRawMatrix.Length == 0)
        {
            ContinuationRawMatrix = new double[MaximumContinuationContacts * MaximumContinuationContacts];
            ContinuationRawDepth = new double[MaximumContinuationContacts];
        }
        var required = ContinuationBendLimits.Count * MaximumContinuationContacts;
        if (ContinuationLimitCoefficients.Length < required)
            Array.Resize(ref ContinuationLimitCoefficients,
                Math.Min(MaximumContinuationJoints * MaximumContinuationContacts,
                    Math.Max(required, ContinuationLimitCoefficients.Length * 2)));
        Array.Copy(ContinuationMatrix, ContinuationRawMatrix, ContinuationMatrix.Length);
        Array.Copy(ContinuationDepth, ContinuationRawDepth, rows);
        Array.Clear(ContinuationLimitCoefficients, 0, required);
        for (var b = 0; b < ContinuationBendLimits.Count; ++b)
        {
            var bound = ContinuationBendLimits[b];
            foreach (var g in ContinuationGradients)
                if (g.Joint == bound.Joint)
                    ContinuationLimitCoefficients[b * MaximumContinuationContacts + g.Row]
                        = ContinuationCompliance[bound.Joint].Bilinear(g.Torque, bound.Normal);
        }

        // Active angular inequalities share the same elastic metric as contacts.
        // Eliminating their reactions gives C - Cn(n'C n)^-1 n'C, a symmetric
        // positive-semidefinite response. An inward aggregate load releases the
        // bound; it is not a permanent pin or a change to tissue stiffness.
        for (var update = 0; update < 8; ++update)
        {
            Array.Copy(ContinuationRawMatrix, ContinuationMatrix, ContinuationMatrix.Length);
            Array.Copy(ContinuationRawDepth, ContinuationDepth, rows);
            Array.Clear(ContinuationForces);
            LastContinuationActiveBendLimits = 0;
            for (var b = 0; b < ContinuationBendLimits.Count; ++b)
            {
                var bound = ContinuationBendLimits[b];
                if (!bound.Active) continue;
                ++LastContinuationActiveBendLimits;
                var offset = b * MaximumContinuationContacts;
                for (var row = 0; row < rows; ++row)
                {
                    var gain = ContinuationLimitCoefficients[offset + row] / bound.Compliance;
                    ContinuationDepth[row] -= gain * bound.Allowance;
                    for (var column = 0; column < rows; ++column)
                        ContinuationMatrix[row * MaximumContinuationContacts + column]
                            -= gain * ContinuationLimitCoefficients[offset + column];
                }
            }
            for (var row = 0; row < rows; ++row)
            {
                var diagonal = row * MaximumContinuationContacts + row;
                if (ContinuationMatrix[diagonal] <= ContinuationRawMatrix[diagonal] * 1e-12)
                {
                    // A direction removed by the bound can leave subtraction
                    // noise. Never turn that noise into enormous reaction forces.
                    if (ContinuationEnabled[row] && ContinuationDepth[row] > 0) return false;
                    ContinuationMatrix[diagonal] = 0;
                }
            }
            if (!SolveContinuationContacts(rows, out residual) || residual > 1e-8)
            {
                if (TraceContacts) Console.WriteLine($"CONTINUATION_LIMIT tangentBudget residualMetres={residual:R} active={LastContinuationActiveBendLimits} rows={rows}");
                return false;
            }
            var add = -1; var release = -1;
            var outward = 1e-8; var inward = -1e-8; // angular residuals, radians
            for (var b = 0; b < ContinuationBendLimits.Count; ++b)
            {
                var bound = ContinuationBendLimits[b];
                var excess = -bound.Allowance;
                for (var row = 0; row < rows; ++row)
                    excess += ContinuationLimitCoefficients[b * MaximumContinuationContacts + row] * ContinuationForces[row];
                if (!double.IsFinite(excess)) return false;
                bound.Force = bound.Active ? excess / bound.Compliance : 0;
                ContinuationBendLimits[b] = bound;
                if (bound.Active && excess < inward) { inward = excess; release = b; }
                if (!bound.Active && excess > outward) { outward = excess; add = b; }
            }
            var change = release >= 0 ? release : add;
            if (change < 0) return true;
            var changed = ContinuationBendLimits[change];
            changed.Active = release < 0;
            ContinuationBendLimits[change] = changed;
        }
        // This is an exhausted tangent work budget, not physical confinement.
        return false;
    }

    bool SolveContinuationContacts(int rows, out double residual, bool allowDirect = true)
    {
        residual = double.NaN;
        for (var row = 0; row < rows; ++row)
            if (ContinuationEnabled[row])
            {
                var diagonal = ContinuationMatrix[row * MaximumContinuationContacts + row];
                if (!double.IsFinite(diagonal) || diagonal < 0 || diagonal <= 1e-20 && ContinuationDepth[row] > 0) return false;
            }
        for (var sweep = 0; sweep < 64; ++sweep)
        {
            var maximumChange = 0d;
            for (var row = 0; row < rows; ++row)
            {
                if (!ContinuationEnabled[row]) continue;
                var predicted = 0d;
                for (var column = 0; column < rows; ++column)
                    if (ContinuationEnabled[column])
                        predicted += ContinuationMatrix[row * MaximumContinuationContacts + column] * ContinuationForces[column];
                var diagonal = ContinuationMatrix[row * MaximumContinuationContacts + row];
                if (diagonal <= 1e-20) continue;
                var force = Math.Max(0, ContinuationForces[row] + (ContinuationDepth[row] - predicted) / diagonal);
                if (!double.IsFinite(force) || force > float.MaxValue) return false;
                maximumChange = Math.Max(maximumChange, Math.Abs(force - ContinuationForces[row]) * Math.Sqrt(diagonal));
                ContinuationForces[row] = force;
            }
            if (maximumChange < 1e-8) break;
        }
        residual = ContinuationContactResidual(rows);
        if (!double.IsFinite(residual)) return false;
        if (!allowDirect) return true;
        if (residual <= 1e-8) return true;
        // Small, strongly coupled sets can converge slowly under coordinate
        // relaxation. A diagonally scaled direct working-set solve resolves
        // them without relaxing material or clearance constraints.
        return SolveContinuationWorkingSet(rows, out residual);
    }

    double ContinuationContactResidual(int rows)
    {
        var residual = 0d;
        for (var row = 0; row < rows; ++row)
        {
            if (!ContinuationEnabled[row]) continue;
            var gap = -ContinuationDepth[row];
            for (var column = 0; column < rows; ++column)
                if (ContinuationEnabled[column])
                    gap += ContinuationMatrix[row * MaximumContinuationContacts + column] * ContinuationForces[column];
            residual = Math.Max(residual, ContinuationForces[row] > 0 ? Math.Abs(gap) : Math.Max(0, -gap));
        }
        return residual;
    }

    bool SolveContinuationWorkingSet(int rows, out double residual)
    {
        ++LastContinuationDirectSolves;
        Span<bool> active = stackalloc bool[MaximumContinuationContacts];
        Span<int> selected = stackalloc int[MaximumContinuationContacts];
        Span<double> candidate = stackalloc double[MaximumContinuationContacts];
        Span<double> diagonal = stackalloc double[MaximumContinuationContacts];
        Span<double> augmented = stackalloc double[MaximumContinuationContacts * (MaximumContinuationContacts + 1)];
        active.Clear();
        Array.Clear(ContinuationForces);
        for (var row = 0; row < rows; ++row)
            diagonal[row] = Math.Sqrt(Math.Max(0, ContinuationMatrix[row * MaximumContinuationContacts + row]));
        residual = double.NaN;
        for (var iteration = 0; iteration < 64; ++iteration)
        {
            var enter = -1; var maximum = 0d;
            for (var row = 0; row < rows; ++row)
            {
                if (active[row] || !ContinuationEnabled[row] || diagonal[row] == 0) continue;
                var deficit = ContinuationDepth[row];
                for (var column = 0; column < rows; ++column)
                    deficit -= ContinuationMatrix[row * MaximumContinuationContacts + column] * ContinuationForces[column];
                if (deficit > 1e-8 && deficit / diagonal[row] > maximum)
                { maximum = deficit / diagonal[row]; enter = row; }
            }
            if (enter < 0)
            {
                residual = ContinuationContactResidual(rows);
                return double.IsFinite(residual) && residual <= 1e-8;
            }
            active[enter] = true;
            var resolved = false;
            for (var inner = 0; inner < 2 * rows; ++inner)
            {
                var count = 0;
                for (var row = 0; row < rows; ++row) if (active[row]) selected[count++] = row;
                var stride = count + 1;
                for (var x = 0; x < count; ++x)
                {
                    var row = selected[x];
                    for (var y = 0; y < count; ++y)
                    {
                        var column = selected[y];
                        augmented[x * stride + y] = ContinuationMatrix[row * MaximumContinuationContacts + column]
                            / (diagonal[row] * diagonal[column]);
                    }
                    augmented[x * stride + count] = ContinuationDepth[row] / diagonal[row];
                }
                for (var x = 0; x < count; ++x)
                {
                    var pivot = x;
                    for (var y = x + 1; y < count; ++y)
                        if (Math.Abs(augmented[y * stride + x]) > Math.Abs(augmented[pivot * stride + x])) pivot = y;
                    // Numerical rank deficiency is unresolved, never repaired by
                    // adding fictitious compliance or loosening a contact.
                    if (!(Math.Abs(augmented[pivot * stride + x]) > 1e-12)) return false;
                    if (pivot != x)
                        for (var y = x; y <= count; ++y)
                            (augmented[x * stride + y], augmented[pivot * stride + y])
                                = (augmented[pivot * stride + y], augmented[x * stride + y]);
                    var divisor = augmented[x * stride + x];
                    for (var y = x; y <= count; ++y) augmented[x * stride + y] /= divisor;
                    for (var z = 0; z < count; ++z)
                        if (z != x)
                        {
                            var factor = augmented[z * stride + x];
                            for (var y = x; y <= count; ++y) augmented[z * stride + y] -= factor * augmented[x * stride + y];
                        }
                }
                candidate.Clear();
                var alpha = 1d; var leave = -1;
                for (var x = 0; x < count; ++x)
                {
                    var row = selected[x];
                    var force = augmented[x * stride + count] / diagonal[row];
                    if (!double.IsFinite(force) || Math.Abs(force) > float.MaxValue) return false;
                    candidate[row] = force;
                    if (force > 0) continue;
                    var previous = ContinuationForces[row];
                    var fraction = previous > force ? previous / (previous - force) : 0;
                    if (fraction <= alpha) { alpha = fraction; leave = row; }
                }
                if (leave < 0)
                {
                    for (var row = 0; row < rows; ++row) ContinuationForces[row] = candidate[row];
                    resolved = true; break;
                }
                for (var row = 0; row < rows; ++row)
                    ContinuationForces[row] = Math.Max(0, ContinuationForces[row] + alpha * (candidate[row] - ContinuationForces[row]));
                ContinuationForces[leave] = 0; active[leave] = false;
            }
            if (!resolved) return false;
        }
        return false;
    }

    void AccumulateContinuationBendReactions(Vector3[] turns)
    {
        foreach (var bound in ContinuationBendLimits)
            if (bound.Active) turns[bound.Joint] -= bound.Response * (float)bound.Force;
    }

    void QueueContinuationBendReactions(float scale)
    {
        foreach (var bound in ContinuationBendLimits)
            if (bound.Active && bound.Force != 0)
            {
                var joint = ContinuationJoints[bound.Joint];
                joint.Plant.AG.QueueCoupledContactResponse(joint.Index,
                    -bound.Response, Vector4.Zero, (float)bound.Force * scale);
            }
    }

}
