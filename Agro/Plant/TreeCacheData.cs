using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Agro;

internal class TreeCacheData
{
	public int Count { get; private set; }
	List<int>[] ChildrenNodes;
	/// <summary>Each node's parent and whether it is a rhizome, as the children lists were built:
	/// the passes over the whole tree read these instead of the agents.</summary>
	int[] ParentNodes = new int[2];
	bool[] RhizomeNodes = new bool[2];
	ushort[] DepthNodes;
	Vector3[] PointNodes;
	int[] Preorder = new int[2], SubtreeEnd = new int[2], CrownAncestor = new int[2];
	/// <summary>The nodes in breadth-first order and each node's place in it; filled on demand
	/// (<see cref="BreadthFirst"/>), once per topology.</summary>
	int[] BreadthOrder = new int[2], BreadthRank = new int[2];
	bool BreadthKnown;
	readonly List<int> HeavyNodes = [], LightRoots = [], PartitionRoots = [];
	/// <summary>Each node's partition for a walk on several cores (<see cref="WalkPartition"/>),
	/// filled on demand, once per topology.</summary>
	int[] PartitionOwner = new int[2];
	int PartitionParts;
	bool PartitionKnown;
	readonly object HeightGate = new();
	[ThreadStatic] static Stack<int>? WalkStack;
	/// <summary>Nodes from which a walk over a subtree is spread over the cores.</summary>
	internal const int ParallelWalkMinimum = 16384;
	readonly Stack<int> TraversalStack = new();
	readonly HashSet<int> Targets = [], StartNodes = [];
	readonly List<int> Roots = [];
	readonly List<int> Leaves = [];
	public float Height { get; private set; }

	ushort MaxDepth = 0;

	public TreeCacheData()
	{
		Count = 0;
		ChildrenNodes = [[], []];
		DepthNodes = [0, 0];
		PointNodes = [default, default];
	}

	public void Clear(int newSize)
	{
		Roots.Clear();
		Leaves.Clear();
		if (newSize > ChildrenNodes.Length)
		{
			var l = ChildrenNodes.Length;
			var capacity = Math.Max(newSize, l + l / 2);
			Array.Resize(ref ChildrenNodes, capacity);
			for(int i = l; i < capacity; ++i)
				ChildrenNodes[i] = [];
			Array.Resize(ref DepthNodes, capacity);
			Array.Resize(ref ParentNodes, capacity);
			Array.Resize(ref RhizomeNodes, capacity);
			Array.Resize(ref PointNodes, capacity);
			Array.Resize(ref Preorder, capacity);
			Array.Resize(ref SubtreeEnd, capacity);
			Array.Resize(ref CrownAncestor, capacity);
			Array.Resize(ref BreadthOrder, capacity);
			Array.Resize(ref BreadthRank, capacity);
		}
		BreadthKnown = false;
		PartitionKnown = false;
		Count = newSize;
		for(int i = 0; i < newSize; ++i)
			ChildrenNodes[i].Clear();
	}

	/// <summary>
	/// Makes room for organs appended at the end, keeping the children lists of the organs
	/// already there. Only for a census with births and no deaths: the existing organs keep
	/// their index and parent, so adding the newborns in index order then gives exactly the
	/// lists <see cref="Clear"/> and adding every organ would.
	/// </summary>
	public void Grow(int newSize)
	{
		Leaves.Clear();
		if (newSize > ChildrenNodes.Length)
		{
			var l = ChildrenNodes.Length;
			var capacity = Math.Max(newSize, l + l / 2);
			Array.Resize(ref ChildrenNodes, capacity);
			for(int i = l; i < capacity; ++i)
				ChildrenNodes[i] = [];
			Array.Resize(ref DepthNodes, capacity);
			Array.Resize(ref ParentNodes, capacity);
			Array.Resize(ref RhizomeNodes, capacity);
			Array.Resize(ref PointNodes, capacity);
			Array.Resize(ref Preorder, capacity);
			Array.Resize(ref SubtreeEnd, capacity);
			Array.Resize(ref CrownAncestor, capacity);
			Array.Resize(ref BreadthOrder, capacity);
			Array.Resize(ref BreadthRank, capacity);
		}
		BreadthKnown = false;
		PartitionKnown = false;
		for (int i = Count; i < newSize; ++i)
			ChildrenNodes[i].Clear();
		Count = newSize;
	}

