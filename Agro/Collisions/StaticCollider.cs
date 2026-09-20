using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using AgentsSystem;
using M = System.Runtime.CompilerServices.MethodImplAttribute;

namespace Agro.Collisions;

/// <summary>What the nearest piece of immutable scene geometry is, and where.</summary>
public readonly struct StaticHit
{
	/// <summary>The point on the triangle nearest to the query.</summary>
	public readonly Vector3 Point;
	/// <summary>The point on the query segment nearest to the triangle. The escape direction is
	/// <c>OnQuery - Point</c>, which is what a response has to push along.</summary>
	public readonly Vector3 OnQuery;
	/// <summary>The triangle's geometric normal. Outward for soil; arbitrary for obstacles.</summary>
	public readonly Vector3 Normal;
	public readonly float Distance;
	public readonly int Triangle;
	public readonly StaticSurface Surface;

	public StaticHit(Vector3 point, Vector3 onQuery, Vector3 normal, float distance, int triangle, StaticSurface surface)
	{
		Point = point;
		OnQuery = onQuery;
		Normal = normal;
		Distance = distance;
		Triangle = triangle;
		Surface = surface;
	}
}

/// <summary>
/// A build-once bounding volume hierarchy over the soil hull and every obstacle, answering the
/// proximity queries the collision phase needs.
/// </summary>
/// <remarks>
/// <para>
/// Obstacles and terrain never move, so this is built once for the run and never refitted - the
/// same split <see cref="RayScene"/> makes for light. The production facade carries a couple of
/// hundred thousand triangles and the full imported model over a million, so the build uses
/// <see cref="BvhBuilder.BuildParallel"/>.
/// </para>
/// <para>
/// These are <em>proximity</em> queries, not ray queries, which is why this cannot simply reuse
/// <see cref="RayScene"/>: a growing organ needs the nearest surface and the direction out of it,
/// not the first surface along a direction.
/// </para>
/// </remarks>
public sealed partial class StaticCollider
{
	const MethodImplOptions AI = MethodImplOptions.AggressiveInlining;
	const int MaxLeaf = 4;
	static readonly int ParallelBuildThreshold = Math.Max(4096, 1 << 14);

	//Triangles, in the order the hierarchy's leaves reference them.
	Vector3[] A = [], U = [], V = [], N = [];
	StaticSurface[] Kind = [];
	BvhNode[] Nodes = [];
	byte[] SurfaceMasks = [];
	int NodeCount;
	int TriangleCount;
	int GeometryRevision;
	bool OpenSoil;

	//Traversal scratch. The phase queries from several threads at once, one plant per thread.
	[ThreadStatic] static int[]? NodeStack;

	public int Triangles => TriangleCount;
	public int SoilTriangles { get; private set; }
	public int ObstacleTriangles { get; private set; }
	public bool IsEmpty => NodeCount == 0;

	/// <summary>True when the soil hull encloses a volume, so an inside/outside test is meaningful.
	/// The regular voxel soil emits only its top face, which is not a closed hull.</summary>
	public bool SoilIsClosed { get; private set; }

	/// <summary>Signed volume of the soil triangles by the divergence theorem. A closed, outward
	/// wound hull sums to its own positive volume; an inward wound one to the negative of it.
	/// Diagnostic only: closure is checked by edge incidence, since an open surface can also
	/// have a nonzero signed volume.</summary>
	public double SoilSignedVolume { get; private set; }

	/// <summary>Volume of <see cref="SoilMin"/>..<see cref="SoilMax"/>, for scale.</summary>
	public double SoilBoundsVolume { get; private set; }

	/// <summary>The soil hull's bounding box, used by the open-hull fallback.</summary>
	public Vector3 SoilMin { get; private set; }
	public Vector3 SoilMax { get; private set; }

	/// <summary>Builds from a world. Call only after every obstacle is registered.</summary>
	public void Build(AgroWorld world) => Build(world.Soil, world.ObstacleList);

