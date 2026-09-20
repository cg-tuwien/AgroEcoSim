using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using M = System.Runtime.CompilerServices.MethodImplAttribute;

namespace Agro;

public enum RayPrimitiveKind : byte
{
	/// <summary>Point <c>P</c> with edge vectors <c>U</c> and <c>V</c>.</summary>
	Triangle = 0,
	/// <summary>Leaf lamina or petal: corner <c>P</c>, length axis <c>U</c>, width axis <c>V</c>,
	/// spanning <c>P + a U + b V</c> with <c>a</c> in [0,1] and <c>b</c> in [-0.5,0.5]. The two
	/// axes are always orthogonal because they come from an organ's quaternion basis.</summary>
	Quad = 1,
	/// <summary>Stem, petiole or pedicel: base <c>P</c>, axis <c>U</c> scaled by the length,
	/// radius <c>R</c>. Open ended; the caps are covered by the neighbouring organs.</summary>
	Cylinder = 2,
	/// <summary>Bud: centre <c>P</c>, radius <c>R</c>.</summary>
	Sphere = 3,
}

/// <summary>One traceable surface. Kept at 48 bytes so that a leaf of the hierarchy fits in a
/// couple of cache lines.</summary>
public struct RayPrimitive
{
	public Vector3 P;
	public Vector3 U;
	public Vector3 V;
	public float R;
	/// <summary>Radiosity and albedo group: terrain, one obstacle, or one plant.</summary>
	public int Group;
	public RayPrimitiveKind Kind;
	/// <summary>See <see cref="RayScene.FlagFoliage"/>.</summary>
	public byte Flags;
}

public struct RayHit
{
	public float T;
	public int Group;
	public Vector3 Normal;
	public bool IsPlant;
}

/// <summary>
/// The ray tracing acceleration structure of the internal light model: one hierarchy over the static
/// scene, built once, and one over all vegetation, rebuilt every daylight step.
/// </summary>
/// <remarks>
/// <para>
/// Splitting static from dynamic is what makes a per-step rebuild affordable. A facade model easily
/// carries a million triangles and never moves, so it is built once into the front of the shared
/// node and primitive arrays. Vegetation is rebuilt from scratch rather than refitted, which
/// sidesteps the question of whether organ indices survived the births and deaths of the last step;
/// <see cref="BvhBuilder.BuildParallel"/> keeps that affordable.
/// </para>
/// <para>
/// All plants go into a <em>single</em> hierarchy rather than one per plant. A hierarchy per plant
/// builds faster and looked like the obvious choice, but plants in a dense stand interpenetrate, so
/// a level above them prunes almost nothing: measured on 800 plants and 640k primitives, one flat
/// hierarchy traced 2.4 times faster, and that dwarfed the extra build cost.
/// </para>
/// </remarks>
public sealed class RayScene
{
	const MethodImplOptions AI = MethodImplOptions.AggressiveInlining;

	public const int MaskPlants = 1;
	public const int MaskStatic = 2;
	public const int MaskAll = MaskPlants | MaskStatic;

	/// <summary>Marks a leaf lamina, the only primitive that counts towards a leaf area index.</summary>
	public const byte FlagFoliage = 1;
	/// <summary>Marks vegetation, whose radiosity comes from the previous step's measurement rather
	/// than from the analytic plane-of-array estimate used for buildings and terrain.</summary>
	public const byte FlagPlant = 2;
	/// <summary>
	/// Marks a surface that only occludes when it is met from the front, its winding taken as
	/// outward. Terrain carries it, and it matters: a plant is seeded a centimetre or two below the
	/// soil surface, so its first organs sit inside the soil hull. Two-sided terrain would leave
	/// every seedling in the dark for no physical reason, while one-sided terrain still shades
	/// correctly under an overhang, whose downward facing triangles are met from the front.
	/// </summary>
	public const byte FlagOneSided = 4;

	//Traversal stacks are thread local and reused. A stackalloc of this size is zero-initialised
	//on every call in safe code, and at millions of rays per step that memset alone is measurable.
	[ThreadStatic] static int[]? NodeStack;
	[ThreadStatic] static float[]? EntryStack;

