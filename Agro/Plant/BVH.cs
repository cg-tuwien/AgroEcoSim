using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Agro;

/// <summary>
/// A lightweight bounding volume hierarchy suitable for quick overlap tests.
/// </summary>
public class Bvh
{
    public readonly struct BoundingBox
    {
        public BoundingBox(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }

        public Vector3 Min { get; }
        public Vector3 Max { get; }

        public Vector3 Center => (Min + Max) * 0.5f;

        public static BoundingBox Merge(BoundingBox a, BoundingBox b) => new(Vector3.Min(a.Min, b.Min), Vector3.Max(a.Max, b.Max));

        public bool Intersects(in BoundingBox other)
        {
            return !(Min.X > other.Max.X || Max.X < other.Min.X
                || Min.Y > other.Max.Y || Max.Y < other.Min.Y
                || Min.Z > other.Max.Z || Max.Z < other.Min.Z);
        }
    }

    internal readonly struct Node
    {
        public readonly BoundingBox Bounds;
        public readonly int Left;
        public readonly int Right;
        public readonly int Parent;
        public readonly int AgentIndex;
        public readonly bool CollisionEligible;
        /// <summary>The crown every organ under this node belongs to, or -1 when they differ or
        /// when the node must never be pruned by crown. See <see cref="Bvh.SetCrowns"/>.</summary>
        public readonly int Crown;

        public bool IsLeaf => Left < 0 && Right < 0;

        public Node(BoundingBox bounds, int left, int right, int parent, int agentIndex, bool collisionEligible = true, int crown = -1)
        {
            Bounds = bounds;
            Left = left;
            Right = right;
            Parent = parent;
            AgentIndex = agentIndex;
            CollisionEligible = collisionEligible;
            Crown = crown;
        }

        public Node WithBounds(BoundingBox bounds) => new(bounds, Left, Right, Parent, AgentIndex, CollisionEligible, Crown);
    }

    int root = -1;
    readonly List<Node> nodes = new();
    internal int Root => root;
    internal ReadOnlySpan<Node> Nodes => CollectionsMarshal.AsSpan(nodes);
    readonly Dictionary<int, int> leafLookup = new();
    (int agentIndex, BoundingBox bounds)[] buildItems = [];
    (int agentIndex, BoundingBox bounds)[] partitionItems = [];
    double buildQuality;
    bool[] pendingRefit = [];
    readonly Stack<int> traversalStack = new();
    readonly Stack<(int a, int b)> pairStack = new();
    static readonly Comparison<(int agentIndex, BoundingBox bounds)>[] AxisComparisons =
    [
        (a, b) => Compare(a, b, 0),
        (a, b) => Compare(a, b, 1),
        (a, b) => Compare(a, b, 2),
    ];
    static int Compare((int agentIndex, BoundingBox bounds) a, (int agentIndex, BoundingBox bounds) b, int axis)
    {
        var order = a.bounds.Center[axis].CompareTo(b.bounds.Center[axis]);
        return order != 0 ? order : a.agentIndex.CompareTo(b.agentIndex);
    }

    public bool IsEmpty => root < 0;

    public void Clear()
    {
        pendingRefit.AsSpan().Clear();
        nodes.Clear();
        leafLookup.Clear();
        root = -1;
    }

    public void Build(List<(int agentIndex, BoundingBox bounds)> leaves) =>
        Build(CollectionsMarshal.AsSpan(leaves));

    public void Build(ReadOnlySpan<(int agentIndex, BoundingBox bounds)> leaves)
    {
        Clear();
        if (leaves.IsEmpty)
            return;
        if (buildItems.Length < leaves.Length)
        {
            Array.Resize(ref buildItems, Math.Max(leaves.Length, Math.Max(8, buildItems.Length + buildItems.Length / 2)));
            Array.Resize(ref partitionItems, buildItems.Length);
        }
        var items = buildItems.AsSpan(0, leaves.Length);
        leaves.CopyTo(items);
        // Stable binned partitions avoid sorting every recursive subtree (N log squared N).
        // Consumers that accumulate responses order their leaf pairs independently of topology.
        root = BuildRecursive(items, -1);
        if (pendingRefit.Length < nodes.Count)
            Array.Resize(ref pendingRefit, Math.Max(nodes.Count, pendingRefit.Length + pendingRefit.Length / 2));
        buildQuality = Quality();
    }

