using S = Agro.Mechanics.ShellDifferentiation.Scalar;
using V = Agro.Mechanics.ShellDifferentiation.Vector;

namespace Agro.Mechanics;

/// <summary>Orthonormal columns transporting the blade's unloaded local frame.</summary>
public readonly record struct PetioleTipFrame(ShellVector X, ShellVector Y, ShellVector Z)
{
    public ShellVector Transform(ShellVector point) => X * point.X + Y * point.Y + Z * point.Z;
}

/// <summary>
/// Experimental inextensible three-span circular beam. A basal bend and two
/// internal bends use three-point Gauss-Radau compliance quadrature. This matches
/// Euler-Bernoulli end-moment, transverse-tip-force and uniform-load compliance
/// at small deflection without multiplying whole-organ flexibility. It is not a
/// complete discrete-elastic-rod model: torsion, shear and buckling are unvalidated.
/// Existing V1 rods and biological petiole accounting are unchanged.
/// </summary>
public sealed partial class PetioleBeam
{
    public const int DofCount = 6;
    static readonly double[] locations = [0, (6 - Math.Sqrt(6)) / 10, (6 + Math.Sqrt(6)) / 10];
    static readonly double[] weights = [1.0 / 9, (16 + Math.Sqrt(6)) / 36, (16 - Math.Sqrt(6)) / 36];
    static readonly ShellDifferentiation EnergyGraph = BuildEnergy();
    static readonly ShellDifferentiation[] PointGraphs = Enumerable.Range(0, 9).Select(BuildPoint).ToArray();
    static readonly ShellDifferentiation[] FrameGraphs = Enumerable.Range(0, 9).Select(BuildFrame).ToArray();
    public static ReadOnlySpan<double> HingeFractions => locations;
    public static ReadOnlySpan<double> ComplianceWeights => weights;
    public double Length { get; }
    public double Radius { get; }
    public double FlexuralRigidity { get; }

    public PetioleBeam(double length, double radius, double elasticModulus)
    {
        if (!(length > 0) || !(radius > 0) || !(elasticModulus > 0)
            || !double.IsFinite(length + radius + elasticModulus))
            throw new ArgumentOutOfRangeException(nameof(length));
        Length = length; Radius = radius;
        FlexuralRigidity = elasticModulus * Math.PI * Math.Pow(radius, 4) / 4;
        if (!(FlexuralRigidity > 0) || !double.IsFinite(FlexuralRigidity))
            throw new ArgumentOutOfRangeException(nameof(radius));
    }

    public sealed class Workspace
    {
        internal readonly ShellDifferentiation.Workspace Tape = new(Math.Max(EnergyGraph.Count,
            Math.Max(PointGraphs.Max(g => g.Count), FrameGraphs.Max(g => g.Count))));
        internal readonly double[] Inputs = new double[14], Gradient = new double[DofCount];
    }

    /// <summary>Moment per radian at this hinge, Nm; independent of simulation timestep.</summary>
    public double AngularStiffness(int hinge) => FlexuralRigidity / (Length * weights[hinge]);

    /// <summary>
    /// Energy (J), angular gradient (Nm), and Hessian (Nm/rad). q holds local Y/Z
    /// rotation-vector components for the basal, first internal, and second
    /// internal hinge. Loads are in the unloaded base frame: tip force in N,
    /// distributed force in N/m of intrinsic length. Gravity is such a distributed
    /// load; a blade/support reaction is supplied as a tip load by the coupled solve.
    /// </summary>
    public double Evaluate(ReadOnlySpan<double> q, ShellVector tipForce, ShellVector distributedForce,
        Span<double> gradient, Span<double> hessian, Workspace w)
    {
        ValidateState(q);
        if ((!gradient.IsEmpty && gradient.Length != DofCount) || (!hessian.IsEmpty && hessian.Length != DofCount * DofCount))
            throw new ArgumentException("Incorrect rod derivative size.");
        q.CopyTo(w.Inputs); w.Inputs[6] = Length; w.Inputs[7] = FlexuralRigidity;
        w.Inputs[8] = tipForce.X; w.Inputs[9] = tipForce.Y; w.Inputs[10] = tipForce.Z;
        w.Inputs[11] = distributedForce.X; w.Inputs[12] = distributedForce.Y; w.Inputs[13] = distributedForce.Z;
        if (gradient.IsEmpty && !hessian.IsEmpty) gradient = w.Gradient;
        return EnergyGraph.Evaluate(w.Inputs, DofCount, gradient, hessian, w.Tape);
    }

    /// <summary>
    /// Actual material point and optional row-major 3-by-6 angular Jacobian. The
    /// blade attaches at fraction 1. Shared span endpoints are identical; points
    /// between them lie on inextensible straight spans of the original total length.
    /// </summary>
    public ShellVector Point(double fraction, ReadOnlySpan<double> q, Span<double> jacobian, Workspace w)
    {
        ValidateState(q);
        if (!double.IsFinite(fraction) || fraction < 0 || fraction > 1)
            throw new ArgumentOutOfRangeException(nameof(fraction));
        if (!jacobian.IsEmpty && jacobian.Length != 3 * DofCount) throw new ArgumentException("Incorrect rod Jacobian size.");
        q.CopyTo(w.Inputs); w.Inputs[6] = Length; w.Inputs[7] = fraction;
        var span = fraction <= locations[1] ? 0 : fraction <= locations[2] ? 1 : 2;
        Span<double> result = stackalloc double[3];
        for (var axis = 0; axis < 3; ++axis)
            result[axis] = PointGraphs[3 * span + axis].Evaluate(w.Inputs, DofCount,
                jacobian.IsEmpty ? [] : jacobian.Slice(axis * DofCount, DofCount), [], w.Tape);
        return new(result[0], result[1], result[2]);
    }

