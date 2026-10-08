using System.Runtime.CompilerServices;

namespace Agro.Mechanics;

public readonly record struct ShellVertexMotion(ShellVector Start, ShellVector End);
public enum ShellCcdStatus : byte { Clear, PotentialContact, Uncertified }
public enum ShellCcdLimit : byte { None, WorkBudget, WorkspaceCapacity, NumericalResolution }

/// <summary>
/// Clear certifies the complete linear vertex motion. PotentialContact encloses a
/// possible contact within the requested tolerances; it is not proof of overlap.
/// Uncertified identifies work/numerical limits, never physical confinement.
/// SafeFraction is a certified prefix, strictly before any unresolved interval.
/// Zero means no positive advance is certified; it does not certify the start.
/// </summary>
public readonly record struct ShellCcdResult(ShellCcdStatus Status, double SafeFraction,
    double CandidateTimeUpperBound, long VisitedBoxes, ShellCcdLimit Limit);

public readonly record struct ShellCcdSettings
{
    public double MinimumSeparation { get; init; } = 0;
    public double SpatialTolerance { get; init; } = 1e-9;
    public double TimeTolerance { get; init; } = 1e-6;
    public int MaximumBoxes { get; init; } = 32768;
    public ShellCcdSettings() { }
    internal void Validate()
    {
        if (!(MinimumSeparation >= 0) || !(SpatialTolerance > 0) || !(TimeTolerance > 0 && TimeTolerance <= 1)
            || !double.IsFinite(MinimumSeparation + SpatialTolerance) || MaximumBoxes < 1)
            throw new ArgumentOutOfRangeException(nameof(ShellCcdSettings));
    }
}

/// <summary>
/// Inclusion-based vertex/triangle and edge/edge CCD for linearly moving vertices.
/// The relative feature map is multiaffine in time and two material coordinates;
/// its corner hull encloses each parameter box. Directed-rounding interval
/// arithmetic and de Casteljau subdivision conservatively exclude boxes. This is
/// a managed implementation of that principle, not the Tight Inclusion codebase
/// or a curved-support CCD algorithm. See SHELL_MECHANICS.md for validation/scope.
/// </summary>
public static class ShellContinuousCollision
{
    // Values entering the query are exact doubles. Each arithmetic operation
    // expands by one adjacent representable value, including subnormal results.
    // No fused multiply-add, heuristic geometric epsilon or fast-math assumption.
    internal readonly record struct Interval(double Low, double High)
    {
        internal static Interval Point(double value) => new(value, value);
        internal static Interval Lerp(double a, double b, Interval t)
            => a == b ? Point(a) : Point(a) + Difference(b, a) * t;
        internal static Interval Difference(double a, double b)
        { if (a == b && double.IsFinite(a)) return Point(0); var x = a - b; return new(Math.BitDecrement(x), Math.BitIncrement(x)); }
        public static Interval operator +(Interval a, Interval b)
        {
            if (a.IsZero) return b; if (b.IsZero) return a;
            return new(Math.BitDecrement(a.Low + b.Low), Math.BitIncrement(a.High + b.High));
        }
        public static Interval operator -(Interval a, Interval b)
        {
            if (b.IsZero) return a; if (a.IsZero) return new(-b.High, -b.Low);
            return new(Math.BitDecrement(a.Low - b.High), Math.BitIncrement(a.High - b.Low));
        }
        public static Interval operator *(Interval a, Interval b)
        {
            if ((a.IsZero && b.Finite) || (b.IsZero && a.Finite)) return Point(0);
            if (a.Low == 1 && a.High == 1) return b;
            if (b.Low == 1 && b.High == 1) return a;
            var aa = a.Low * b.Low; var ab = a.Low * b.High;
            var ba = a.High * b.Low; var bb = a.High * b.High;
            return new(Math.BitDecrement(Math.Min(Math.Min(aa, ab), Math.Min(ba, bb))),
                Math.BitIncrement(Math.Max(Math.Max(aa, ab), Math.Max(ba, bb))));
        }
        internal Interval DividePositive(double value)
            => new(Math.BitDecrement(Low / value), Math.BitIncrement(High / value));
        internal Interval Square()
        {
            if (IsZero) return Point(0);
            var low = DistanceFromZero; var high = Magnitude;
            return new(Math.Max(0, Math.BitDecrement(low * low)), Math.BitIncrement(high * high));
        }
        internal static Interval Mean(Interval a, Interval b)
        {
            if (a == b) return a;
            var sum = a + b;
            return new(Math.BitDecrement(sum.Low * .5), Math.BitIncrement(sum.High * .5));
        }
        internal bool Finite => double.IsFinite(Low) && double.IsFinite(High);
        internal bool IsZero => Low == 0 && High == 0;
        internal double Width => Math.BitIncrement(High - Low);
        internal double Magnitude => Math.Max(Math.Abs(Low), Math.Abs(High));
        internal double DistanceFromZero => Low > 0 ? Low : High < 0 ? -High : 0;
    }

