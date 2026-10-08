namespace Agro.Mechanics;

/// <summary>
/// Minimum vertex cover of a bipartite cut via Hopcroft-Karp matching and
/// alternating reachability. Used only while building immutable sparse ordering.
/// Input neighbours are right-side indices, in deterministic order. No recursion
/// along augmenting paths; long thin cuts cannot exhaust the call stack.
/// </summary>
internal static class ShellBipartiteCover
{
    internal static bool[] Find(int[][] neighbours, int rightCount)
    {
        var leftCount = neighbours.Length;
        var leftMatch = new int[leftCount]; var rightMatch = new int[rightCount];
        Array.Fill(leftMatch, -1); Array.Fill(rightMatch, -1);
        var distance = new int[leftCount]; var cursor = new int[leftCount];
        var queue = new int[leftCount]; var path = new int[leftCount];
        var via = new int[leftCount]; var reachedRight = new bool[rightCount];
        while (true)
        {
            Array.Fill(distance, -1); Array.Clear(reachedRight);
            var head = 0; var tail = 0; var shortest = int.MaxValue;
            for (var u = 0; u < leftCount; ++u)
                if (leftMatch[u] < 0) { distance[u] = 0; queue[tail++] = u; }
            while (head < tail)
            {
                var u = queue[head++];
                if (distance[u] >= shortest) continue;
                foreach (var v in neighbours[u])
                {
                    reachedRight[v] = true;
                    var next = rightMatch[v];
                    if (next < 0) shortest = distance[u] + 1;
                    else if (distance[next] < 0)
                    { distance[next] = distance[u] + 1; queue[tail++] = next; }
                }
            }
            if (shortest == int.MaxValue) break;
            Array.Clear(cursor);
            for (var root = 0; root < leftCount; ++root)
            {
                if (leftMatch[root] >= 0 || distance[root] < 0) continue;
                var depth = 0; path[0] = root;
                while (depth >= 0)
                {
                    var u = path[depth]; var descended = false; var augmented = false;
                    while (cursor[u] < neighbours[u].Length)
                    {
                        var v = neighbours[u][cursor[u]++]; var next = rightMatch[v];
                        if (next < 0)
                        {
                            if (distance[u] + 1 != shortest) continue;
                            via[depth] = v;
                            for (var k = depth; k >= 0; --k)
                            { leftMatch[path[k]] = via[k]; rightMatch[via[k]] = path[k]; }
                            augmented = true; break;
                        }
                        if (distance[next] != distance[u] + 1) continue;
                        via[depth] = v; path[++depth] = next; descended = true; break;
                    }
                    if (augmented) break;
                    if (!descended) { distance[u] = -1; --depth; }
                }
            }
        }
        // Konig's construction: unreachable left vertices plus reachable right
        // vertices cover every cut edge, with size equal to the maximum matching.
        var cover = new bool[leftCount + rightCount];
        for (var u = 0; u < leftCount; ++u) cover[u] = distance[u] < 0;
        reachedRight.CopyTo(cover, leftCount);
        return cover;
    }
}
