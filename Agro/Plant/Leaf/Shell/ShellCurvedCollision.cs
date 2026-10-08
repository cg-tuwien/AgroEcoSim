using System.Runtime.CompilerServices;
using I = Agro.Mechanics.ShellContinuousCollision.Interval;
using V = Agro.Mechanics.ShellContinuousCollision.VectorInterval;

namespace Agro.Mechanics;

/// <summary>
/// Conservative inclusion queries for the composed paths in ShellSupportMotion.
/// Time subdivision reevaluates interval rotations; spatial subdivision uses the
/// affine feature coordinates. Endpoint chords never stand in for the curved path.
/// This is a contact predicate, not a force law or an equilibrium solver.
/// </summary>
public static class ShellCurvedCollision
{
    [InlineArray(4)] internal struct Corners { V first; }
    internal struct Box
    {
        internal double T0, T1, U0, U1, V0, V1, UScale, VScale;
        internal long Order;
        internal bool Enclosed;
        internal Corners Points;
        internal readonly double Lower(int axis) => axis == 0 ? T0 : axis == 1 ? U0 : V0;
        internal readonly double Upper(int axis) => axis == 0 ? T1 : axis == 1 ? U1 : V1;
        internal void Lower(int axis, double x) { if (axis == 0) T0 = x; else if (axis == 1) U0 = x; else V0 = x; }
        internal void Upper(int axis, double x) { if (axis == 0) T1 = x; else if (axis == 1) U1 = x; else V1 = x; }
    }

    public sealed class Workspace
    {
        internal struct CachedTransform
        {
            internal double From, To;
            internal ShellSupportMotion.Transform A, B;
            internal bool Enclosed;
        }
        internal readonly Box[] Heap;
        internal readonly ShellContinuousCollision.Workspace Linear;
        readonly CachedTransform[] transforms = new CachedTransform[64];
        ShellSupportMotion cachedA, cachedB;
        ulong validTransforms;
        bool supportsCached;
        internal int Count;
        internal long Sequence;
        public int Capacity => Heap.Length;
        public int PeakBoxes { get; internal set; }
        public int TransformEvaluations { get; internal set; }
        public int TransformCacheHits { get; internal set; }
        /// <summary>Own worker storage; excludes the caller-supplied linear workspace.</summary>
        public int AccountedBytes => checked(120 + 2 * Unsafe.SizeOf<ShellSupportMotion>()
            + 64 * Unsafe.SizeOf<CachedTransform>() + Unsafe.SizeOf<Box>() * Capacity);
        public Workspace(ShellContinuousCollision.Workspace linear, int capacity = 1024)
        {
            ArgumentNullException.ThrowIfNull(linear);
            if (capacity < 2 || capacity > 65536) throw new ArgumentOutOfRangeException(nameof(capacity));
            Linear = linear; Heap = new Box[capacity];
        }
        /// <summary>Discard only reusable kinematics; results must not depend on this cache.</summary>
        public void ResetTransformCache() { validTransforms = 0; supportsCached = false; }

