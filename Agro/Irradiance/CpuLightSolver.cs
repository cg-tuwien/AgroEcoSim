using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using AgentsSystem;

namespace Agro;

/// <summary>
/// The internal, CPU-only light model: per-leaf irradiance under a real sun and sky, with shadows
/// and inter-reflection, plus the obstacle measurements of <see cref="ObstacleLightMeter"/>.
/// </summary>
/// <remarks>
/// <para>
/// It replaces the Mitsuba round trip when no renderer answers, and it is what makes light-guided
/// growth usable again on scenes whose terrain and obstacles are arbitrary triangle meshes rather
/// than the boxes the primitive export was written for.
/// </para>
/// <para>
/// The budget per leaf and step is one shadow ray per sun sub-step plus
/// <see cref="DiffuseSamples"/> hemisphere rays, so a hundred thousand leaves cost a couple of
/// million rays - two orders of magnitude less than a path tracer with a converged estimate per
/// sensor, and affordable every simulation step. What buys that is the split of the integral:
/// </para>
/// <list type="bullet">
/// <item>the sun is a delta direction, so its visibility needs one any-hit ray per sub-step and
/// stays crisp;</item>
/// <item>the sky is sampled cosine-weighted, which carries the cosine factor of the irradiance
/// integral exactly in the density, so a gather is a plain mean;</item>
/// <item>indirect light is not traced recursively. A ray that lands on a surface reads that
/// surface's exitant radiance from <see cref="SurfaceRadianceModel"/>, which is built from the
/// previous step. Since a simulation runs hundreds of steps over a slowly changing scene, the
/// effective path length grows on its own.</item>
/// </list>
/// <para>
/// Leaves are two-sided: the hemispheres on both faces are gathered and summed, which is what an
/// irradiance meter on a two-sided lamina measures and what the photosynthesis code, multiplying by
/// the one-sided leaf area, expects.
/// </para>
/// </remarks>
public sealed class CpuLightSolver
{
	/// <summary>Broadband shortwave reflectance of a leaf: near 0.1 in the visible band and near
	/// 0.4 in the near infrared, so about 0.2 over the shortwave band the simulation accounts in.</summary>
	public const float FoliageAlbedo = 0.2f;
	/// <summary>Broadband shortwave reflectance of a rendered or painted facade. Fresh white paint
	/// reaches 0.6, weathered and mixed surfaces sit far lower.</summary>
	public const float ObstacleAlbedo = 0.35f;

	const float Epsilon = 1e-4f;
	const float FarDistance = 1e5f;
	/// <summary>Smallest organ extent that still produces a well conditioned primitive, in metres.
	/// Newborn organs are otherwise degenerate and their surface normal is undefined.</summary>
	const float MinExtent = 1e-4f;

	enum PrimitiveClass : byte { None, Lamina, Cylinder, Sphere }

	readonly struct LeafSensor
	{
		public readonly Vector3 P;
		public readonly Vector3 U;
		public readonly Vector3 V;
		public LeafSensor(Vector3 p, Vector3 u, Vector3 v)
		{
			P = p;
			U = u;
			V = v;
		}
	}

	readonly RayScene Scene = new();
	uint GuidanceSceneTimestep = uint.MaxValue;

	// Previous-step foliage radiosity is temporal state, not reconstructible geometry.
	// Without it a resumed CPU run agrees for one frame, then diverges at its first bounce.
	internal void WriteState(BinaryWriter writer)
	{
		writer.Write(PlantRadiosity.Length);
		for(int i=0;i<PlantRadiosity.Length;++i)
		{ writer.Write(PlantRadiosity[i]); writer.Write(PlantRadiosityKnown[i]); }
	}

	internal void ReadState(BinaryReader reader)
	{
		var count=reader.ReadInt32();
		EnsureSlots(count);
		for(int i=0;i<count;++i)
		{ PlantRadiosity[i]=reader.ReadSingle(); PlantRadiosityKnown[i]=reader.ReadBoolean(); }
		GuidanceSceneTimestep=uint.MaxValue;
	}

	public void UpdateGrowthGuidance(AgroWorld world, uint timestep, IList<IFormation> formations, IList<IObstacle> obstacles)
	{
		if (!world.GrowthLightGuidanceEnabled || !formations.OfType<PlantFormation2>().Any(p => p.AG.NeedsGrowthGuidance())) return;
		if (GuidanceSceneTimestep != timestep)
			Run(world, timestep, formations, obstacles, null, null, gatherLeaves: false, guidanceOnly: true);
		else
			foreach (var plant in formations.OfType<PlantFormation2>()) plant.AG.RefreshGrowthGuidance(Scene);
	}