	public void Build(ISoilFormation? soil, IReadOnlyList<IObstacle> obstacles)
	{
		++GeometryRevision;
		OpenSoil = soil is SoilFormationRegularVoxels;
		var vertices = new List<Vector3>();
		var indices = new List<int>();
		var groups = new List<StaticSceneSource.Group>();
		StaticSceneSource.Collect(soil, obstacles, vertices, indices, groups);

		var count = indices.Count / 3;
		if (count == 0)
		{
			NodeCount = 0;
			TriangleCount = 0;
			return;
		}

		var surfaceOf = new StaticSurface[count];
		SoilTriangles = 0;
		ObstacleTriangles = 0;
		foreach (var g in groups)
		{
			for (int t = 0; t < g.TriangleCount; ++t)
				surfaceOf[g.FirstTriangle + t] = g.Surface;
			if (g.Surface == StaticSurface.Soil) SoilTriangles += g.TriangleCount;
			else ObstacleTriangles += g.TriangleCount;
		}

		var items = new BvhItem[count];
		var a = new Vector3[count];
		var u = new Vector3[count];
		var v = new Vector3[count];
		var n = new Vector3[count];

		var soilMin = new Vector3(float.MaxValue);
		var soilMax = new Vector3(float.MinValue);
		double signedVolume = 0d;
		var soilEdges = new Dictionary<ulong, int>();

		for (int t = 0; t < count; ++t)
		{
			var p0 = vertices[indices[t * 3]];
			var p1 = vertices[indices[t * 3 + 1]];
			var p2 = vertices[indices[t * 3 + 2]];
			a[t] = p0;
			u[t] = p1 - p0;
			v[t] = p2 - p0;
			var cross = Vector3.Cross(u[t], v[t]);
			var len = cross.Length();
			n[t] = len > 1e-20f ? cross / len : Vector3.UnitY;

			items[t] = new BvhItem
			{
				Min = Vector3.Min(p0, Vector3.Min(p1, p2)),
				Max = Vector3.Max(p0, Vector3.Max(p1, p2)),
				Payload = t,
			};

			if (surfaceOf[t] == StaticSurface.Soil)
			{
				for (int edge = 0; edge < 3; ++edge)
				{
					var first = (uint)indices[t * 3 + edge];
					var second = (uint)indices[t * 3 + (edge + 1) % 3];
					var key = ((ulong)Math.Min(first, second) << 32) | Math.Max(first, second);
					soilEdges.TryGetValue(key, out var uses);
					soilEdges[key] = uses + 1;
				}
				soilMin = Vector3.Min(soilMin, items[t].Min);
				soilMax = Vector3.Max(soilMax, items[t].Max);
				//Divergence theorem: a closed, outward-wound hull sums to its own positive volume.
				signedVolume += Vector3.Dot(p0, cross) / 6d;
			}
		}

		SoilMin = SoilTriangles > 0 ? soilMin : Vector3.Zero;
		SoilMax = SoilTriangles > 0 ? soilMax : Vector3.Zero;

		var extent = SoilTriangles > 0 ? soilMax - soilMin : Vector3.Zero;
		var reference = (double)MathF.Max(1e-9f, extent.X * extent.Y * extent.Z);
		SoilSignedVolume = signedVolume;
		SoilBoundsVolume = reference;
		// Disconnected trays occupy only a small fraction of their combined bounds. Their
		// total signed volume is not a closure test; every mesh edge must have two faces.
		SoilIsClosed = soilEdges.Count > 0 && soilEdges.Values.All(uses => uses == 2);

		var budget = BvhBuilder.NodeBudgetFor(count, ParallelBuildThreshold);
		var nodes = new BvhNode[budget];
		NodeCount = count >= ParallelBuildThreshold
			? BvhBuilder.BuildParallel(items, 0, count, nodes, 0, budget, MaxLeaf, ParallelBuildThreshold)
			: new BvhBuilder().Build(items, 0, count, nodes, 0, budget, MaxLeaf);

		//The build reorders items in place, so leaf ranges are contiguous. Reorder the triangle
		//data to match, which is what makes a leaf's triangles adjacent in memory.
		A = new Vector3[count];
		U = new Vector3[count];
		V = new Vector3[count];
		N = new Vector3[count];
		Kind = new StaticSurface[count];
		for (int i = 0; i < count; ++i)
		{
			var src = items[i].Payload;
			A[i] = a[src];
			U[i] = u[src];
			V[i] = v[src];
			N[i] = n[src];
			Kind[i] = surfaceOf[src];
		}

		Nodes = BvhBuilder.Compact(nodes);
		NodeCount = Nodes.Length;
		SurfaceMasks = new byte[NodeCount];
		for (int i = NodeCount - 1; i >= 0; --i)
		{
			var node = Nodes[i];
			if (node.Count == 0) SurfaceMasks[i] = (byte)(SurfaceMasks[node.Left] | SurfaceMasks[node.Left + 1]);
			else for (int j = 0; j < node.Count; ++j) SurfaceMasks[i] |= (byte)(1 << (int)Kind[node.Left + j]);
		}
		TriangleCount = count;
	}