    /// <summary>Refit unchanged membership, rebuilding only when the normalized internal area
    /// has grown by more than 50 percent. Scratch and tree topology remain reconstructible.</summary>
    public bool TryRefit(ReadOnlySpan<(int agentIndex, BoundingBox bounds)> leaves)
    {
        if (leaves.Length != leafLookup.Count) return false;
        foreach (var leaf in leaves)
            if (!leafLookup.ContainsKey(leaf.agentIndex)) return false;
        foreach (var leaf in leaves) UpdateLeafBounds(leaf.agentIndex, leaf.bounds);
        RefitUpdatedLeaves();
        return IsEmpty || Quality() <= Math.Max(1e-12, buildQuality) * 1.5;
    }

    double Quality()
    {
        if (IsEmpty) return 0;
        double area = 0;
        foreach (var node in nodes) if (!node.IsLeaf) area += SurfaceArea(node.Bounds);
        return area / Math.Max(1e-20, SurfaceArea(nodes[root].Bounds));
    }

    static float SurfaceArea(BoundingBox bounds)
    {
        var e = Vector3.Max(Vector3.Zero, bounds.Max - bounds.Min);
        return e.X * e.Y + e.X * e.Z + e.Y * e.Z;
    }

    int BuildRecursive(Span<(int agentIndex, BoundingBox bounds)> leaves, int parent)
    {
        if (leaves.Length == 1)
        {
            var index = nodes.Count;
            var (agentIndex, bounds) = leaves[0];
            nodes.Add(new Node(bounds, -1, -1, parent, agentIndex));
            leafLookup[agentIndex] = index;
            return index;
        }

        int mid = Partition(leaves);

        var nodeIndex = nodes.Count;
        nodes.Add(default);

        int left = BuildRecursive(leaves[..mid], nodeIndex);
        int right = BuildRecursive(leaves[mid..], nodeIndex);

        var merged = BoundingBox.Merge(nodes[left].Bounds, nodes[right].Bounds);
        nodes[nodeIndex] = new Node(merged, left, right, parent, -1);
        return nodeIndex;
    }

    struct Bin
    {
        public int Count;
        public BoundingBox Bounds;
        public void Add(BoundingBox bounds)
        {
            Bounds = Count++ == 0 ? bounds : BoundingBox.Merge(Bounds, bounds);
        }
    }

    int Partition(Span<(int agentIndex, BoundingBox bounds)> leaves)
    {
        if (leaves.Length <= 8)
        {
            leaves.Sort(AxisComparisons[ChooseSplitAxis(leaves)]);
            return leaves.Length / 2;
        }
        const int binCount = 16;
        var lo = new Vector3(float.PositiveInfinity);
        var hi = new Vector3(float.NegativeInfinity);
        foreach (var leaf in leaves)
        {
            lo = Vector3.Min(lo, leaf.bounds.Center);
            hi = Vector3.Max(hi, leaf.bounds.Center);
        }
        Span<Bin> bins = stackalloc Bin[binCount];
        Span<float> rightCost = stackalloc float[binCount];
        var bestCost = float.PositiveInfinity;
        var bestAxis = -1; var bestSplit = 0; var bestCount = 0;
        var minChild = Math.Max(1, leaves.Length / 8);
        for (int axis = 0; axis < 3; ++axis)
        {
            if (!(hi[axis] > lo[axis])) continue;
            bins.Clear();
            var scale = binCount / (hi[axis] - lo[axis]);
            foreach (var leaf in leaves)
                bins[Math.Clamp((int)((leaf.bounds.Center[axis] - lo[axis]) * scale), 0, binCount - 1)].Add(leaf.bounds);
            var right = new Bin();
            for (int b = binCount - 1; b > 0; --b)
            {
                if (bins[b].Count > 0)
                {
                    right.Bounds = right.Count == 0 ? bins[b].Bounds : BoundingBox.Merge(right.Bounds, bins[b].Bounds);
                    right.Count += bins[b].Count;
                }
                rightCost[b] = SurfaceArea(right.Bounds) * right.Count;
            }
            var left = new Bin();
            for (int b = 0; b < binCount - 1; ++b)
            {
                if (bins[b].Count > 0)
                {
                    left.Bounds = left.Count == 0 ? bins[b].Bounds : BoundingBox.Merge(left.Bounds, bins[b].Bounds);
                    left.Count += bins[b].Count;
                }
                if (left.Count < minChild || leaves.Length - left.Count < minChild) continue;
                var cost = SurfaceArea(left.Bounds) * left.Count + rightCost[b + 1];
                if (cost >= bestCost) continue;
                bestCost = cost; bestAxis = axis; bestSplit = b; bestCount = left.Count;
            }
        }
        // Coincident centres have no useful spatial split; input order gives a deterministic,
        // balanced tree without sorting equal keys at every level.
        if (bestAxis < 0) return leaves.Length / 2;
        var factor = binCount / (hi[bestAxis] - lo[bestAxis]);
        var first = 0; var second = bestCount;
        var scratch = partitionItems.AsSpan(0, leaves.Length);
        foreach (var leaf in leaves)
        {
            var bin = Math.Clamp((int)((leaf.bounds.Center[bestAxis] - lo[bestAxis]) * factor), 0, binCount - 1);
            scratch[bin <= bestSplit ? first++ : second++] = leaf;
        }
        scratch.CopyTo(leaves);
        return bestCount;
    }