	/// <summary>Hemisphere rays per sensor and step. Split between the two faces of a leaf.</summary>
	public int DiffuseSamples { get; }
	readonly string? ObstacleRegex;
	readonly int ObstacleSensorBudget;
	readonly float CanopyDepth;
	readonly float BandHeight;

	SkyDome[] SunDomes = [];
	int SunSubSteps;

	bool StaticReady;
	IObstacle[] GuidanceStaticObstacles = [];
	int FirstPlantGroup;
	float[] StaticAlbedo = [];
	float[] StaticShading = [];

	float[] PlantRadiosity = [];
	bool[] PlantRadiosityKnown = [];

	int[] PrimCounts = [];
	int[] SensorCounts = [];
	int[] SensorFirst = [];
	int[]?[] OffsetArrays = [];
	LeafSensor[] Sensors = [];
	float[] SensorValues = [];
	int SensorTotal;

	public ObstacleLightMeter? Meter { get; private set; }
	/// <summary>Obstacles the scene offered the filter, and how many it matched. Reported even when
	/// the answer is zero, so a mistyped filter is distinguishable from a measurement that never
	/// ran.</summary>
	public int ObstacleCandidates { get; private set; }
	public int ObstacleMatches { get; private set; }
	/// <summary>The requested filter, or null when none was.</summary>
	public string? ObstacleFilterText => ObstacleRegex;

	/// <summary>Number of primitives in the traced scene after the last solve.</summary>
	public int TracedPrimitives { get; private set; }

	public CpuLightSolver(int diffuseSamples, string? obstacleRegexFilter, int obstacleSensorBudget, float canopyDepth, float bandHeight)
	{
		//An even count keeps the two faces of a leaf equally sampled.
		DiffuseSamples = Math.Max(2, diffuseSamples + (diffuseSamples & 1));
		ObstacleRegex = string.IsNullOrWhiteSpace(obstacleRegexFilter) ? null : obstacleRegexFilter.Trim();
		ObstacleSensorBudget = Math.Max(64, obstacleSensorBudget);
		CanopyDepth = MathF.Max(0.05f, canopyDepth);
		BandHeight = MathF.Max(0.05f, bandHeight);
	}

	/// <summary>True when a leaf area index measurement was requested, whether or not this solver
	/// is also the one lighting the plants.</summary>
	public bool MeasuresObstacles => ObstacleRegex != null;

	/// <summary>
	/// Computes this step's irradiance for every leaf and petal, filling
	/// <paramref name="irradiances"/> with Wh/m² accumulated over the step and
	/// <paramref name="offsets"/> with the per-organ index into it (-1 where an organ is no sensor).
	/// Also measures the selected obstacles.
	/// </summary>
	/// <returns>False when there is no daylight worth tracing, so the caller can treat the step as
	/// night.</returns>
	public bool Solve(AgroWorld world, uint timestep, IList<IFormation> formations, IList<IObstacle> obstacles,
					  Dictionary<IFormation, int[]> offsets, List<float> irradiances)
		=> Run(world, timestep, formations, obstacles, offsets, irradiances, gatherLeaves: true);

	/// <summary>
	/// Measures the selected obstacles only, leaving the per-leaf irradiance to whoever else is
	/// computing it.
	/// </summary>
	/// <remarks>
	/// This exists so that a leaf area index study does not force a choice of plant light model.
	/// Mitsuba and Tamashii light the plants but measure nothing on the facade, and the facade
	/// measurement needs the same traced scene either way - so when a filter is set it runs
	/// regardless, and the only thing it gives up is that the canopy's own reflected contribution
	/// falls back to the analytic seed rather than the measured per-leaf irradiance.
	/// </remarks>
	public void MeasureObstacles(AgroWorld world, uint timestep, IList<IFormation> formations, IList<IObstacle> obstacles)
	{
		if (MeasuresObstacles)
			Run(world, timestep, formations, obstacles, null, null, gatherLeaves: false);
	}

