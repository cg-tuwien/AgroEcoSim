using System.Numerics;
using S = Agro.Mechanics.ShellDifferentiation.Scalar;
using V = Agro.Mechanics.ShellDifferentiation.Vector;

namespace Agro.Mechanics;

/// <summary>Double-precision geometry used by the shell and its numerical fixtures.</summary>
public readonly record struct ShellVector(double X, double Y, double Z)
{
    public ShellVector(Vector3 p) : this(p.X, p.Y, p.Z) { }
    public static ShellVector operator +(ShellVector a, ShellVector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static ShellVector operator -(ShellVector a, ShellVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static ShellVector operator *(ShellVector a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public double Dot(ShellVector b) => X * b.X + Y * b.Y + Z * b.Z;
    public ShellVector Cross(ShellVector b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public double Norm => Math.Sqrt(Dot(this));
    public Vector3 Single => new((float)X, (float)Y, (float)Z);
}

/// <summary>Effective lamina properties, independent of drawn relief and physiological mass.</summary>
public readonly record struct ShellMaterial(double MembraneModulus, double BendingModulus,
    double Thickness, double PoissonRatio = .3, double StrainBarrierScale = 1e-6)
{
    public static ShellMaterial FromSpecies(SpeciesSettings species)
    {
        var fallback = species.LeafElasticModulus > 0 ? species.LeafElasticModulus : species.GreenElasticModulus;
        return new(species.LeafMembraneElasticModulus > 0 ? species.LeafMembraneElasticModulus : fallback,
            species.LeafBendingElasticModulus > 0 ? species.LeafBendingElasticModulus : fallback,
            species.LeafMechanicalThickness > 0 ? species.LeafMechanicalThickness : AboveGroundAgent.LeafThickness,
            species.LeafPoissonRatio);
    }

    public void Validate()
    {
        if (!double.IsFinite(MembraneModulus + BendingModulus + Thickness + PoissonRatio + StrainBarrierScale)
            || MembraneModulus <= 0 || BendingModulus <= 0 || Thickness <= 0
            || PoissonRatio <= -1 || PoissonRatio >= .5 || StrainBarrierScale < 0)
            throw new ArgumentOutOfRangeException(nameof(ShellMaterial));
    }
}

/// <summary>Energy terms in joules; diagnostic components of the same assembled objective.</summary>
public readonly record struct ShellEnergyTerms(double Membrane, double Bending, double StrainBarrier)
{
    public double Total => Membrane + Bending + StrainBarrier;
    public static ShellEnergyTerms operator +(ShellEnergyTerms a, ShellEnergyTerms b)
        => new(a.Membrane + b.Membrane, a.Bending + b.Bending, a.StrainBarrier + b.StrainBarrier);
}

/// <summary>
/// Rest-curved Bending-Active Cosserat shell (Chen, Vouga &amp; Kaufman, SIGGRAPH 2026).
/// One director angle per edge; plane-stress StVK membrane and BAC bending energies.
/// The second fundamental form follows the published tan-angle formulation. Exact
/// derivatives are obtained by scalar chain rules, rather than finite differences.
/// See SHELL_MECHANICS.md for equations, provenance and acceptance status.
/// </summary>
public sealed class BacShell
{
    public const double StrainLimit = .02;
    public const int ElementDofs = 21;
    const double Lower = (1 - StrainLimit) * (1 - StrainLimit);
    const double Upper = (1 + StrainLimit) * (1 + StrainLimit);
    readonly record struct Edge(int A, int B, int Opposite0, int Opposite1);
    readonly Edge[] edges;
    readonly int[] triangles, faceEdges, maps;
    readonly int[] masks;
    readonly ShellVector[] rest;
    readonly double[] restDirectors;
    readonly double[] restData;
    readonly double[] restVertexAreas;
    readonly record struct ElementGraph(ShellDifferentiation Tape, int Membrane, int Bending, int Barrier);
    static readonly ElementGraph[] Graphs = Enumerable.Range(0, 8).Select(BuildGraph).ToArray();
    public int VertexCount => rest.Length;
    public int FaceCount => triangles.Length / 3;
    public int EdgeCount => edges.Length;
    public int DofCount => 3 * VertexCount + EdgeCount;
    public ReadOnlySpan<int> Triangles => triangles;
    public ReadOnlySpan<ShellVector> RestVertices => rest;
    public ReadOnlySpan<double> RestDirectors => restDirectors;
    /// <summary>Barycentric material-area quadrature: one third of each incident
    /// rest triangle. Immutable morphology data, not current deformed area.</summary>
    public ReadOnlySpan<double> RestVertexAreas => restVertexAreas;
    public double RestArea { get; }
    public long RestAreaPayloadBytes => (long)restVertexAreas.Length * sizeof(double);
    public ShellMaterial Material { get; }
    internal object TopologyIdentity { get; }

    public BacShell(ShellMesh mesh, ShellMaterial material, ReadOnlySpan<double> restDirectors = default)
        : this(mesh.RestVertices, mesh.Triangles, material, restDirectors, false, mesh) { }

    public BacShell(ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> indices, ShellMaterial material,
        ReadOnlySpan<double> restDirectors = default)
        : this(positions, indices, material, restDirectors, false) { }

    /// <summary>
    /// Reference used to fit rest directors with positions fixed, following the
    /// curved-rest benchmark in Better Bending section 8.1. This is a preprocessing
    /// objective, not the elastic energy of the final curved lamina.
    /// </summary>
    public static BacShell ForRestDirectorFit(ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> indices, ShellMaterial material)
        => new(positions, indices, material, default, true);

    public static BacShell ForRestDirectorFit(ShellMesh mesh, ShellMaterial material)
        => new(mesh.RestVertices, mesh.Triangles, material, default, true, mesh);

    BacShell(ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> indices, ShellMaterial material,
        ReadOnlySpan<double> restDirectors, bool zeroRestCurvature, object? topologyIdentity = null)
    {
        TopologyIdentity = topologyIdentity ?? this;
        material.Validate(); Material = material;
        if (positions.Length < 3 || indices.Length == 0 || indices.Length % 3 != 0) throw new ArgumentException("Invalid shell mesh.");
        rest = new ShellVector[positions.Length];
        for (var i = 0; i < rest.Length; ++i)
        {
            rest[i] = new(positions[i]);
            if (!double.IsFinite(rest[i].X + rest[i].Y + rest[i].Z)) throw new ArgumentException("Nonfinite shell vertex.");
        }
        triangles = indices.ToArray();
        var connectivity = new SortedDictionary<(int, int), (int, int)>();
        for (var f = 0; f < FaceCount; ++f)
            for (var j = 0; j < 3; ++j)
            {
                var a = triangles[3 * f + (j + 1) % 3]; var b = triangles[3 * f + (j + 2) % 3]; var opposite = triangles[3 * f + j];
                if ((uint)a >= rest.Length || (uint)b >= rest.Length || a == b) throw new ArgumentException("Invalid shell index.");
                var key = (Math.Min(a, b), Math.Max(a, b));
                var previous = connectivity.GetValueOrDefault(key, (-1, -1));
                if ((a < b ? previous.Item1 : previous.Item2) >= 0) throw new ArgumentException("Nonmanifold or inconsistently wound shell.");
                connectivity[key] = a < b ? (opposite, previous.Item2) : (previous.Item1, opposite);
            }
        edges = connectivity.Select(p => new Edge(p.Key.Item1, p.Key.Item2, p.Value.Item1, p.Value.Item2)).ToArray();
        if (!restDirectors.IsEmpty && restDirectors.Length != edges.Length) throw new ArgumentException("Incorrect rest director count.");
        this.restDirectors = restDirectors.IsEmpty ? new double[edges.Length] : restDirectors.ToArray();
        foreach (var angle in this.restDirectors)
            if (!double.IsFinite(angle)) throw new ArgumentException("Nonfinite rest director.");
        var edgeIds = connectivity.Keys.Select((key, id) => (key, id)).ToDictionary(p => p.key, p => p.id);
        faceEdges = new int[triangles.Length]; maps = new int[FaceCount * ElementDofs]; masks = new int[FaceCount];
        restData = new double[FaceCount * 14];
        restVertexAreas = new double[VertexCount];
        Span<double> ii = stackalloc double[3];
        for (var f = 0; f < FaceCount; ++f)
        {
            for (var j = 0; j < 3; ++j)
            {
                var a = triangles[3 * f + (j + 1) % 3]; var b = triangles[3 * f + (j + 2) % 3];
                var id = faceEdges[3 * f + j] = edgeIds[(Math.Min(a, b), Math.Max(a, b))];
                var edge = edges[id];
                var other = a < b ? edge.Opposite1 : edge.Opposite0;
                if (other < 0) { other = triangles[3 * f + j]; masks[f] |= 1 << j; }
                for (var k = 0; k < 3; ++k)
                {
                    maps[f * ElementDofs + 3 * j + k] = 3 * triangles[3 * f + j] + k;
                    maps[f * ElementDofs + 9 + 3 * j + k] = 3 * other + k;
                }
                maps[f * ElementDofs + 18 + j] = 3 * VertexCount + id;
            }
            var u = rest[triangles[3 * f + 1]] - rest[triangles[3 * f]];
            var v = rest[triangles[3 * f + 2]] - rest[triangles[3 * f]];
            var g00 = u.Dot(u); var g01 = u.Dot(v); var g11 = v.Dot(v); var det = g00 * g11 - g01 * g01;
            if (det <= 0 || !double.IsFinite(det)) throw new ArgumentException("Degenerate shell triangle.");
            var d = restData.AsSpan(14 * f, 14);
            d[0] = g11 / det; d[1] = -g01 / det; d[2] = g00 / det;
            d[3] = g00; d[4] = g01; d[5] = g11;
            for (var j = 0; j < 3; ++j)
            {
                var edgeId = faceEdges[3 * f + j]; var e = edges[edgeId];
                var altitude = Math.Sqrt(det) / (rest[e.B] - rest[e.A]).Norm;
                var sign = triangles[3 * f + (j + 1) % 3] < triangles[3 * f + (j + 2) % 3] ? 1 : -1;
                var angle = EdgeAngle(e, rest) * .5 + sign * this.restDirectors[edgeId];
                if (Math.Abs(angle) >= Math.PI * .5 - 1e-8) throw new ArgumentException("Inadmissible rest director.");
                ii[j] = zeroRestCurvature ? 0 : 2 * altitude * Math.Tan(angle);
            }
            d[6] = ii[0] + ii[1]; d[7] = ii[0]; d[8] = ii[0] + ii[2];
            var area = .5 * Math.Sqrt(det);
            RestArea += area;
            for (var j = 0; j < 3; ++j) restVertexAreas[triangles[3 * f + j]] += area / 3;
            var membrane = material.Thickness * area / 4;
            var bending = Math.Pow(material.Thickness, 3) * area / 12;
            var nu = material.PoissonRatio;
            d[9] = membrane * material.MembraneModulus * nu / (1 - nu * nu);
            d[10] = membrane * material.MembraneModulus / (2 * (1 + nu));
            d[11] = bending * material.BendingModulus * nu / (1 - nu * nu);
            d[12] = bending * material.BendingModulus / (2 * (1 + nu));
            d[13] = material.StrainBarrierScale * material.MembraneModulus * material.Thickness * area;
        }
    }

    static double EdgeAngle(Edge e, ShellVector[] p)
    {
        if (e.Opposite0 < 0 || e.Opposite1 < 0) return 0;
        var n0 = (p[e.A] - p[e.Opposite0]).Cross(p[e.B] - p[e.Opposite0]);
        var n1 = (p[e.B] - p[e.Opposite1]).Cross(p[e.A] - p[e.Opposite1]);
        var axis = p[e.B] - p[e.A];
        return 2 * Math.Atan2(n0.Cross(n1).Dot(axis) / axis.Norm, n0.Dot(n1) + n0.Norm * n1.Norm);
    }

    public double[] CreateRestState()
    {
        var q = new double[DofCount];
        for (var i = 0; i < VertexCount; ++i) { q[3 * i] = rest[i].X; q[3 * i + 1] = rest[i].Y; q[3 * i + 2] = rest[i].Z; }
        restDirectors.CopyTo(q, 3 * VertexCount);
        return q;
    }

    public ReadOnlySpan<int> ElementMap(int face) => maps.AsSpan(face * ElementDofs, ElementDofs);
    /// <summary>Derivative-workspace array payload, excluding CLR object/array headers.</summary>
    public static long WorkspacePayloadBytes => (9L * Graphs.Max(g => g.Tape.Count)
        + 35 + 2 * ElementDofs + ElementDofs * ElementDofs) * sizeof(double);
    public sealed class Workspace
    {
        internal readonly ShellDifferentiation.Workspace Tape = new(Graphs.Max(g => g.Tape.Count));
        internal readonly double[] Inputs = new double[35], Gradient = new double[ElementDofs], Hessian = new double[ElementDofs * ElementDofs];
        internal readonly double[] Direction = new double[ElementDofs];
    }

    /// <summary>Evaluate one element. Both derivative spans empty request energy only.</summary>
    public double EvaluateElement(int face, ReadOnlySpan<double> q, Span<double> gradient, Span<double> hessian, Workspace w)
    {
        if (q.Length != DofCount) throw new ArgumentException("Incorrect shell state size.");
        if (!gradient.IsEmpty && gradient.Length != ElementDofs) throw new ArgumentException("Incorrect element gradient size.");
        if (!hessian.IsEmpty && hessian.Length != ElementDofs * ElementDofs) throw new ArgumentException("Incorrect element Hessian size.");
        // Hessian-only callers still need the reverse pass and orientation signs.
        if (gradient.IsEmpty && !hessian.IsEmpty) gradient = w.Gradient;
        var map = ElementMap(face);
        for (var i = 0; i < ElementDofs; ++i) w.Inputs[i] = q[map[i]];
        // The edge director's sign follows the oriented face-edge convention.
        for (var j = 0; j < 3; ++j)
            if (triangles[3 * face + (j + 1) % 3] > triangles[3 * face + (j + 2) % 3]) w.Inputs[18 + j] *= -1;
        restData.AsSpan(14 * face, 14).CopyTo(w.Inputs.AsSpan(ElementDofs));
        if (!ElementValid(w.Inputs, masks[face], out _, out _)) return double.PositiveInfinity;
        var energy = Graphs[masks[face]].Tape.Evaluate(w.Inputs, ElementDofs, gradient, hessian, w.Tape);
        if (!gradient.IsEmpty)
            for (var j = 0; j < 3; ++j)
            {
                if (triangles[3 * face + (j + 1) % 3] < triangles[3 * face + (j + 2) % 3]) continue;
                var k = 18 + j; gradient[k] = -gradient[k];
                if (!hessian.IsEmpty)
                    for (var l = 0; l < ElementDofs; ++l)
                    { hessian[k * ElementDofs + l] *= -1; hessian[l * ElementDofs + k] *= -1; }
            }
        return energy;
    }

    /// <summary>
    /// Exact element Hessian-vector product, without forming a 21x21 matrix.
    /// Direction and outputs use ElementMap order, including oriented directors.
    /// Scratch belongs to the worker. This does not project the Hessian to PSD.
    /// </summary>
    public double EvaluateElementProduct(int face, ReadOnlySpan<double> q, ReadOnlySpan<double> direction,
        Span<double> gradient, Span<double> product, Workspace w)
    {
        if (q.Length != DofCount || direction.Length != ElementDofs || product.Length != ElementDofs
            || (!gradient.IsEmpty && gradient.Length != ElementDofs)) throw new ArgumentException("Incorrect element product size.");
        if (direction.Overlaps(product) || direction.Overlaps(gradient) || q.Overlaps(product)
            || q.Overlaps(gradient) || gradient.Overlaps(product)) throw new ArgumentException("Element outputs must not alias inputs.");
        gradient.Clear(); product.Clear();
        foreach (var value in direction) if (!double.IsFinite(value)) return double.PositiveInfinity;
        var map = ElementMap(face);
        for (var i = 0; i < ElementDofs; ++i) { w.Inputs[i] = q[map[i]]; w.Direction[i] = direction[i]; }
        for (var j = 0; j < 3; ++j)
            if (triangles[3 * face + (j + 1) % 3] > triangles[3 * face + (j + 2) % 3])
            { w.Inputs[18 + j] *= -1; w.Direction[18 + j] *= -1; }
        restData.AsSpan(14 * face, 14).CopyTo(w.Inputs.AsSpan(ElementDofs));
        if (!ElementValid(w.Inputs, masks[face], out _, out _)) return double.PositiveInfinity;
        var g = gradient.IsEmpty ? w.Gradient.AsSpan() : gradient;
        var energy = Graphs[masks[face]].Tape.EvaluateProduct(w.Inputs, ElementDofs, w.Direction, g, product, w.Tape);
        for (var j = 0; j < 3; ++j)
            if (triangles[3 * face + (j + 1) % 3] > triangles[3 * face + (j + 2) % 3])
            { g[18 + j] *= -1; product[18 + j] *= -1; }
        for (var i = 0; i < ElementDofs; ++i)
            if (!double.IsFinite(g[i]) || !double.IsFinite(product[i]))
            { gradient.Clear(); product.Clear(); return double.PositiveInfinity; }
        return energy;
    }

    public double Energy(ReadOnlySpan<double> q, Span<double> gradient, Workspace w)
    {
        if (!gradient.IsEmpty && gradient.Length != DofCount) throw new ArgumentException("Incorrect shell gradient size.");
        gradient.Clear(); var total = 0.0;
        for (var f = 0; f < FaceCount; ++f)
        {
            var g = gradient.IsEmpty ? Span<double>.Empty : w.Gradient;
            var value = EvaluateElement(f, q, g, [], w);
            if (!double.IsFinite(value)) return value;
            total += value;
            if (!gradient.IsEmpty) for (var i = 0; i < ElementDofs; ++i) gradient[maps[ElementDofs * f + i]] += g[i];
        }
        return total;
    }

    /// <summary>
    /// Inspect the membrane, bending and strain-barrier contributions without
    /// duplicating constitutive equations. Optional per-face output supports
    /// offline refinement diagnostics. Uses only caller/worker storage and does
    /// not alter the solve, tissue coefficients or biological dimensions.
    /// </summary>
    public ShellEnergyTerms EnergyTerms(ReadOnlySpan<double> q, Workspace w, Span<ShellEnergyTerms> perFace = default)
    {
        if (q.Length != DofCount) throw new ArgumentException("Incorrect shell state size.");
        if (!perFace.IsEmpty && perFace.Length != FaceCount) throw new ArgumentException("Incorrect face output size.");
        var total = new ShellEnergyTerms();
        for (var f = 0; f < FaceCount; ++f)
        {
            var energy = EvaluateElement(f, q, [], [], w);
            var graph = Graphs[masks[f]];
            var terms = double.IsFinite(energy)
                ? new ShellEnergyTerms(w.Tape.Values[graph.Membrane], w.Tape.Values[graph.Bending], w.Tape.Values[graph.Barrier])
                : new ShellEnergyTerms(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
            if (!perFace.IsEmpty) perFace[f] = terms;
            total += terms;
        }
        return total;
    }

    /// <summary>
    /// Scan the complete surface, including after a rejected element. False
    /// means an inadmissible element/director; finite outputs still describe the
    /// full principal-stretch range. Nonfinite vertex data may produce NaN bounds.
    /// This diagnostic is not a motion or collision certificate.
    /// </summary>
    public bool StrainRange(ReadOnlySpan<double> q, out double minimum, out double maximum)
    {
        if (q.Length != DofCount) throw new ArgumentException("Incorrect shell state size.");
        minimum = double.PositiveInfinity; maximum = 0; var admissible = true;
        Span<double> inputs = stackalloc double[35];
        for (var f = 0; f < FaceCount; ++f)
        {
            for (var i = 0; i < ElementDofs; ++i) inputs[i] = q[maps[f * ElementDofs + i]];
            for (var j = 0; j < 3; ++j)
                if (triangles[3 * f + (j + 1) % 3] > triangles[3 * f + (j + 2) % 3]) inputs[18 + j] *= -1;
            restData.AsSpan(14 * f, 14).CopyTo(inputs[ElementDofs..]);
            var valid = ElementValid(inputs, masks[f], out var low, out var high);
            minimum = Math.Min(minimum, low); maximum = Math.Max(maximum, high);
            admissible &= valid;
        }
        return admissible;
    }

    /// <summary>
    /// Bound principal stretches over the complete linear vertex path. A false
    /// result includes intervals too close to a limit to certify numerically.
    /// Directors, self-contact, obstacles and curved support paths require their
    /// own tests; this method does not constitute a collision certificate.
    /// </summary>
    public bool TryCertifyLinearStrainPath(ReadOnlySpan<double> from, ReadOnlySpan<double> to)
    {
        if (from.Length != DofCount || to.Length != DofCount) throw new ArgumentException("Incorrect shell state size.");
        for (var f = 0; f < FaceCount; ++f)
        {
            var a = triangles[3 * f]; var b = triangles[3 * f + 1]; var c = triangles[3 * f + 2];
            var u = Point(from, b) - Point(from, a); var v = Point(from, c) - Point(from, a);
            var du = Point(to, b) - Point(to, a) - u; var dv = Point(to, c) - Point(to, a) - v;
            var d = restData.AsSpan(14 * f, 14);
            if (!ShellStrainPath.Check(u, v, du, dv, d[3], d[4], d[5], Lower, Upper)) return false;
        }
        return true;
    }

    static ShellVector Point(ReadOnlySpan<double> q, int vertex) => new(q[3 * vertex], q[3 * vertex + 1], q[3 * vertex + 2]);
    static bool ElementValid(ReadOnlySpan<double> x, int boundary, out double low, out double high)
    {
        var u = Point(x, 1) - Point(x, 0); var v = Point(x, 2) - Point(x, 0);
        var a = u.Dot(u); var b = u.Dot(v); var c = v.Dot(v);
        var trace = x[21] * a + 2 * x[22] * b + x[23] * c;
        var det = (a * c - b * b) * (x[21] * x[23] - x[22] * x[22]);
        var delta = Math.Sqrt(Math.Max(0, trace * trace - 4 * det));
        low = Math.Sqrt(Math.Max(0, .5 * (trace - delta))); high = Math.Sqrt(Math.Max(0, .5 * (trace + delta)));
        if (!double.IsFinite(low + high) || low <= 1 - StrainLimit || high >= 1 + StrainLimit) return false;
        for (var j = 0; j < 3; ++j)
        {
            var a0 = Point(x, (j + 1) % 3); var a1 = Point(x, (j + 2) % 3); var a2 = Point(x, j); var a3 = Point(x, 3 + j);
            var theta = 0.0;
            if ((boundary & (1 << j)) == 0)
            {
                var n0 = (a0 - a2).Cross(a1 - a2); var n1 = (a1 - a3).Cross(a0 - a3); var axis = a1 - a0;
                theta = 2 * Math.Atan2(n0.Cross(n1).Dot(axis) / axis.Norm, n0.Dot(n1) + n0.Norm * n1.Norm);
            }
            var angle = theta * .5 + x[18 + j];
            if (!double.IsFinite(angle) || Math.Abs(angle) >= Math.PI * .5 - 1e-8) return false;
        }
        return true;
    }

    static ElementGraph BuildGraph(int boundary)
    {
        var t = new ShellDifferentiation(); var x = Enumerable.Range(0, 35).Select(t.Input).ToArray();
        V P(int i) => new(x[3 * i], x[3 * i + 1], x[3 * i + 2]);
        var u = P(1) - P(0); var v = P(2) - P(0);
        var a = u.Dot(u); var b = u.Dot(v); var c = v.Dot(v);
        S Constitutive(S da, S db, S dc, S lambda, S mu)
        {
            var m00 = x[21] * da + x[22] * db; var m01 = x[21] * db + x[22] * dc;
            var m10 = x[22] * da + x[23] * db; var m11 = x[22] * db + x[23] * dc;
            var trace = m00 + m11;
            return lambda * trace * trace * .5 + mu * (m00 * m00 + m01 * m10 * 2 + m11 * m11);
        }
        var membrane = Constitutive(a - x[24], b - x[25], c - x[26], x[30], x[31]);
        var ii = new S[3]; var doubleArea = (a * c - b * b).Sqrt();
        for (var j = 0; j < 3; ++j)
        {
            var p0 = P((j + 1) % 3); var p1 = P((j + 2) % 3); var p2 = P(j); var p3 = P(3 + j);
            var axis = p1 - p0; var theta = t.Constant(0);
            if ((boundary & (1 << j)) == 0)
            {
                var n0 = (p0 - p2).Cross(p1 - p2); var n1 = (p1 - p3).Cross(p0 - p3);
                theta = (n0.Cross(n1).Dot(axis) / axis.Norm()).Atan2(n0.Dot(n1) + n0.Norm() * n1.Norm()) * 2;
            }
            ii[j] = doubleArea / axis.Norm() * (theta * .5 + x[18 + j]).Tan() * 2;
        }
        var bending = Constitutive(ii[0] + ii[1] - x[27], ii[0] - x[28], ii[0] + ii[2] - x[29], x[32], x[33]);
        var energy = membrane + bending;
        // Spectral log barrier written as 2x2 determinants: differentiable even when
        // principal stretches coincide (unlike differentiating eigenvalue square roots).
        // Write the normalized determinants in terms of C-I, and cancel their
        // linear terms algebraically. Direct det(C-LI) evaluation leaves an energy
        // noise floor that stalls otherwise converged thin-shell Newton steps.
        var da = a - x[24]; var db = b - x[25]; var dc = c - x[26];
        var traceD = x[21] * da + x[22] * db * 2 + x[23] * dc;
        var detD = (da * dc - db * db) * (x[21] * x[23] - x[22] * x[22]);
        const double lowerInverse = 1 / (1 - Lower), upperInverse = 1 / (Upper - 1);
        var lower = traceD * lowerInverse + detD * (lowerInverse * lowerInverse);
        var upper = traceD * -upperInverse + detD * (upperInverse * upperInverse);
        var barrier = lower.Log1pMinusX() * -1 - upper.Log1pMinusX()
            - detD * (lowerInverse * lowerInverse + upperInverse * upperInverse);
        var strain = x[34] * barrier;
        t.Finish(energy + strain);
        return new(t, membrane.Id, bending.Id, strain.Id);
    }
}
