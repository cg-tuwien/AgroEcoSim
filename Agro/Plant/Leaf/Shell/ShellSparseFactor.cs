using System.Numerics;

namespace Agro.Mechanics;

/// <summary>Reusable numeric Cholesky workspace over a shared deterministic shell pattern.</summary>
internal sealed class ShellSparseFactor
{
    readonly ShellFactorPattern pattern;
    readonly int[] order, starts, rows, matrixStarts, matrixRows, elementSlots;
    readonly int[] heads, nextColumn, nextEntry, active, nextRun;
    readonly double[] values, factor, work, columnWork;
    readonly BacShell shell;
    readonly long symbolicPayloadBytes, maximumNumericPayloadBytes;
    ShellParallelWork? parallelWork;
    int parallelColumn, parallelCount, parallelWorkers;
    internal int ParallelColumns { get; private set; }
    internal int PeakParallelWorkers { get; private set; }
    internal long ParallelWorkThreshold { get; set; } = 1 << 20;
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, ShellFactorPattern> Patterns = new();
    // NativeAOT's generic Array.Sort path can allocate a comparison delegate on
    // each column. The comparison overload with one cached delegate avoids it.
    static readonly Comparison<int> CompareColumns = static (a, b) => a.CompareTo(b);

    internal long NumericPayloadBytes => ((long)values.Length + factor.Length + work.Length + columnWork.Length) * sizeof(double)
        + ((long)heads.Length + nextColumn.Length + nextEntry.Length + active.Length + nextRun.Length) * sizeof(int)
        + (parallelWork?.ArrayPayloadBytes ?? 0);
    internal long SymbolicPayloadBytes => symbolicPayloadBytes;

    internal ShellSparseFactor(BacShell shell, long maximumNumericPayloadBytes = long.MaxValue, ShellFactorProfile? profile = null)
    {
        if (profile != null && !ReferenceEquals(profile.Shell, shell))
            throw new ArgumentException("The factor profile belongs to another shell.", nameof(profile));
        this.shell = shell; this.maximumNumericPayloadBytes = maximumNumericPayloadBytes;
        pattern = profile?.Pattern ?? Patterns.GetValue(shell.TopologyIdentity, _ => new(shell));
        symbolicPayloadBytes = profile?.ArrayPayloadBytes ?? pattern.ArrayPayloadBytes;
        order = pattern.Order; starts = pattern.Starts; rows = pattern.Rows;
        matrixStarts = pattern.MatrixStarts; matrixRows = pattern.MatrixRows;
        elementSlots = pattern.ElementSlots;
        if (maximumNumericPayloadBytes < ((long)matrixRows.Length + rows.Length + 2L * order.Length) * sizeof(double)
            + 5L * order.Length * sizeof(int))
            throw new ArgumentException("Sparse factor exceeds the explicit numeric worker payload budget.", nameof(maximumNumericPayloadBytes));
        values = new double[matrixRows.Length]; factor = new double[rows.Length];
        work = new double[order.Length]; columnWork = new double[order.Length];
        heads = new int[order.Length]; nextColumn = new int[order.Length];
        nextEntry = new int[order.Length]; active = new int[order.Length]; nextRun = new int[order.Length];
    }

    internal void Clear() => Array.Clear(values);

    /// <summary>Symmetric diagonal equilibration, without adding stiffness.</summary>
    internal bool TryEquilibrate(Span<double> scale)
    {
        if (scale.Length != order.Length) throw new ArgumentException("Incorrect factor scale size.");
        for (var i = 0; i < order.Length; ++i)
        {
            var diagonal = values[matrixStarts[order[i]]];
            if (!(diagonal > 0) || !double.IsFinite(diagonal)) return false;
            scale[i] = work[order[i]] = 1 / Math.Sqrt(diagonal);
        }
        for (var j = 0; j < order.Length; ++j)
            for (var k = matrixStarts[j]; k < matrixStarts[j + 1]; ++k)
                values[k] *= work[j] * work[matrixRows[k]];
        return true;
    }
    internal void Add(int i, int j, double value)
    {
        values[pattern.Slot(i, j)] += value;
    }

    internal void AddElement(int face, ReadOnlySpan<double> block, ReadOnlySpan<bool> fixedDofs)
    {
        const int m = BacShell.ElementDofs;
        var map = shell.ElementMap(face); var entry = pattern.ElementStarts[face];
        for (var i = 0; i < m; ++i)
        {
            var bits = (uint)pattern.ElementRowMasks[face * m + i];
            if (fixedDofs[map[i]]) { entry += BitOperations.PopCount(bits); continue; }
            for (; bits != 0; bits &= bits - 1)
            {
                var j = BitOperations.TrailingZeroCount(bits);
                var index = elementSlots[entry++];
                if (!fixedDofs[map[j]]) values[index] += block[i * m + j];
            }
        }
    }

    internal int BatchedColumnGroups { get; private set; }