    internal readonly record struct VectorInterval(Interval X, Interval Y, Interval Z)
    {
        internal static VectorInterval Point(ShellVector p) => new(Interval.Point(p.X), Interval.Point(p.Y), Interval.Point(p.Z));
        internal static VectorInterval Lerp(in ShellVertexMotion p, Interval t)
            => new(Interval.Lerp(p.Start.X, p.End.X, t), Interval.Lerp(p.Start.Y, p.End.Y, t), Interval.Lerp(p.Start.Z, p.End.Z, t));
        internal static VectorInterval Difference(ShellVector a, ShellVector b)
            => new(Interval.Difference(a.X, b.X), Interval.Difference(a.Y, b.Y), Interval.Difference(a.Z, b.Z));
        public static VectorInterval operator +(VectorInterval a, VectorInterval b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static VectorInterval operator -(VectorInterval a, VectorInterval b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static VectorInterval operator *(VectorInterval a, Interval b) => new(a.X * b, a.Y * b, a.Z * b);
        internal static VectorInterval Mean(VectorInterval a, VectorInterval b)
            => new(Interval.Mean(a.X, b.X), Interval.Mean(a.Y, b.Y), Interval.Mean(a.Z, b.Z));
        internal VectorInterval Hull(VectorInterval b) => new(
            new(Math.Min(X.Low, b.X.Low), Math.Max(X.High, b.X.High)),
            new(Math.Min(Y.Low, b.Y.Low), Math.Max(Y.High, b.Y.High)),
            new(Math.Min(Z.Low, b.Z.Low), Math.Max(Z.High, b.Z.High)));
        internal bool Finite => X.Finite && Y.Finite && Z.Finite;
        internal double MaximumWidth => Math.Max(X.Width, Math.Max(Y.Width, Z.Width));
        internal double Magnitude => Math.Max(X.Magnitude, Math.Max(Y.Magnitude, Z.Magnitude));
        internal bool Inside(double distance)
        {
            var x = X.Magnitude; var y = Y.Magnitude; var z = Z.Magnitude;
            var square = Math.BitIncrement(Math.BitIncrement(Math.BitIncrement(x * x) + Math.BitIncrement(y * y)) + Math.BitIncrement(z * z));
            return square <= Math.BitDecrement(distance * distance);
        }
        internal bool Separated(double distance)
        {
            var x = X.DistanceFromZero; var y = Y.DistanceFromZero; var z = Z.DistanceFromZero;
            if (Math.Max(x, Math.Max(y, z)) > distance) return true;
            // Euclidean distance to the hull, rounded downward; compare with an
            // upward bound for squared clearance. This also prunes diagonal gaps.
            var d = Math.BitDecrement(Math.BitDecrement(Math.BitDecrement(x * x) + Math.BitDecrement(y * y)) + Math.BitDecrement(z * z));
            return d > Math.BitIncrement(distance * distance);
        }
    }

    [InlineArray(8)] internal struct ControlPoints { VectorInterval first; }
    internal struct Box
    {
        internal double T0, T1, U0, U1, V0, V1;
        internal long Order;
        // Corner index = 4*time + 2*u + v. The triangle additionally requires u+v<=1.
        internal ControlPoints Points;
        internal readonly double Lower(int axis) => axis == 0 ? T0 : axis == 1 ? U0 : V0;
        internal readonly double Upper(int axis) => axis == 0 ? T1 : axis == 1 ? U1 : V1;
        internal void SetLower(int axis, double value) { if (axis == 0) T0 = value; else if (axis == 1) U0 = value; else V0 = value; }
        internal void SetUpper(int axis, double value) { if (axis == 0) T1 = value; else if (axis == 1) U1 = value; else V1 = value; }
    }

    /// <summary>Bounded reusable worker scratch, with zero allocation per query.</summary>
    public sealed class Workspace
    {
        internal readonly Box[] Heap;
        internal int Count;
        internal long Sequence;
        public int Capacity => Heap.Length;
        public int PeakBoxes { get; internal set; }
        public int AccountedBytes => checked(64 + Unsafe.SizeOf<Box>() * Capacity);
        public Workspace(int capacity = 1024)
        {
            if (capacity < 2 || capacity > 65536) throw new ArgumentOutOfRangeException(nameof(capacity));
            Heap = new Box[capacity];
        }
        static bool Before(in Box a, in Box b)
            => a.T0 < b.T0 || (a.T0 == b.T0 && (a.T1 < b.T1 || (a.T1 == b.T1 && a.Order < b.Order)));
        internal void Push(Box box)
        {
            box.Order = Sequence++;
            var i = Count++;
            while (i > 0)
            {
                var parent = (i - 1) / 2;
                if (!Before(box, Heap[parent])) break;
                Heap[i] = Heap[parent]; i = parent;
            }
            Heap[i] = box; PeakBoxes = Math.Max(PeakBoxes, Count);
        }
        internal Box Pop()
        {
            var result = Heap[0]; var tail = Heap[--Count];
            var i = 0;
            while (2 * i + 1 < Count)
            {
                var child = 2 * i + 1;
                if (child + 1 < Count && Before(Heap[child + 1], Heap[child])) ++child;
                if (!Before(Heap[child], tail)) break;
                Heap[i] = Heap[child]; i = child;
            }
            if (Count > 0) Heap[i] = tail;
            return result;
        }
    }

    public static ShellCcdResult VertexFace(in ShellVertexMotion vertex, in ShellVertexMotion a,
        in ShellVertexMotion b, in ShellVertexMotion c, Workspace workspace, ShellCcdSettings settings)
        => Query(vertex, a, b, c, true, workspace, settings);

    public static ShellCcdResult EdgeEdge(in ShellVertexMotion a0, in ShellVertexMotion a1,
        in ShellVertexMotion b0, in ShellVertexMotion b1, Workspace workspace, ShellCcdSettings settings)
        => Query(a0, a1, b0, b1, false, workspace, settings);

    static ShellCcdResult Query(in ShellVertexMotion a, in ShellVertexMotion b,
        in ShellVertexMotion c, in ShellVertexMotion d, bool triangle, Workspace w, ShellCcdSettings settings)
    {
        settings.Validate(); w.Count = w.PeakBoxes = 0; w.Sequence = 0;
        var root = new Box { T1 = 1, U1 = 1, V1 = 1 };
        Fill(ref root.Points, 0, a.Start, b.Start, c.Start, d.Start, triangle);
        Fill(ref root.Points, 4, a.End, b.End, c.End, d.End, triangle);
        w.Push(root); var visited = 0;
        while (w.Count > 0)
        {
            if (visited == settings.MaximumBoxes)
                return Stop(ShellCcdStatus.Uncertified, w.Heap[0], visited, ShellCcdLimit.WorkBudget);
            var box = w.Pop(); ++visited;
            if (triangle && box.U0 + box.V0 > 1) continue;
            var bounds = box.Points[0];
            for (var i = 1; i < 8; ++i) bounds = bounds.Hull(box.Points[i]);
            if (!bounds.Finite) return Stop(ShellCcdStatus.Uncertified, box, visited, ShellCcdLimit.NumericalResolution);
            if (bounds.Separated(settings.MinimumSeparation)) continue;
            // A material corner certified inside the requested clearance is a
            // witness even when the surrounding spatial box is large. Refine
            // its time interval instead of filling a volume with tiny boxes.
            var witness = settings.MinimumSeparation > 0 && HasInteriorWitness(box, triangle, settings.MinimumSeparation);
            if ((witness || bounds.MaximumWidth <= settings.SpatialTolerance) && box.T1 - box.T0 <= settings.TimeTolerance)
                return Stop(ShellCcdStatus.PotentialContact, box, visited, ShellCcdLimit.None);
            var axis = witness ? 0 : ChooseAxis(box, settings.SpatialTolerance, settings.TimeTolerance);
            var midpoint = (box.Lower(axis) + box.Upper(axis)) * .5;
            if (!(midpoint > box.Lower(axis) && midpoint < box.Upper(axis)))
                return Stop(ShellCcdStatus.Uncertified, box, visited, ShellCcdLimit.NumericalResolution);
            if (w.Count + 2 > w.Capacity)
                return Stop(ShellCcdStatus.Uncertified, box, visited, ShellCcdLimit.WorkspaceCapacity);
            var left = box; var right = box;
            left.SetUpper(axis, midpoint); right.SetLower(axis, midpoint);
            var bit = 4 >> axis;
            for (var i = 0; i < 8; ++i) if ((i & bit) == 0)
            {
                var mean = VectorInterval.Mean(box.Points[i], box.Points[i | bit]);
                left.Points[i | bit] = mean; right.Points[i] = mean;
            }
            w.Push(left); w.Push(right);
        }
        return new(ShellCcdStatus.Clear, 1, 1, visited, ShellCcdLimit.None);
    }

    static ShellCcdResult Stop(ShellCcdStatus status, in Box box, int visited, ShellCcdLimit limit)
        => new(status, box.T0 <= 0 ? 0 : Math.BitDecrement(box.T0), box.T1, visited, limit);

    static bool HasInteriorWitness(in Box box, bool triangle, double separation)
    {
        for (var i = 0; i < 8; ++i)
        {
            var u = (i & 2) == 0 ? box.U0 : box.U1;
            var v = (i & 1) == 0 ? box.V0 : box.V1;
            if (triangle && Math.BitIncrement(u + v) > 1) continue;
            if (box.Points[i].Inside(separation)) return true;
        }
        return false;
    }

    static int ChooseAxis(in Box box, double spatialTolerance, double timeTolerance)
    {
        var chosen = 0; var maximum = -1.0;
        for (var axis = 0; axis < 3; ++axis)
        {
            var bit = 4 >> axis; var variation = 0.0;
            for (var i = 0; i < 8; ++i) if ((i & bit) == 0)
                variation = Math.Max(variation, (box.Points[i | bit] - box.Points[i]).Magnitude);
            if (variation > maximum) { maximum = variation; chosen = axis; }
        }
        // A nearly constant feature map still needs a bounded time interval.
        return maximum <= spatialTolerance && box.T1 - box.T0 > timeTolerance ? 0 : chosen;
    }

    static void Fill(ref ControlPoints points, int offset, ShellVector a, ShellVector b,
        ShellVector c, ShellVector d, bool triangle)
    {
        if (triangle)
        {
            points[offset] = VectorInterval.Difference(a, b);
            points[offset + 2] = VectorInterval.Difference(a, c);
            points[offset + 1] = VectorInterval.Difference(a, d);
            points[offset + 3] = points[offset + 2] + points[offset + 1] - points[offset];
        }
        else
        {
            points[offset] = VectorInterval.Difference(a, c);
            points[offset + 1] = VectorInterval.Difference(a, d);
            points[offset + 2] = VectorInterval.Difference(b, c);
            points[offset + 3] = VectorInterval.Difference(b, d);
        }
    }
}
