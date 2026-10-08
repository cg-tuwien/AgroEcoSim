namespace Agro.Mechanics;

/// <summary>Immutable ordering, elimination tree and scatter bindings shared by a rest profile.</summary>
internal sealed class ShellFactorPattern
{
    internal readonly int[] Order, Starts, Rows, MatrixStarts, MatrixRows, ElementSlots;
    internal readonly int[] ElementStarts, ElementRowMasks;
    // Long consecutive row runs permit contiguous double-precision updates.
    // Shared topology metadata, not per-leaf state; short runs stay scalar.
    internal readonly int[] RunOffsets, RunStarts, RunEnds;
    internal long ArrayPayloadBytes => ((long)Order.Length + Starts.Length + Rows.Length + MatrixStarts.Length
        + MatrixRows.Length + ElementSlots.Length + ElementStarts.Length + ElementRowMasks.Length
        + RunOffsets.Length + RunStarts.Length + RunEnds.Length) * sizeof(int);

    internal ShellFactorPattern(BacShell shell, ReadOnlySpan<bool> prescribed = default)
    {
        var n = shell.DofCount;
        // Prescribed increments are identically zero. Their off-diagonal entries
        // never enter the numeric matrix, so they must not create symbolic fill.
        // Keep isolated unit diagonals and the full coordinate indexing.
        var adjacency = ShellDofOrdering.BuildAdjacency(shell, prescribed);
        Order = ShellDofOrdering.Create(shell, adjacency);

        // Symbolic Cholesky via elimination-tree reach. Enumerating fill cliques
        // during every elimination made the refined reference setup quadratic
        // in dense separator size. Tree reach visits each resulting entry once.
        var upper = new List<int>[n];
        for (var i = 0; i < n; ++i) upper[i] = [];
        for (var i = 0; i < n; ++i)
            foreach (var j in adjacency[i]) if (Order[i] < Order[j]) upper[Order[j]].Add(Order[i]);
        foreach (var list in upper) list.Sort();
        // Assembled coefficients occupy the original graph, not Cholesky fill.
        // Keep a separate compact lower CSC so numeric workers do not allocate
        // a second full factor-sized array merely to hold mostly zero entries.
        MatrixStarts = new int[n + 1];
        for (var i = 0; i < n; ++i) MatrixStarts[i + 1] = 1;
        foreach (var list in upper) foreach (var j in list) ++MatrixStarts[j + 1];
        for (var i = 0; i < n; ++i) MatrixStarts[i + 1] = checked(MatrixStarts[i + 1] + MatrixStarts[i]);
        MatrixRows = new int[MatrixStarts[n]]; var cursor = MatrixStarts.ToArray();
        for (var i = 0; i < n; ++i) MatrixRows[cursor[i]++] = i;
        for (var k = 0; k < n; ++k) foreach (var j in upper[k]) MatrixRows[cursor[j]++] = k;
        var parent = new int[n]; var ancestor = new int[n]; Array.Fill(parent, -1); Array.Fill(ancestor, -1);
        for (var k = 0; k < n; ++k)
            foreach (var row in upper[k])
            {
                var i = row;
                while (i >= 0 && i < k)
                {
                    var next = ancestor[i]; ancestor[i] = k;
                    if (next < 0) parent[i] = k;
                    i = next;
                }
            }
        var columnRows = new List<int>[n]; var seen = new int[n]; Array.Fill(seen, -1);
        for (var i = 0; i < n; ++i) columnRows[i] = [i];
        for (var k = 0; k < n; ++k)
            foreach (var row in upper[k])
                for (var i = row; i >= 0 && i < k && seen[i] != k; i = parent[i])
                { seen[i] = k; columnRows[i].Add(k); }
        Starts = new int[n + 1];
        for (var i = 0; i < n; ++i) Starts[i + 1] = checked(Starts[i] + columnRows[i].Count);
        Rows = new int[Starts[n]];
        for (var i = 0; i < n; ++i) columnRows[i].CopyTo(Rows, Starts[i]);
        RunOffsets = new int[n + 1];
        var runStarts = new List<int>(); var runEnds = new List<int>();
        for (var i = 0; i < n; ++i)
        {
            for (var start = Starts[i]; start < Starts[i + 1];)
            {
                var end = start + 1;
                while (end < Starts[i + 1] && Rows[end - 1] + 1 == Rows[end]) ++end;
                if (end - start >= 16) { runStarts.Add(start); runEnds.Add(end); }
                start = end;
            }
            RunOffsets[i + 1] = runStarts.Count;
        }
        RunStarts = runStarts.ToArray(); RunEnds = runEnds.ToArray();
        const int m = BacShell.ElementDofs;
        // Pack only the contributing entries, in the original row-major order.
        // Boundary elements repeat global DOFs: keep every local contribution
        // on a global diagonal, including entries above the local diagonal.
        ElementStarts = new int[shell.FaceCount + 1];
        ElementRowMasks = new int[checked(shell.FaceCount * m)];
        for (var f = 0; f < shell.FaceCount; ++f)
        {
            var map = shell.ElementMap(f);
            var count = 0;
            for (var i = 0; i < m; ++i) for (var j = 0; j < m; ++j)
                if (map[i] >= map[j] && (prescribed.IsEmpty || (!prescribed[map[i]] && !prescribed[map[j]])))
                { ElementRowMasks[f * m + i] |= 1 << j; ++count; }
            ElementStarts[f + 1] = checked(ElementStarts[f] + count);
        }
        ElementSlots = new int[ElementStarts[^1]];
        for (var f = 0; f < shell.FaceCount; ++f)
        {
            var map = shell.ElementMap(f); var entry = ElementStarts[f];
            for (var i = 0; i < m; ++i)
                for (var bits = (uint)ElementRowMasks[f * m + i]; bits != 0; bits &= bits - 1)
                    ElementSlots[entry++] = Slot(map[i], map[System.Numerics.BitOperations.TrailingZeroCount(bits)]);
        }
    }

    internal int Slot(int i, int j)
    {
        i = Order[i]; j = Order[j]; if (i < j) (i, j) = (j, i);
        if (i == j) return MatrixStarts[j];
        var slot = Array.BinarySearch(MatrixRows, MatrixStarts[j], MatrixStarts[j + 1] - MatrixStarts[j], i);
        return slot >= 0 ? slot : throw new InvalidOperationException("Missing symbolic shell entry.");
    }

}
