namespace Agro.Mechanics;

/// <summary>
/// Deterministic shell coordinate ordering for sparse reference solves and
/// topology-based preconditioners. Returns original-to-ordered indices, not
/// numeric factors. Build once per rest topology; never once per contact.
/// </summary>
public static class ShellDofOrdering
{
    public static int[] Create(BacShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        return Create(shell, BuildAdjacency(shell));
    }

    internal static HashSet<int>[] BuildAdjacency(BacShell shell, ReadOnlySpan<bool> prescribed = default)
    {
        if (!prescribed.IsEmpty && prescribed.Length != shell.DofCount)
            throw new ArgumentException("Incorrect prescribed-DOF mask size.", nameof(prescribed));
        var adjacency = new HashSet<int>[shell.DofCount];
        for (var i = 0; i < adjacency.Length; ++i) adjacency[i] = [];
        for (var f = 0; f < shell.FaceCount; ++f)
            foreach (var i in shell.ElementMap(f)) foreach (var j in shell.ElementMap(f))
                if (i != j && (prescribed.IsEmpty || (!prescribed[i] && !prescribed[j]))) adjacency[i].Add(j);
        return adjacency;
    }

    internal static int[] Create(BacShell shell, HashSet<int>[] adjacency) =>
        shell.DofCount <= 3000 ? MinimumDegree(adjacency) : NestedDissection(shell, adjacency);

    static int[] MinimumDegree(HashSet<int>[] original)
    {
        var adjacency = original.Select(neighbours => new HashSet<int>(neighbours)).ToArray();
        var n = adjacency.Length; var removed = new bool[n]; var order = new int[n];
        for (var step = 0; step < n; ++step)
        {
            var best = -1;
            for (var i = 0; i < n; ++i)
                if (!removed[i] && (best < 0 || adjacency[i].Count < adjacency[best].Count)) best = i;
            order[best] = step; removed[best] = true;
            var neighbours = adjacency[best].Order().ToArray();
            foreach (var i in neighbours)
            {
                adjacency[i].Remove(best);
                foreach (var j in neighbours) if (i != j) adjacency[i].Add(j);
            }
        }
        return order;
    }

    static int[] NestedDissection(BacShell shell, HashSet<int>[] adjacency)
    {
        var n = shell.DofCount; var centres = new ShellVector[n];
        for (var i = 0; i < shell.VertexCount; ++i)
            for (var j = 0; j < 3; ++j) centres[3 * i + j] = shell.RestVertices[i];
        for (var f = 0; f < shell.FaceCount; ++f)
            for (var j = 0; j < 3; ++j)
                centres[shell.ElementMap(f)[18 + j]] = (shell.RestVertices[shell.Triangles[3 * f + (j + 1) % 3]]
                    + shell.RestVertices[shell.Triangles[3 * f + (j + 2) % 3]]) * .5;
        var membership = new int[n]; var localIndex = new int[n]; var generation = 0;
        var order = new int[n]; var count = 0;
        void Partition(int[] nodes)
        {
            if (nodes.Length <= 96)
            { Array.Sort(nodes); foreach (var i in nodes) order[i] = count++; return; }
            var min = centres[nodes[0]]; var max = min;
            foreach (var i in nodes)
            {
                var p = centres[i];
                min = new(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                max = new(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
            }
            var extent = max - min; var axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
            double Coordinate(int i) => axis == 0 ? centres[i].X : axis == 1 ? centres[i].Y : centres[i].Z;
            Array.Sort(nodes, (a, b) => { var c = Coordinate(a).CompareTo(Coordinate(b)); return c != 0 ? c : a.CompareTo(b); });
            var half = nodes.Length / 2; var stamp = ++generation;
            for (var i = 0; i < nodes.Length; ++i) { membership[nodes[i]] = stamp; localIndex[nodes[i]] = i; }
            // The old one-sided separator retained every right endpoint of a
            // crossing edge. A minimum cover can choose either endpoint and is
            // never larger for this cut. This reduces fill without altering any
            // shell coefficient or removing a coupling. Sort cut neighbours so
            // matching is independent of HashSet enumeration order.
            var cut = new int[half][];
            for (var k = 0; k < half; ++k)
            {
                var edges = new List<int>();
                foreach (var j in adjacency[nodes[k]])
                    if (membership[j] == stamp && localIndex[j] >= half) edges.Add(localIndex[j] - half);
                edges.Sort(); cut[k] = edges.ToArray();
            }
            var cover = ShellBipartiteCover.Find(cut, nodes.Length - half);
            var left = new List<int>(); var right = new List<int>(); var separator = new List<int>();
            for (var k = 0; k < nodes.Length; ++k)
                (cover[k] ? separator : k < half ? left : right).Add(nodes[k]);
            Partition(left.ToArray()); Partition(right.ToArray()); Partition(separator.ToArray());
        }
        Partition(Enumerable.Range(0, n).ToArray());
        return order;
    }
}
