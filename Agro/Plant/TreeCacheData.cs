using System.Drawing;
using System.Numerics;

namespace Agro;

internal class TreeCacheData
{
	public int Count { get; private set; }
	List<int>[] ChildrenNodes;
	ushort[] DepthNodes;
	Vector3[] PointNodes;
	readonly List<int> Roots = new();
	readonly List<int> Leaves = new();
	//readonly List<int> Meristems = new();
	public float Height { get; private set; }

	ushort MaxDepth = 0;

	public TreeCacheData()
	{
		Count = 0;
		ChildrenNodes = new List<int>[]{ new(), new() };
		DepthNodes = new ushort[]{ 0, 0 };
		PointNodes = new Vector3[] {default, default};
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
				ChildrenNodes[i] = new();
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

		for(int i = 0; i < ChildrenNodes[i].Count; ++i)
			if (ChildrenNodes[i].Count == 0)
				Leaves.Add(i);
	}

	internal IList<int> GetChildren(int index) => ChildrenNodes[index];
	internal ICollection<int> GetRoots() => Roots;
	internal ICollection<int> GetLeaves() => Leaves;
	internal ushort GetAbsDepth(int index) => DepthNodes[index];
	internal ushort GetAbsInvDepth(int index) => (ushort)(MaxDepth - DepthNodes[index]);
	internal float GetRelDepth(int index) => MaxDepth > 0 ? (DepthNodes[index] + 1) / (float)MaxDepth : 1f;
	internal Vector3 GetBaseCenter(int index) => PointNodes[index];

	internal void UpdateBases<T>(PlantSubFormation<T> formation) where T : struct, IPlantAgent
	{
		Height = 0f;
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
			else
				Height = Math.Max(Height, PointNodes[root].Y);
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
			else
				Height = Math.Max(Height, PointNodes[next].Y);
		}
	}
}