    static int ChooseSplitAxis(ReadOnlySpan<(int agentIndex, BoundingBox bounds)> leaves)
    {
        var min = leaves[0].bounds.Min;
        var max = leaves[0].bounds.Max;

        for (int i = 1; i < leaves.Length; ++i)
        {
            min = Vector3.Min(min, leaves[i].bounds.Min);
            max = Vector3.Max(max, leaves[i].bounds.Max);
        }

        var extents = max - min;
        if (extents.X >= extents.Y && extents.X >= extents.Z)
            return 0;
        if (extents.Y >= extents.Z)
            return 1;
        return 2;
    }

    // A moving subtree updates many leaves that share ancestors. Mark each
    // ancestor once and merge bottom-up after all leaf bounds have been written.
    internal void UpdateLeafBounds(int agentIndex, BoundingBox bounds)
    {
        if (!leafLookup.TryGetValue(agentIndex, out var leaf))
            return;
        // AddLeaf may also have extended a tree since its last full build.
        if (pendingRefit.Length < nodes.Count)
            Array.Resize(ref pendingRefit, Math.Max(nodes.Count, Math.Max(8, pendingRefit.Length + pendingRefit.Length / 2)));
        nodes[leaf] = nodes[leaf].WithBounds(bounds);
        for (var parent = nodes[leaf].Parent; parent >= 0 && !pendingRefit[parent]; parent = nodes[parent].Parent)
            pendingRefit[parent] = true;
    }

    internal void RefitUpdatedLeaves()
    {
        if (!IsEmpty)
            RefitUpdatedNode(root);
    }

    void RefitUpdatedNode(int index)
    {
        if (!pendingRefit[index])
            return;
        var node = nodes[index];
        RefitUpdatedNode(node.Left);
        RefitUpdatedNode(node.Right);
        nodes[index] = node.WithBounds(BoundingBox.Merge(nodes[node.Left].Bounds, nodes[node.Right].Bounds));
        pendingRefit[index] = false;
    }

    internal void RefreshCollisionEligibility(Func<int, bool> eligible)
    {
        if (!IsEmpty)
            RefreshCollisionNode(root, eligible);
    }

    bool RefreshCollisionNode(int index, Func<int, bool> eligible)
    {
        var node = nodes[index];
        // Both subtrees must be refreshed even when the first one is eligible.
        var active = node.IsLeaf ? eligible(node.AgentIndex)
            : RefreshCollisionNode(node.Left, eligible) | RefreshCollisionNode(node.Right, eligible);
        //Forward the crown: the six-argument overload would silently reset it to -1, and this runs
        //before every broad-phase query, which left the crown pruning permanently disabled.
        nodes[index] = new Node(node.Bounds, node.Left, node.Right, node.Parent, node.AgentIndex, active, node.Crown);
        return active;
    }

