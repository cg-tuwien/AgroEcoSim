using System.Diagnostics;
using AgentsSystem;

namespace Agro;

/// <summary>
/// Where the wall time of a step goes, by phase.
///
/// A step is the formations' tick, post delivery and census, then the collision pass, the light
/// model and the daily vegetation survey. The last three are callbacks, so four time stamps taken
/// between them split a step exactly; the formations keep their own counters for what happens
/// inside their share (<see cref="PlantFormation2.ShootTickTicks"/> and its neighbours,
/// <see cref="PlantSubFormation{T}.GravityTicks"/>, the soil's <c>TickTicks</c>). The totals are
/// always kept - four time stamps a step - and <c>AGRO_PLANT_TIMING=n</c> prints one line every
/// <c>n</c> steps (1 when the value is not a number). Nothing here is read by the simulation, so it
/// cannot change a result; it exists because a tree's untimed share was a fifth to a half of a run
/// and nothing said which phase it was.
/// </summary>
public sealed class PhaseTiming
{
	/// <summary>Cumulative stopwatch ticks: formations (tick, post, census), collisions, light, survey.</summary>
	public long FormationTicks, CollisionTicks, LightTicks, SurveyTicks;
	/// <summary>Steps the totals cover.</summary>
	public long Steps;

	readonly AgroWorld World;
	readonly int PrintEvery;
	long Mark;
	long[] Printed = new long[Columns.Length];
	long PrintedAt;

	static readonly string[] Columns = ["formations", "soil", "rootTick", "shootTick", "hormones", "gravity", "deliver",
		"census", "rootCensus", "shootCensus", "rebuild", "bvh", "collisions", "light", "survey"];

	internal PhaseTiming(AgroWorld world)
	{
		World = world;
		var setting = Environment.GetEnvironmentVariable("AGRO_PLANT_TIMING");
		PrintEvery = setting == null ? 0 : int.TryParse(setting, out var every) && every > 0 ? every : 1;
		Mark = PrintedAt = Stopwatch.GetTimestamp();
	}

	/// <summary>The host is about to run steps: what it did since the last one is not a step's time.</summary>
	internal void BeginRun() => Mark = Stopwatch.GetTimestamp();

	long Lap()
	{
		var now = Stopwatch.GetTimestamp();
		var elapsed = now - Mark;
		Mark = now;
		return elapsed;
	}

	internal void BeforeCollisions(SimulationWorld world, uint timestep, IList<IFormation> formations, IList<IObstacle> obstacles) => FormationTicks += Lap();
	internal void AfterCollisions(SimulationWorld world, uint timestep, IList<IFormation> formations, IList<IObstacle> obstacles) => CollisionTicks += Lap();
	internal void AfterLight(SimulationWorld world, uint timestep, IList<IFormation> formations, IList<IObstacle> obstacles) => LightTicks += Lap();

	internal void EndOfStep(SimulationWorld world, uint timestep, IList<IFormation> formations, IList<IObstacle> obstacles)
	{
		SurveyTicks += Lap();
		++Steps;
		if (PrintEvery > 0 && Steps % PrintEvery == 0) Print(timestep, formations);
	}

	void Print(uint timestep, IList<IFormation> formations)
	{
		var now = new long[Columns.Length];
		long organs = 0, births = 0, deaths = 0;
		now[0] = FormationTicks; now[12] = CollisionTicks; now[13] = LightTicks; now[14] = SurveyTicks;
		if (World.Soil is SoilFormationRegularVoxels soil) now[1] = soil.TickTicks;
		for (int i = 0; i < formations.Count; ++i)
			if (formations[i] is PlantFormation2 plant)
			{
				organs += plant.AG.Count;
				births += plant.AG.CensusBirths; deaths += plant.AG.CensusDeaths;
				now[2] += plant.RootTickTicks; now[3] += plant.ShootTickTicks; now[4] += plant.HormoneTicks;
				now[5] += plant.AG.GravityTicks; now[6] += plant.DeliverPostTicks; now[7] += plant.CensusTicks;
				now[8] += plant.RootCensusTicks; now[9] += plant.ShootCensusTicks;
				now[10] += plant.AG.CensusRebuildTicks; now[11] += plant.AG.BuildBvhTicks;
			}

		// Per step, in milliseconds. The plant counters are summed over plants that tick in
		// parallel, so with several plants they are CPU time and can exceed the formations' wall time.
		var scale = 1000d / Stopwatch.Frequency / PrintEvery;
		var stamp = Stopwatch.GetTimestamp();
		var line = new System.Text.StringBuilder(256);
		line.Append("PLANT_TIMING step=").Append(timestep).Append(" organs=").Append(organs)
			.Append(" births=").Append(births).Append(" deaths=").Append(deaths)
			.Append(System.Globalization.CultureInfo.InvariantCulture, $" wallMs={(stamp - PrintedAt) * scale:F1}");
		for (int c = 0; c < Columns.Length; ++c)
			line.Append(System.Globalization.CultureInfo.InvariantCulture, $" {Columns[c]}={(now[c] - Printed[c]) * scale:F1}");
		Console.WriteLine(line.ToString());
		Printed = now;
		PrintedAt = stamp;
	}
}