	public void AddChild(int parentIndex, int childIndex, bool rhizome)
	{
		ParentNodes[childIndex] = parentIndex;
		RhizomeNodes[childIndex] = rhizome;
		if (parentIndex >= 0)
			ChildrenNodes[parentIndex].Add(childIndex);
		else
			Roots.Add(childIndex);
	}

	/// <summary>
	/// Numbers the nodes after the children lists were rebuilt: depth, crown ancestor, preorder
	/// and subtree end, the depth range and the leaves.
	/// </summary>
	/// <remarks>
	/// The numbering is that of a depth-first walk that pushes the roots and every node's
	/// children in list order on a stack, so the last root and the last child are visited first.
	/// It is produced here by array passes instead of the walk, reading the parents and rhizome
	/// flags kept beside the lists rather than the agents: subtree sizes bottom-up in reverse
	/// breadth-first order; each node's offset among its siblings - the sizes of the siblings the
	/// walk visits before it, which are those later in the list, i.e. of higher index, since the
	/// lists are built in index order - in one pass of falling index; then depth, crown, preorder
	/// (parent's plus one plus the offset) and subtree end (preorder plus size) top-down in
	/// breadth-first order. A leaf is a node of size one. Those are the walk's numbers exactly,
	/// and the breadth-first order is then known for the topology's other readers.
	/// </remarks>
	public void FinishUpdate<T>(PlantSubFormation<T> formation) where T : struct, IPlantAgent
	{
		var order = BreadthFirst();
		if (SubtreeSizes.Length < Count) { SubtreeSizes = new int[ChildrenNodes.Length]; SiblingOffsets = new int[ChildrenNodes.Length]; }
		var sizes = SubtreeSizes; var offsets = SiblingOffsets; var parents = ParentNodes;
		for (int i = 0; i < Count; ++i) { sizes[i] = 1; offsets[i] = 0; }
		for (int k = order.Length - 1; k >= 0; --k)
		{
			var node = order[k];
			var parent = parents[node];
			if (parent >= 0) sizes[parent] += sizes[node];
		}
		// Running sums per parent in falling index order, kept in the parent's own slot of a
		// second array; the roots share one sum.
		var rootsBefore = 0;
		for (int i = Count - 1; i >= 0; --i)
		{
			var parent = parents[i];
			if (parent < 0) { Preorder[i] = rootsBefore; rootsBefore += sizes[i]; }
			else { SubtreeEnd[i] = offsets[parent]; offsets[parent] += sizes[i]; }
		}
		ushort deepest = 0;
		for (int k = 0; k < order.Length; ++k)
		{
			var node = order[k];
			var parent = parents[node];
			if (parent < 0)
			{
				DepthNodes[node] = 0;
				CrownAncestor[node] = node;
			}
			else
			{
				var depth = (ushort)(DepthNodes[parent] + 1);
				DepthNodes[node] = depth;
				if (depth > deepest) deepest = depth;
				CrownAncestor[node] = RhizomeNodes[node] ? node : CrownAncestor[parent];
				Preorder[node] = Preorder[parent] + 1 + SubtreeEnd[node];
			}
			SubtreeEnd[node] = Preorder[node] + sizes[node];
		}
		MaxDepth = (ushort)(deepest + 1);

		for(int i = 0; i < Count; ++i)
			if (sizes[i] == 1)
				Leaves.Add(i);
	}
	int[] SiblingOffsets = [];
	int[] SubtreeSizes = [];

	/// <summary>
	/// The nodes in breadth-first order from the roots: the order in which a walk that keeps its
	/// frontier in a queue visits them, every parent before its children.
	/// </summary>
	internal ReadOnlySpan<int> BreadthFirst()
	{
		if (!BreadthKnown)
		{
			var order = BreadthOrder; var filled = 0;
			foreach (var root in Roots) order[filled++] = root;
			for (int k = 0; k < filled; ++k)
				foreach (var child in ChildrenNodes[order[k]]) order[filled++] = child;
			for (int k = 0; k < filled; ++k) BreadthRank[order[k]] = k;
			BreadthKnown = true;
		}
		return BreadthOrder.AsSpan(0, Count);
	}

	/// <summary>Each node's place in <see cref="BreadthFirst"/>.</summary>
	internal int[] BreadthRanks()
	{
		BreadthFirst();
		return BreadthRank;
	}

