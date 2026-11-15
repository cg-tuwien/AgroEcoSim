using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using M = System.Runtime.CompilerServices.MethodImplAttribute;

namespace Agro;

/// <summary>A node of a flat binary bounding volume hierarchy. Interior nodes have
/// <see cref="Count"/> == 0 and store the index of their first child in <see cref="Left"/>; the
/// second child always follows it. Leaves store the first index of their item range instead.</summary>
public struct BvhNode
{
	public Vector3 Min;
	public Vector3 Max;
	public int Left;
	public int Count;
}

/// <summary>One buildable entity: its bounds and the index of whatever it stands for.</summary>
public struct BvhItem
{
	public Vector3 Min;
	public Vector3 Max;
	public int Payload;
}

/// <summary>
/// Binned surface-area-heuristic builder over a slice of a shared <see cref="BvhItem"/> array.
/// </summary>
/// <remarks>
/// Items are reordered in place, so after a build a leaf's range is contiguous, and nodes are
/// emitted into a caller-owned slice, so several builds can run in parallel without locking.
/// <see cref="BuildParallel"/> uses both properties on one hierarchy: it descends the top levels on
/// one thread and then hands the resulting subtrees, whose item ranges and node budgets are
/// disjoint by construction, to as many threads as there are.
/// </remarks>
public struct BvhBuilder
{
	/// <summary>Remove unused parallel-build ranges without changing leaf or sibling order.</summary>
	public static BvhNode[] Compact(BvhNode[] source)
	{
		if (source.Length == 0) return [];
		var result = new List<BvhNode> { source[0] };
		var queue = new Queue<(int Source, int Target)>();
		queue.Enqueue((0, 0));
		while (queue.Count > 0)
		{
			var (s, t) = queue.Dequeue();
			var node = source[s];
			if (node.Count == 0)
			{
				var next = result.Count;
				result.Add(source[node.Left]); result.Add(source[node.Left + 1]);
				queue.Enqueue((node.Left, next)); queue.Enqueue((node.Left + 1, next + 1));
				node.Left = next;
			}
			result[t] = node;
		}
		return result.ToArray();
	}
	const MethodImplOptions AI = MethodImplOptions.AggressiveInlining;
	const int Bins = 12;
	const int MaxDepth = 48;

	BvhItem[] Items;
	BvhNode[] Nodes;
	int AllocNext;
	int AllocEnd;
	int MaxLeaf;
	/// <summary>While positive, a node this small is left unsplit and recorded in
	/// <see cref="Pending"/> for a worker thread to finish.</summary>
	int StopBelow;
	List<int>? Pending;
	/// <summary>Number of axes the split search considers. The top levels of a large build use one,
	/// because each of their levels costs a full pass over every item and the shape of the very top
	/// of the tree barely affects traversal.</summary>
	int Axes;

	/// <summary>Builds a hierarchy over <paramref name="count"/> items starting at
	/// <paramref name="itemFirst"/>, emitting nodes from <paramref name="nodeFirst"/> onwards.
	/// The node budget must be at least <c>2 * count</c>.</summary>
	/// <returns>The number of nodes written.</returns>
	public int Build(BvhItem[] items, int itemFirst, int count, BvhNode[] nodes, int nodeFirst, int nodeBudget, int maxLeaf)
	{
		if (count <= 0)
			return 0;

		Items = items;
		Nodes = nodes;
		MaxLeaf = maxLeaf;
		AllocNext = nodeFirst + 1;
		AllocEnd = nodeFirst + nodeBudget;
		if (Axes == 0)
			Axes = 3;

		Nodes[nodeFirst] = MakeLeaf(itemFirst, count);
		Subdivide(nodeFirst, 0);
		return AllocNext - nodeFirst;
	}

	/// <summary>Finishes an existing leaf node, allocating its subtree from a private node range.</summary>
	public int Grow(BvhItem[] items, BvhNode[] nodes, int nodeIndex, int allocFirst, int allocBudget, int maxLeaf)
	{
		Items = items;
		Nodes = nodes;
		MaxLeaf = maxLeaf;
		AllocNext = allocFirst;
		AllocEnd = allocFirst + allocBudget;
		StopBelow = 0;
		Pending = null;
		Axes = 3;

		Subdivide(nodeIndex, 0);
		return AllocNext - allocFirst;
	}

	/// <summary>Node budget <see cref="BuildParallel"/> needs: two per item for the subtrees plus
	/// what the serial descent to <paramref name="serialThreshold"/> spends on the way down.</summary>
	public static int NodeBudgetFor(int count, int serialThreshold)
		=> count <= 0 ? 0 : 2 * count + 4 * (count / Math.Max(1, serialThreshold) + 1) + 64;

