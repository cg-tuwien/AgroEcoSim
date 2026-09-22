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
    long StaticRevision;
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
        InitializeBackend((AgroWorld)world);



		var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
		var previousIntervals = SweepIntervals; var previousTriangles = TriangleTests;
        PhaseThread = Environment.CurrentManagedThreadId;
        AttributedMainIntervals = AttributedMainTriangles = 0;
        var mainIntervals = FoliageGeometry.IntervalCount;
        var mainTriangles = FoliageGeometry.TriangleCount;
		var plants = ActivePlants;
		plants.Clear();
		for (int i = 0; i < formations.Count; ++i)
			if (formations[i] is PlantFormation2 p && p.AG.Count > 0)
				plants.Add(p);

        PrepareWork(plants);

        ResolveHardPlants(plants);

        var afterHard = TraceTiming ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        if (((AgroWorld)world).SoftCollisions) ResolveVegetation(plants);
        var afterFoliage = TraceTiming ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        // Soft collisions can be disabled, but development still calls through the same
        // hierarchy APIs. Keep the plant lookup ready in either mode.
        if (!((AgroWorld)world).SoftCollisions) RefreshNeighbours(plants);
        foreach (var plant in plants)
        {
            plant.AG.ProcessDevelopments(this);
            plant.AG.SealNewGeometry();
            plant.AG.CompleteGrowthAttempts();
        }
        FlushCounters();
        // Include serial development/rollback trials as well as worker jobs. A job can
        // run on this thread too, so remove its already-reduced counts before adding.
        SweepIntervals += FoliageGeometry.IntervalCount - mainIntervals - AttributedMainIntervals;
        TriangleTests += FoliageGeometry.TriangleCount - mainTriangles - AttributedMainTriangles;

        if (TraceTiming)
        {
            var scale = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            Console.WriteLine($"CONTACT_TIMING step={timestep} hardMs={(afterHard-t0)*scale:F2} foliageMs={(afterFoliage-afterHard)*scale:F2} birthMs={(System.Diagnostics.Stopwatch.GetTimestamp()-afterFoliage)*scale:F2} plants={plants.Count} organs={plants.Sum(p=>p.AG.Count)} intervals={SweepIntervals-previousIntervals} triangles={TriangleTests-previousTriangles} correctionRounds={LastCorrectionRounds} validationRounds={LastValidationRounds} fallbackRounds={LastFallbackRounds}");
        }

		Ticks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
	}

    void ResolveHardPlants(List<PlantFormation2> plants)
    {
        RunJobs(plants, plants.Sum(p => (long)p.AG.Count), plant => MeasureJob(Work[plant], () => ResolveHard(plant)));
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
            ++StaticRevision;
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
	/// <summary>Pairs of plants whose swept canopy bounds overlap, in canonical order.</summary>
	void RefreshNeighbours(List<PlantFormation2> plants)
	{
		Neighbours.Clear();
		PlantLeaves.Clear();
		for (int i = 0; i < plants.Count; ++i)
			if (plants[i].AG.TryGetWorldBounds(out var min, out var max))
			{
				PlantLeaves.Add((i, new(min, max)));
			}
        var leaves = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(PlantLeaves);
        if (PlantMembershipChanged || !PlantBounds.TryRefit(leaves)) PlantBounds.Build(PlantLeaves);
        PlantMembershipChanged = false;
        PlantPairs.Clear(); PlantBounds.GetOverlappingPairs(PlantPairs);
        PlantPairs.Sort(static (a, b) => a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : a.Item2.CompareTo(b.Item2));
        foreach (var (a, b) in PlantPairs) Neighbours.Add((plants[a], plants[b]));

		NeighbourPairs = Neighbours.Count;
	}
}
