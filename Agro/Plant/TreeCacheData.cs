using System.Drawing;
using System.Numerics;

namespace Agro;

internal class TreeCacheData
{
	public int Count { get; private set; }
	List<int>[] ChildrenNodes;
	ushort[] DepthNodes;
	Vector3[] PointNodes;
	int[] Preorder = new int[2], SubtreeEnd = new int[2], CrownAncestor = new int[2];
	/// <summary>The nodes in breadth-first order and each node's place in it; filled on demand
	/// (<see cref="BreadthFirst"/>), once per topology.</summary>
	int[] BreadthOrder = new int[2], BreadthRank = new int[2];
	bool BreadthKnown;
	readonly List<int> HeavyNodes = [], LightRoots = [];
	[ThreadStatic] static Stack<int>? WalkStack;
	/// <summary>Nodes from which a walk over a subtree is spread over the cores.</summary>
	internal const int ParallelWalkMinimum = 16384;
	readonly Stack<(int index, ushort depth, int crown, bool exit)> TopologyStack = new();
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
			Array.Resize(ref PointNodes, capacity);
			Array.Resize(ref Preorder, capacity);
			Array.Resize(ref SubtreeEnd, capacity);
			Array.Resize(ref CrownAncestor, capacity);
			Array.Resize(ref BreadthOrder, capacity);
			Array.Resize(ref BreadthRank, capacity);
		}
		BreadthKnown = false;
		Count = newSize;
		for(int i = 0; i < newSize; ++i)
			ChildrenNodes[i].Clear();
	}

	public void AddChild(int parentIndex, int childIndex)
	{
		if (parentIndex >= 0)
			ChildrenNodes[parentIndex].Add(childIndex);
		else
			Roots.Add(childIndex);
	}

	public void FinishUpdate<T>(PlantSubFormation<T> formation) where T : struct, IPlantAgent
	{
		var buffer = TopologyStack;
		buffer.Clear();
		var preorder = 0;
		foreach(var item in Roots)
			buffer.Push((item, 0, item, false));

		MaxDepth = 0;

		while(buffer.Count > 0)
		{
			var (index, depth, crown, exit) = buffer.Pop();
			if (exit)
			{
				SubtreeEnd[index] = preorder;
				continue;
			}
			Preorder[index] = preorder++;
			if (formation.GetIsRizome(index))
				crown = index;
			CrownAncestor[index] = crown;
			buffer.Push((index, depth, crown, true));
			DepthNodes[index] = depth;
			if (depth > MaxDepth)
				MaxDepth = depth;
			var nextDepth = (ushort)(depth + 1);
			foreach(var child in ChildrenNodes[index])
				buffer.Push((child, nextDepth, crown, false));
		}

		++MaxDepth;

		for(int i = 0; i < Count; ++i)
			if (ChildrenNodes[i].Count == 0)
				Leaves.Add(i);
	}

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
		Height = 0f;
		var groundHeight = formation.Plant.Position.Y;
		for (var i = 0; i < Count; ++i)
		{
			var tip = PointNodes[i] + formation.GetAttachmentOffset(i);
			Height = MathF.Max(Height, MathF.Max(PointNodes[i].Y, tip.Y) - groundHeight);
		}
	}
}