	/// <summary>
	/// Builds one hierarchy over a large item range using every available core.
	/// </summary>
	/// <param name="serialThreshold">Item count at which the serial descent stops and a subtree is
	/// handed to a worker. Smaller values parallelise more of the work but spend more time in the
	/// serial passes above them.</param>
	public static int BuildParallel(BvhItem[] items, int itemFirst, int count, BvhNode[] nodes,
									int nodeFirst, int nodeBudget, int maxLeaf, int serialThreshold)
	{
		if (count <= 0)
			return 0;
		if (count <= serialThreshold)
			return new BvhBuilder().Build(items, itemFirst, count, nodes, nodeFirst, nodeBudget, maxLeaf);

		var pending = new List<int>();
		var top = new BvhBuilder { StopBelow = serialThreshold, Pending = pending, Axes = 1 };
		var used = top.Build(items, itemFirst, count, nodes, nodeFirst, nodeBudget, maxLeaf);
		if (pending.Count == 0)
			return used;

		var limit = nodeFirst + nodeBudget;
		var starts = new int[pending.Count];
		var budgets = new int[pending.Count];
		var cursor = nodeFirst + used;
		for (int i = 0; i < pending.Count; ++i)
		{
			var budget = Math.Min(2 * nodes[pending[i]].Count, Math.Max(0, limit - cursor));
			starts[i] = cursor;
			budgets[i] = budget;
			cursor += budget;
		}

		Parallel.For(0, pending.Count, i =>
		{
			if (budgets[i] >= 2)
				new BvhBuilder().Grow(items, nodes, pending[i], starts[i], budgets[i], maxLeaf);
		});

		return cursor - nodeFirst;
	}

	BvhNode MakeLeaf(int first, int count)
	{
		var min = new Vector3(float.MaxValue);
		var max = new Vector3(float.MinValue);
		for (int i = first; i < first + count; ++i)
		{
			min = Vector3.Min(min, Items[i].Min);
			max = Vector3.Max(max, Items[i].Max);
		}
		return new BvhNode { Min = min, Max = max, Left = first, Count = count };
	}

	void Subdivide(int nodeIndex, int depth) => Subdivide(nodeIndex, depth, default, default, false);

	/// <param name="centroidsKnown">The node's centroid bounds are given in
	/// <paramref name="knownCentroidMin"/> and <paramref name="knownCentroidMax"/>, because the
	/// partition that made the node gathered them (<see cref="Partition"/>).</param>
	void Subdivide(int nodeIndex, int depth, Vector3 knownCentroidMin, Vector3 knownCentroidMax, bool centroidsKnown)
	{
		var node = Nodes[nodeIndex];
		if (node.Count <= MaxLeaf || depth >= MaxDepth || AllocNext + 2 > AllocEnd)
			return;
		if (StopBelow > 0 && node.Count <= StopBelow)
		{
			Pending!.Add(nodeIndex);
			return;
		}

		var first = node.Left;
		var count = node.Count;

		//Centroid bounds decide where the bins go; object bounds decide their cost.
		var centroidMin = knownCentroidMin;
		var centroidMax = knownCentroidMax;
		if (!centroidsKnown)
		{
			centroidMin = new Vector3(float.MaxValue);
			centroidMax = new Vector3(float.MinValue);
			for (int i = first; i < first + count; ++i)
			{
				var c = (Items[i].Min + Items[i].Max) * 0.5f;
				centroidMin = Vector3.Min(centroidMin, c);
				centroidMax = Vector3.Max(centroidMax, c);
			}
		}

		var bestCost = float.MaxValue;
		var bestAxis = -1;
		var bestSplit = 0f;
		var parentArea = SurfaceArea(node.Min, node.Max);
		var leafCost = count * (parentArea > 0f ? parentArea : 1f);

		Span<Vector3> binMin = stackalloc Vector3[Bins];
		Span<Vector3> binMax = stackalloc Vector3[Bins];
		Span<int> binCount = stackalloc int[Bins];
		Span<float> rightArea = stackalloc float[Bins];
		Span<int> rightCount = stackalloc int[Bins];

		var extents = centroidMax - centroidMin;
		var widest = extents.X >= extents.Y && extents.X >= extents.Z ? 0 : (extents.Y >= extents.Z ? 1 : 2);

		for (int a = 0; a < Axes; ++a)
		{
			var axis = Axes == 1 ? widest : a;
			var lo = Component(centroidMin, axis);
			var extent = Component(centroidMax, axis) - lo;
			if (extent < 1e-9f)
				continue;

			var scale = Bins / extent;
			for (int b = 0; b < Bins; ++b)
			{
				binMin[b] = new Vector3(float.MaxValue);
				binMax[b] = new Vector3(float.MinValue);
				binCount[b] = 0;
			}

			for (int i = first; i < first + count; ++i)
			{
				var c = (Component(Items[i].Min, axis) + Component(Items[i].Max, axis)) * 0.5f;
				var b = Math.Clamp((int)((c - lo) * scale), 0, Bins - 1);
				binMin[b] = Vector3.Min(binMin[b], Items[i].Min);
				binMax[b] = Vector3.Max(binMax[b], Items[i].Max);
				++binCount[b];
			}

			//Sweep from the right first so both sides are available in one left-to-right pass.
			var accMin = new Vector3(float.MaxValue);
			var accMax = new Vector3(float.MinValue);
			var accCount = 0;
			for (int b = Bins - 1; b > 0; --b)
			{
				accMin = Vector3.Min(accMin, binMin[b]);
				accMax = Vector3.Max(accMax, binMax[b]);
				accCount += binCount[b];
				rightArea[b] = accCount > 0 ? SurfaceArea(accMin, accMax) : 0f;
				rightCount[b] = accCount;
			}

			accMin = new Vector3(float.MaxValue);
			accMax = new Vector3(float.MinValue);
			accCount = 0;
			for (int b = 0; b < Bins - 1; ++b)
			{
				accMin = Vector3.Min(accMin, binMin[b]);
				accMax = Vector3.Max(accMax, binMax[b]);
				accCount += binCount[b];
				if (accCount == 0 || rightCount[b + 1] == 0)
					continue;
				var cost = accCount * SurfaceArea(accMin, accMax) + rightCount[b + 1] * rightArea[b + 1];
				if (cost < bestCost)
				{
					bestCost = cost;
					bestAxis = axis;
					bestSplit = lo + (b + 1) / scale;
				}
			}
		}

		if (bestAxis < 0 || bestCost >= leafCost)
			return;

		var mid = Partition(first, count, bestAxis, bestSplit, out var sides);
		if (mid == first || mid == first + count)
			return;

		var left = AllocNext;
		AllocNext += 2;
		Nodes[left] = new BvhNode { Min = sides.LeftMin, Max = sides.LeftMax, Left = first, Count = mid - first };
		Nodes[left + 1] = new BvhNode { Min = sides.RightMin, Max = sides.RightMax, Left = mid, Count = first + count - mid };
		node.Left = left;
		node.Count = 0;
		Nodes[nodeIndex] = node;

		Subdivide(left, depth + 1, sides.LeftCentroidMin, sides.LeftCentroidMax, true);
		Subdivide(left + 1, depth + 1, sides.RightCentroidMin, sides.RightCentroidMax, true);
	}

