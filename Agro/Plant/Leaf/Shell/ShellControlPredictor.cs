namespace Agro.Mechanics;

/// <summary>
/// Immutable sparse displacement interpolation for a shell's initial guess.
/// Rows are supplied by profile preprocessing (for example affine-augmented RBF
/// interpolation). This is NOT a reduced constitutive model or a decoder: all
/// unretained coordinates/directors must still relax, and the resulting complete
/// state must pass equilibrium, strain, contact and continuation checks.
/// </summary>
public sealed class ShellControlPredictor
{
    readonly ShellVector[] rest;
    readonly int[] controls, starts, indices;
    readonly double[] weights;
    public int VertexCount => rest.Length;
    public int ControlCount => controls.Length;
    public ReadOnlySpan<int> ControlVertices => controls;
    public ReadOnlySpan<ShellVector> RestPositions => rest;
    /// <summary>Shared immutable arrays only; excludes headers and preprocessing scratch.</summary>
    public long BindingPayloadBytes { get; }

    /// <param name="restPositions">Current biological rest geometry in local coordinates.</param>
    /// <param name="controlVertices">Distinct retained vertex IDs in saved-coordinate order.</param>
    /// <param name="rowStarts">CSR row offsets, one row per fine vertex.</param>
    /// <param name="controlIndices">Strictly increasing control-slot indices within each row.</param>
    /// <param name="rowWeights">Scalar displacement weights, shared by X, Y and Z.</param>
    /// <param name="maximumBindingPayloadBytes">Explicit shared-profile array budget.</param>
    public ShellControlPredictor(ReadOnlySpan<ShellVector> restPositions, ReadOnlySpan<int> controlVertices,
        ReadOnlySpan<int> rowStarts, ReadOnlySpan<int> controlIndices, ReadOnlySpan<double> rowWeights,
        long maximumBindingPayloadBytes)
    {
        if (restPositions.IsEmpty || controlVertices.IsEmpty || controlVertices.Length > restPositions.Length
            || rowStarts.Length != restPositions.Length + 1 || controlIndices.Length != rowWeights.Length
            || rowStarts[0] != 0 || rowStarts[^1] != rowWeights.Length)
            throw new ArgumentException("Incorrect control predictor dimensions.");
        var bytes = checked(24L * restPositions.Length + sizeof(int) * ((long)controlVertices.Length
            + rowStarts.Length + controlIndices.Length) + sizeof(double) * (long)rowWeights.Length);
        if (maximumBindingPayloadBytes < bytes)
            throw new ArgumentException("Predictor exceeds the explicit shared-profile payload budget.", nameof(maximumBindingPayloadBytes));
        var origin = restPositions[0]; var scale = 0.0;
        foreach (var p in restPositions)
        {
            if (!Finite(p)) throw new ArgumentException("Nonfinite predictor rest position.");
            scale = Math.Max(scale, (p - origin).Norm);
        }
        if (!(scale > 0) || !double.IsFinite(scale)) throw new ArgumentException("Collapsed predictor rest geometry.");
        for (var i = 0; i < restPositions.Length; ++i)
            if (rowStarts[i] < 0 || rowStarts[i + 1] <= rowStarts[i] || rowStarts[i + 1] > rowWeights.Length)
                throw new ArgumentException("Empty or invalid predictor row.");
        var seen = new bool[restPositions.Length];
        for (var slot = 0; slot < controlVertices.Length; ++slot)
        {
            var vertex = controlVertices[slot];
            if ((uint)vertex >= (uint)restPositions.Length || seen[vertex])
                throw new ArgumentException("Invalid or duplicate control vertex.");
            seen[vertex] = true;
            var begin = rowStarts[vertex];
            if (rowStarts[vertex + 1] != begin + 1 || controlIndices[begin] != slot || rowWeights[begin] != 1)
                throw new ArgumentException("A retained vertex must have an exact identity row.");
        }
        for (var vertex = 0; vertex < restPositions.Length; ++vertex)
        {
            var sum = 0.0; var moment = new ShellVector(); var previous = -1;
            for (var entry = rowStarts[vertex]; entry < rowStarts[vertex + 1]; ++entry)
            {
                var index = controlIndices[entry]; var weight = rowWeights[entry];
                if (index <= previous || (uint)index >= (uint)controlVertices.Length || !double.IsFinite(weight))
                    throw new ArgumentException("Predictor row indices/weights are invalid.");
                previous = index; sum += weight;
                moment += (restPositions[controlVertices[index]] - origin) * weight;
            }
            // Reproduce affine displacements, including rigid motion of curved
            // surfaces. Rest preservation alone would not guarantee this.
            if (!double.IsFinite(sum) || Math.Abs(sum - 1) > 1e-10 || !Finite(moment)
                || (moment - (restPositions[vertex] - origin)).Norm > 1e-10 * scale)
                throw new ArgumentException("Predictor row fails affine precision.");
        }
        rest = restPositions.ToArray(); controls = controlVertices.ToArray(); starts = rowStarts.ToArray();
        indices = controlIndices.ToArray(); weights = rowWeights.ToArray(); BindingPayloadBytes = bytes;
    }

