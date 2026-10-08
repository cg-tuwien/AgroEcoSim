using S = Agro.Mechanics.ShellDifferentiation.Scalar;
using V = Agro.Mechanics.ShellDifferentiation.Vector;

namespace Agro.Mechanics;

/// <summary>An explicit material correspondence to one coarse triangle; weights are (1-B-C,B,C).</summary>
public readonly record struct ShellSurfaceSample(int Face, double B, double C, ShellVector RestPosition);

/// <summary>
/// Rest-shape-preserving position transfer using the normal/polar offset of
/// Progressive Shell Quasistatics (Zhang et al. 2023, equations 4-7).
/// Correspondences are supplied by preprocessing; this does not construct the
/// paper's intrinsic decimation map, transfer BAC directors, or certify contact
/// or strain. Fine-surface energy and safe continuation remain separate tasks.
/// Immutable bindings are shared per profile. There is no per-organ fine state.
/// </summary>
public sealed class ShellSurfaceProlongation
{
    public const int ElementDofs = 9;
    readonly record struct Frame(int A, int B, int C, double I00, double I01, double I11);
    readonly record struct Binding(int Face, double B, double C, double Normal, double U, double V);
    internal readonly record struct Transport(ShellVector Origin, ShellVector U, ShellVector V,
        ShellVector Normal, ShellVector R0, ShellVector R1);
    readonly Frame[] frames;
    readonly Binding[] bindings;
    readonly record struct CoordinateGraph(ShellDifferentiation Tape, int X, int Y, int Z);
    static readonly CoordinateGraph[] Graphs = Enumerable.Range(0, 3).Select(BuildGraph).ToArray();
    static readonly CoordinateGraph WeightedGraph = BuildGraph(-1);
    public int SourceVertexCount { get; }
    public int SampleCount => bindings.Length;

    public ShellSurfaceProlongation(ReadOnlySpan<ShellVector> rest, ReadOnlySpan<int> triangles,
        ReadOnlySpan<ShellSurfaceSample> samples)
    {
        if (rest.Length < 3 || triangles.Length == 0 || triangles.Length % 3 != 0)
            throw new ArgumentException("Invalid coarse surface.");
        foreach (var p in rest) if (!Finite(p)) throw new ArgumentException("Nonfinite rest position.");
        SourceVertexCount = rest.Length;
        frames = new Frame[triangles.Length / 3]; bindings = new Binding[samples.Length];
        for (var f = 0; f < frames.Length; ++f)
        {
            var a = triangles[3 * f]; var b = triangles[3 * f + 1]; var c = triangles[3 * f + 2];
            if ((uint)a >= rest.Length || (uint)b >= rest.Length || (uint)c >= rest.Length)
                throw new ArgumentException("Invalid coarse index.");
            var u = rest[b] - rest[a]; var v = rest[c] - rest[a];
            var length = u.Norm; var area = u.Cross(v).Norm;
            if (!double.IsFinite(length + area) || !(length > 0) || !(area > 1e-12 * Math.Max(u.Dot(u), v.Dot(v))))
                throw new ArgumentException("Degenerate coarse triangle.");
            var i00 = 1 / length; var i11 = length / area; var i01 = -u.Dot(v) / (length * area);
            if (!double.IsFinite(i00 + i01 + i11)) throw new ArgumentException("Unrepresentable rest frame.");
            frames[f] = new(a, b, c, i00, i01, i11);
        }
        for (var i = 0; i < samples.Length; ++i)
        {
            var sample = samples[i];
            if ((uint)sample.Face >= frames.Length || !double.IsFinite(sample.B + sample.C)
                || sample.B < 0 || sample.C < 0 || sample.B + sample.C > 1 || !Finite(sample.RestPosition))
                throw new ArgumentException("Invalid material correspondence.");
            var frame = frames[sample.Face]; var a = rest[frame.A];
            var u = rest[frame.B] - a; var v = rest[frame.C] - a;
            var e = u * frame.I00; var n = u.Cross(v); n *= 1 / n.Norm;
            var d = sample.RestPosition - (a + u * sample.B + v * sample.C);
            if (!Finite(d)) throw new ArgumentException("Unrepresentable rest offset.");
            var offset = new ShellVector(n.Dot(d), e.Dot(d), n.Cross(e).Dot(d));
            if (!Finite(offset)) throw new ArgumentException("Unrepresentable local rest offset.");
            bindings[i] = new(sample.Face, sample.B, sample.C, offset.X, offset.Y, offset.Z);
        }
    }