    /// <summary>
    /// Labels every node with the crown its organs belong to, so a pair of subtrees that lie
    /// wholly inside one crown can be rejected without being opened.
    /// </summary>
    /// <remarks>
    /// The pair filter rejects two organs of the same crown anyway, but it did so one leaf pair at
    /// a time, after the descent that produced them. In a dense rosette essentially every bounding
    /// box overlaps every other, so that descent is quadratic and over 99% of what it produced was
    /// then discarded. Labelling costs one bottom-up pass per census.
    ///
    /// <paramref name="crownOf"/> must return -1 for an organ that is itself a crown root: the
    /// leaf-level test treats such a pair as distinct crowns, so it must stay enumerable.
    /// </remarks>
    internal void SetCrowns(Func<int, int> crownOf)
    {
        if (!IsEmpty)
            SetCrownNode(root, crownOf);
    }

    int SetCrownNode(int index, Func<int, int> crownOf)
    {
        var node = nodes[index];
        int crown;
        if (node.IsLeaf)
            crown = crownOf(node.AgentIndex);
        else
        {
            var left = SetCrownNode(node.Left, crownOf);
            var right = SetCrownNode(node.Right, crownOf);
            crown = left >= 0 && left == right ? left : -1;
        }
        nodes[index] = new Node(node.Bounds, node.Left, node.Right, node.Parent, node.AgentIndex, node.CollisionEligible, crown);
        return crown;
    }

    public void RefitLeaf(int agentIndex, BoundingBox bounds)
    {
        if (!leafLookup.TryGetValue(agentIndex, out var leaf))
            return;

        nodes[leaf] = nodes[leaf].WithBounds(bounds);

        var parent = nodes[leaf].Parent;
        while (parent >= 0)
        {
            var node = nodes[parent];
            var merged = BoundingBox.Merge(nodes[node.Left].Bounds, nodes[node.Right].Bounds);
            nodes[parent] = node.WithBounds(merged);
            parent = node.Parent;
        }
    }

    public List<(int first, int second)> GetOverlappingPairs()
    {
        var result = new List<(int, int)>();
        GetOverlappingPairs(result);
        return result;
    }

    /// <summary>Traverse two hierarchies together rather than querying the second tree from
    /// every leaf of the first. Offsets put their local bounds in the same coordinate frame.</summary>
    public void GetOverlappingPairs(Bvh other, Vector3 thisOffset, Vector3 otherOffset,
        List<(int first, int second)> result)
    {
        if (IsEmpty || other.IsEmpty) return;
        pairStack.Clear(); pairStack.Push((root, other.root));
        var offset = otherOffset - thisOffset;
        while (pairStack.Count > 0)
        {
            var (ia, ib) = pairStack.Pop();
            var a = nodes[ia]; var b = other.nodes[ib];
            var roundoff = (Vector3.Max(Vector3.Abs(b.Bounds.Min), Vector3.Abs(b.Bounds.Max))
                + Vector3.Abs(otherOffset) + Vector3.Abs(thisOffset)) * 2e-7f + new Vector3(1e-8f);
            var otherBounds = new BoundingBox(b.Bounds.Min + offset - roundoff, b.Bounds.Max + offset + roundoff);
            if (!a.Bounds.Intersects(otherBounds)) continue;
            if (a.IsLeaf && b.IsLeaf) { result.Add((a.AgentIndex, b.AgentIndex)); continue; }
            if (b.IsLeaf || !a.IsLeaf && SurfaceArea(a.Bounds) >= SurfaceArea(b.Bounds))
            { pairStack.Push((a.Left, ib)); pairStack.Push((a.Right, ib)); }
            else
            { pairStack.Push((ia, b.Left)); pairStack.Push((ia, b.Right)); }
        }
    }