    internal bool Factor(int maximumWorkers = 1)
    {
        if (maximumWorkers < 1) throw new ArgumentOutOfRangeException(nameof(maximumWorkers));
        if (ParallelWorkThreshold < 1) throw new ArgumentOutOfRangeException(nameof(ParallelWorkThreshold));
        BatchedColumnGroups = ParallelColumns = 0; PeakParallelWorkers = 1;
        Array.Fill(heads, -1);
        Array.Copy(pattern.RunOffsets, nextRun, order.Length);
        for (var k = 0; k < order.Length; ++k)
        {
            var first = starts[k]; var end = starts[k + 1];
            for (var p = first; p < end; ++p) columnWork[rows[p]] = 0;
            for (var p = matrixStarts[k]; p < matrixStarts[k + 1]; ++p) columnWork[matrixRows[p]] = values[p];
            // Stream the row traversal with one cursor per factor column instead
            // of storing two integers for every off-diagonal fill entry. Sorting
            // active columns preserves the previous ascending accumulation order.
            var count = 0; long updateWork = 0;
            for (var j = heads[k]; j >= 0; j = nextColumn[j])
            { active[count++] = j; updateWork += starts[j + 1] - nextEntry[j]; }
            active.AsSpan(0, count).Sort(CompareColumns);
            var workers = updateWork >= ParallelWorkThreshold
                ? Math.Min(Math.Min(maximumWorkers, 32), Math.Min((end - first) / 64,
                    (int)Math.Min(32, updateWork / Math.Max(1, ParallelWorkThreshold / 4)))) : 1;
            if (workers > 1)
            {
                if (parallelWork == null)
                {
                    if (NumericPayloadBytes + 32L * IntPtr.Size > maximumNumericPayloadBytes)
                        throw new ArgumentException("Parallel factor dispatch exceeds the explicit worker array budget.");
                    parallelWork = new(32, ApplyParallelRows);
                }
                parallelColumn = k; parallelCount = count; parallelWorkers = workers;
                parallelWork.Run(workers);
                ++ParallelColumns; PeakParallelWorkers = Math.Max(PeakParallelWorkers, workers);
                // No cursors or linked lists are mutated while workers read them.
                for (var r = 0; r < count; ++r)
                {
                    var j = active[r]; var entry = nextEntry[j]; var run = nextRun[j];
                    while (run < pattern.RunOffsets[j + 1] && pattern.RunEnds[run] <= entry) ++run;
                    nextRun[j] = run; Schedule(j, entry + 1);
                }
            }
            else for (var r = 0; r < count; ++r)
            {
                if (r + 3 < count && TrySubtractFour(active[r], active[r + 1], active[r + 2], active[r + 3]))
                {
                    for (var b = 0; b < 4; ++b) { var column = active[r + b]; Schedule(column, nextEntry[column] + 1); }
                    r += 3;
                    continue;
                }
                var j = active[r]; var entry = nextEntry[j]; var multiplier = factor[entry];
                // Clamped DOFs retain the shared symbolic pattern but introduce
                // exact numeric zeros. They contribute no rank-one update.
                if (multiplier != 0) SubtractColumn(j, entry, multiplier);
                Schedule(j, entry + 1);
            }
            var d = columnWork[k];
            if (!(d > 0) || !double.IsFinite(d)) return false;
            factor[first] = Math.Sqrt(d);
            for (var p = first + 1; p < end; ++p) factor[p] = columnWork[rows[p]] / factor[first];
            Schedule(k, first + 1);
        }
        return true;
    }

    // Destination rows are disjoint; every worker visits source columns in the
    // same sorted order as serial factorization. All source factors are complete.
    void ApplyParallelRows(int worker)
    {
        var start = starts[parallelColumn]; var count = starts[parallelColumn + 1] - start;
        var begin = start + (int)((long)count * worker / parallelWorkers);
        var end = start + (int)((long)count * (worker + 1) / parallelWorkers);
        var low = rows[begin]; var high = rows[end - 1] + 1;
        for (var r = 0; r < parallelCount; ++r)
        {
            var column = active[r]; var entry = nextEntry[column]; var multiplier = factor[entry];
            if (multiplier == 0) continue;
            var last = starts[column + 1];
            var a = LowerBound(rows, entry, last, low); var b = LowerBound(rows, a, last, high);
            if (a < b) SubtractColumn(column, a, multiplier, b, updateCursor: false);
        }
    }

    static int LowerBound(int[] values, int start, int end, int value)
    {
        while (start < end)
        {
            var middle = start + (end - start) / 2;
            if (values[middle] < value) start = middle + 1; else end = middle;
        }
        return start;
    }

    void Schedule(int column, int entry)
    {
        if (entry >= starts[column + 1]) return;
        var row = rows[entry]; nextEntry[column] = entry;
        nextColumn[column] = heads[row]; heads[row] = column;
    }

