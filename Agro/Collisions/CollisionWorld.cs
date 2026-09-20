using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using AgentsSystem;

namespace Agro.Collisions;

/// <summary>
/// The world-level collision phase: soft foliage proposals followed by hard static constraints.
/// </summary>
/// <remarks>
/// <para>
/// Runs as a world callback, which puts it after <c>Census</c> - every tree cache rebuilt, every
/// index compacted - and outside the <c>Parallel.For</c> over formations that drives <c>Tick</c>.
/// That matters: a plant's <c>Tick</c> may only touch its own agents, so nothing involving two
/// plants can live there. It is registered ahead of the irradiance callback so the light model sees
/// resolved geometry.
/// </para>
/// <para>
/// Plants are independent within a pass - each writes only its own organs and reads only immutable
/// scene geometry - so the parallel form produces the same result as the sequential one.
/// </para>
/// </remarks>
public sealed partial class CollisionWorld
{
	readonly StaticCollider Static = new();
	bool StaticReady;
	IObstacle[] StaticObstacles = [];
	static readonly bool TraceTiming = Environment.GetEnvironmentVariable("AGRO_COLLISION_TIMING") != null;

	/// <summary>Organs pushed out of an obstacle since the run began.</summary>
	public long ObstacleContacts;
	/// <summary>Organs pushed back out of the soil since the run began.</summary>
	public long SoilContacts;
	/// <summary>Legacy diagnostic, always zero: contacts no longer kill plant tissue.</summary>
	public long ObstructionCuts;
	/// <summary>Contacts resolved between organs of different plants.</summary>
	public long InterPlantContacts;
	/// <summary>Organ pairs the inter-plant narrow phase examined.</summary>
	public long InterPlantCandidates;
	/// <summary>Plant pairs whose canopies overlap, as of the last refresh.</summary>
	public int NeighbourPairs;
	/// <summary>Organ queries issued against the static scene.</summary>
	public long StaticQueries;
	/// <summary>Organ-steps skipped because the organ was still inside its cached clearance.</summary>
	public long ClearanceSkips;
	public long Ticks;

	public StaticCollider StaticScene => Static;

	/// <summary>Largest angular increment applied by one hard-contact sweep.</summary>
	static float MaxCorrectionRadians => CollisionSettings.MaxCorrectionRadians;

	public void Tick(SimulationWorld world, uint timestep, IList<IFormation> formations, IList<IObstacle> obstacles)
	{
		if (!CollisionSettings.Enabled)
			return;

		EnsureStatic((AgroWorld)world);



		var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
		var previousIntervals = FoliageGeometry.IntervalCount; var previousTriangles = FoliageGeometry.TriangleCount;
		var plants = ActivePlants;
		plants.Clear();
		for (int i = 0; i < formations.Count; ++i)
			if (formations[i] is PlantFormation2 p && p.AG.Count > 0)
				plants.Add(p);

        ResolveHardPlants(plants);

        var afterHard = TraceTiming ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        if (((AgroWorld)world).SoftCollisions) ResolveVegetation(plants);
        var afterFoliage = TraceTiming ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        foreach (var plant in plants)
        {
            plant.AG.ProcessDevelopments(this);
            plant.AG.SealNewGeometry();
            plant.AG.CompleteGrowthAttempts();
        }

        if (TraceTiming)
        {
            var scale = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            Console.WriteLine($"CONTACT_TIMING step={timestep} hardMs={(afterHard-t0)*scale:F2} foliageMs={(afterFoliage-afterHard)*scale:F2} birthMs={(System.Diagnostics.Stopwatch.GetTimestamp()-afterFoliage)*scale:F2} plants={plants.Count} organs={plants.Sum(p=>p.AG.Count)} intervals={FoliageGeometry.IntervalCount-previousIntervals} triangles={FoliageGeometry.TriangleCount-previousTriangles}");
        }

		Ticks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
	}

    void ResolveHardPlants(List<PlantFormation2> plants)
    {
        if (plants.Count < 2) foreach (var plant in plants) ResolveHard(plant);
        else Parallel.ForEach(plants, ResolveHard);
    }

	internal void EnsureStatic(AgroWorld world)
	{
		var changed = StaticObstacles.Length != world.ObstacleList.Count;
		for (int i = 0; !changed && i < StaticObstacles.Length; ++i)
			changed = !ReferenceEquals(StaticObstacles[i], world.ObstacleList[i]);
		if (!StaticReady || changed)
		{
			var wasReady = StaticReady;
			StaticReady = true;
			StaticObstacles = world.ObstacleList.ToArray();
			var started = System.Diagnostics.Stopwatch.GetTimestamp();
			Static.Build(world);
			if (wasReady)
				foreach (var plant in world.FormationList.OfType<PlantFormation2>())
				{ plant.AG.InvalidateStaticClearance(); plant.AG.WakeGrowth(); }
			var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
			//Say it out loud. At the production facade this is nearly two million triangles and a
			//visible one-step stall; a silent pause there looks like a hang.
			Console.WriteLine($"Collision scene: {Static.Triangles} triangles "
				+ $"({Static.SoilTriangles} soil, {Static.ObstacleTriangles} obstacle) in {ms:F0} ms"
				+ $"{(Static.SoilTriangles > 0 ? Static.SoilIsClosed ? ", soil hull closed" : ", open soil surface (regular fields use their finite footprint)" : "")}");
		}
	}

	readonly List<(PlantFormation2 A, PlantFormation2 B)> Neighbours = [];
	(int Index, Vector3 Min, Vector3 Max)[] SweepBoxes = [];
	static readonly Comparer<(int Index, Vector3 Min, Vector3 Max)> SweepOrder =
		Comparer<(int Index, Vector3 Min, Vector3 Max)>.Create(
			(x, y) => x.Min.X != y.Min.X ? x.Min.X.CompareTo(y.Min.X) : x.Index.CompareTo(y.Index));

	/// <summary>Pairs of plants whose canopy boxes overlap, by a sweep along X.</summary>
	void RefreshNeighbours(List<PlantFormation2> plants)
	{
		Neighbours.Clear();
		PlantLeaves.Clear();
		if (SweepBoxes.Length < plants.Count)
			Array.Resize(ref SweepBoxes, Math.Max(plants.Count, SweepBoxes.Length + SweepBoxes.Length / 2 + 8));
		var boxes = SweepBoxes;
		var live = 0;
		for (int i = 0; i < plants.Count; ++i)
			if (plants[i].AG.TryGetWorldBounds(out var min, out var max))
			{
				boxes[live++] = (i, min, max);
				PlantLeaves.Add((i, new(min, max)));
			}
		PlantBounds.Build(PlantLeaves);

		Array.Sort(boxes, 0, live, SweepOrder);

		for (int i = 0; i < live; ++i)
			for (int j = i + 1; j < live && boxes[j].Min.X <= boxes[i].Max.X; ++j)
			{
				if (boxes[i].Min.Y > boxes[j].Max.Y || boxes[i].Max.Y < boxes[j].Min.Y
					|| boxes[i].Min.Z > boxes[j].Max.Z || boxes[i].Max.Z < boxes[j].Min.Z)
					continue;
				var a = boxes[i].Index;
				var b = boxes[j].Index;
				Neighbours.Add(a < b ? (plants[a], plants[b]) : (plants[b], plants[a]));
			}

		NeighbourPairs = Neighbours.Count;
	}
}