	/// <summary>Primitives per hierarchy leaf. Four measured slightly better than eight on a dense
	/// stand: the extra nodes pay for themselves in pruning.</summary>
	const int PrimitiveMaxLeaf = 4;
	const int TopMaxLeaf = 2;
	const int TraversalStack = 192;

	//Two primitive buffers. The solver and the hierarchy build write PrimsBuild, and the order the
	//build settled on is then materialised into Prims, so that traversal reads a leaf's primitives
	//sequentially instead of through an index indirection.
	RayPrimitive[] PrimsBuild = [];
	RayPrimitive[] Prims = [];
	BvhNode[] Nodes = [];
	BvhItem[] Items = [];
	// Static geometry is built once. Thereafter scratch contains vegetation only, with
	// local payload indices; traversal adds the immutable primitive prefix at a leaf.
	int BuildBase;

	int StaticPrimCount;
	int StaticNodeCount;

	int[] PlantPrimFirst = [];
	int[] PlantPrimCount = [];
	int DynamicPrimCount;
	int DynamicNodeBudget;

	//Blob 0 is the static geometry when it exists, blob 1 all the vegetation.
	int[] BlobNodeRoot = new int[2];
	int[] BlobMask = new int[2];
	int BlobCount;

	BvhNode[] TopNodes = new BvhNode[4];
	BvhItem[] TopItems = new BvhItem[2];
	int[] TopBlob = new int[2];
	int TopNodeCount;

	/// <summary>Item count above which a build spreads itself over the thread pool.</summary>
	static readonly int ParallelBuildThreshold = Math.Max(4096, 1 << 14);

	public int PrimitiveCount => StaticPrimCount + DynamicPrimCount;

	/// <summary>Replaces the static geometry and builds its hierarchy. Call once per world.</summary>
	public void SetStatic(RayPrimitive[] prims, int count)
	{
		StaticPrimCount = count;
		EnsurePrimCapacity(count);
		PrimsBuild = prims;
		Items = new BvhItem[count];
		BuildBase = 0;

		if (count == 0)
		{
			StaticNodeCount = 0;
			return;
		}

		var budget = BvhBuilder.NodeBudgetFor(count, ParallelBuildThreshold);
		EnsureNodeCapacity(budget);
		FillItems(0, count);
		StaticNodeCount = BvhBuilder.BuildParallel(Items, 0, count, Nodes, 0, budget, PrimitiveMaxLeaf, ParallelBuildThreshold);
		CommitItems(0, count);
		// Parallel builders reserve disjoint ranges. Reclaim the holes once for the immutable
		// hierarchy; dynamic geometry starts immediately after its reachable nodes.
		Nodes = BvhBuilder.Compact(Nodes);
		StaticNodeCount = Nodes.Length;
		BuildBase = count;
		PrimsBuild = [];
		Items = [];
	}

	/// <summary>
	/// Reserves room for this step's plant geometry. <paramref name="primCounts"/> holds the number
	/// of primitives of every plant slot; empty slots are allowed and are simply skipped.
	/// </summary>
	public void AllocatePlants(int[] primCounts, int plantCount)
	{
		if (PlantPrimFirst.Length < plantCount + 1)
		{
			PlantPrimFirst = new int[plantCount + 1];
			PlantPrimCount = new int[plantCount + 1];
		}

		var prim = StaticPrimCount;
		for (int i = 0; i < plantCount; ++i)
		{
			var c = primCounts[i];
			PlantPrimFirst[i] = prim;
			PlantPrimCount[i] = c;
			prim += c;
		}
		PlantPrimFirst[plantCount] = prim;
		DynamicPrimCount = prim - StaticPrimCount;
		DynamicNodeBudget = BvhBuilder.NodeBudgetFor(DynamicPrimCount, ParallelBuildThreshold);

		EnsurePrimCapacity(prim);
		if (PrimsBuild.Length < DynamicPrimCount)
		{
			var capacity = Math.Max(DynamicPrimCount, PrimsBuild.Length + (PrimsBuild.Length >> 1) + 1024);
			PrimsBuild = new RayPrimitive[capacity];
			Items = new BvhItem[capacity];
		}
		EnsureNodeCapacity(StaticNodeCount + DynamicNodeBudget);
	}