        internal bool Transform(in ShellSupportMotion a, in ShellSupportMotion b, double from, double to,
            out ShellSupportMotion.Transform ta, out ShellSupportMotion.Transform tb)
        {
            if (!supportsCached || cachedA != a || cachedB != b)
            { cachedA = a; cachedB = b; supportsCached = true; validTransforms = 0; }
            var hash = unchecked(((ulong)BitConverter.DoubleToInt64Bits(from) ^
                ((ulong)BitConverter.DoubleToInt64Bits(to) * 0x9E3779B97F4A7C15UL)) * 0xD6E8FEB86659FD93UL);
            var slot = (int)(hash >> 58); ref var entry = ref transforms[slot]; var bit = 1UL << slot;
            if ((validTransforms & bit) != 0 && entry.From == from && entry.To == to)
            { ++TransformCacheHits; ta = entry.A; tb = entry.B; return entry.Enclosed; }
            TransformEvaluations += 2; var time = new I(from, to);
            var okA = a.TryTransform(time, out ta); var okB = b.TryTransform(time, out tb);
            entry = new() { From = from, To = to, A = ta, B = tb, Enclosed = okA && okB };
            validTransforms |= bit; return entry.Enclosed;
        }
        static bool Before(in Box a, in Box b)
            => a.T0 < b.T0 || (a.T0 == b.T0 && (a.T1 < b.T1 || (a.T1 == b.T1 && a.Order < b.Order)));
        internal void Push(Box box)
        {
            box.Order = Sequence++; var i = Count++;
            while (i > 0)
            {
                var p = (i - 1) / 2; if (!Before(box, Heap[p])) break;
                Heap[i] = Heap[p]; i = p;
            }
            Heap[i] = box; PeakBoxes = Math.Max(PeakBoxes, Count);
        }
        internal Box Pop()
        {
            var answer = Heap[0]; var tail = Heap[--Count]; var i = 0;
            while (2 * i + 1 < Count)
            {
                var child = 2 * i + 1;
                if (child + 1 < Count && Before(Heap[child + 1], Heap[child])) ++child;
                if (!Before(Heap[child], tail)) break;
                Heap[i] = Heap[child]; i = child;
            }
            if (Count > 0) Heap[i] = tail;
            return answer;
        }
    }

    public static ShellCcdResult VertexFace(in ShellVertexMotion vertex, in ShellSupportMotion vertexSupport,
        in ShellVertexMotion a, in ShellVertexMotion b, in ShellVertexMotion c, in ShellSupportMotion faceSupport,
        Workspace workspace, ShellCcdSettings settings)
        => Query(vertex, a, b, c, vertexSupport, faceSupport, true, workspace, settings);

    public static ShellCcdResult EdgeEdge(in ShellVertexMotion a0, in ShellVertexMotion a1, in ShellSupportMotion aSupport,
        in ShellVertexMotion b0, in ShellVertexMotion b1, in ShellSupportMotion bSupport,
        Workspace workspace, ShellCcdSettings settings)
        => Query(a0, a1, b0, b1, aSupport, bSupport, false, workspace, settings);

