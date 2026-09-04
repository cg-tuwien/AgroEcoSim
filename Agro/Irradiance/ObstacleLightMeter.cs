using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using AgentsSystem;

namespace Agro;

/// <summary>
/// One timestep of light and foliage measurements on one obstacle.
/// </summary>
/// <remarks>
/// "Leaf area index" has no single definition, and the readings below differ by factors of two on
/// the same canopy, so four of them are reported side by side rather than one number that could be
/// misread. All four count only foliage within <see cref="ObstacleLightMeter.CanopyDepth_m"/> of the
/// surface except the two gap fractions, which count whatever actually blocks the light.
/// <list type="bullet">
/// <item><see cref="LeafAreaIndex"/> is the classic definition transferred from ground to facade:
/// total one-sided leaf area per unit of facade area, the quantity the green facade literature
/// reports (roughly 2 to 8 for a developed living wall). It is measured, not inverted from a
/// transmittance.</item>
/// <item><see cref="ProjectedLeafAreaIndex"/> is the leaf area <em>projected</em> onto the facade
/// per unit of facade area - the "crown shadow over unshaded area" reading, but resolved per leaf
/// rather than per crown, so it does not saturate at one. It equals
/// <see cref="LeafAreaIndex"/> for leaves lying parallel to the wall and about half of it for
/// randomly oriented ones.</item>
/// <item><see cref="GapFractionBeam"/> and <see cref="GapFractionDiffuse"/> are canopy cover: the
/// fraction of the direct beam, respectively of the cosine-weighted sky hemisphere, that reaches the
/// surface without meeting foliage. These, not any leaf area index, are what saturate at full
/// cover.</item>
/// <item><see cref="LeafAreaIndexFromGaps"/> inverts the diffuse gap fraction through
/// Beer-Lambert, <c>-ln(P) / 0.93</c>, the standard hemispherical-photograph estimate for a
/// spherical leaf angle distribution (extinction 0.5 / cos 57.5 deg). It is what a canopy analyser
/// in front of the wall would report, and it falls below <see cref="LeafAreaIndex"/> wherever
/// leaves clump, which is the usual definition of the clumping index.</item>
/// </list>
/// The cooling question is answered by the irradiance pair rather than by any leaf area index:
/// <c>(IrradianceOpenSky_Wh_m2 - Irradiance_Wh_m2) * ExposedArea_m2</c> is the radiant energy the
/// vegetation kept off the wall during this step. It can go slightly negative in low sun, when a
/// bright canopy reflects more onto the wall than the sky it hides.
/// </remarks>
public readonly record struct ObstacleLightSample(
	float LeafAreaIndex,
	float ProjectedLeafAreaIndex,
	float GapFractionBeam,
	float GapFractionDiffuse,
	float LeafAreaIndexFromGaps,
	float Irradiance_Wh_m2,
	float IrradianceOpenSky_Wh_m2);

/// <summary>
/// Measures foliage cover and solar load on selected obstacles, one value set per obstacle per
/// simulation step.
/// </summary>
/// <remarks>
/// Sensors are placed once, area-stratified over the obstacle's triangles, with a budget shared out
/// in proportion to area. The cost therefore follows the total facade area rather than the triangle
/// count, which matters because an imported facade carries a few thousand triangles per wall while
/// only one number per wall is wanted.
/// </remarks>
public sealed class ObstacleLightMeter
{
	/// <summary>Sensors are dropped from the averages when less than this fraction of their
	/// hemisphere can see the sky past the building itself. That removes the inward faces of the
	/// solid that an imported wall object really is, which would otherwise halve every reading.</summary>
	const float MinSkyAccess = 0.05f;
	const float Epsilon = 1e-4f;
	const float FarDistance = 1e5f;
	/// <summary>Extinction coefficient of a spherical leaf angle distribution at the hemispherical
	/// mean view angle of 57.5 degrees: 0.5 / cos(57.5 deg).</summary>
	const float BeerLambertExtinction = 0.93f;

	const int MinSensorsPerTarget = 16;
	const int MaxSensorsPerTarget = 4096;