    public sealed class Workspace
    {
        internal readonly ShellDifferentiation.Workspace Tape = new(Math.Max(WeightedGraph.Tape.Count, Graphs.Max(g => g.Tape.Count)));
        internal readonly double[] Inputs = new double[20], Gradient = new double[ElementDofs];
        internal readonly double[] Jacobian = new double[3 * ElementDofs], Hessians = new double[3 * ElementDofs * ElementDofs];
        internal readonly Transport[] Frames;
        public Workspace() { Frames = []; }
        /// <summary>Preallocate bulk-transfer scratch once per worker, separately from accepted organ state.</summary>
        public Workspace(ShellSurfaceProlongation profile) { Frames = new Transport[profile.frames.Length]; }
    }

    /// <summary>
    /// Transfer all positions, preparing each triangle's frame only once. Caller
    /// owns the fine-coordinate buffer and a workspace constructed for this
    /// profile (or one with at least as many faces). No runtime allocations or
    /// cached clearances. Failure clears the whole output.
    /// </summary>
    public bool TryProlong(ReadOnlySpan<double> state, Span<double> positions, Workspace w)
    {
        if (state.Length < 3 * SourceVertexCount || positions.Length != 3 * SampleCount || w.Frames.Length < frames.Length)
            throw new ArgumentException("Incorrect bulk-transfer buffers.");
        if (state.Overlaps(positions)) throw new ArgumentException("Transfer output must not alias its input.");
        for (var f = 0; f < frames.Length; ++f)
        {
            var frame = frames[f];
            var a = new ShellVector(state[3 * frame.A], state[3 * frame.A + 1], state[3 * frame.A + 2]);
            var b = new ShellVector(state[3 * frame.B], state[3 * frame.B + 1], state[3 * frame.B + 2]);
            var c = new ShellVector(state[3 * frame.C], state[3 * frame.C + 1], state[3 * frame.C + 2]);
            var u = b - a; var v = c - a; var f0 = u * frame.I00; var f1 = u * frame.I01 + v * frame.I11;
            var normal = f0.Cross(f1); var d = normal.Norm;
            var g00 = f0.Dot(f0); var g01 = f0.Dot(f1); var g11 = f1.Dot(f1);
            if (!Finite(a) || !double.IsFinite(d + g00 + g11) || !(d > 1e-12 * Math.Max(g00, g11)))
            { positions.Clear(); return false; }
            var denominator = d * Math.Sqrt(g00 + g11 + 2 * d);
            var r0 = f0 * ((g11 + d) / denominator) - f1 * (g01 / denominator);
            var r1 = f1 * ((g00 + d) / denominator) - f0 * (g01 / denominator);
            w.Frames[f] = new(a, u, v, normal * (1 / d), r0, r1);
        }
        for (var i = 0; i < bindings.Length; ++i)
        {
            var binding = bindings[i]; var f = w.Frames[binding.Face];
            var point = f.Origin + f.U * binding.B + f.V * binding.C + f.Normal * binding.Normal
                + f.R0 * binding.U + f.R1 * binding.V;
            if (!Finite(point)) { positions.Clear(); return false; }
            positions[3 * i] = point.X; positions[3 * i + 1] = point.Y; positions[3 * i + 2] = point.Z;
        }
        return true;
    }

    /// <summary>Write the nine source-coordinate indices used by a sample, in local derivative order.</summary>
    public void CopyElementMap(int sample, Span<int> map)
    {
        if (map.Length != ElementDofs) throw new ArgumentException("Incorrect map size.");
        var f = frames[bindings[sample].Face];
        for (var k = 0; k < 3; ++k) { map[k] = 3 * f.A + k; map[3 + k] = 3 * f.B + k; map[6 + k] = 3 * f.C + k; }
    }

