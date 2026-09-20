using System.Collections.Generic;
using System.Numerics;
using AgentsSystem;

namespace Agro.Collisions;

/// <summary>What a triangle of the immutable scene belongs to.</summary>
public enum StaticSurface : byte
{
	/// <summary>The soil hull. Wound outward, so the sign of the distance to it says whether a
	/// point is buried.</summary>
	Soil = 0,
	/// <summary>An obstacle: a wall, an umbrella, or an imported facade mesh. Winding is not
	/// reliable - <see cref="Agro.Wall"/> is wound inwards throughout and imported meshes are
	/// inconsistent - so obstacle response must never depend on it.</summary>
	Obstacle = 1,
}

/// <summary>
/// Assembles the geometry that does not move for the whole run: the soil hull and every obstacle.
/// </summary>
/// <remarks>
/// Both the collision phase and <see cref="CpuLightSolver"/> trace against this same set. They used
/// to assemble it independently, and a divergence would be invisible in both: plants deflected by
/// geometry that does not shade them, or shaded by geometry that does not stop them.
/// </remarks>
public static class StaticSceneSource
{
	/// <summary>One contiguous run of triangles that came from a single source object.</summary>
	public readonly struct Group
	{
		public readonly string Name;
		public readonly StaticSurface Surface;
		/// <summary>Index of the first triangle, i.e. of its first entry in the index list / 3.</summary>
		public readonly int FirstTriangle;
		public readonly int TriangleCount;

		public Group(string name, StaticSurface surface, int firstTriangle, int triangleCount)
		{
			Name = name;
			Surface = surface;
			FirstTriangle = firstTriangle;
			TriangleCount = triangleCount;
		}
	}

	/// <summary>Appends the soil hull followed by every obstacle into caller-owned buffers.</summary>
	/// <remarks>
	/// Call this only once every obstacle is registered. <see cref="SoilFormationsList.SetWorld"/>
	/// adds the imported mesh obstacles during world construction, before the request's own
	/// obstacles are added, so collecting any earlier silently misses geometry.
	/// </remarks>
	public static void Collect(ISoilFormation? soil, IReadOnlyList<IObstacle> obstacles,
		List<Vector3> vertices, List<int> indices, List<Group> groups)
	{
		vertices.Clear();
		indices.Clear();
		groups.Clear();

		if (soil != null)
		{
			var first = indices.Count / 3;
			soil.AppendSurfaceTriangles(vertices, indices);
			groups.Add(new Group("soil", StaticSurface.Soil, first, indices.Count / 3 - first));
		}

		for (int o = 0; o < obstacles.Count; ++o)
		{
			var first = indices.Count / 3;
			obstacles[o].AppendTriangles(vertices, indices);
			groups.Add(new Group(obstacles[o].Name, StaticSurface.Obstacle, first, indices.Count / 3 - first));
		}
	}
}