	/// <summary>The nearest static surface of any kind within <paramref name="maxDistance"/>.</summary>
	public bool ClosestSurface(Vector3 point, float maxDistance, out StaticHit hit)
		=> Closest(point, point, maxDistance, null, out hit);

	/// <summary>
	/// The nearest static surface within <paramref name="maxDistance"/> of the segment
	/// <paramref name="p0"/>-<paramref name="p1"/> - the axis of a capsule or lamina proxy.
	/// </summary>
	public bool ClosestSurface(Vector3 p0, Vector3 p1, float maxDistance, out StaticHit hit)
		=> Closest(p0, p1, maxDistance, null, out hit);

	/// <summary>The nearest obstacle, ignoring soil.</summary>
	public bool ClosestObstacleSurface(Vector3 p0, Vector3 p1, float maxDistance, out StaticHit hit)
		=> Closest(p0, p1, maxDistance, StaticSurface.Obstacle, out hit);

	/// <summary>The nearest obstacle to an organ proxy, blade included.</summary>
	public bool ClosestObstacleSurface(Vector3 p0, Vector3 p1, Vector3 across, float maxDistance, out StaticHit hit)
		=> Closest(p0, p1, across, maxDistance, StaticSurface.Obstacle, out hit);

	/// <summary>The nearest soil triangle, ignoring obstacles.</summary>
	public bool ClosestSoilSurface(Vector3 point, float maxDistance, out StaticHit hit)
		=> Closest(point, point, maxDistance, StaticSurface.Soil, out hit);

	/// <summary>The nearest soil triangle to a segment, ignoring obstacles.</summary>
	public bool ClosestSoilSurface(Vector3 p0, Vector3 p1, float maxDistance, out StaticHit hit)
		=> Closest(p0, p1, maxDistance, StaticSurface.Soil, out hit);

	/// <summary>The nearest soil triangle to an organ proxy, blade included.</summary>
	public bool ClosestSoilSurface(Vector3 p0, Vector3 p1, Vector3 across, float maxDistance, out StaticHit hit)
		=> Closest(p0, p1, across, maxDistance, StaticSurface.Soil, out hit);

	/// <summary>
	/// Nearest surface to a segment, optionally restricted to one kind.
	/// </summary>
	/// <remarks>
	/// The filter is not a convenience. Asking for the nearest surface of any kind and discarding
	/// the answer when it is the wrong kind silently skips the test: a leaf lying near the ground
	/// and near a wall reports the ground, and the wall is never examined at all.
	/// </remarks>
	bool Closest(Vector3 p0, Vector3 p1, float maxDistance, StaticSurface? filter, out StaticHit hit)
		=> Closest(p0, p1, Vector3.Zero, maxDistance, filter, out hit);