    /// <summary>
    /// Optional Jacobian is 3x9; optional coordinate Hessians are three 9x9 blocks,
    /// all row-major. Only the first 3*SourceVertexCount state values are positions.
    /// A singular/nonfinite current triangle returns false and clears outputs.
    /// This is an endpoint transfer, not a linear or curved motion certificate.
    /// </summary>
    public bool TryEvaluate(int sample, ReadOnlySpan<double> state, out ShellVector point,
        Span<double> jacobian, Span<double> hessians, Workspace w)
    {
        if (state.Length < 3 * SourceVertexCount || (!jacobian.IsEmpty && jacobian.Length != 27)
            || (!hessians.IsEmpty && hessians.Length != 243)) throw new ArgumentException("Incorrect transfer buffer size.");
        if (state.Overlaps(jacobian) || state.Overlaps(hessians) || jacobian.Overlaps(hessians))
            throw new ArgumentException("Transfer outputs must not alias inputs or each other.");
        point = default; jacobian.Clear(); hessians.Clear();
        var b = bindings[sample]; var f = frames[b.Face]; var x = w.Inputs;
        state.Slice(3 * f.A, 3).CopyTo(x); state.Slice(3 * f.B, 3).CopyTo(x.AsSpan(3)); state.Slice(3 * f.C, 3).CopyTo(x.AsSpan(6));
        for (var i = 0; i < 9; ++i) if (!double.IsFinite(x[i])) return false;
        var u = new ShellVector(x[3] - x[0], x[4] - x[1], x[5] - x[2]);
        var v = new ShellVector(x[6] - x[0], x[7] - x[1], x[8] - x[2]);
        var f0 = u * f.I00; var f1 = u * f.I01 + v * f.I11;
        var area = f0.Cross(f1).Norm; var scale = Math.Max(f0.Dot(f0), f1.Dot(f1));
        if (!double.IsFinite(area + scale) || !(area > 1e-12 * scale)) return false;
        x[9] = f.I00; x[10] = f.I01; x[11] = f.I11; x[12] = b.B; x[13] = b.C;
        x[14] = b.Normal; x[15] = b.U; x[16] = b.V;
        var derivatives = !jacobian.IsEmpty || !hessians.IsEmpty;
        for (var k = 0; k < (derivatives ? 3 : 1); ++k)
        {
            var g = derivatives ? (jacobian.IsEmpty ? w.Gradient : jacobian.Slice(9 * k, 9)) : Span<double>.Empty;
            var h = hessians.IsEmpty ? Span<double>.Empty : hessians.Slice(81 * k, 81);
            var graph = Graphs[k]; graph.Tape.Evaluate(x, ElementDofs, g, h, w.Tape);
        }
        var last = Graphs[derivatives ? 2 : 0];
        point = new(w.Tape.Values[last.X], w.Tape.Values[last.Y], w.Tape.Values[last.Z]);
        if (Finite(point) && AllFinite(jacobian) && AllFinite(hessians)) return true;
        point = default; jacobian.Clear(); hessians.Clear(); return false;
    }

    /// <summary>
    /// Exact pullback of a point energy, including geometric stiffness:
    /// J^T H J + sum(g_k Hessian(P_k)). Outputs overwrite, not accumulate.
    /// Cross-sample energy couplings must separately assemble J_i^T H_ij J_j.
    /// </summary>
    public bool TryPullback(int sample, ReadOnlySpan<double> state, ReadOnlySpan<double> pointGradient,
        ReadOnlySpan<double> pointHessian, Span<double> gradient, Span<double> hessian, Workspace w)
    {
        if (pointGradient.Length != 3 || gradient.Length != 9 || (!pointHessian.IsEmpty && pointHessian.Length != 9)
            || (!hessian.IsEmpty && (pointHessian.Length != 9 || hessian.Length != 81)))
            throw new ArgumentException("Incorrect pullback buffer size.");
        if (state.Overlaps(gradient) || state.Overlaps(hessian) || gradient.Overlaps(hessian)
            || pointGradient.Overlaps(gradient) || pointGradient.Overlaps(hessian)
            || pointHessian.Overlaps(gradient) || pointHessian.Overlaps(hessian))
            throw new ArgumentException("Pullback outputs must not alias inputs or each other.");
        gradient.Clear(); hessian.Clear();
        if (!AllFinite(pointGradient) || !AllFinite(pointHessian)
            || !TryEvaluate(sample, state, out _, w.Jacobian, hessian.IsEmpty ? [] : w.Hessians, w)) return false;
        for (var i = 0; i < 9; ++i)
        {
            for (var k = 0; k < 3; ++k) gradient[i] += w.Jacobian[9 * k + i] * pointGradient[k];
            if (hessian.IsEmpty) continue;
            for (var j = 0; j < 9; ++j)
                for (var k = 0; k < 3; ++k)
                {
                    hessian[9 * i + j] += pointGradient[k] * w.Hessians[81 * k + 9 * i + j];
                    for (var l = 0; l < 3; ++l)
                        hessian[9 * i + j] += w.Jacobian[9 * k + i] * pointHessian[3 * k + l] * w.Jacobian[9 * l + j];
                }
        }
        if (AllFinite(gradient) && AllFinite(hessian)) return true;
        gradient.Clear(); hessian.Clear(); return false;
    }