	bool Run(AgroWorld world, uint timestep, IList<IFormation> formations, IList<IObstacle> obstacles,
			 Dictionary<IFormation, int[]>? offsets, List<float>? irradiances, bool gatherLeaves, bool guidanceOnly = false)
	{
		offsets?.Clear();
		irradiances?.Clear();

		var diffuseDome = BuildDomes(world, timestep);
		//Nothing to measure in the dark: the obstacle meter accumulates energies, so a step that
		//carries none simply does not belong to any day.
		if (!diffuseDome.IsLit && !guidanceOnly)
			return false;

		EnsureStatic(world, obstacles);

		var slots = formations.Count;
		EnsureSlots(slots);

		Parallel.For(0, slots, i =>
		{
			PrimCounts[i] = 0;
			SensorCounts[i] = 0;
			if (formations[i] is not PlantFormation2 plant || !plant.AG.Alive)
				return;
			var ag = plant.AG;
			var count = ag.Count;
			int prims = 0, sensors = 0;
			for (int k = 0; k < count; ++k)
				switch (Classify(ag.GetOrgan(k)))
				{
					case PrimitiveClass.Lamina: ++prims; ++sensors; break;
					case PrimitiveClass.Cylinder: case PrimitiveClass.Sphere: ++prims; break;
				}
			PrimCounts[i] = prims;
			SensorCounts[i] = sensors;
		});

		SensorTotal = 0;
		for (int i = 0; i < slots; ++i)
		{
			SensorFirst[i] = SensorTotal;
			SensorTotal += SensorCounts[i];
		}

		if (Sensors.Length < SensorTotal)
		{
			Sensors = new LeafSensor[Math.Max(SensorTotal, Math.Max(1024, Sensors.Length + Sensors.Length / 2))];
			SensorValues = new float[Sensors.Length];
		}

		Scene.AllocatePlants(PrimCounts, slots);

		Parallel.For(0, slots, i =>
		{
			OffsetArrays[i] = null;
			if (PrimCounts[i] == 0)
				return;
			var ag = ((PlantFormation2)formations[i]).AG;
			var organs = GetOffsetBuffer(i, ag.Count);
			organs.AsSpan(ag.Count).Fill(-1);
			FillPlant(ag, FirstPlantGroup + i, Scene.PlantPrimitives(i), organs.AsSpan(0, ag.Count), SensorFirst[i]);
			OffsetArrays[i] = organs;
		});

		if (offsets != null)
			for (int i = 0; i < slots; ++i)
				if (OffsetArrays[i] is int[] organs)
					offsets[((PlantFormation2)formations[i]).AG] = organs;

		Scene.BuildPlants();
		TracedPrimitives = Scene.PrimitiveCount;
		GuidanceSceneTimestep = timestep;
		if (guidanceOnly)
		{
			foreach (var plant in formations.OfType<PlantFormation2>()) plant.AG.RefreshGrowthGuidance(Scene);
			return true;
		}

		//A plant that has never been measured still has to reflect something, or the first lit step
		//would come out unnaturally dark. A canopy receives roughly 40% of the open sky on average.
		var seededRadiosity = FoliageAlbedo * 0.4f * diffuseDome.GlobalHorizontal / MathF.PI;
		for (int i = 0; i < slots; ++i)
			if (!PlantRadiosityKnown[i])
				PlantRadiosity[i] = seededRadiosity;

		var model = new SurfaceRadianceModel(StaticAlbedo, StaticShading, PlantRadiosity, FirstPlantGroup);
		var hoursPerTick = (float)world.HoursPerTick;

		if (SensorTotal > 0 && gatherLeaves)
		{
			var suns = SunDomes;
			var sunCount = SunSubSteps;
			var samples = DiffuseSamples;
			Parallel.ForEach(Partitioner.Create(0, SensorTotal), range =>
			{
				for (int i = range.Item1; i < range.Item2; ++i)
					SensorValues[i] = GatherSensor(i, timestep, in diffuseDome, suns, sunCount, in model, samples) * hoursPerTick;
			});

			irradiances!.EnsureCapacity(SensorTotal);
			for (int i = 0; i < SensorTotal; ++i)
				irradiances.Add(SensorValues[i]);

			Parallel.For(0, slots, i =>
			{
				var count = SensorCounts[i];
				if (count == 0)
					return;
				var first = SensorFirst[i];
				double sum = 0.0;
				for (int k = 0; k < count; ++k)
					sum += SensorValues[first + k];
				PlantRadiosity[i] = FoliageAlbedo * (float)(sum / count) / (hoursPerTick * MathF.PI);
				PlantRadiosityKnown[i] = true;
			});
		}

		if (Meter != null)
		{
			//Days are closed on the local calendar date at the site, so the meter needs local time.
			var localStart = TimeZoneInfo.ConvertTimeFromUtc(world.GetTime(timestep), world.TimeZone);
			Meter.Measure(Scene, in diffuseDome, SunDomes, SunSubSteps, in model, DiffuseSamples,
				hoursPerTick, localStart, StaticShading);
		}
		return true;
	}

