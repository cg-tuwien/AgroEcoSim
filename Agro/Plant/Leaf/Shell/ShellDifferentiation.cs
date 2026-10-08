namespace Agro.Mechanics;

/// <summary>
/// Immutable scalar expression graph with exact first and second derivatives.
/// Forward-over-reverse differentiation avoids finite differences and per-element
/// allocations. Graphs are shared; evaluation buffers belong to the worker.
/// </summary>
internal sealed class ShellDifferentiation
{
    internal enum Operation : byte { Constant, Input, Add, Subtract, Multiply, Divide, Sqrt, Tan, Atan2, Log1pMinusX, SincSqrt, CoscSqrt }
    internal readonly record struct Node(Operation Op, int A, int B, double Constant);
    readonly List<Node> building = [];
    Node[] nodes = [];
    int output;
    internal int Count => nodes.Length;

    internal readonly record struct Scalar(ShellDifferentiation Graph, int Id)
    {
        public static Scalar operator +(Scalar a, Scalar b) => a.Graph.Add(Operation.Add, a, b);
        public static Scalar operator -(Scalar a, Scalar b) => a.Graph.Add(Operation.Subtract, a, b);
        public static Scalar operator *(Scalar a, Scalar b) => a.Graph.Add(Operation.Multiply, a, b);
        public static Scalar operator /(Scalar a, Scalar b) => a.Graph.Add(Operation.Divide, a, b);
        public static Scalar operator +(Scalar a, double b) => a + a.Graph.Constant(b);
        public static Scalar operator -(Scalar a, double b) => a - a.Graph.Constant(b);
        public static Scalar operator *(Scalar a, double b) => a * a.Graph.Constant(b);
        public static Scalar operator /(Scalar a, double b) => a / a.Graph.Constant(b);
        public Scalar Sqrt() => Graph.Add(Operation.Sqrt, this, this);
        public Scalar Tan() => Graph.Add(Operation.Tan, this, this);
        public Scalar Log1pMinusX() => Graph.Add(Operation.Log1pMinusX, this, this);
        public Scalar Atan2(Scalar x) => Graph.Add(Operation.Atan2, this, x);
        public Scalar SincSqrt() => Graph.Add(Operation.SincSqrt, this, this);
        public Scalar CoscSqrt() => Graph.Add(Operation.CoscSqrt, this, this);
    }