	/// <summary>What <see cref="Partition"/> gathers about each side while it sorts the items:
	/// the bounds that make the side's node, and the centroid bounds its own split starts from.</summary>
	struct PartitionSides
	{
		public Vector3 LeftMin, LeftMax, RightMin, RightMax;
		public Vector3 LeftCentroidMin, LeftCentroidMax, RightCentroidMin, RightCentroidMax;
	}

	/// <summary>
	/// Sorts the items to the two sides of a split and gathers each side's bounds and centroid
	/// bounds on the way.
	/// </summary>
	/// <remarks>
	/// An item's side is settled the moment it is examined: an item that goes left stays where it
	/// is, one that goes right is swapped to the far end and never looked at again. So each item
	/// is added to its side's bounds exactly once, and those bounds - a minimum and a maximum over
	/// the same items - are the ones a separate pass over each side afterwards found. That pass,
	/// and the pass each side's own split began with to find its centroid bounds, were two of the
	/// four passes over the items each level of the hierarchy made.
	/// </remarks>
	int Partition(int first, int count, int axis, float split, out PartitionSides sides)
	{
		var empty = new Vector3(float.MaxValue); var none = new Vector3(float.MinValue);
		Vector3 leftMin = empty, leftMax = none, rightMin = empty, rightMax = none;
		Vector3 leftCentroidMin = empty, leftCentroidMax = none, rightCentroidMin = empty, rightCentroidMax = none;
		var i = first;
		var j = first + count - 1;
		while (i <= j)
		{
			var item = Items[i];
			var c = (Component(item.Min, axis) + Component(item.Max, axis)) * 0.5f;
			var centroid = (item.Min + item.Max) * 0.5f;
			if (c < split)
			{
				leftMin = Vector3.Min(leftMin, item.Min); leftMax = Vector3.Max(leftMax, item.Max);
				leftCentroidMin = Vector3.Min(leftCentroidMin, centroid); leftCentroidMax = Vector3.Max(leftCentroidMax, centroid);
				++i;
			}
			else
			{
				rightMin = Vector3.Min(rightMin, item.Min); rightMax = Vector3.Max(rightMax, item.Max);
				rightCentroidMin = Vector3.Min(rightCentroidMin, centroid); rightCentroidMax = Vector3.Max(rightCentroidMax, centroid);
				(Items[i], Items[j]) = (Items[j], Items[i]);
				--j;
			}
		}
		sides = new PartitionSides
		{
			LeftMin = leftMin, LeftMax = leftMax, RightMin = rightMin, RightMax = rightMax,
			LeftCentroidMin = leftCentroidMin, LeftCentroidMax = leftCentroidMax,
			RightCentroidMin = rightCentroidMin, RightCentroidMax = rightCentroidMax,
		};
		return i;
	}

	[M(AI)]
	static float Component(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

	[M(AI)]
	static float SurfaceArea(Vector3 min, Vector3 max)
	{
		var e = max - min;
		if (e.X < 0f)
			return 0f;
		return e.X * e.Y + e.Y * e.Z + e.Z * e.X;
	}
}