    static ShellCcdResult Query(in ShellVertexMotion a, in ShellVertexMotion b, in ShellVertexMotion c,
        in ShellVertexMotion d, ShellSupportMotion sa, ShellSupportMotion sb, bool triangle, Workspace w, ShellCcdSettings settings)
    {
        settings.Validate(); w.Count = w.PeakBoxes = w.TransformEvaluations = w.TransformCacheHits = 0; w.Sequence = 0;
        if (!sa.Valid || !sb.Valid) return new(ShellCcdStatus.Uncertified, 0, 1, 0, ShellCcdLimit.NumericalResolution);
        // The entire common transported frame preserves distances at every time,
        // including changing length. Remove it before any numerical enclosure.
        if (sa == sb)
            return triangle ? ShellContinuousCollision.VertexFace(a, b, c, d, w.Linear, settings)
                : ShellContinuousCollision.EdgeEdge(a, b, c, d, w.Linear, settings);
        ShellSupportMotion.RemoveCommonBase(ref sa, ref sb);
        var root = new Box { T1 = 1, U1 = 1, V1 = 1 };
        Fill(ref root, a, b, c, d, sa, sb, triangle, w); w.Push(root); var visited = 0;
        while (w.Count > 0)
        {
            if (visited == settings.MaximumBoxes) return Stop(w.Heap[0], visited, ShellCcdLimit.WorkBudget);
            var box = w.Pop(); ++visited;
            if (triangle && box.U0 + box.V0 > 1) continue;
            var axis = 0;
            if (box.Enclosed)
            {
                var bounds = box.Points[0]; var timeScore = bounds.MaximumWidth;
                var witness = false;
                for (var i = 0; i < 4; ++i)
                {
                    bounds = bounds.Hull(box.Points[i]); timeScore = Math.Max(timeScore, box.Points[i].MaximumWidth);
                    var u = (i & 2) == 0 ? box.U0 : box.U1; var v = (i & 1) == 0 ? box.V0 : box.V1;
                    if (settings.MinimumSeparation > 0 && (!triangle || Math.BitIncrement(u + v) <= 1)
                        && box.Points[i].Inside(settings.MinimumSeparation)) witness = true;
                }
                if (bounds.Separated(settings.MinimumSeparation)) continue;
                if ((witness || bounds.MaximumWidth <= settings.SpatialTolerance) && box.T1 - box.T0 <= settings.TimeTolerance)
                    return Stop(box, visited, ShellCcdLimit.None);
                if (!witness)
                {
                    var uScore = (box.U1 - box.U0) * box.UScale; var vScore = (box.V1 - box.V0) * box.VScale;
                    if (uScore > timeScore) { axis = 1; timeScore = uScore; }
                    if (vScore > timeScore) axis = 2;
                }
            }
            else if (box.T1 - box.T0 <= settings.TimeTolerance)
                return Stop(box, visited, ShellCcdLimit.NumericalResolution);
            var midpoint = (box.Lower(axis) + box.Upper(axis)) * .5;
            if (!(midpoint > box.Lower(axis) && midpoint < box.Upper(axis))) return Stop(box, visited, ShellCcdLimit.NumericalResolution);
            if (w.Count + 2 > w.Capacity) return Stop(box, visited, ShellCcdLimit.WorkspaceCapacity);
            var left = box; var right = box; left.Upper(axis, midpoint); right.Lower(axis, midpoint);
            if (axis == 0)
            {
                Fill(ref left, a, b, c, d, sa, sb, triangle, w);
                Fill(ref right, a, b, c, d, sa, sb, triangle, w);
            }
            else
            {
                var bit = 2 >> (axis - 1);
                for (var i = 0; i < 4; ++i) if ((i & bit) == 0)
                {
                    var mean = V.Mean(box.Points[i], box.Points[i | bit]);
                    left.Points[i | bit] = mean; right.Points[i] = mean;
                }
            }
            w.Push(left); w.Push(right);
        }
        return new(ShellCcdStatus.Clear, 1, 1, visited, ShellCcdLimit.None);
    }

    static ShellCcdResult Stop(in Box box, int visits, ShellCcdLimit limit)
        => new(limit == ShellCcdLimit.None ? ShellCcdStatus.PotentialContact : ShellCcdStatus.Uncertified,
            box.T0 <= 0 ? 0 : Math.BitDecrement(box.T0), box.T1, visits, limit);

    static void Fill(ref Box box, in ShellVertexMotion a, in ShellVertexMotion b, in ShellVertexMotion c,
        in ShellVertexMotion d, in ShellSupportMotion sa, in ShellSupportMotion sb, bool triangle, Workspace w)
    {
        box.Enclosed = false; var time = new I(box.T0, box.T1);
        if (!w.Transform(sa, sb, box.T0, box.T1, out var ta, out var tb)) return;
        var pa = ta.Point(V.Lerp(a, time)); var pb = (triangle ? tb : ta).Point(V.Lerp(b, time));
        var pc = tb.Point(V.Lerp(c, time)); var pd = tb.Point(V.Lerp(d, time));
        if (!pa.Finite || !pb.Finite || !pc.Finite || !pd.Finite) return;
        var origin = pa - (triangle ? pb : pc);
        var u = triangle ? pb - pc : pb - pa; var v = triangle ? pb - pd : pc - pd;
        box.UScale = u.Magnitude; box.VScale = v.Magnitude;
        for (var i = 0; i < 4; ++i)
        {
            var su = I.Point((i & 2) == 0 ? box.U0 : box.U1); var sv = I.Point((i & 1) == 0 ? box.V0 : box.V1);
            box.Points[i] = origin + u * su + v * sv;
            if (!box.Points[i].Finite) return;
        }
        box.Enclosed = true;
    }
}