    internal readonly record struct Vector(Scalar X, Scalar Y, Scalar Z)
    {
        public static Vector operator +(Vector a, Vector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector operator -(Vector a, Vector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public Scalar Dot(Vector b) => X * b.X + Y * b.Y + Z * b.Z;
        public Vector Cross(Vector b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public Scalar Norm() => Dot(this).Sqrt();
    }

    internal Scalar Input(int index) => Append(new(Operation.Input, index, 0, 0));
    internal Scalar Constant(double value) => Append(new(Operation.Constant, 0, 0, value));
    Scalar Append(Node node) { var id = building.Count; building.Add(node); return new(this, id); }
    Scalar Add(Operation op, Scalar a, Scalar b) => Append(new(op, a.Id, b.Id, 0));
    internal void Finish(Scalar result) { output = result.Id; nodes = building.ToArray(); building.Clear(); }

    internal sealed class Workspace(int size)
    {
        internal readonly double[] Values = new double[size], Adjoint = new double[size], Tangent = new double[size], AdjointTangent = new double[size];
        internal readonly double[] A = new double[size], B = new double[size], AA = new double[size], AB = new double[size], BB = new double[size];
    }

    internal double Evaluate(ReadOnlySpan<double> inputs, int variables, Span<double> gradient,
        Span<double> hessian, Workspace w)
    {
        var derivatives = !gradient.IsEmpty;
        for (var i = 0; i < nodes.Length; ++i)
        {
            var n = nodes[i];
            if (n.Op == Operation.Constant) { w.Values[i] = n.Constant; continue; }
            if (n.Op == Operation.Input) { w.Values[i] = inputs[n.A]; continue; }
            var a = w.Values[n.A]; var b = w.Values[n.B];
            double value, da = 0, db = 0, daa = 0, dab = 0, dbb = 0;
            switch (n.Op)
            {
                case Operation.Add: value = a + b; da = db = 1; break;
                case Operation.Subtract: value = a - b; da = 1; db = -1; break;
                case Operation.Multiply: value = a * b; da = b; db = a; dab = 1; break;
                case Operation.Divide:
                    value = a / b; da = 1 / b; db = -value / b;
                    dab = -da / b; dbb = -2 * db / b; break;
                case Operation.Sqrt:
                    value = Math.Sqrt(a); da = .5 / value; daa = -.5 * da / a; break;
                case Operation.Tan:
                    value = Math.Tan(a); da = 1 + value * value; daa = 2 * value * da; break;
                case Operation.SincSqrt:
                case Operation.CoscSqrt:
                    RotationCoefficient(a, n.Op == Operation.CoscSqrt, out value, out da, out daa); break;
                case Operation.Log1pMinusX:
                    // log(1+x)-x loses the entire quadratic energy near rest if
                    // evaluated by subtraction. Its small-x series avoids a noisy
                    // Armijo test without weakening the equilibrium tolerance.
                    if (Math.Abs(a) < .01)
                    {
                        var power = a * a; value = -.5 * power;
                        for (var k = 3; k <= 12; ++k) { power *= -a; value -= power / k; }
                    }
                    else value = Math.Log(1 + a) - a;
                    da = -a / (1 + a); daa = -1 / ((1 + a) * (1 + a)); break;
                case Operation.Atan2:
                    value = Math.Atan2(a, b);
                    var r2 = a * a + b * b; var r4 = r2 * r2;
                    da = b / r2; db = -a / r2;
                    daa = -2 * a * b / r4; dab = (a * a - b * b) / r4; dbb = -daa; break;
                default: throw new InvalidOperationException();
            }
            w.Values[i] = value;
            if (derivatives) { w.A[i] = da; w.B[i] = db; w.AA[i] = daa; w.AB[i] = dab; w.BB[i] = dbb; }
        }
        if (!derivatives || !double.IsFinite(w.Values[output])) return w.Values[output];
        Array.Clear(w.Adjoint); w.Adjoint[output] = 1;
        for (var i = nodes.Length - 1; i >= 0; --i)
        {
            var n = nodes[i];
            if (n.Op <= Operation.Input) continue;
            w.Adjoint[n.A] += w.Adjoint[i] * w.A[i];
            w.Adjoint[n.B] += w.Adjoint[i] * w.B[i];
        }
        gradient.Clear();
        for (var i = 0; i < nodes.Length; ++i)
            if (nodes[i].Op == Operation.Input && nodes[i].A < variables) gradient[nodes[i].A] += w.Adjoint[i];
        if (hessian.IsEmpty) return w.Values[output];
        hessian.Clear();
        for (var column = 0; column < variables; ++column)
        {
            for (var i = 0; i < nodes.Length; ++i)
            {
                var n = nodes[i];
                w.Tangent[i] = n.Op == Operation.Constant ? 0 : n.Op == Operation.Input
                    ? (n.A == column ? 1 : 0) : w.A[i] * w.Tangent[n.A] + w.B[i] * w.Tangent[n.B];
            }
            Array.Clear(w.AdjointTangent);
            for (var i = nodes.Length - 1; i >= 0; --i)
            {
                var n = nodes[i];
                if (n.Op <= Operation.Input) continue;
                var ta = w.Tangent[n.A]; var tb = w.Tangent[n.B];
                w.AdjointTangent[n.A] += w.A[i] * w.AdjointTangent[i] + w.Adjoint[i] * (w.AA[i] * ta + w.AB[i] * tb);
                w.AdjointTangent[n.B] += w.B[i] * w.AdjointTangent[i] + w.Adjoint[i] * (w.AB[i] * ta + w.BB[i] * tb);
            }
            for (var i = 0; i < nodes.Length; ++i)
                if (nodes[i].Op == Operation.Input && nodes[i].A < variables)
                    hessian[nodes[i].A * variables + column] += w.AdjointTangent[i];
        }
        return w.Values[output];
    }

    /// <summary>Exact Hessian-vector product with one directional forward/reverse pass.</summary>
    internal double EvaluateProduct(ReadOnlySpan<double> inputs, int variables, ReadOnlySpan<double> direction,
        Span<double> gradient, Span<double> product, Workspace w)
    {
        if (direction.Length != variables || gradient.Length != variables || product.Length != variables)
            throw new ArgumentException("Incorrect directional derivative size.");
        var value = Evaluate(inputs, variables, gradient, [], w);
        product.Clear();
        if (!double.IsFinite(value)) { gradient.Clear(); return value; }
        for (var i = 0; i < nodes.Length; ++i)
        {
            var n = nodes[i];
            w.Tangent[i] = n.Op == Operation.Constant ? 0 : n.Op == Operation.Input
                ? (n.A < variables ? direction[n.A] : 0) : w.A[i] * w.Tangent[n.A] + w.B[i] * w.Tangent[n.B];
        }
        Array.Clear(w.AdjointTangent);
        for (var i = nodes.Length - 1; i >= 0; --i)
        {
            var n = nodes[i];
            if (n.Op <= Operation.Input) continue;
            var ta = w.Tangent[n.A]; var tb = w.Tangent[n.B];
            w.AdjointTangent[n.A] += w.A[i] * w.AdjointTangent[i] + w.Adjoint[i] * (w.AA[i] * ta + w.AB[i] * tb);
            w.AdjointTangent[n.B] += w.B[i] * w.AdjointTangent[i] + w.Adjoint[i] * (w.AB[i] * ta + w.BB[i] * tb);
        }
        for (var i = 0; i < nodes.Length; ++i)
            if (nodes[i].Op == Operation.Input && nodes[i].A < variables)
                product[nodes[i].A] += w.AdjointTangent[i];
        return value;
    }

    // Analytic Rodrigues coefficients in squared angle. Differentiating sqrt at
    // zero would produce 0/0 even though these even functions are smooth there.
    internal static void RotationCoefficient(double a, bool cosc, out double value, out double first, out double second)
    {
        if (a < 1)
        {
            var coefficient = cosc ? .5 : 1.0;
            value = coefficient; first = second = 0;
            // Twelve terms are ample through one radian. Derivative powers are
            // tracked separately, with no division by the squared angle at rest.
            var power = 1.0; var previous = 1.0; var preceding = 1.0;
            for (var k = 1; k <= 12; ++k)
            {
                var n = 2 * k + (cosc ? 2 : 1);
                coefficient /= -n * (n - 1);
                power *= a; value += coefficient * power;
                first += k * coefficient * previous;
                if (k >= 2) second += k * (k - 1) * coefficient * preceding;
                if (k >= 2) preceding *= a;
                previous *= a;
            }
            return;
        }
        var angle = Math.Sqrt(a); var sin = Math.Sin(angle); var cos = Math.Cos(angle);
        if (cosc)
        {
            value = (1 - cos) / a;
            first = (angle * sin - 2 * (1 - cos)) / (2 * a * a);
            second = (a * cos - 5 * angle * sin + 8 * (1 - cos)) / (4 * a * a * a);
        }
        else
        {
            value = sin / angle;
            first = (angle * cos - sin) / (2 * a * angle);
            second = ((3 - a) * sin - 3 * angle * cos) / (4 * a * a * angle);
        }
    }
}