	/// <summary>
	/// Nearest surface to an organ proxy: a segment when <paramref name="across"/> is zero, and the
	/// blade swept by it when it is not.
	/// </summary>
	public bool Closest(Vector3 p0, Vector3 p1, Vector3 across, float maxDistance, StaticSurface? filter, out StaticHit hit)
	{
		hit = default;
		if (NodeCount == 0)
			return false;
		if (filter == StaticSurface.Soil && SoilTriangles == 0)
			return false;
		if (filter == StaticSurface.Obstacle && ObstacleTriangles == 0)
			return false;

		var stack = NodeStack ??= new int[128];
		var sp = 0;
		stack[sp++] = 0;

		var best = maxDistance;
		var bestSquared = best * best;
		var found = false;
		var acrossLength = across.Length();
		var blade = acrossLength > 1e-7f;
		var c0 = p0 - across; var c1 = p0 + across; var c2 = p1 + across; var c3 = p1 - across;
		var qMin = Vector3.Min(Vector3.Min(c0, c1), Vector3.Min(c2, c3));
		var qMax = Vector3.Max(Vector3.Max(c0, c1), Vector3.Max(c2, c3));

		while (sp > 0)
		{
			var nodeIndex = stack[--sp];
			if (filter.HasValue && (SurfaceMasks[nodeIndex] & (1 << (int)filter.Value)) == 0) continue;
			ref var node = ref Nodes[nodeIndex];
			if (BoxDistanceSquared(in node, qMin, qMax) > bestSquared)
				continue;

			if (node.Count == 0)
			{
				//Both children, nearer one explored first.
				var l = node.Left;
				var r = node.Left + 1;
				var dl = BoxDistanceSquared(in Nodes[l], qMin, qMax);
				var dr = BoxDistanceSquared(in Nodes[r], qMin, qMax);
				if (dl < dr)
				{
					if (dr <= bestSquared) stack[sp++] = r;
					if (dl <= bestSquared) stack[sp++] = l;
				}
				else
				{
					if (dl <= bestSquared) stack[sp++] = l;
					if (dr <= bestSquared) stack[sp++] = r;
				}
				continue;
			}

			for (int i = 0; i < node.Count; ++i)
			{
				var t = node.Left + i;
				if (filter.HasValue && Kind[t] != filter.Value)
					continue;
				//Every point of the blade lies within its half width of the axis, so the axis
				//distance less that half width is a lower bound on the blade distance. One
				//segment test then rules out most triangles before the twelve the rectangle costs.
				var d = SegmentTriangleDistance(p0, p1, A[t], U[t], V[t], out var onTriangle, out var onQuery);
				if (blade)
				{
					if (d - acrossLength >= best)
						continue;
					d = RectangleTriangleDistance(c0, c1, c2, c3, A[t], U[t], V[t], out onTriangle, out onQuery);
				}
				if (d < best)
				{
					best = d;
					bestSquared = d * d;
					found = true;
					hit = new StaticHit(onTriangle, onQuery, N[t], d, t, Kind[t]);
					if (d == 0f) return true;
				}
			}
		}

		return found;
	}

	/// <summary>Distance to the nearest static surface, clamped to <paramref name="maxDistance"/>.
	/// The collision phase caches this per organ so an organ far from anything is not retested
	/// until it has moved further than its own clearance.</summary>
	public float Clearance(Vector3 point, float maxDistance)
		=> ClosestSurface(point, maxDistance, out var hit) ? hit.Distance : maxDistance;