    /// <summary>Append pairs to a caller-owned buffer. The eligibility mask only applies to
    /// collision queries; the full tree remains available for rhizome placement queries.</summary>
    public void GetOverlappingPairs(List<(int first, int second)> result,
        Func<int, int, bool>? filter = null, bool collisionsOnly = false, int maxPairs = int.MaxValue,
        uint traversalPhase = 0)
    {
        if (IsEmpty)
            return;

        var internalStack = traversalStack;
        internalStack.Clear();
        pairStack.Clear();
        internalStack.Push(root);

        while (internalStack.Count > 0)
        {
            int idx = internalStack.Pop();
            var n = nodes[idx];

            if (n.IsLeaf || (collisionsOnly && !n.CollisionEligible))
                continue;

            pairStack.Push((n.Left, n.Right));
            var reverse = traversalPhase != 0 && (((uint)idx * 2654435761u ^ traversalPhase * 2246822519u) & 4) != 0;
            internalStack.Push(reverse ? n.Right : n.Left);
            internalStack.Push(reverse ? n.Left : n.Right);
        }
        var visited = 0;
        // Bound rejected pairs too: capping only emitted contacts leaves a dense crown's
        // quadratic search unbounded. Vary the traversal with the step to share the budget.
        while (pairStack.Count > 0 && visited++ < maxPairs)
        {
            var (aIndex, bIndex) = pairStack.Pop();
            var a = nodes[aIndex];
            var b = nodes[bIndex];

            if ((collisionsOnly && (!a.CollisionEligible || !b.CollisionEligible)) || !a.Bounds.Intersects(in b.Bounds))
                continue;

            //Everything under both nodes shares one crown, and the pair filter rejects same-crown
            //pairs, so nothing below can survive. Skipping here is what keeps the descent out of
            //the quadratic case.
            if (collisionsOnly && a.Crown >= 0 && a.Crown == b.Crown)
                continue;

            bool aLeaf = a.IsLeaf;
            bool bLeaf = b.IsLeaf;

            if (aLeaf && bLeaf)
            {
                var pair = a.AgentIndex < b.AgentIndex
                    ? (a.AgentIndex, b.AgentIndex)
                    : (b.AgentIndex, a.AgentIndex);

                // Each leaf pair belongs to exactly one lowest common ancestor.
                if (filter == null || filter(pair.Item1, pair.Item2))
                    result.Add((pair.Item1, pair.Item2));
                if (result.Count >= maxPairs) return;
            }
            else if (aLeaf)
            {
                pairStack.Push((aIndex, b.Left));
                pairStack.Push((aIndex, b.Right));
            }
            else if (bLeaf)
            {
                pairStack.Push((a.Left, bIndex));
                pairStack.Push((a.Right, bIndex));
            }
            else
            {
                pairStack.Push((a.Left, b.Left));
                pairStack.Push((a.Left, b.Right));
                pairStack.Push((a.Right, b.Left));
                pairStack.Push((a.Right, b.Right));
            }
        }
    }

    public void AddLeaf(int agentIndex, BoundingBox bounds)
    {
        if (leafLookup.ContainsKey(agentIndex))
        {
            RefitLeaf(agentIndex, bounds);
            return;
        }

        if (IsEmpty)
        {
            root = 0;
            nodes.Add(new Node(bounds, -1, -1, -1, agentIndex));
            leafLookup[agentIndex] = 0;
            return;
        }

        int sibling = ChooseBestSibling(bounds);
        int oldParent = nodes[sibling].Parent;

        int leafIndex = nodes.Count;
        var leafNode = new Node(bounds, -1, -1, -1, agentIndex);
        nodes.Add(leafNode);
        leafLookup[agentIndex] = leafIndex;

        int newParentIndex = nodes.Count;
        var siblingNode = nodes[sibling];
        var newParentBounds = BoundingBox.Merge(bounds, siblingNode.Bounds);
        var newParent = new Node(newParentBounds, sibling, leafIndex, oldParent, -1);
        nodes.Add(newParent);

        // Fix sibling parent link
        nodes[sibling] = new Node(siblingNode.Bounds, siblingNode.Left, siblingNode.Right, newParentIndex, siblingNode.AgentIndex);

        if (oldParent >= 0)
        {
            var parentNode = nodes[oldParent];
            if (parentNode.Left == sibling)
                nodes[oldParent] = new Node(parentNode.Bounds, newParentIndex, parentNode.Right, parentNode.Parent, parentNode.AgentIndex);
            else
                nodes[oldParent] = new Node(parentNode.Bounds, parentNode.Left, newParentIndex, parentNode.Parent, parentNode.AgentIndex);
        }
        else
        {
            root = newParentIndex;
        }

        FixUpwardsBounds(oldParent >= 0 ? oldParent : newParentIndex);
    }