	/// <summary>The writable primitive slice of one plant slot, to be filled before
	/// <see cref="BuildPlants"/>.</summary>
	public Span<RayPrimitive> PlantPrimitives(int plant) => PrimsBuild.AsSpan(PlantPrimFirst[plant] - StaticPrimCount, PlantPrimCount[plant]);

	/// <summary>Builds the vegetation hierarchy and the two-blob top level.</summary>
	public void BuildPlants()
	{
		if (DynamicPrimCount > 0)
		{
			FillItemsParallel(StaticPrimCount, DynamicPrimCount);
			BvhBuilder.BuildParallel(Items, 0, DynamicPrimCount, Nodes, StaticNodeCount,
				DynamicNodeBudget, PrimitiveMaxLeaf, ParallelBuildThreshold);
			CommitItemsParallel(StaticPrimCount, DynamicPrimCount);
		}

		BlobCount = 0;
		if (StaticPrimCount > 0)
		{
			BlobNodeRoot[BlobCount] = 0;
			BlobMask[BlobCount] = MaskStatic;
			++BlobCount;
		}
		if (DynamicPrimCount > 0)
		{
			BlobNodeRoot[BlobCount] = StaticNodeCount;
			BlobMask[BlobCount] = MaskPlants;
			++BlobCount;
		}

		if (BlobCount == 0)
		{
			TopNodeCount = 0;
			return;
		}

		for (int b = 0; b < BlobCount; ++b)
		{
			var root = Nodes[BlobNodeRoot[b]];
			TopItems[b] = new BvhItem { Min = root.Min, Max = root.Max, Payload = b };
		}

		var topBuilder = new BvhBuilder();
		TopNodeCount = topBuilder.Build(TopItems, 0, BlobCount, TopNodes, 0, TopNodes.Length, TopMaxLeaf);
		for (int i = 0; i < BlobCount; ++i)
			TopBlob[i] = TopItems[i].Payload;
	}

	void FillItems(int first, int count)
	{
		for (int i = first; i < first + count; ++i)
		{
			var local = i - BuildBase;
			Bounds(in PrimsBuild[local], out var min, out var max);
			Items[local] = new BvhItem { Min = min, Max = max, Payload = local };
		}
	}

	void FillItemsParallel(int first, int count)
	{
		if (count < 8192)
		{
			FillItems(first, count);
			return;
		}
		Parallel.ForEach(Partitioner.Create(first, first + count), range => FillItems(range.Item1, range.Item2 - range.Item1));
	}

	/// <summary>Materialises the order the build settled on, so that traversal reads primitives
	/// sequentially.</summary>
	void CommitItems(int first, int count)
	{
		for (int i = first; i < first + count; ++i)
			Prims[i] = PrimsBuild[Items[i - BuildBase].Payload];
	}

	void CommitItemsParallel(int first, int count)
	{
		if (count < 8192)
		{
			CommitItems(first, count);
			return;
		}
		Parallel.ForEach(Partitioner.Create(first, first + count), range => CommitItems(range.Item1, range.Item2 - range.Item1));
	}

	void EnsurePrimCapacity(int count)
	{
		if (Prims.Length >= count)
			return;
		var dynamicCapacity = Math.Max(0, Prims.Length - StaticPrimCount);
		var capacity = Math.Max(count, StaticPrimCount + dynamicCapacity + (dynamicCapacity >> 1) + 1024);
		var grown = new RayPrimitive[capacity];
		Array.Copy(Prims, grown, Math.Min(Prims.Length, StaticPrimCount));
		Prims = grown;
	}

	void EnsureNodeCapacity(int count)
	{
		if (Nodes.Length >= count)
			return;
		var dynamicCapacity = Math.Max(0, Nodes.Length - StaticNodeCount);
		var capacity = Math.Max(count, StaticNodeCount + dynamicCapacity + (dynamicCapacity >> 1) + 1024);
		var grown = new BvhNode[capacity];
		Array.Copy(Nodes, grown, Math.Min(Nodes.Length, StaticNodeCount));
		Nodes = grown;
	}