	/// <summary>
	/// Whether a point is inside the soil body.
	/// </summary>
	/// <remarks>
	/// Imported troughs use ray parity through their closed hulls, independent of winding.
	/// A global facade box and a nearest face plane both misclassify gaps between trays.
	/// The regular voxel field uses a half space bounded by its actual horizontal footprint.
	/// </remarks>
    bool InsideSoilUncached(Vector3 point, float searchRadius)
    {
        if (SoilTriangles == 0 || point.X < SoilMin.X || point.X > SoilMax.X
            || point.Z < SoilMin.Z || point.Z > SoilMax.Z || point.Y > SoilMax.Y) return false;
        if (OpenSoil) return true;
        if (point.Y < SoilMin.Y) return false;
        // A ray through disconnected tray hulls. Deduplicate coplanar triangle hits at shared
        // edges; a global facade bounding box or nearest face plane is not a soil volume.
        var hits = SoilRayHits ??= new List<float>();
        hits.Clear();
        var direction = Vector3.Normalize(new Vector3(0.173f, 1f, 0.237f));
        var inverse = new Vector3(1f / direction.X, 1f / direction.Y, 1f / direction.Z);
        var stack = NodeStack ??= new int[128];
        int sp = 0; stack[sp++] = 0;
        while (sp > 0)
        {
            var nodeIndex = stack[--sp];
            if ((SurfaceMasks[nodeIndex] & 1) == 0) continue;
            var node = Nodes[nodeIndex];
            var t0 = (node.Min - point) * inverse; var t1 = (node.Max - point) * inverse;
            var near = Vector3.Min(t0, t1); var far = Vector3.Max(t0, t1);
            if (MathF.Min(far.X, MathF.Min(far.Y, far.Z)) < MathF.Max(0f, MathF.Max(near.X, MathF.Max(near.Y, near.Z)))) continue;
            if (node.Count == 0) { stack[sp++] = node.Left; stack[sp++] = node.Left + 1; continue; }
            for (int j = 0; j < node.Count; ++j)
            {
                int t = node.Left + j;
                if (Kind[t] != StaticSurface.Soil) continue;
                var p = Vector3.Cross(direction, V[t]); var det = Vector3.Dot(U[t], p);
                if (MathF.Abs(det) < 1e-12f) continue;
                var offset = point - A[t]; var u = Vector3.Dot(offset, p) / det;
                if (u < -1e-6f || u > 1.000001f) continue;
                var q = Vector3.Cross(offset, U[t]); var v = Vector3.Dot(direction, q) / det;
                if (v < -1e-6f || u + v > 1.000001f) continue;
                var distance = Vector3.Dot(V[t], q) / det;
                if (distance > 1e-6f) hits.Add(distance);
            }
        }
        hits.Sort();
        int crossings = 0; float previous = float.NegativeInfinity;
        foreach (var d in hits) if (d - previous > 1e-5f) { ++crossings; previous = d; }
        return (crossings & 1) != 0;
    }
    [ThreadStatic] static List<float>? SoilRayHits;

	/// <summary>Distance to the nearest surface by a linear scan over every triangle. Exists so a
	/// probe can hold the hierarchy to an answer that cannot itself be wrong; far too slow for the
	/// simulation.</summary>
	public float BruteForceClearance(Vector3 point)
	{
		var best = float.MaxValue;
		for (int t = 0; t < TriangleCount; ++t)
			best = MathF.Min(best, SegmentTriangleDistance(point, point, A[t], U[t], V[t], out _, out _));
		return best;
	}

	[M(AI)] static float BoxDistanceSquared(in BvhNode node, Vector3 qMin, Vector3 qMax)
	{
		//Gap between two axis aligned boxes, zero when they overlap.
		var d = Vector3.Max(Vector3.Max(node.Min - qMax, qMin - node.Max), Vector3.Zero);
		return d.LengthSquared();
	}