	public sealed class Target
	{
		public string Name { get; internal init; } = string.Empty;
		/// <summary>Group id of this obstacle in the traced scene.</summary>
		public int Group { get; internal init; }
		/// <summary>Total triangle area of the obstacle, both sides of a solid included.</summary>
		public float Area_m2 { get; internal init; }
		/// <summary>Share of <see cref="Area_m2"/> that can actually see the sky and therefore
		/// carries the reported values.</summary>
		public float ExposedArea_m2 { get; internal set; }
		public List<ObstacleLightSample> Samples { get; } = [];
		internal int SensorFirst;
		internal int SensorCount;
	}

	readonly struct Sensor
	{
		public readonly Vector3 Position;
		public readonly Vector3 Normal;
		public Sensor(Vector3 position, Vector3 normal)
		{
			Position = position;
			Normal = normal;
		}
	}

	/// <summary>How far in front of a surface foliage still counts towards its leaf area index.</summary>
	public float CanopyDepth_m { get; }

	readonly Target[] TargetArray;
	readonly Sensor[] Sensors;
	readonly List<uint> TimestepList = [];

	//Per sensor scratch, reused every step.
	readonly float[] SensorOpen;
	readonly float[] SensorVeg;
	readonly float[] SensorGapDiffuse;
	readonly float[] SensorSkyAccess;
	readonly float[] SensorBeamOpen;
	readonly float[] SensorBeamVisible;
	readonly int[] SensorCrossings;
	readonly float[] SensorLeafArea;

	bool Oriented;

	public IReadOnlyList<Target> Targets => TargetArray;
	/// <summary>Simulation steps for which a sample was recorded; night steps are skipped.</summary>
	public IReadOnlyList<uint> Timesteps => TimestepList;