	static void Bounds(in RayPrimitive prim, out Vector3 min, out Vector3 max)
	{
		switch (prim.Kind)
		{
			case RayPrimitiveKind.Triangle:
			{
				var b = prim.P + prim.U;
				var c = prim.P + prim.V;
				min = Vector3.Min(prim.P, Vector3.Min(b, c));
				max = Vector3.Max(prim.P, Vector3.Max(b, c));
				break;
			}
			case RayPrimitiveKind.Quad:
			{
				var half = prim.V * 0.5f;
				var p0 = prim.P - half;
				var p1 = prim.P + half;
				var p2 = p0 + prim.U;
				var p3 = p1 + prim.U;
				min = Vector3.Min(Vector3.Min(p0, p1), Vector3.Min(p2, p3));
				max = Vector3.Max(Vector3.Max(p0, p1), Vector3.Max(p2, p3));
				break;
			}
			case RayPrimitiveKind.Cylinder:
			{
				var r = new Vector3(prim.R);
				var tip = prim.P + prim.U;
				min = Vector3.Min(prim.P, tip) - r;
				max = Vector3.Max(prim.P, tip) + r;
				break;
			}
			default:
			{
				var r = new Vector3(prim.R);
				min = prim.P - r;
				max = prim.P + r;
				break;
			}
		}
	}

	/// <summary>Reciprocal of a direction with exact zeros nudged away from zero. Without this a
	/// ray that lies exactly in a slab plane produces <c>0 * infinity</c> and the whole slab test
	/// collapses to NaN, which silently drops hits.</summary>
	[M(AI)]
	static Vector3 SafeReciprocal(Vector3 d) => new(
		1f / (d.X == 0f ? 1e-20f : d.X),
		1f / (d.Y == 0f ? 1e-20f : d.Y),
		1f / (d.Z == 0f ? 1e-20f : d.Z));

	[M(AI)]
	static bool HitsBox(in BvhNode node, Vector3 origin, Vector3 invDirection, float tMin, float tMax)
	{
		var t0 = (node.Min - origin) * invDirection;
		var t1 = (node.Max - origin) * invDirection;
		var lo = Vector3.Min(t0, t1);
		var hi = Vector3.Max(t0, t1);
		var near = MathF.Max(MathF.Max(lo.X, lo.Y), MathF.Max(lo.Z, tMin));
		var far = MathF.Min(MathF.Min(hi.X, hi.Y), MathF.Min(hi.Z, tMax));
		return near <= far;
	}

	/// <summary>As <see cref="HitsBox"/> but also reports the entry distance, which the
	/// closest-hit traversal needs to visit children in front-to-back order.</summary>
	[M(AI)]
	static bool HitsBox(in BvhNode node, Vector3 origin, Vector3 invDirection, float tMin, float tMax, out float tNear)
	{
		var t0 = (node.Min - origin) * invDirection;
		var t1 = (node.Max - origin) * invDirection;
		var lo = Vector3.Min(t0, t1);
		var hi = Vector3.Max(t0, t1);
		tNear = MathF.Max(MathF.Max(lo.X, lo.Y), MathF.Max(lo.Z, tMin));
		var far = MathF.Min(MathF.Min(hi.X, hi.Y), MathF.Min(hi.Z, tMax));
		return tNear <= far;
	}