    // Only combine a common, contiguous suffix. Prefixes stay sparse. Each row
    // receives exactly the old ascending-column sequence of multiply/subtracts;
    // there is no dot-product reassociation or change to the factorization.
    bool TrySubtractFour(int a, int b, int c, int d)
    {
        var ra = pattern.RunOffsets[a + 1] - 1; var rb = pattern.RunOffsets[b + 1] - 1;
        var rc = pattern.RunOffsets[c + 1] - 1; var rd = pattern.RunOffsets[d + 1] - 1;
        if (ra < pattern.RunOffsets[a] || rb < pattern.RunOffsets[b]
            || rc < pattern.RunOffsets[c] || rd < pattern.RunOffsets[d]) return false;
        var ea = starts[a + 1]; var eb = starts[b + 1]; var ec = starts[c + 1]; var ed = starts[d + 1];
        if (pattern.RunEnds[ra] != ea || pattern.RunEnds[rb] != eb
            || pattern.RunEnds[rc] != ec || pattern.RunEnds[rd] != ed) return false;
        var last = rows[ea - 1];
        if (rows[eb - 1] != last || rows[ec - 1] != last || rows[ed - 1] != last) return false;
        var ia = nextEntry[a]; var ib = nextEntry[b]; var ic = nextEntry[c]; var id = nextEntry[d];
        var first = Math.Max(Math.Max(rows[Math.Max(ia, pattern.RunStarts[ra])], rows[Math.Max(ib, pattern.RunStarts[rb])]),
            Math.Max(rows[Math.Max(ic, pattern.RunStarts[rc])], rows[Math.Max(id, pattern.RunStarts[rd])]));
        var count = last - first + 1;
        if (count < 32) return false;
        var ma = factor[ia]; var mb = factor[ib]; var mc = factor[ic]; var md = factor[id];
        if (ma == 0 || mb == 0 || mc == 0 || md == 0) return false;
        var pa = ea - count; var pb = eb - count; var pc = ec - count; var pd = ed - count;
        SubtractColumn(a, ia, ma, pa); SubtractColumn(b, ib, mb, pb);
        SubtractColumn(c, ic, mc, pc); SubtractColumn(d, id, md, pd);
        var va = new Vector<double>(ma); var vb = new Vector<double>(mb);
        var vc = new Vector<double>(mc); var vd = new Vector<double>(md);
        var i = 0;
        if (Vector.IsHardwareAccelerated)
            for (; i <= count - Vector<double>.Count; i += Vector<double>.Count)
            {
                var value = new Vector<double>(columnWork, first + i);
                value -= va * new Vector<double>(factor, pa + i);
                value -= vb * new Vector<double>(factor, pb + i);
                value -= vc * new Vector<double>(factor, pc + i);
                value -= vd * new Vector<double>(factor, pd + i);
                value.CopyTo(columnWork, first + i);
            }
        for (; i < count; ++i)
        {
            var value = columnWork[first + i];
            value -= ma * factor[pa + i]; value -= mb * factor[pb + i];
            value -= mc * factor[pc + i]; value -= md * factor[pd + i];
            columnWork[first + i] = value;
        }
        ++BatchedColumnGroups;
        return true;
    }

    void SubtractColumn(int column, int entry, double multiplier) =>
        SubtractColumn(column, entry, multiplier, starts[column + 1]);

    void SubtractColumn(int column, int entry, double multiplier, int end, bool updateCursor = true)
    {
        var run = nextRun[column]; var runEnd = pattern.RunOffsets[column + 1];
        while (run < runEnd && pattern.RunEnds[run] <= entry) ++run;
        if (updateCursor) nextRun[column] = run;
        var scale = new Vector<double>(multiplier);
        for (; run < runEnd && entry < end; ++run)
        {
            var first = Math.Min(end, Math.Max(entry, pattern.RunStarts[run]));
            for (; entry < first; ++entry) columnWork[rows[entry]] -= multiplier * factor[entry];
            if (entry == end) break;
            var last = Math.Min(end, pattern.RunEnds[run]); var row = rows[entry];
            if (Vector.IsHardwareAccelerated)
                for (; entry <= last - Vector<double>.Count; entry += Vector<double>.Count, row += Vector<double>.Count)
                {
                    // Independent rows; preserve separate multiply/subtract and
                    // ascending column accumulation, with no horizontal reduction.
                    var update = scale * new Vector<double>(factor, entry);
                    (new Vector<double>(columnWork, row) - update).CopyTo(columnWork, row);
                }
            for (; entry < last; ++entry, ++row) columnWork[row] -= multiplier * factor[entry];
        }
        for (; entry < end; ++entry) columnWork[rows[entry]] -= multiplier * factor[entry];
    }

    internal void Solve(ReadOnlySpan<double> rhs, Span<double> result)
    {
        for (var i = 0; i < order.Length; ++i) work[order[i]] = rhs[i];
        for (var i = 0; i < order.Length; ++i)
        {
            var first = starts[i]; var v = work[i] /= factor[first];
            for (var k = first + 1; k < starts[i + 1]; ++k) work[rows[k]] -= factor[k] * v;
        }
        for (var i = order.Length - 1; i >= 0; --i)
        {
            var first = starts[i]; var v = work[i];
            for (var k = first + 1; k < starts[i + 1]; ++k) v -= factor[k] * work[rows[k]];
            work[i] = v / factor[first];
        }
        for (var i = 0; i < order.Length; ++i) result[i] = work[order[i]];
    }
}
