using System.Drawing;
using System.Numerics;

namespace Agro;

internal class TreeCacheData
{
	public int Count { get; private set; }
	List<int>[] ChildrenNodes;
	ushort[] DepthNodes;
	Vector3[] PointNodes;
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
			Array.Resize(ref ChildrenNodes, newSize);
			for(int i = l; i < newSize; ++i)
				ChildrenNodes[i] = [];
			Array.Resize(ref DepthNodes, newSize);
			Array.Resize(ref PointNodes, newSize);
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

	public void FinishUpdate()
	{
		var buffer = new Stack<(int, ushort)>();
		foreach(var item in Roots)
			buffer.Push((item, 0));

		MaxDepth = 0;

		while(buffer.Count > 0)
		{
			var (index, depth) = buffer.Pop();
			DepthNodes[index] = depth;
			if (depth > MaxDepth)
				MaxDepth = depth;
			var nextDepth = (ushort)(depth + 1);
			foreach(var child in ChildrenNodes[index])
				buffer.Push((child, nextDepth));
		}

		++MaxDepth;

		for(int i = 0; i < Count; ++i)
			if (ChildrenNodes[i].Count == 0)
				Leaves.Add(i);
	}

	internal IList<int> GetChildren(int index) => ChildrenNodes[index];
	internal ICollection<int> GetRoots() => Roots;
	internal ICollection<int> GetLeaves() => Leaves;
	internal ushort GetAbsDepth(int index) => DepthNodes[index];
	internal ushort GetAbsInvDepth(int index) => (ushort)(MaxDepth - DepthNodes[index]);
	internal float GetRelDepth(int index) => MaxDepth > 0 ? (DepthNodes[index] + 1) / (float)MaxDepth : 1f;
	internal Vector3 GetBaseCenter(int index) => PointNodes[index] ;

	internal void UpdateBases<T>(PlantSubFormation<T> formation) where T : struct, IPlantAgent
	{
		var buffer = new Stack<int>();
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
    internal void UpdateBases<T>(PlantSubFormation<T> formation, IEnumerable<int> indexes) where T : struct, IPlantAgent
    {
        if (indexes == null)
            return;

        var targets = new HashSet<int>();
        foreach (var index in indexes)
        {
            if (index >= 0 && index < Count)
                targets.Add(index);
        }

        if (targets.Count == 0)
            return;

        var startNodes = new HashSet<int>();
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

        var stack = new Stack<int>();
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

		UpdateHeight(formation);
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