	/// <summary>
	/// Selects the obstacles whose name contains <paramref name="filter"/> and places the sensors.
	/// </summary>
	/// <param name="prims">The static primitives before the hierarchy reordered them.</param>
	/// <param name="ranges">Primitive range of every obstacle inside <paramref name="prims"/>.</param>
	public ObstacleLightMeter(IList<IObstacle> obstacles, RayPrimitive[] prims, (int First, int Count)[] ranges,
							  string filter, int sensorBudget, float canopyDepth)
	{
		CanopyDepth_m = canopyDepth;

		var selected = new List<(IObstacle Obstacle, int Index, float Area)>();
		var totalArea = 0f;
		for (int o = 0; o < obstacles.Count; ++o)
		{
			if (obstacles[o].Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
				continue;
			var area = 0f;
			var (first, count) = ranges[o];
			for (int i = first; i < first + count; ++i)
				area += 0.5f * Vector3.Cross(prims[i].U, prims[i].V).Length();
			if (area <= 0f)
				continue;
			selected.Add((obstacles[o], o, area));
			totalArea += area;
		}

		TargetArray = new Target[selected.Count];
		var sensors = new List<Sensor>(Math.Min(sensorBudget * 2, 1 << 20));

		for (int t = 0; t < selected.Count; ++t)
		{
			var (obstacle, index, area) = selected[t];
			var budget = totalArea > 0f
				? Math.Clamp((int)MathF.Round(sensorBudget * area / totalArea), MinSensorsPerTarget, MaxSensorsPerTarget)
				: MinSensorsPerTarget;

			var target = new Target
			{
				Name = obstacle.Name,
				Group = 1 + index, //group 0 is the terrain
				Area_m2 = area,
				SensorFirst = sensors.Count,
				SensorCount = budget,
			};
			TargetArray[t] = target;

			//Area weighted prefix sum over the obstacle's triangles, so sensors land uniformly on
			//the surface no matter how unevenly it is tessellated.
			var (first, count) = ranges[index];
			var cumulative = new float[count];
			var running = 0f;
			for (int i = 0; i < count; ++i)
			{
				running += 0.5f * Vector3.Cross(prims[first + i].U, prims[first + i].V).Length();
				cumulative[i] = running;
			}

			for (int s = 0; s < budget; ++s)
			{
				var pick = (s + 0.5f) / budget * running;
				var tri = Array.BinarySearch(cumulative, pick);
				if (tri < 0)
					tri = Math.Min(count - 1, ~tri);
				ref var prim = ref prims[first + tri];
				RaySampling.Sample2D(s, budget, RaySampling.Hash((uint)index, (uint)s), out var u1, out var u2);
				var position = RaySampling.TrianglePoint(prim.P, prim.U, prim.V, u1, u2);
				var normal = Vector3.Normalize(Vector3.Cross(prim.U, prim.V));
				sensors.Add(new Sensor(position, normal));
			}
		}

		Sensors = [.. sensors];
		SensorOpen = new float[Sensors.Length];
		SensorVeg = new float[Sensors.Length];
		SensorGapDiffuse = new float[Sensors.Length];
		SensorSkyAccess = new float[Sensors.Length];
		SensorBeamOpen = new float[Sensors.Length];
		SensorBeamVisible = new float[Sensors.Length];
		SensorCrossings = new int[Sensors.Length];
		SensorLeafArea = new float[Sensors.Length];
	}

	public int SensorCount => Sensors.Length;

	/// <summary>
	/// Winding of an imported mesh decides which way a triangle normal points, and an object that
	/// is really a solid has faces pointing both ways. A short probe along the normal and against
	/// it settles which side is the outside, once, before the first measurement.
	/// </summary>
	void Orient(RayScene scene)
	{
		if (Oriented || Sensors.Length == 0)
			return;
		Oriented = true;
		const float probe = 0.5f;
		Parallel.ForEach(Partitioner.Create(0, Sensors.Length), range =>
		{
			for (int i = range.Item1; i < range.Item2; ++i)
			{
				var s = Sensors[i];
				var forward = scene.AnyHit(s.Position + s.Normal * Epsilon, s.Normal, Epsilon, probe, RayScene.MaskStatic);
				if (!forward)
					continue;
				var backward = scene.AnyHit(s.Position - s.Normal * Epsilon, -s.Normal, Epsilon, probe, RayScene.MaskStatic);
				if (!backward)
					Sensors[i] = new Sensor(s.Position, -s.Normal);
			}
		});
	}

	/// <summary>
	/// Measures every target for one simulation step and appends the results.
	/// <paramref name="staticShading"/> is updated in place with the fraction of the open-sky load
	/// that still reaches each obstacle, which feeds the indirect light of the next step.
	/// </summary>
	public void Measure(RayScene scene, in SkyDome diffuse, SkyDome[] suns, int sunCount,
						in SurfaceRadianceModel model, int diffuseSamples, float hoursPerTick,
						uint timestep, float[] staticShading)
	{
		if (Sensors.Length == 0)
			return;
		Orient(scene);

		var samples = Math.Max(1, diffuseSamples);
		var depth = CanopyDepth_m;
		var localModel = model;
		var localDiffuse = diffuse;

		Parallel.ForEach(Partitioner.Create(0, Sensors.Length), range =>
		{
			for (int i = range.Item1; i < range.Item2; ++i)
			{
				var sensor = Sensors[i];
				var n = sensor.Normal;
				var origin = sensor.Position + n * Epsilon;
				var key = RaySampling.Hash((uint)i, timestep, 0x9e3779b9u);

				//Direct beam, sub-sampled over the step because the sun sweeps a long arc in it.
				float beamOpen = 0f, beamVeg = 0f, beamOpenCount = 0f, beamVisibleCount = 0f;
				for (int k = 0; k < sunCount; ++k)
				{
					ref readonly var sun = ref suns[k];
					var cosIncidence = Vector3.Dot(n, sun.SunDirection);
					if (sun.DirectNormal <= 0f || cosIncidence <= 1e-3f)
						continue;
					var blockedByBuilding = scene.AnyHit(origin, sun.SunDirection, Epsilon, FarDistance, RayScene.MaskStatic);
					var blockedByPlants = !blockedByBuilding
						&& scene.AnyHit(origin, sun.SunDirection, Epsilon, FarDistance, RayScene.MaskPlants);
					if (blockedByBuilding)
						continue;
					var load = sun.DirectNormal * cosIncidence;
					beamOpen += load;
					beamOpenCount += 1f;
					if (!blockedByPlants)
					{
						beamVeg += load;
						beamVisibleCount += 1f;
					}
				}

				//Diffuse sky and one bounce of everything it lights, cosine sampled over the
				//outward hemisphere. Tracing the building and the plants separately yields the
				//with-vegetation and the open-sky load from the same set of directions.
				RaySampling.OrthonormalBasis(n, out var tangent, out var bitangent);
				float sumOpen = 0f, sumVeg = 0f, freeOfFoliage = 0f, seesSky = 0f;
				for (int j = 0; j < samples; ++j)
				{
					RaySampling.Sample2D(j, samples, key, out var u1, out var u2);
					var d = RaySampling.CosineHemisphere(n, tangent, bitangent, u1, u2);

					var hasStatic = scene.ClosestHit(origin, d, Epsilon, FarDistance, RayScene.MaskStatic, out var staticHit);
					var hasPlant = scene.ClosestHit(origin, d, Epsilon, FarDistance, RayScene.MaskPlants, out var plantHit);

					var radianceOpen = hasStatic ? localModel.Radiance(in staticHit, in localDiffuse) : localDiffuse.Radiance(d);
					sumOpen += radianceOpen;
					if (!hasStatic)
						seesSky += 1f;

					if (hasPlant && (!hasStatic || plantHit.T < staticHit.T))
						sumVeg += localModel.Radiance(in plantHit, in localDiffuse);
					else
					{
						sumVeg += radianceOpen;
						freeOfFoliage += 1f;
					}
				}

				var scale = MathF.PI / samples;
				SensorOpen[i] = (sunCount > 0 ? beamOpen / sunCount : 0f) + scale * sumOpen;
				SensorVeg[i] = (sunCount > 0 ? beamVeg / sunCount : 0f) + scale * sumVeg;
				SensorGapDiffuse[i] = freeOfFoliage / samples;
				SensorSkyAccess[i] = seesSky / samples;
				SensorBeamOpen[i] = beamOpenCount;
				SensorBeamVisible[i] = beamVisibleCount;
				SensorCrossings[i] = scene.CountFoliageCrossings(origin, n, Epsilon, depth, out var leafArea);
				SensorLeafArea[i] = leafArea;
			}
		});

		TimestepList.Add(timestep);
		Parallel.For(0, TargetArray.Length, t =>
		{
			var target = TargetArray[t];
			double crossings = 0, leafArea = 0, gap = 0, open = 0, veg = 0, beamOpen = 0, beamVisible = 0;
			var exposed = 0;
			for (int i = target.SensorFirst; i < target.SensorFirst + target.SensorCount; ++i)
			{
				if (SensorSkyAccess[i] < MinSkyAccess)
					continue;
				++exposed;
				crossings += SensorCrossings[i];
				leafArea += SensorLeafArea[i];
				gap += SensorGapDiffuse[i];
				open += SensorOpen[i];
				veg += SensorVeg[i];
				beamOpen += SensorBeamOpen[i];
				beamVisible += SensorBeamVisible[i];
			}

			target.ExposedArea_m2 = target.SensorCount > 0 ? target.Area_m2 * exposed / target.SensorCount : 0f;
			if (exposed == 0)
			{
				target.Samples.Add(new ObstacleLightSample(0f, 0f, 1f, 1f, 0f, 0f, 0f));
				staticShading[target.Group] = 1f;
				return;
			}

			var gapDiffuse = (float)(gap / exposed);
			//No beam reaches this surface at all in this step, so nothing is being blocked.
			var gapBeam = beamOpen > 0 ? (float)(beamVisible / beamOpen) : 1f;
			var openIrradiance = (float)(open / exposed);
			var vegIrradiance = (float)(veg / exposed);

			target.Samples.Add(new ObstacleLightSample(
				(float)(leafArea / exposed),
				(float)(crossings / exposed),
				gapBeam,
				gapDiffuse,
				MathF.Max(0f, -MathF.Log(MathF.Max(gapDiffuse, 1e-3f)) / BeerLambertExtinction),
				vegIrradiance * hoursPerTick,
				openIrradiance * hoursPerTick));

			staticShading[target.Group] = openIrradiance > 1e-3f
				? Math.Clamp(vegIrradiance / openIrradiance, 0f, 1f)
				: 1f;
		});
	}

	/// <summary>Records a step in which there was no daylight, so that the series stays aligned
	/// with a caller that iterates every timestep.</summary>
	public void SkipStep(uint timestep)
	{
		TimestepList.Add(timestep);
		for (int t = 0; t < TargetArray.Length; ++t)
		{
			//Leaves do not disappear at night, so the geometric readings are carried forward.
			var last = TargetArray[t].Samples.Count > 0 ? TargetArray[t].Samples[^1] : default;
			TargetArray[t].Samples.Add(new ObstacleLightSample(
				last.LeafAreaIndex, last.ProjectedLeafAreaIndex, 1f, 1f, 0f, 0f, 0f));
		}
	}
}