	/// <summary>
	/// Distance from a flat rectangular blade to a triangle.
	/// </summary>
	/// <remarks>
	/// A lamina is a rectangle, and approximating it by a capsule of its half width is not a
	/// conservative simplification but a wrong one: a leaf lying flat ALONG a wall reads as
	/// centimetres inside it, because the half width extends across the wall rather than into it.
	/// Measured on a Geranium grown against a wall, 28 of 45 reported breaches were this artefact.
	///
	/// The rectangle splits into two triangles and each is measured against the target by running
	/// every edge of one against the other - which also returns zero when they cross, since
	/// <see cref="SegmentTriangleDistance"/> detects a segment piercing a face.
	/// </remarks>
	public static float RectangleTriangleDistance(Vector3 c0, Vector3 c1, Vector3 c2, Vector3 c3,
		Vector3 a, Vector3 ab, Vector3 ac, out Vector3 onTriangle, out Vector3 onRectangle)
	{
		var best = float.MaxValue;
		onTriangle = a;
		onRectangle = c0;

		//Edges of the blade against the target triangle.
		EdgeVsTriangle(c0, c1, a, ab, ac, ref best, ref onTriangle, ref onRectangle);
		EdgeVsTriangle(c1, c2, a, ab, ac, ref best, ref onTriangle, ref onRectangle);
		EdgeVsTriangle(c2, c3, a, ab, ac, ref best, ref onTriangle, ref onRectangle);
		EdgeVsTriangle(c3, c0, a, ab, ac, ref best, ref onTriangle, ref onRectangle);
		if (best <= 0f)
			return 0f;

		//And the target's edges against the blade's two triangles, which is what catches a small
		//triangle sitting entirely within the blade's outline.
		var b = a + ab;
		var c = a + ac;
		EdgeVsBlade(a, b, c0, c1, c2, c3, ref best, ref onTriangle, ref onRectangle);
		EdgeVsBlade(b, c, c0, c1, c2, c3, ref best, ref onTriangle, ref onRectangle);
		EdgeVsBlade(c, a, c0, c1, c2, c3, ref best, ref onTriangle, ref onRectangle);

		return best;

		static void EdgeVsTriangle(Vector3 e0, Vector3 e1, Vector3 a, Vector3 ab, Vector3 ac,
			ref float best, ref Vector3 onTriangle, ref Vector3 onRectangle)
		{
			var d = SegmentTriangleDistance(e0, e1, a, ab, ac, out var tri, out var seg);
			if (d < best) { best = d; onTriangle = tri; onRectangle = seg; }
		}

		static void EdgeVsBlade(Vector3 e0, Vector3 e1, Vector3 c0, Vector3 c1, Vector3 c2, Vector3 c3,
			ref float best, ref Vector3 onTriangle, ref Vector3 onRectangle)
		{
			var d = SegmentTriangleDistance(e0, e1, c0, c1 - c0, c2 - c0, out var onBlade, out var onEdge);
			if (d < best) { best = d; onTriangle = onEdge; onRectangle = onBlade; }
			d = SegmentTriangleDistance(e0, e1, c0, c2 - c0, c3 - c0, out onBlade, out onEdge);
			if (d < best) { best = d; onTriangle = onEdge; onRectangle = onBlade; }
		}
	}