	static bool Intersect(in RayPrimitive prim, Vector3 o, Vector3 d, float tMin, float tMax, out float t)
	{
		t = 0f;
		switch (prim.Kind)
		{
			case RayPrimitiveKind.Triangle:
			{
				//Moeller-Trumbore. A positive determinant means the ray meets the front face, since
				//det(d, U, V) = -dot(d, cross(U, V)) = -dot(d, normal).
				var pv = Vector3.Cross(d, prim.V);
				var det = Vector3.Dot(prim.U, pv);
				if ((prim.Flags & FlagOneSided) != 0 ? det < 1e-12f : MathF.Abs(det) < 1e-12f)
					return false;
				var inv = 1f / det;
				var tv = o - prim.P;
				var u = Vector3.Dot(tv, pv) * inv;
				if (u < 0f || u > 1f)
					return false;
				var qv = Vector3.Cross(tv, prim.U);
				var v = Vector3.Dot(d, qv) * inv;
				if (v < 0f || u + v > 1f)
					return false;
				t = Vector3.Dot(prim.V, qv) * inv;
				return t >= tMin && t <= tMax;
			}
			case RayPrimitiveKind.Quad:
			{
				var n = Vector3.Cross(prim.U, prim.V);
				var den = Vector3.Dot(d, n);
				if (MathF.Abs(den) < 1e-14f)
					return false;
				t = Vector3.Dot(prim.P - o, n) / den;
				if (t < tMin || t > tMax)
					return false;
				var p = o + d * t - prim.P;
				//U and V are orthogonal by construction, so the plane coordinates are plain
				//projections; comparing against the squared axis lengths avoids normalising them.
				var alongU = Vector3.Dot(p, prim.U);
				if (alongU < 0f || alongU > Vector3.Dot(prim.U, prim.U))
					return false;
				var alongV = Vector3.Dot(p, prim.V);
				var halfV = 0.5f * Vector3.Dot(prim.V, prim.V);
				return alongV >= -halfV && alongV <= halfV;
			}
			case RayPrimitiveKind.Cylinder:
			{
				var axis = prim.U;
				var axisLen2 = Vector3.Dot(axis, axis);
				if (axisLen2 < 1e-16f)
					return false;
				var oc = o - prim.P;
				var dPerp = d - axis * (Vector3.Dot(d, axis) / axisLen2);
				var ocPerp = oc - axis * (Vector3.Dot(oc, axis) / axisLen2);
				var a2 = Vector3.Dot(dPerp, dPerp);
				if (a2 < 1e-16f)
					return false;
				var b2 = 2f * Vector3.Dot(dPerp, ocPerp);
				var c2 = Vector3.Dot(ocPerp, ocPerp) - prim.R * prim.R;
				var disc = b2 * b2 - 4f * a2 * c2;
				if (disc < 0f)
					return false;
				var sq = MathF.Sqrt(disc);
				var inv2a = 0.5f / a2;
				for (int r = 0; r < 2; ++r)
				{
					var cand = (r == 0 ? -b2 - sq : -b2 + sq) * inv2a;
					if (cand < tMin || cand > tMax)
						continue;
					var s = Vector3.Dot(oc + d * cand, axis) / axisLen2;
					if (s < 0f || s > 1f)
						continue;
					t = cand;
					return true;
				}
				return false;
			}
			default:
			{
				var oc = o - prim.P;
				var b2 = Vector3.Dot(oc, d);
				var c2 = Vector3.Dot(oc, oc) - prim.R * prim.R;
				var disc = b2 * b2 - c2;
				if (disc < 0f)
					return false;
				var sq = MathF.Sqrt(disc);
				var cand = -b2 - sq;
				if (cand < tMin || cand > tMax)
				{
					cand = -b2 + sq;
					if (cand < tMin || cand > tMax)
						return false;
				}
				t = cand;
				return true;
			}
		}
	}

	static Vector3 NormalAt(in RayPrimitive prim, Vector3 point)
	{
		switch (prim.Kind)
		{
			case RayPrimitiveKind.Triangle: return Vector3.Normalize(Vector3.Cross(prim.U, prim.V));
			case RayPrimitiveKind.Quad: return Vector3.Normalize(Vector3.Cross(prim.U, prim.V));
			case RayPrimitiveKind.Cylinder:
			{
				var axis = prim.U;
				var s = Vector3.Dot(point - prim.P, axis) / Vector3.Dot(axis, axis);
				return Vector3.Normalize(point - (prim.P + axis * s));
			}
			default: return Vector3.Normalize(point - prim.P);
		}
	}