    /// <summary>
    /// Multiply the geometric stiffness sum(g_k Hessian(P_k)) by a local
    /// nine-coordinate direction, without constructing coordinate Hessians.
    /// The point gradient is held constant for this contraction.
    /// </summary>
    public bool TryContractHessian(int sample, ReadOnlySpan<double> state, ReadOnlySpan<double> pointGradient,
        ReadOnlySpan<double> direction, Span<double> product, Workspace w)
    {
        if (pointGradient.Length != 3 || direction.Length != ElementDofs || product.Length != ElementDofs)
            throw new ArgumentException("Incorrect contraction size.");
        if (state.Overlaps(product) || pointGradient.Overlaps(product) || direction.Overlaps(product))
            throw new ArgumentException("Contraction output must not alias inputs.");
        product.Clear();
        if (!AllFinite(pointGradient) || !AllFinite(direction) || !TryEvaluate(sample, state, out _, [], [], w)) return false;
        pointGradient.CopyTo(w.Inputs.AsSpan(17));
        var value = WeightedGraph.Tape.EvaluateProduct(w.Inputs, ElementDofs, direction, w.Gradient, product, w.Tape);
        if (double.IsFinite(value) && AllFinite(product)) return true;
        product.Clear(); return false;
    }

    static bool Finite(ShellVector p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
    static bool AllFinite(ReadOnlySpan<double> values) { foreach (var v in values) if (!double.IsFinite(v)) return false; return true; }

    static CoordinateGraph BuildGraph(int coordinate)
    {
        var tape = new ShellDifferentiation(); var x = Enumerable.Range(0, coordinate < 0 ? 20 : 17).Select(tape.Input).ToArray();
        V P(int i) => new(x[3 * i], x[3 * i + 1], x[3 * i + 2]);
        V Scale(V v, S s) => new(v.X * s, v.Y * s, v.Z * s);
        var u = P(1) - P(0); var v = P(2) - P(0);
        var f0 = Scale(u, x[9]); var f1 = Scale(u, x[10]) + Scale(v, x[11]);
        var normal = f0.Cross(f1); var d = normal.Norm();
        var a = f0.Dot(f0); var b = f0.Dot(f1); var c = f1.Dot(f1);
        // 2x2 polar factor F*(F^T F)^(-1/2), avoiding SVD derivatives at repeated singular values.
        var denominator = d * (a + c + d * 2).Sqrt();
        var r0 = Scale(f0, (c + d) / denominator) - Scale(f1, b / denominator);
        var r1 = Scale(f1, (a + d) / denominator) - Scale(f0, b / denominator);
        var point = P(0) + Scale(u, x[12]) + Scale(v, x[13])
            + Scale(normal, x[14] / d) + Scale(r0, x[15]) + Scale(r1, x[16]);
        tape.Finish(coordinate < 0 ? point.X * x[17] + point.Y * x[18] + point.Z * x[19]
            : coordinate == 0 ? point.X : coordinate == 1 ? point.Y : point.Z);
        return new(tape, point.X.Id, point.Y.Id, point.Z.Id);
    }
}