    /// <summary>Tip frame and optional 9-by-6 Jacobian, columns X/Y/Z each in XYZ order.</summary>
    public PetioleTipFrame TipFrame(ReadOnlySpan<double> q, Span<double> jacobian, Workspace w)
    {
        ValidateState(q);
        if (!jacobian.IsEmpty && jacobian.Length != 9 * DofCount) throw new ArgumentException("Incorrect frame Jacobian size.");
        q.CopyTo(w.Inputs); Span<double> result = stackalloc double[9];
        for (var i = 0; i < result.Length; ++i)
            result[i] = FrameGraphs[i].Evaluate(w.Inputs, DofCount,
                jacobian.IsEmpty ? [] : jacobian.Slice(i * DofCount, DofCount), [], w.Tape);
        return new(new(result[0], result[1], result[2]), new(result[3], result[4], result[5]), new(result[6], result[7], result[8]));
    }

    /// <summary>Shared numerical bend safeguard; internal hinges do not each get a whole-organ allowance.</summary>
    public static bool WithinBendLimit(ReadOnlySpan<double> q, double wholeOrganRadians)
    {
        ValidateState(q);
        if (!double.IsFinite(wholeOrganRadians) || wholeOrganRadians < 0) throw new ArgumentOutOfRangeException(nameof(wholeOrganRadians));
        var total = 0.0;
        for (var i = 0; i < DofCount; i += 2) total += Math.Sqrt(q[i] * q[i] + q[i + 1] * q[i + 1]);
        return total <= wholeOrganRadians;
    }

    static void ValidateState(ReadOnlySpan<double> q)
    {
        if (q.Length != DofCount) throw new ArgumentException("Incorrect rod state size.");
        foreach (var angle in q) if (!double.IsFinite(angle)) throw new ArgumentException("Nonfinite rod bend.");
    }

    static V Scale(V v, S s) => new(v.X * s, v.Y * s, v.Z * s);
    static V Rotate(V v, S y, S z)
    {
        var omega = new V(y.Graph.Constant(0), y, z); var square = y * y + z * z;
        var cross = omega.Cross(v);
        return v + Scale(cross, square.SincSqrt()) + Scale(omega.Cross(cross), square.CoscSqrt());
    }

    static V[] Directions(ShellDifferentiation graph, S[] q)
    {
        var axis = new V(graph.Constant(1), graph.Constant(0), graph.Constant(0));
        return [Rotate(axis, q[0], q[1]),
            Rotate(Rotate(axis, q[2], q[3]), q[0], q[1]),
            Rotate(Rotate(Rotate(axis, q[4], q[5]), q[2], q[3]), q[0], q[1])];
    }

    static ShellDifferentiation BuildEnergy()
    {
        var graph = new ShellDifferentiation(); var x = Enumerable.Range(0, 14).Select(graph.Input).ToArray();
        var d = Directions(graph, x); var energy = graph.Constant(0);
        var start = new V(graph.Constant(0), graph.Constant(0), graph.Constant(0));
        var tipForce = new V(x[8], x[9], x[10]); var distributed = new V(x[11], x[12], x[13]);
        for (var i = 0; i < 3; ++i)
        {
            var length = x[6] * ((i < 2 ? locations[i + 1] : 1) - locations[i]);
            var centre = start + Scale(d[i], length * .5);
            energy = energy + (x[2 * i] * x[2 * i] + x[2 * i + 1] * x[2 * i + 1]) * x[7] / x[6] / (2 * weights[i]);
            energy = energy - centre.Dot(distributed) * length;
            start = start + Scale(d[i], length);
        }
        graph.Finish(energy - start.Dot(tipForce)); return graph;
    }

    static ShellDifferentiation BuildPoint(int index)
    {
        var graph = new ShellDifferentiation(); var x = Enumerable.Range(0, 8).Select(graph.Input).ToArray();
        var d = Directions(graph, x); var span = index / 3;
        var point = new V(graph.Constant(0), graph.Constant(0), graph.Constant(0));
        for (var i = 0; i <= span; ++i)
        {
            var length = x[6] * (i == span ? x[7] - locations[i] : graph.Constant(locations[i + 1] - locations[i]));
            point = point + Scale(d[i], length);
        }
        graph.Finish(index % 3 == 0 ? point.X : index % 3 == 1 ? point.Y : point.Z); return graph;
    }

    static ShellDifferentiation BuildFrame(int index)
    {
        var graph = new ShellDifferentiation(); var x = Enumerable.Range(0, DofCount).Select(graph.Input).ToArray();
        var column = index / 3;
        var axis = new V(graph.Constant(column == 0 ? 1 : 0), graph.Constant(column == 1 ? 1 : 0), graph.Constant(column == 2 ? 1 : 0));
        var direction = Rotate(Rotate(Rotate(axis, x[4], x[5]), x[2], x[3]), x[0], x[1]);
        graph.Finish(index % 3 == 0 ? direction.X : index % 3 == 1 ? direction.Y : direction.Z); return graph;
    }
}