	/// <summary>Nodes in the subtree of <paramref name="node"/>, itself included.</summary>
	internal int SubtreeSize(int node) => SubtreeEnd[node] - Preorder[node];

	/// <summary>
	/// Splits the subtrees under <paramref name="roots"/> for a walk that visits parents before
	/// children on several cores. The result holds, parents first, every node whose subtree is
	/// larger than the grain - to be visited one after another - and <paramref name="light"/>
	/// receives the roots of the subtrees left over, which no longer depend on one another.
	/// </summary>
	internal List<int> SplitForWalk(ReadOnlySpan<int> roots, List<int> light)
	{
		var heavy = HeavyNodes;
		heavy.Clear(); light.Clear();
		var total = 0;
		foreach (var root in roots) total += SubtreeSize(root);
		// Small enough that the cores stay busy to the end, large enough to be worth a task.
		var grain = Math.Clamp(total / (8 * Environment.ProcessorCount), 256, 4096);
		foreach (var root in roots) (SubtreeSize(root) > grain ? heavy : light).Add(root);
		for (int k = 0; k < heavy.Count; ++k)
			foreach (var child in ChildrenNodes[heavy[k]])
				(SubtreeSize(child) > grain ? heavy : light).Add(child);
		return heavy;
	}

	/// <summary>Levels of <see cref="WalkPartition"/>; a walk towards the roots crosses at most
	/// this many partitions.</summary>
	internal const int PartitionLevels = 7;

	/// <summary>
	/// Every node's partition for work along walks towards the roots spread over the cores, and
	/// the number of partitions in <paramref name="parts"/>.
	/// </summary>
	/// <remarks>
	/// A node's level is the smallest <c>l</c> for which its subtree has at most <c>g * 8^l</c>
	/// nodes (the last level takes the rest), where <c>g</c> is the grain of
	/// <see cref="SplitForWalk"/>; a partition is a node of some level whose parent is of a higher
	/// one, with everything below it of the same level. A subtree is never larger than its
	/// parent's, so levels never fall on the way to the roots: a walk stays in one partition
	/// until the level rises, and never comes back to it. Level 0 is the subtrees the split
	/// leaves over; the levels above cut the trunk that carries them, so that no partition takes
	/// every walk that reaches the trunk. Once per topology.
	/// </remarks>
	internal int[] WalkPartition<T>(PlantSubFormation<T> formation, out int parts) where T : struct, IPlantAgent
	{
		if (!PartitionKnown)
		{
			if (PartitionOwner.Length < Count) PartitionOwner = new int[ChildrenNodes.Length];
			if (PartitionLevel.Length < Count) PartitionLevel = new byte[ChildrenNodes.Length];
			LevelPartitions(formation);
			PartitionParts = PartitionRoots.Count;
			MarkPartitions();
			PartitionKnown = true;
		}
		parts = PartitionParts;
		return PartitionOwner;
	}
	byte[] PartitionLevel = new byte[2];

	/// <summary>Every node's level, and the partition roots in index order, on several cores.</summary>
	void LevelPartitions<T>(PlantSubFormation<T> formation) where T : struct, IPlantAgent
	{
		var grain = Math.Clamp(Count / (8 * Environment.ProcessorCount), 256, 4096);
		var blocks = (Count + 8191) / 8192;
		if (PartitionBlockRoots.Length < blocks)
		{
			var old = PartitionBlockRoots.Length;
			Array.Resize(ref PartitionBlockRoots, blocks);
			for (int b = old; b < blocks; ++b) PartitionBlockRoots[b] = [];
		}
		Parallel.For(0, blocks, b =>
		{
			var last = Math.Min(Count, (b + 1) * 8192);
			for (int i = b * 8192; i < last; ++i)
			{
				var size = SubtreeSize(i); byte level = 0;
				for (long bound = grain; size > bound && level < PartitionLevels - 1; bound *= 8) ++level;
				PartitionLevel[i] = level;
			}
		});
		Parallel.For(0, blocks, b =>
		{
			var roots = PartitionBlockRoots[b];
			roots.Clear();
			var last = Math.Min(Count, (b + 1) * 8192);
			for (int i = b * 8192; i < last; ++i)
			{
				var parent = ParentNodes[i];
				if (parent < 0 || PartitionLevel[parent] != PartitionLevel[i]) roots.Add(i);
			}
		});
		PartitionRoots.Clear();
		for (int b = 0; b < blocks; ++b) PartitionRoots.AddRange(PartitionBlockRoots[b]);
	}
	List<int>[] PartitionBlockRoots = [];