    void FixUpwardsBounds(int startNode)
    {
        int nodeIndex = startNode;
        while (nodeIndex >= 0)
        {
            var node = nodes[nodeIndex];
            if (node.IsLeaf)
            {
                nodeIndex = node.Parent;
                continue;
            }

            var merged = BoundingBox.Merge(nodes[node.Left].Bounds, nodes[node.Right].Bounds);
            nodes[nodeIndex] = node.WithBounds(merged);
            nodeIndex = node.Parent;
        }
    }

    int ChooseBestSibling(BoundingBox bounds)
    {
        int index = root;
        while (!nodes[index].IsLeaf)
        {
            var node =  nodes[index];
            var left =  nodes[node.Left];
            var right =  nodes[node.Right];

            float currentArea = BoundsVolume(node.Bounds);
            float mergeLeft = BoundsVolume(BoundingBox.Merge(left.Bounds, bounds)) - BoundsVolume(left.Bounds);
            float mergeRight = BoundsVolume(BoundingBox.Merge(right.Bounds, bounds)) - BoundsVolume(right.Bounds);

            if (mergeLeft < mergeRight)
                index = node.Left;
            else if (mergeRight < mergeLeft)
                index = node.Right;
            else
                index = BoundsVolume(left.Bounds) < BoundsVolume(right.Bounds) ? node.Left : node.Right;
        }
        return index;
    }

    static float BoundsVolume(BoundingBox bounds)
    {
        var extents = bounds.Max - bounds.Min;
        return extents.X * extents.Y * extents.Z;
    }

    public List<int> QueryOverlaps(int agentIndex)
    {
        var result = new List<int>();
        QueryOverlaps(agentIndex, result);
        return result;
    }

    public void QueryOverlaps(int agentIndex, List<int> result,
        Func<int, int, bool>? filter = null, bool collisionsOnly = false)
    {
        if (!leafLookup.TryGetValue(agentIndex, out var leafIndex) ||
            (collisionsOnly && !nodes[leafIndex].CollisionEligible))
            return;

        var targetBounds = nodes[leafIndex].Bounds;
        var stack = traversalStack;
        stack.Clear();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var nodeIndex = stack.Pop();
            var node = nodes[nodeIndex];

            if ((collisionsOnly && !node.CollisionEligible) || !targetBounds.Intersects(in node.Bounds))
                continue;

            if (node.IsLeaf)
            {
                if (node.AgentIndex != agentIndex && (filter == null || filter(agentIndex, node.AgentIndex)))
                    result.Add(node.AgentIndex);
            }
            else
            {
                stack.Push(node.Left);
                stack.Push(node.Right);
            }
        }
    }

    public List<int> QueryOverlaps(BoundingBox bounds)
    {
        var result = new List<int>();
        QueryOverlaps(in bounds, result);
        return result;
    }

    /// <summary>Appends the agents whose bounds meet <paramref name="bounds"/> to a caller-owned
    /// buffer. The allocating overload above is called once per growing meristem per step, and
    /// used to allocate both the result list and its traversal stack every time.</summary>
    public void QueryOverlaps(in BoundingBox bounds, List<int> result)
        => QueryOverlaps(bounds, result, traversalStack);

    /// <summary>Read-only traversal with caller-owned scratch for shared immutable hierarchies.</summary>
    public void QueryOverlaps(in BoundingBox bounds, List<int> result, Stack<int> stack)
    {
        if (IsEmpty)
            return;

        stack.Clear();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var nodeIndex = stack.Pop();
            var node = nodes[nodeIndex];

            if (!bounds.Intersects(in node.Bounds))
                continue;

            if (node.IsLeaf)
            {
                result.Add(node.AgentIndex);
            }
            else
            {
                stack.Push(node.Left);
                stack.Push(node.Right);
            }
        }
    }
    /// <summary>Bounds of everything in the hierarchy, which is the plant's canopy box.</summary>
    public bool TryGetRootBounds(out BoundingBox bounds)
    {
        if (IsEmpty)
        {
            bounds = default;
            return false;
        }
        bounds = nodes[root].Bounds;
        return true;
    }

    public bool TryGetBounds(int agentIndex, out BoundingBox bounds)
    {
        if (leafLookup.TryGetValue(agentIndex, out var leaf))
        {
            bounds = nodes[leaf].Bounds;
            return true;
        }

        bounds = default;
        return false;
    }
}