	float GatherSensor(int sensorIndex, uint timestep, in SkyDome diffuse, SkyDome[] suns, int sunCount,
					   in SurfaceRadianceModel model, int samples)
	{
		var sensor = Sensors[sensorIndex];
		var normal = Vector3.Normalize(Vector3.Cross(sensor.U, sensor.V));
		var key = RaySampling.Hash((uint)sensorIndex, timestep, 0x2545f491u);

		//Direct beam. The sun is sub-sampled inside the step because it sweeps 15 degrees per hour,
		//so a single sample would keep a leaf fully lit or fully shaded for the whole step.
		var beam = 0f;
		for (int k = 0; k < sunCount; ++k)
		{
			ref readonly var sun = ref suns[k];
			if (sun.DirectNormal <= 0f)
				continue;
			var cosine = Vector3.Dot(normal, sun.SunDirection);
			var lit = MathF.Abs(cosine);
			if (lit < 1e-3f)
				continue;
			RaySampling.Sample2D(k, sunCount, key ^ 0x51ed2701u, out var a, out var b);
			var face = cosine >= 0f ? normal : -normal;
			var origin = sensor.P + sensor.U * a + sensor.V * (b - 0.5f) + face * Epsilon;
			if (!Scene.AnyHit(origin, sun.SunDirection, Epsilon, FarDistance, RayScene.MaskAll))
				beam += sun.DirectNormal * lit;
		}
		if (sunCount > 0)
			beam /= sunCount;

		//Diffuse sky and one bounce off everything it lights, over both faces of the lamina.
		var half = samples >> 1;
		var diffuseTotal = 0f;
		for (int face = 0; face < 2; ++face)
		{
			var faceNormal = face == 0 ? normal : -normal;
			RaySampling.OrthonormalBasis(faceNormal, out var tangent, out var bitangent);
			var faceKey = key ^ (face == 0 ? 0x9e3779b1u : 0x85ebca77u);
			var sum = 0f;
			for (int j = 0; j < half; ++j)
			{
				RaySampling.Sample2D(j, half, faceKey, out var u1, out var u2);
				var direction = RaySampling.CosineHemisphere(faceNormal, tangent, bitangent, u1, u2);
				RaySampling.Sample2D(j, half, faceKey ^ 0x27d4eb2du, out var a, out var b);
				var origin = sensor.P + sensor.U * a + sensor.V * (b - 0.5f) + faceNormal * Epsilon;
				sum += Scene.ClosestHit(origin, direction, Epsilon, FarDistance, RayScene.MaskAll, out var hit)
					? model.Radiance(in hit, in diffuse)
					: diffuse.Radiance(direction);
			}
			diffuseTotal += MathF.PI * sum / half;
		}

		return beam + diffuseTotal;
	}

	static PrimitiveClass Classify(OrganTypes organ) => organ switch
	{
		OrganTypes.Leaf or OrganTypes.FlowerPadel => PrimitiveClass.Lamina,
		OrganTypes.Stem or OrganTypes.Petiole or OrganTypes.Meristem or OrganTypes.RizomeMeristem
			or OrganTypes.FlowerStem or OrganTypes.FlowerMeristem or OrganTypes.FlowerPedicel => PrimitiveClass.Cylinder,
		OrganTypes.Bud or OrganTypes.FlowerBud or OrganTypes.FlowerBaseBud or OrganTypes.Fruit
			or OrganTypes.Seed => PrimitiveClass.Sphere,
		_ => PrimitiveClass.None,
	};

