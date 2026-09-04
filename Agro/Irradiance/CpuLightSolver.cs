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

	/// <summary>Hemisphere rays per sensor and step. Split between the two faces of a leaf.</summary>
	public int DiffuseSamples { get; }
	readonly string? ObstacleFilter;
	readonly int ObstacleSensorBudget;
	readonly float CanopyDepth;

	SkyDome[] SunDomes = [];
	int SunSubSteps;

	bool StaticReady;
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

	/// <summary>Number of primitives in the traced scene after the last solve.</summary>
	public int TracedPrimitives { get; private set; }

	public CpuLightSolver(int diffuseSamples, string? obstacleFilter, int obstacleSensorBudget, float canopyDepth)
	{
		//An even count keeps the two faces of a leaf equally sampled.
		DiffuseSamples = Math.Max(2, diffuseSamples + (diffuseSamples & 1));
		ObstacleFilter = string.IsNullOrWhiteSpace(obstacleFilter) ? null : obstacleFilter.Trim();
		ObstacleSensorBudget = Math.Max(64, obstacleSensorBudget);
		CanopyDepth = MathF.Max(0.05f, canopyDepth);
	}

	/// <summary>
	/// Computes this step's irradiance for every leaf and petal, filling
	/// <paramref name="irradiances"/> with Wh/m² accumulated over the step and
	/// <paramref name="offsets"/> with the per-organ index into it (-1 where an organ is no sensor).
	/// </summary>
	/// <returns>False when there is no daylight worth tracing, so the caller can treat the step as
	/// night.</returns>
	public bool Solve(AgroWorld world, uint timestep, IList<IFormation> formations, IList<IObstacle> obstacles,
					  Dictionary<IFormation, int[]> offsets, List<float> irradiances)
	{
		offsets.Clear();
		irradiances.Clear();

		var diffuseDome = BuildDomes(world, timestep);
		if (!diffuseDome.IsLit)
		{
			Meter?.SkipStep(timestep);
			return false;
		}

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
			Sensors = new LeafSensor[Math.Max(SensorTotal, 1024)];
			SensorValues = new float[Sensors.Length];
		}

		Scene.AllocatePlants(PrimCounts, slots);

		Parallel.For(0, slots, i =>
		{
			OffsetArrays[i] = null;
			if (PrimCounts[i] == 0)
				return;
			var ag = ((PlantFormation2)formations[i]).AG;
			var organs = RentOffsets(i, ag.Count);
			FillPlant(ag, FirstPlantGroup + i, Scene.PlantPrimitives(i), organs, SensorFirst[i]);
			OffsetArrays[i] = organs;
		});

		for (int i = 0; i < slots; ++i)
			if (OffsetArrays[i] is int[] organs)
				offsets[((PlantFormation2)formations[i]).AG] = organs;

		Scene.BuildPlants();
		TracedPrimitives = Scene.PrimitiveCount;

		//A plant that has never been measured still has to reflect something, or the first lit step
		//would come out unnaturally dark. A canopy receives roughly 40% of the open sky on average.
		var seededRadiosity = FoliageAlbedo * 0.4f * diffuseDome.GlobalHorizontal / MathF.PI;
		for (int i = 0; i < slots; ++i)
			if (!PlantRadiosityKnown[i])
				PlantRadiosity[i] = seededRadiosity;

		var model = new SurfaceRadianceModel(StaticAlbedo, StaticShading, PlantRadiosity, FirstPlantGroup);
		var hoursPerTick = (float)world.HoursPerTick;

		if (SensorTotal > 0)
		{
			var suns = SunDomes;
			var sunCount = SunSubSteps;
			var samples = DiffuseSamples;
			Parallel.ForEach(Partitioner.Create(0, SensorTotal), range =>
			{
				for (int i = range.Item1; i < range.Item2; ++i)
					SensorValues[i] = GatherSensor(i, timestep, in diffuseDome, suns, sunCount, in model, samples) * hoursPerTick;
			});

			irradiances.EnsureCapacity(SensorTotal);
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

		Meter?.Measure(Scene, in diffuseDome, SunDomes, SunSubSteps, in model, DiffuseSamples, hoursPerTick, timestep, StaticShading);
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
				   int[] organOffsets, int sensorFirst)
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
		if (StaticReady)
			return;
		StaticReady = true;

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

		world.Soil?.AppendSurfaceTriangles(vertices, indices);
		AppendTriangles(prims, vertices, indices, 0, RayScene.FlagOneSided);

		var ranges = new (int First, int Count)[obstacles.Count];
		for (int o = 0; o < obstacles.Count; ++o)
		{
			vertices.Clear();
			indices.Clear();
			obstacles[o].AppendTriangles(vertices, indices);
			var first = prims.Count;
			AppendTriangles(prims, vertices, indices, 1 + o);
			ranges[o] = (first, prims.Count - first);
		}

		var array = prims.ToArray();
		if (ObstacleFilter != null && obstacles.Count > 0)
		{
			var meter = new ObstacleLightMeter(obstacles, array, ranges, ObstacleFilter, ObstacleSensorBudget, CanopyDepth);
			//A filter that matched nothing stays null, so callers can tell "no targets" from "no request".
			Meter = meter.Targets.Count > 0 ? meter : null;
		}

		//The hierarchy reorders its own copy, so the meter had to be built from the list above.
		Scene.SetStatic(array, array.Length);
	}

	static void AppendTriangles(List<RayPrimitive> prims, List<Vector3> vertices, List<int> indices, int group, byte flags = 0)
	{
		for (int i = 0; i + 2 < indices.Count; i += 3)
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
		var capacity = Math.Max(slots, 64);
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
		OffsetPool = new int[]?[capacity];
	}

	int[]?[] OffsetPool = [];

	/// <summary>Reuses the per-organ offset array of a plant whenever its organ count is unchanged.
	/// Reallocating one per plant per step is what the previous ambient fallback did, and on a scene
	/// with thousands of plants it is pure garbage.</summary>
	int[] RentOffsets(int slot, int count)
	{
		var cached = OffsetPool[slot];
		if (cached != null && cached.Length == count)
			return cached;
		var fresh = new int[count];
		OffsetPool[slot] = fresh;
		return fresh;
	}
}