	/// <summary>Kept apart so that nothing captured is allocated before it is needed.</summary>
	void MarkPartitions() => Parallel.For(0, PartitionRoots.Count, part =>
	{
		var stack = WalkStack ??= new();
		stack.Clear();
		var root = PartitionRoots[part]; var level = PartitionLevel[root];
		stack.Push(root);
		while (stack.TryPop(out var node))
		{
			PartitionOwner[node] = part;
			foreach (var child in ChildrenNodes[node])
				if (PartitionLevel[child] == level) stack.Push(child);
		}
	});

	// The strict ancestor relation excludes a node from its own descendants.
	internal bool IsDescendant(int ancestor, int node) =>
		Preorder[ancestor] < Preorder[node] && Preorder[node] < SubtreeEnd[ancestor];
	internal bool SharesCrown(int first, int second) => IsDescendant(CrownAncestor[first], second);

	/// <summary>The crown an organ belongs to, for the hierarchy's node-level crown pruning, or -1
	/// when the organ is itself a crown root. <see cref="SharesCrown"/> is a strict descendancy
	/// test, so a crown root does not share a crown with its own subtree and its pairs must stay
	/// enumerable.</summary>
	internal int PrunableCrown(int index)
	{
		var crown = CrownAncestor[index];
		return crown == index ? -1 : crown;
	}

	internal List<int> GetChildren(int index) => ChildrenNodes[index];
	internal List<int> GetRoots() => Roots;
	internal List<int> GetLeaves() => Leaves;
	internal ushort GetAbsDepth(int index) => DepthNodes[index];
	internal ushort GetAbsInvDepth(int index) => (ushort)(MaxDepth - DepthNodes[index]);
	internal float GetRelDepth(int index) => MaxDepth > 0 ? (DepthNodes[index] + 1) / (float)MaxDepth : 1f;
	internal Vector3 GetBaseCenter(int index) => PointNodes[index] ;