	void FillPlant(PlantSubFormation<AboveGroundAgent> ag, int group, Span<RayPrimitive> destination,
				   Span<int> organOffsets, int sensorFirst)
	{
		var count = ag.Count;
		int prim = 0, sensor = 0;
		for (int i = 0; i < count; ++i)
		{
			organOffsets[i] = -1;
			var organ = ag.GetOrgan(i);
			var kind = Classify(organ);
			if (kind == PrimitiveClass.None)
				continue;

			//The same geometry the primitive export hands to Mitsuba: an organ grows from its base
			//centre along the local X axis of its orientation.
			var center = ag.GetBaseCenterWorld(i) + ag.GetBaseOffset(i);
			var scale = ag.GetScale(i);
			var orientation = ag.GetDirection(i);
			var axis = Vector3.Transform(Vector3.UnitX, orientation);
			var length = MathF.Max(scale.X, MinExtent);

			switch (kind)
			{
				case PrimitiveClass.Lamina:
				{
					var across = Vector3.Transform(Vector3.UnitZ, orientation);
					var u = axis * length;
					var v = across * -MathF.Max(scale.Z, MinExtent);
					destination[prim++] = new RayPrimitive
					{
						P = center,
						U = u,
						V = v,
						Group = group,
						Kind = RayPrimitiveKind.Quad,
						//Petals are laminae too, but a leaf area index counts leaves.
						Flags = (byte)(RayScene.FlagPlant | (organ == OrganTypes.Leaf ? RayScene.FlagFoliage : 0)),
					};
					var index = sensorFirst + sensor++;
					organOffsets[i] = index;
					Sensors[index] = new LeafSensor(center, u, v);
					break;
				}
				case PrimitiveClass.Cylinder:
					destination[prim++] = new RayPrimitive
					{
						P = center,
						U = axis * length,
						R = MathF.Max(scale.Z * 0.5f, MinExtent),
						Group = group,
						Kind = RayPrimitiveKind.Cylinder,
						Flags = RayScene.FlagPlant,
					};
					break;
				default:
					destination[prim++] = new RayPrimitive
					{
						P = center,
						R = length,
						Group = group,
						Kind = RayPrimitiveKind.Sphere,
						Flags = RayScene.FlagPlant,
					};
					break;
			}
		}
	}

	SkyDome BuildDomes(AgroWorld world, uint timestep)
	{
		//Six sub-steps cover a whole day tick at four hour resolution and cost one shadow ray each.
		var subSteps = Math.Clamp((int)world.HoursPerTick, 1, 6);
		if (SunDomes.Length < subSteps)
			SunDomes = new SkyDome[subSteps];
		SunSubSteps = subSteps;

		var coverage = 1f - world.GetAmbientLight(timestep);
		var start = world.GetTime(timestep);
		var span = world.HoursPerTick / (double)subSteps;

		var brightest = 0;
		var meanGlobal = 0.0;
		for (int s = 0; s < subSteps; ++s)
		{
			SunDomes[s] = SkyDome.Sample(start.AddHours((s + 0.5) * span), AgroWorld.Latitude, AgroWorld.Longitude, coverage);
			meanGlobal += SunDomes[s].GlobalHorizontal;
			if (SunDomes[s].GlobalHorizontal > SunDomes[brightest].GlobalHorizontal)
				brightest = s;
		}
		meanGlobal /= subSteps;

		var reference = SunDomes[brightest];
		return reference.IsLit ? reference.Scaled((float)(meanGlobal / reference.GlobalHorizontal)) : reference;
	}