    /// <summary>Small reusable worker scratch; never persistent per-organ state.</summary>
    public sealed class Workspace
    {
        internal readonly ShellControlPredictor Owner;
        internal readonly double[] Displacements;
        public long ArrayPayloadBytes => (long)Displacements.Length * sizeof(double);
        public Workspace(ShellControlPredictor owner)
        {
            ArgumentNullException.ThrowIfNull(owner); Owner = owner;
            Displacements = new double[checked(3 * owner.ControlCount)];
        }
    }

    /// <summary>
    /// Predict absolute local positions from saved absolute control positions.
    /// Retained vertices are copied bit-for-bit. Output must be worker scratch:
    /// true only means finite interpolation, NOT an admissible simulation state.
    /// False clears the complete output. Inputs/output may not alias.
    /// </summary>
    public bool TryPredict(ReadOnlySpan<double> controlPositions, Span<double> positions, Workspace w)
    {
        if (w.Owner != this || controlPositions.Length != 3 * ControlCount || positions.Length != 3 * VertexCount
            || controlPositions.Overlaps(positions)) throw new ArgumentException("Incorrect or aliased predictor buffers.");
        positions.Clear();
        for (var j = 0; j < controls.Length; ++j)
        {
            var p = rest[controls[j]];
            var dx = controlPositions[3 * j] - p.X;
            var dy = controlPositions[3 * j + 1] - p.Y;
            var dz = controlPositions[3 * j + 2] - p.Z;
            if (!double.IsFinite(dx) || !double.IsFinite(dy) || !double.IsFinite(dz)) return false;
            w.Displacements[3 * j] = dx; w.Displacements[3 * j + 1] = dy; w.Displacements[3 * j + 2] = dz;
        }
        for (var vertex = 0; vertex < rest.Length; ++vertex)
        {
            var x = rest[vertex].X; var y = rest[vertex].Y; var z = rest[vertex].Z;
            for (var entry = starts[vertex]; entry < starts[vertex + 1]; ++entry)
            {
                var j = 3 * indices[entry]; var weight = weights[entry];
                x += weight * w.Displacements[j]; y += weight * w.Displacements[j + 1]; z += weight * w.Displacements[j + 2];
            }
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            { positions.Clear(); return false; }
            positions[3 * vertex] = x; positions[3 * vertex + 1] = y; positions[3 * vertex + 2] = z;
        }
        // Avoid losing low bits in rest + (saved - rest), including after large
        // local displacements. These coordinates are prescribed during relaxation.
        for (var j = 0; j < controls.Length; ++j)
            controlPositions.Slice(3 * j, 3).CopyTo(positions.Slice(3 * controls[j], 3));
        return true;
    }

    static bool Finite(ShellVector p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
}