	internal void UpdateBases<T>(PlantSubFormation<T> formation) where T : struct, IPlantAgent
	{
		if (Count >= ParallelWalkMinimum && Environment.ProcessorCount > 1)
		{
			PlaceAllOnAllCores(formation);
			return;
		}
		var buffer = TraversalStack;
		buffer.Clear();
		foreach(var root in Roots)
		{
			PointNodes[root] = formation.Plant.Position;
			var point = formation.Plant.Position + formation.GetAttachmentOffset(root);
			var children = GetChildren(root);
			if (children.Count > 0)
			{
				foreach(var child in children)
				{
					PointNodes[child] = point;
					buffer.Push(child);
				}
			}
		}

		while (buffer.Count > 0)
		{
			var next = buffer.Pop();
			var children = GetChildren(next);
			if (children.Count > 0)
			{
				var point = PointNodes[next] + formation.GetAttachmentOffset(next);
				foreach(var child in children)
				{
					PointNodes[child] = point;
					buffer.Push(child);
				}
			}
		}

		UpdateHeight(formation);
	}
    internal void UpdateBases<T>(PlantSubFormation<T> formation, ReadOnlySpan<int> indexes) where T : struct, IPlantAgent
    {
        var targets = Targets;
        targets.Clear();
        foreach (var index in indexes)
        {
            if (index >= 0 && index < Count)
                targets.Add(index);
        }

        if (targets.Count == 0)
            return;

        var startNodes = StartNodes;
        startNodes.Clear();
        foreach (var index in targets)
        {
            var current = index;
            var parent = formation.GetParent(current);
            while (parent >= 0 && targets.Contains(parent))
            {
                current = parent;
                parent = formation.GetParent(current);
            }
            startNodes.Add(current);
        }

        var stack = TraversalStack;
        stack.Clear();
        foreach (var start in startNodes)
        {
            var parent = formation.GetParent(start);
            PointNodes[start] = parent >= 0
                ? PointNodes[parent] + formation.GetAttachmentOffset(parent)
                : formation.Plant.Position;

            stack.Push(start);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                var children = GetChildren(node);
                if (children.Count == 0)
                    continue;

                var point = PointNodes[node] + formation.GetAttachmentOffset(node);
                foreach (var child in children)
                {
                    PointNodes[child] = point;
                    stack.Push(child);
                }
            }
        }
    }

	/// <summary>Rebases a single subtree. The collision solver calls this per contact, so it must
	/// not allocate and must not run the O(Count) height pass.</summary>
	/// <remarks>
	/// A node's base is its parent's base plus the parent's reach, so nodes can be placed in any
	/// order that takes parents first. A large subtree - a contact sweep that turned a trunk
	/// moves the whole tree - is placed on several cores: the nodes that carry most of it one
	/// after another, then the subtrees left over side by side.
	/// </remarks>
	internal void UpdateBases<T>(PlantSubFormation<T> formation, int index) where T : struct, IPlantAgent
	{
		if (index < 0 || index >= Count)
			return;

		var parent = formation.GetParent(index);
		PointNodes[index] = parent >= 0
			? PointNodes[parent] + formation.GetAttachmentOffset(parent)
			: formation.Plant.Position;

		if (SubtreeSize(index) < ParallelWalkMinimum) PlaceBelow(formation, index, TraversalStack);
		else PlaceBelowOnAllCores(formation, index);
	}

	/// <summary>
	/// Every base and the height of a large plant, on several cores: a base is its parent's base
	/// plus the parent's reach whatever the order, and the height is a maximum.
	/// </summary>
	void PlaceAllOnAllCores<T>(PlantSubFormation<T> formation) where T : struct, IPlantAgent
	{
		foreach (var root in Roots) PointNodes[root] = formation.Plant.Position;
		var light = LightRoots;
		foreach (var node in SplitForWalk(CollectionsMarshal.AsSpan(Roots), light)) PlaceChildren(formation, node);
		Parallel.For(0, light.Count, k => PlaceBelow(formation, light[k], WalkStack ??= new()));

		HeightOnAllCores(formation);
	}

	/// <summary>The height, a maximum, on several cores.</summary>
	void HeightOnAllCores<T>(PlantSubFormation<T> formation) where T : struct, IPlantAgent
	{
		var groundHeight = formation.Plant.Position.Y;
		var height = 0f;
		var gate = HeightGate;
		Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(0, Count, 4096), () => 0f, (range, _, highest) =>
		{
			for (var i = range.Item1; i < range.Item2; ++i)
			{
				var tip = PointNodes[i] + formation.GetAttachmentOffset(i);
				highest = MathF.Max(highest, MathF.Max(PointNodes[i].Y, tip.Y) - groundHeight);
			}
			return highest;
		}, highest => { lock (gate) height = MathF.Max(height, highest); });
		Height = height;
	}

	/// <summary>Kept apart from the per-contact rebase, which must not allocate.</summary>
	void PlaceBelowOnAllCores<T>(PlantSubFormation<T> formation, int index) where T : struct, IPlantAgent
	{
		var light = LightRoots;
		foreach (var node in SplitForWalk([index], light)) PlaceChildren(formation, node);
		Parallel.For(0, light.Count, k => PlaceBelow(formation, light[k], WalkStack ??= new()));
	}

	void PlaceChildren<T>(PlantSubFormation<T> formation, int node) where T : struct, IPlantAgent
	{
		var children = GetChildren(node);
		if (children.Count == 0)
			return;
		var point = PointNodes[node] + formation.GetAttachmentOffset(node);
		foreach (var child in children)
			PointNodes[child] = point;
	}

	/// <summary>Places every node below <paramref name="root"/>, whose own base is already set.</summary>
	void PlaceBelow<T>(PlantSubFormation<T> formation, int root, Stack<int> stack) where T : struct, IPlantAgent
	{
		stack.Clear();
		stack.Push(root);
		while (stack.Count > 0)
		{
			var node = stack.Pop();
			PlaceChildren(formation, node);
			foreach (var child in GetChildren(node))
				stack.Push(child);
		}
	}

	internal void UpdateHeight<T>(PlantSubFormation<T> formation) where T : struct, IPlantAgent
	{
		// A maximum: the same on several cores. A woody plant asks for it in the middle of its
		// organ tick after every contact sweep that rebased a subtree.
		if (Count >= ParallelWalkMinimum && Environment.ProcessorCount > 1) { HeightOnAllCores(formation); return; }
		Height = 0f;
		var groundHeight = formation.Plant.Position.Y;
		for (var i = 0; i < Count; ++i)
		{
			var tip = PointNodes[i] + formation.GetAttachmentOffset(i);
			Height = MathF.Max(Height, MathF.Max(PointNodes[i].Y, tip.Y) - groundHeight);
		}
	}
}