	void EnsureStatic(AgroWorld world, IList<IObstacle> obstacles)
	{
		var unchanged = GuidanceStaticObstacles.Length == obstacles.Count;
		for (int i = 0; unchanged && i < obstacles.Count; ++i)
			unchanged = ReferenceEquals(GuidanceStaticObstacles[i], obstacles[i]);
		if (StaticReady && unchanged)
			return;
		StaticReady = true;
		GuidanceStaticObstacles = obstacles.ToArray();

		FirstPlantGroup = 1 + obstacles.Count;
		StaticAlbedo = new float[1 + obstacles.Count];
		StaticShading = new float[1 + obstacles.Count];
		StaticAlbedo[0] = SkyDome.GroundAlbedo;
		StaticShading[0] = 1f;
		for (int o = 0; o < obstacles.Count; ++o)
		{
			StaticAlbedo[1 + o] = ObstacleAlbedo;
			StaticShading[1 + o] = 1f;
		}

		var prims = new List<RayPrimitive>();
		var vertices = new List<Vector3>();
		var indices = new List<int>();
		var groups = new List<Collisions.StaticSceneSource.Group>();
		Collisions.StaticSceneSource.Collect(world.Soil,
			obstacles as IReadOnlyList<IObstacle> ?? obstacles.ToArray(), vertices, indices, groups);
		var ranges = new (int First, int Count)[obstacles.Count];
		var obstacleIndex = 0;
		foreach (var group in groups)
		{
			var first = prims.Count;
			var soil = group.Surface == Collisions.StaticSurface.Soil;
			AppendTriangles(prims, vertices, indices, soil ? 0 : 1 + obstacleIndex,
				soil ? RayScene.FlagOneSided : (byte)0, group.FirstTriangle, group.TriangleCount);
			if (!soil) ranges[obstacleIndex++] = (first, prims.Count - first);
		}

		var array = prims.ToArray();
		if (ObstacleRegex != null)
		{
			ObstacleCandidates = obstacles.Count;
			var meter = obstacles.Count > 0 ? new ObstacleLightMeter(obstacles, array, ranges, ObstacleRegex, ObstacleSensorBudget, CanopyDepth, BandHeight) : null;
			//A filter that matched nothing stays null, so callers can tell "no targets" from "no request".
			Meter = meter?.Targets.Count > 0 ? meter : null;
			ObstacleMatches = Meter?.Targets.Count ?? 0;

			//Say so out loud. A filter that quietly matches nothing is indistinguishable from a
			//measurement that never ran, and that ambiguity has already cost a debugging session.
			if (Meter == null)
			{
				var sample = string.Join(", ", obstacles.Take(6).Select(o => o.Name));
				Console.WriteLine($"WARNING: LeafAreaIndexObstacles \"{ObstacleRegex}\" matched none of the "
					+ $"{obstacles.Count} obstacles, so nothing will be measured. Names look like: {sample}");
			}
			else
				Console.WriteLine($"Leaf area index: \"{ObstacleRegex}\" matched {Meter.Targets.Count} of "
					+ $"{obstacles.Count} obstacles, {Meter.SensorCount} measurement points.");
		}

		//The hierarchy reorders its own copy, so the meter had to be built from the list above.
		Scene.SetStatic(array, array.Length);
	}

	static void AppendTriangles(List<RayPrimitive> prims, List<Vector3> vertices, List<int> indices,
		int group, byte flags, int firstTriangle, int triangleCount)
	{
		for (int i = firstTriangle * 3; i < (firstTriangle + triangleCount) * 3; i += 3)
		{
			var a = vertices[indices[i]];
			var u = vertices[indices[i + 1]] - a;
			var v = vertices[indices[i + 2]] - a;
			if (Vector3.Cross(u, v).LengthSquared() < 1e-18f)
				continue; //degenerate triangle: no surface to shade with and bad for the hierarchy
			prims.Add(new RayPrimitive { P = a, U = u, V = v, Group = group, Kind = RayPrimitiveKind.Triangle, Flags = flags });
		}
	}

	void EnsureSlots(int slots)
	{
		if (PrimCounts.Length >= slots)
			return;
		var capacity = Math.Max(slots, Math.Max(64, PrimCounts.Length + PrimCounts.Length / 2));
		PrimCounts = new int[capacity];
		SensorCounts = new int[capacity];
		SensorFirst = new int[capacity];
		var offsets = new int[]?[capacity];
		Array.Copy(OffsetArrays, offsets, OffsetArrays.Length);
		OffsetArrays = offsets;
		var radiosity = new float[capacity];
		var known = new bool[capacity];
		Array.Copy(PlantRadiosity, radiosity, PlantRadiosity.Length);
		Array.Copy(PlantRadiosityKnown, known, PlantRadiosityKnown.Length);
		PlantRadiosity = radiosity;
		PlantRadiosityKnown = known;
		Array.Resize(ref OffsetPool, capacity);
	}

	int[]?[] OffsetPool = [];

	/// <summary>Keep capacity as plants grow and shrink. These arrays remain referenced by the
	/// irradiance lookup between ticks, so they are owned here rather than rented per call.</summary>
	int[] GetOffsetBuffer(int slot, int count)
	{
		var cached = OffsetPool[slot];
		if (cached != null && cached.Length >= count)
			return cached;
		var capacity = Math.Max(count, Math.Max(8, (cached?.Length ?? 0) * 3 / 2));
		var fresh = new int[capacity];
		OffsetPool[slot] = fresh;
		return fresh;
	}
}