	/// <summary>True as soon as any primitive selected by <paramref name="mask"/> blocks the ray.</summary>
	public bool AnyHit(Vector3 origin, Vector3 direction, float tMin, float tMax, int mask)
	{
		if (TopNodeCount == 0)
			return false;

		var inv = SafeReciprocal(direction);
		var stack = NodeStack ??= new int[TraversalStack];
		var sp = 0;
		stack[sp++] = ~0;

		while (sp > 0)
		{
			var entry = stack[--sp];
			if (entry < 0)
			{
				ref var node = ref TopNodes[~entry];
				if (!HitsBox(in node, origin, inv, tMin, tMax))
					continue;
				if (node.Count == 0)
				{
					stack[sp++] = ~node.Left;
					stack[sp++] = ~(node.Left + 1);
				}
				else
					for (int i = 0; i < node.Count; ++i)
					{
						var blob = TopBlob[node.Left + i];
						if ((BlobMask[blob] & mask) != 0)
							stack[sp++] = BlobNodeRoot[blob];
					}
			}
			else
			{
				ref var node = ref Nodes[entry];
				if (!HitsBox(in node, origin, inv, tMin, tMax))
					continue;
				if (node.Count == 0)
				{
					stack[sp++] = node.Left;
					stack[sp++] = node.Left + 1;
				}
				else
					for (int i = 0; i < node.Count; ++i)
						if (Intersect(in Prims[node.Left + i + (entry < StaticNodeCount ? 0 : StaticPrimCount)], origin, direction, tMin, tMax, out _))
							return true;
			}
		}
		return false;
	}

	/// <summary>The nearest primitive selected by <paramref name="mask"/>, with its shading normal.</summary>
	/// <remarks>
	/// Children are tested when they are pushed rather than when they are popped, and the nearer one
	/// is popped first. That way the stack carries each child's entry distance and a node whose box
	/// has since fallen behind the best hit is discarded without being opened - the standard
	/// front-to-back ordering, and the single largest saving in a closest-hit traversal.
	/// </remarks>
	public bool ClosestHit(Vector3 origin, Vector3 direction, float tMin, float tMax, int mask, out RayHit hit)
	{
		hit = default;
		if (TopNodeCount == 0)
			return false;

		var inv = SafeReciprocal(direction);
		var stack = NodeStack ??= new int[TraversalStack];
		var entry = EntryStack ??= new float[TraversalStack];
		var sp = 0;

		var best = -1;
		var bestT = tMax;

		if (!HitsBox(in TopNodes[0], origin, inv, tMin, bestT, out var rootNear))
			return false;
		stack[sp] = ~0;
		entry[sp++] = rootNear;

		while (sp > 0)
		{
			--sp;
			if (entry[sp] >= bestT)
				continue;
			var current = stack[sp];

			if (current < 0)
			{
				ref var node = ref TopNodes[~current];
				if (node.Count == 0)
				{
					var left = node.Left;
					var hitLeft = HitsBox(in TopNodes[left], origin, inv, tMin, bestT, out var tLeft);
					var hitRight = HitsBox(in TopNodes[left + 1], origin, inv, tMin, bestT, out var tRight);
					if (hitLeft && hitRight && tRight < tLeft)
					{
						stack[sp] = ~left; entry[sp++] = tLeft;
						stack[sp] = ~(left + 1); entry[sp++] = tRight;
					}
					else
					{
						if (hitRight) { stack[sp] = ~(left + 1); entry[sp++] = tRight; }
						if (hitLeft) { stack[sp] = ~left; entry[sp++] = tLeft; }
					}
				}
				else
					for (int i = 0; i < node.Count; ++i)
					{
						var blob = TopBlob[node.Left + i];
						if ((BlobMask[blob] & mask) == 0)
							continue;
						var root = BlobNodeRoot[blob];
						if (HitsBox(in Nodes[root], origin, inv, tMin, bestT, out var tRoot))
						{
							stack[sp] = root;
							entry[sp++] = tRoot;
						}
					}
			}
			else
			{
				ref var node = ref Nodes[current];
				if (node.Count == 0)
				{
					var left = node.Left;
					var hitLeft = HitsBox(in Nodes[left], origin, inv, tMin, bestT, out var tLeft);
					var hitRight = HitsBox(in Nodes[left + 1], origin, inv, tMin, bestT, out var tRight);
					if (hitLeft && hitRight && tRight < tLeft)
					{
						stack[sp] = left; entry[sp++] = tLeft;
						stack[sp] = left + 1; entry[sp++] = tRight;
					}
					else
					{
						if (hitRight) { stack[sp] = left + 1; entry[sp++] = tRight; }
						if (hitLeft) { stack[sp] = left; entry[sp++] = tLeft; }
					}
				}
				else
					for (int i = 0; i < node.Count; ++i)
					{
						var index = node.Left + i + (current < StaticNodeCount ? 0 : StaticPrimCount);
						if (Intersect(in Prims[index], origin, direction, tMin, bestT, out var t) && t < bestT)
						{
							bestT = t;
							best = index;
						}
					}
			}
		}

		if (best < 0)
			return false;

		ref var prim = ref Prims[best];
		hit.T = bestT;
		hit.Group = prim.Group;
		var normal = NormalAt(in prim, origin + direction * bestT);
		//Orient towards the incoming ray: imported meshes are not consistently wound, and the
		//plane-of-array estimate of the radiosity model needs the face that is actually seen.
		hit.Normal = Vector3.Dot(normal, direction) > 0f ? -normal : normal;
		hit.IsPlant = (prim.Flags & FlagPlant) != 0;
		return true;
	}