	/// <summary>Closest point of a triangle to <paramref name="p"/>, by Ericson's region test.</summary>
	[M(AI)] public static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 ab, Vector3 ac)
	{
		var ap = p - a;
		float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
		if (d1 <= 0f && d2 <= 0f) return a;

		var b = a + ab;
		var bp = p - b;
		float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
		if (d3 >= 0f && d4 <= d3) return b;

		float vc = d1 * d4 - d3 * d2;
		if (vc <= 0f && d1 >= 0f && d3 <= 0f)
			return a + ab * (d1 / (d1 - d3));

		var c = a + ac;
		var cp = p - c;
		float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
		if (d6 >= 0f && d5 <= d6) return c;

		float vb = d5 * d2 - d1 * d6;
		if (vb <= 0f && d2 >= 0f && d6 <= 0f)
			return a + ac * (d2 / (d2 - d6));

		float va = d3 * d6 - d5 * d4;
		if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
			return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

		float denom = 1f / (va + vb + vc);
		return a + ab * (vb * denom) + ac * (vc * denom);
	}

	/// <summary>
	/// Distance between a segment and a triangle, with the closest point on the triangle.
	/// </summary>
	/// <remarks>
	/// A degenerate segment (both endpoints equal) reduces to the point case, so the point query
	/// and the capsule query share one routine.
	/// </remarks>
	public static float SegmentTriangleDistance(Vector3 p0, Vector3 p1, Vector3 a, Vector3 ab, Vector3 ac,
		out Vector3 onTriangle, out Vector3 onSegment)
	{
		var dir = p1 - p0;
		var lenSq = dir.LengthSquared();

		if (lenSq > 1e-16f)
		{
			//A segment crossing the plane inside the triangle touches it, distance zero.
			var normal = Vector3.Cross(ab, ac);
			var denom = Vector3.Dot(normal, dir);
			if (MathF.Abs(denom) > 1e-20f)
			{
				var t = Vector3.Dot(normal, a - p0) / denom;
				if (t >= 0f && t <= 1f)
				{
					var hit = p0 + dir * t;
					var closest = ClosestPointOnTriangle(hit, a, ab, ac);
					if (Vector3.DistanceSquared(hit, closest) <= 1e-12f)
					{
						onTriangle = hit;
						onSegment = hit;
						return 0f;
					}
				}
			}
		}

		//Otherwise the minimum sits on an endpoint or on a triangle edge.
		var best = float.MaxValue;
		var nearest = a;
		var nearestOnSegment = p0;

		Consider(ClosestPointOnTriangle(p0, a, ab, ac), p0, ref best, ref nearest, ref nearestOnSegment);
		if (lenSq > 1e-16f)
			Consider(ClosestPointOnTriangle(p1, a, ab, ac), p1, ref best, ref nearest, ref nearestOnSegment);

		var b = a + ab;
		var c = a + ac;
		EdgeAgainstSegment(p0, p1, a, b, ref best, ref nearest, ref nearestOnSegment);
		EdgeAgainstSegment(p0, p1, b, c, ref best, ref nearest, ref nearestOnSegment);
		EdgeAgainstSegment(p0, p1, c, a, ref best, ref nearest, ref nearestOnSegment);

		onTriangle = nearest;
		onSegment = nearestOnSegment;
		return MathF.Sqrt(best);
	}

	[M(AI)] static void Consider(Vector3 tri, Vector3 seg, ref float best, ref Vector3 nearest, ref Vector3 nearestOnSegment)
	{
		var d = Vector3.DistanceSquared(tri, seg);
		if (d < best) { best = d; nearest = tri; nearestOnSegment = seg; }
	}

	[M(AI)] static void EdgeAgainstSegment(Vector3 p0, Vector3 p1, Vector3 e0, Vector3 e1,
		ref float best, ref Vector3 nearest, ref Vector3 nearestOnSegment)
	{
		ClosestPointsBetweenSegments(p0, p1, e0, e1, out var onSeg, out var onEdge, out var distSq);
		if (distSq < best) { best = distSq; nearest = onEdge; nearestOnSegment = onSeg; }
	}

	/// <summary>Closest points of two segments, the clamped-parameter form.</summary>
	public static void ClosestPointsBetweenSegments(Vector3 p0, Vector3 p1, Vector3 q0, Vector3 q1,
		out Vector3 onFirst, out Vector3 onSecond, out float distanceSquared)
	{
		var d1 = p1 - p0;
		var d2 = q1 - q0;
		var r = p0 - q0;
		double a = Dot(d1, d1), e = Dot(d2, d2), f = Dot(d2, r);
		double s, t;

		if (a <= 1e-16f && e <= 1e-16f)
		{
			onFirst = p0;
			onSecond = q0;
			distanceSquared = Vector3.DistanceSquared(p0, q0);
			return;
		}

		if (a <= 1e-16f)
		{
			s = 0f;
			t = Math.Clamp(f / e, 0f, 1f);
		}
		else
		{
			double c = Dot(d1, r);
			if (e <= 1e-16f)
			{
				t = 0f;
				s = Math.Clamp(-c / a, 0f, 1f);
			}
			else
			{
				double b = Dot(d1, d2);
				double denom = a * e - b * b;
				// The determinant has units m^4. An absolute threshold classifies young,
				// perpendicular millimetre segments as parallel. Test relative conditioning.
				s = denom > 1e-12 * a * e ? Math.Clamp((b * f - c * e) / denom, 0d, 1d) : 0d;
				t = (b * s + f) / e;
				if (t < 0f) { t = 0f; s = Math.Clamp(-c / a, 0f, 1f); }
				else if (t > 1f) { t = 1f; s = Math.Clamp((b - c) / a, 0f, 1f); }
			}
		}

		onFirst = p0 + d1 * (float)s;
		onSecond = q0 + d2 * (float)t;
		distanceSquared = Vector3.DistanceSquared(onFirst, onSecond);

		static double Dot(Vector3 x, Vector3 y) => (double)x.X * y.X + (double)x.Y * y.Y + (double)x.Z * y.Z;
	}
}
