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
		}
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
			var point = formation.Plant.Position + Vector3.Transform(Vector3.UnitX, formation.GetDirection(root)) * formation.GetLength(root);
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
				var point = PointNodes[next] + Vector3.Transform(Vector3.UnitX, formation.GetDirection(next)) * formation.GetLength(next);
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
                ? PointNodes[parent] + Vector3.Transform(Vector3.UnitX, formation.GetDirection(parent)) * formation.GetLength(parent)
                : formation.Plant.Position;

            stack.Push(start);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                var children = GetChildren(node);
                if (children.Count == 0)
                    continue;

                var point = PointNodes[node] + Vector3.Transform(Vector3.UnitX, formation.GetDirection(node)) * formation.GetLength(node);
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
	internal void UpdateBases<T>(PlantSubFormation<T> formation, int index) where T : struct, IPlantAgent
	{
		if (index < 0 || index >= Count)
			return;

		var stack = TraversalStack;
		stack.Clear();
		var parent = formation.GetParent(index);
		PointNodes[index] = parent >= 0
			? PointNodes[parent] + Vector3.Transform(Vector3.UnitX, formation.GetDirection(parent)) * formation.GetLength(parent)
			: formation.Plant.Position;

		stack.Push(index);
		while (stack.Count > 0)
		{
			var node = stack.Pop();
			var children = GetChildren(node);
			if (children.Count == 0)
				continue;

			var point = PointNodes[node] + Vector3.Transform(Vector3.UnitX, formation.GetDirection(node)) * formation.GetLength(node);
			foreach (var child in children)
			{
				PointNodes[child] = point;
				stack.Push(child);
			}
		}
	}

	private void UpdateHeight<T>(PlantSubFormation<T> formation) where T : struct, IPlantAgent
	{
		Height = 0f;
		var groundHeight = formation.Plant.Position.Y;
		for (var i = 0; i < Count; ++i)
		{
			var tip = PointNodes[i] + Vector3.Transform(Vector3.UnitX, formation.GetDirection(i)) * formation.GetLength(i);
			Height = MathF.Max(Height, MathF.Max(PointNodes[i].Y, tip.Y) - groundHeight);
		}
	}
}