	/// <summary>Smallest cosine a leaf may present to the ray before its contribution to the total
	/// leaf area is capped. An edge-on leaf carries unbounded weight but is almost never hit, so
	/// capping trades a slight underestimate for a bounded variance.</summary>
	const float MinFoliageCosine = 0.05f;

	/// <summary>
	/// Walks the ray through the foliage and reports both leaf area readings for it.
	/// </summary>
	/// <returns>The number of leaf laminae crossed. Averaged over area-stratified points of a
	/// surface and cast along its normal, this is exactly the leaf area <em>projected</em> onto that
	/// surface per unit of surface area.</returns>
	/// <param name="totalAreaSum">The same crossings weighted by the reciprocal of the cosine each
	/// lamina presents to the ray. Its average over the same points is the <em>total</em> one-sided
	/// leaf area per unit of surface area, because weighting a leaf by one over its foreshortening
	/// undoes the projection - the reading that the green facade literature calls the leaf area
	/// index.</param>
	public int CountFoliageCrossings(Vector3 origin, Vector3 direction, float tMin, float tMax, out float totalAreaSum)
	{
		totalAreaSum = 0f;
		if (TopNodeCount == 0)
			return 0;

		var inv = SafeReciprocal(direction);
		var stack = NodeStack ??= new int[TraversalStack];
		var sp = 0;
		stack[sp++] = ~0;
		var crossings = 0;

		while (sp > 0)
		{
			var entry = stack[--sp];
			if (entry < 0)
			{
				ref var node = ref TopNodes[~entry];
				if (!HitsBox(in node, origin, inv, tMin, tMax))
					continue;
				if (node.Count == 0)
				{
					stack[sp++] = ~node.Left;
					stack[sp++] = ~(node.Left + 1);
				}
				else
					for (int i = 0; i < node.Count; ++i)
					{
						var blob = TopBlob[node.Left + i];
						if ((BlobMask[blob] & MaskPlants) != 0)
							stack[sp++] = BlobNodeRoot[blob];
					}
			}
			else
			{
				ref var node = ref Nodes[entry];
				if (!HitsBox(in node, origin, inv, tMin, tMax))
					continue;
				if (node.Count == 0)
				{
					stack[sp++] = node.Left;
					stack[sp++] = node.Left + 1;
				}
				else
					for (int i = 0; i < node.Count; ++i)
					{
						var index = node.Left + i + StaticPrimCount;
						if ((Prims[index].Flags & FlagFoliage) != 0
							&& Intersect(in Prims[index], origin, direction, tMin, tMax, out _))
						{
							++crossings;
							ref var lamina = ref Prims[index];
							var cosine = MathF.Abs(Vector3.Dot(Vector3.Normalize(Vector3.Cross(lamina.U, lamina.V)), direction));
							totalAreaSum += 1f / MathF.Max(cosine, MinFoliageCosine);
						}
					}
			}
		}
		return crossings;
	}
}
