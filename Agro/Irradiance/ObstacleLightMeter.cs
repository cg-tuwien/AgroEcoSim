using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AgentsSystem;

namespace Agro;

/// <summary>
/// Measures foliage cover and solar load on selected obstacles, resolved by facade orientation and
/// by height.
/// </summary>
/// <remarks>
/// <para>
/// The unit of measurement is a <see cref="Band"/>: one height slice of one orientation of one named
/// object. Both subdivisions exist because without them the headline numbers are wrong rather than
/// merely coarse.
/// </para>
/// <para>
/// Height, because foliage on a facade grows from the planting level upwards: a wall imported as one
/// object spanning three storeys averages a shaded base against a bare top into a middle value that
/// describes neither. Orientation, because an imported wall is usually a <em>solid</em>, so its
/// surface includes the vegetated street face, the bare courtyard face, two reveals and a top. Those
/// have nothing in common but their name, and mixing them halves every shading figure - measured on
/// a test wall, 14% against the 27% its planted face actually sees.
/// </para>
/// <para>
/// Orientation classes are the eight compass sectors plus up and down, assigned from each sensor's
/// outward normal <em>after</em> it has been corrected against the scene, because the winding of an
/// imported mesh cannot be trusted (<see cref="Wall"/>, for one, is wound inwards throughout).
/// </para>
/// <para>
/// Nothing per-step is retained. Each step folds into the running day at all three levels at once,
/// and a day is closed the moment the local date changes, so memory follows days rather than
/// simulation steps. Call <see cref="Finish"/> before reading the results, or the last day stays
/// open.
/// </para>
/// </remarks>
public sealed class ObstacleLightMeter
{
	/// <summary>Sensors are dropped from the readings when less than this fraction of their
	/// hemisphere can see the sky past the building itself. That removes the inward faces of a wall
	/// that encloses a modelled room, which would otherwise dilute every reading.</summary>
	const float MinSkyAccess = 0.05f;
	const float Epsilon = 1e-4f;
	const float FarDistance = 1e5f;
	/// <summary>Extinction coefficient of a spherical leaf angle distribution at the hemispherical
	/// mean view angle of 57.5 degrees: 0.5 / cos(57.5 deg).</summary>
	internal const float BeerLambertExtinction = 0.93f;
	/// <summary>A normal this close to vertical is a roof or a soffit rather than a facade.</summary>
	const float HorizontalCosine = 0.866f; //30 degrees from vertical

	const int MinSensorsPerBand = 8;
	const int MaxSensorsPerBand = 2048;
	/// <summary>Height bands per object. A tall object silently gets thicker bands rather than
	/// hundreds of thin ones.</summary>
	const int MaxBandsPerTarget = 64;

	public const string OuterSide = "outer";
	public const string InnerSide = "inner";
	public const string EdgeSide = "edge";

	/// <summary>A surface counts as facade-parallel when its normal is within 45 degrees of the
	/// object's facade normal; the rest are rims and returns.</summary>
	const float ParallelCosine = 0.707f;
	/// <summary>Depth spread below which an object is a single sheet rather than a channel, so
	/// there is no inner side to separate, in metres.</summary>
	const float MinSideSeparation = 0.02f;
	/// <summary>Escapes out of <see cref="ExposureRays"/> needed before a side counts as open, and
	/// the margin by which an edge's two sides must differ before the better one is believed.</summary>
	const int ExposureRays = 64;
	const int EdgeMargin = 4;

	static readonly string[] SectorNames = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
	const int UpClass = 8;
	const int DownClass = 9;
	const int ClassCount = 10;

	/// <summary>One height slice of one orientation of one object: the unit every reading is
	/// measured on.</summary>
	public sealed class Band
	{
		/// <summary>Height range in world coordinates, metres.</summary>
		public float MinY { get; internal init; }
		public float MaxY { get; internal init; }
		/// <summary>Surface area of the slice, m². Estimated from the area its sensors stand for,
		/// which is exact up to the sampling of the orientation split.</summary>
		public float Area_m2 { get; internal set; }
		public List<ObstacleLightDay> Days { get; } = [];
		public ObstacleLightSummary Summary { get; internal set; }
		internal int[] Sensors = [];
		internal Accumulator Open;
	}

	/// <summary>One orientation of one object - a facade, a roof, a reveal.</summary>
	public sealed class Surface
	{
		/// <summary>"N".."NW" for the eight compass sectors, "up" or "down" for near-horizontal.</summary>
		public string Orientation { get; internal init; } = string.Empty;
		/// <summary>Area-weighted mean outward normal.</summary>
		public Vector3 Normal { get; internal set; }
		/// <summary>Compass bearing it faces, degrees clockwise from north. Zero when horizontal.</summary>
		public float AzimuthDegrees => Azimuth(Normal);
		/// <summary>Tilt from horizontal, degrees: 0 faces up, 90 is vertical.</summary>
		public float TiltDegrees => Tilt(Normal);
		/// <summary>
		/// Which side of the object this is: <c>outer</c> the facade face, <c>inner</c> a face set
		/// back from it - the far wall of a trough, seen through its opening - or <c>edge</c> a rim
		/// or return perpendicular to the facade.
		/// </summary>
		/// <remarks>
		/// Compass orientation alone cannot separate these. A planting tray is a channel, so its
		/// front panel and the trough wall behind it face the <em>same</em> way and differ only in
		/// how far out they sit; merged into one reading they average a planted facade against a
		/// surface that is mostly soil and shade.
		/// </remarks>
		public string Side { get; internal init; } = OuterSide;
		/// <summary>Surface area of this orientation and side, m².</summary>
		public float Area_m2 { get; internal set; }
		/// <summary>How far this side sits out from the object's own facade plane, metres. Larger is
		/// further from the building.</summary>
		public float Depth_m { get; internal set; }
		public Band[] Bands { get; internal set; } = [];
		public List<ObstacleLightDay> Days { get; } = [];
		public ObstacleLightSummary Summary { get; internal set; }
		internal Accumulator Open;
	}

	/// <summary>One named obstacle.</summary>
	public sealed class Target
	{
		public string Name { get; internal init; } = string.Empty;
		/// <summary>Group id of this obstacle in the traced scene.</summary>
		public int Group { get; internal init; }
		/// <summary>Total triangle area, every face of a solid included, m².</summary>
		public float Area_m2 { get; internal init; }
		/// <summary>Share of it that can see the sky and therefore carries readings, m². The sum of
		/// the surfaces' areas.</summary>
		public float ExposedArea_m2 { get; internal set; }
		/// <summary>Area-weighted mean outward normal over the exposed surface. Close to zero for a
		/// closed solid, whose faces cancel - which is exactly why the readings are resolved per
		/// <see cref="Surface"/> and this is only a hint.</summary>
		public Vector3 Normal { get; internal set; }
		/// <summary>Area-weighted centre of the exposed surface.</summary>
		public Vector3 Centroid { get; internal set; }
		public Vector3 BoundsMin { get; internal set; }
		public Vector3 BoundsMax { get; internal set; }
		/// <summary>Unit normal of the object's own facade plane, pointing away from the building.
		/// Zero when the object is too compact for a plane to mean anything, in which case every
		/// side falls back to the sky-access rule.</summary>
		public Vector3 Outward { get; internal set; }
		/// <summary>Area whose light-receiving side turned out to be enclosed by the building, m².
		/// Reported rather than silently dropped: on a stacked tray system it is the measure of how
		/// much of the geometry is interior and carries no reading.</summary>
		public float EnclosedArea_m2 { get; internal set; }
		/// <summary>Orientations present, the one carrying the most solar load first. That is the
		/// facade the object is mostly about, which its area is not: a slab has two faces of
		/// equal area and only one of them matters.</summary>
		public Surface[] Surfaces { get; internal set; } = [];
		/// <summary>Whole-object roll-up. Its energies are exact; its intensive readings mix
		/// orientations, so quote a <see cref="Surface"/> where that matters.</summary>
		public List<ObstacleLightDay> Days { get; } = [];
		public ObstacleLightSummary Summary { get; internal set; }
		internal Accumulator Open;
		//Scaffolding: height bands of the whole object, used to place sensors before the scene is
		//available to say which way they face.
		internal float[] BandEdges = [];
		internal int[] BandFirstSensor = [];
		internal int[] BandSensorCount = [];
	}

	/// <summary>The running day of one level. Sums are energies, so closing a day is at most a
	/// division and nothing has to be revisited.</summary>
	internal struct Accumulator
	{
		public DateTime Date;
		public bool HasData;
		public int Steps;
		public double DaylightHours, BeamHours;
		public double BeamOpen, Beam, DiffuseOpen, Diffuse;
		public double LeafAreaSum, ProjectedSum, GapSum, Weight;
		public float LeafAreaMax, PeakOpen, PeakReduction;
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
	/// <summary>Requested height of a band, metres.</summary>
	public float BandHeight_m { get; }

	readonly Target[] TargetArray;
	readonly Sensor[] Sensors;

	//Per sensor, fixed after Prepare.
	readonly bool[] SensorExposed;
	readonly float[] SensorArea;
	/// <summary>Which side of its object each sensor is on: see <see cref="Surface.Side"/>.</summary>
	readonly string[] SensorSide;

	//Per sensor scratch, rewritten every step.
	readonly float[] SensorBeamOpen;
	readonly float[] SensorBeam;
	readonly float[] SensorDiffuseOpen;
	readonly float[] SensorDiffuse;
	readonly float[] SensorBeamHourFraction;
	readonly float[] SensorGapDiffuse;
	readonly float[] SensorLeafArea;
	readonly int[] SensorCrossings;

	bool Prepared;

	//One step's totals per target, so the selection-wide roll-up can be summed sequentially after
	//the parallel pass and stay bit-identical from run to run.
	readonly Totals[] StepTotals;
	Accumulator SelectionOpen;

	public IReadOnlyList<Target> Targets => TargetArray;
	public int SensorCount => Sensors.Length;

	/// <summary>
	/// The whole selection as one patch: the area-weighted daily series over every measured
	/// obstacle.
	/// </summary>
	/// <remarks>
	/// Always kept, and cheap, because it is the one series that has to survive. A facade system
	/// imported as sixteen hundred trays cannot carry a daily series per tray without running to a
	/// hundred megabytes, but the seasonal curve of the facade as a whole is exactly what the study
	/// is about.
	/// </remarks>
	public List<ObstacleLightDay> SelectionDays { get; } = [];
	public ObstacleLightSummary SelectionSummary { get; private set; }
	/// <summary>Total area of every measured obstacle, and the part of it that carries readings.</summary>
	public float SelectionArea_m2 { get; private set; }
	public float SelectionExposedArea_m2 { get; private set; }
	/// <summary>Area whose light-receiving side is enclosed by the building and therefore carries no
	/// reading. On a stacked tray system this is the measure of how much of the imported geometry is
	/// interior.</summary>
	public float SelectionEnclosedArea_m2 { get; private set; }

	static float Azimuth(Vector3 normal)
	{
		//North is -Z and east is +X, the convention the whole light model uses.
		if (MathF.Sqrt(normal.X * normal.X + normal.Z * normal.Z) < 1e-4f)
			return 0f;
		var degrees = MathF.Atan2(normal.X, -normal.Z) * (180f / MathF.PI);
		if (degrees < 0f)
			degrees += 360f;
		//Due north comes out as a hair under zero and used to be reported as 360.
		return degrees >= 359.9995f ? 0f : degrees;
	}

	static float Tilt(Vector3 normal) => MathF.Acos(Math.Clamp(normal.Y, -1f, 1f)) * (180f / MathF.PI);

	static int Classify(Vector3 normal)
	{
		if (normal.Y >= HorizontalCosine)
			return UpClass;
		if (normal.Y <= -HorizontalCosine)
			return DownClass;
		//Sectors are centred on the compass points, so a due-south wall is "S" and not a boundary case.
		var sector = (int)MathF.Round(Azimuth(normal) / 45f) & 7;
		return sector;
	}

	static string ClassName(int index) => index switch
	{
		UpClass => "up",
		DownClass => "down",
		_ => SectorNames[index],
	};

	/// <summary>
	/// Selects the obstacles whose name contains <paramref name="filter"/>, slices them into height
	/// bands and places the sensors. The orientation split happens later, in
	/// <see cref="Prepare"/>, because it needs normals the scene has corrected.
	/// </summary>
	/// <param name="prims">The static primitives before the hierarchy reordered them.</param>
	/// <param name="ranges">Primitive range of every obstacle inside <paramref name="prims"/>.</param>
	/// <param name="sensorBudget">Target total number of sensors. A lower bound per band can push
	/// the actual count above it when many bands are selected.</param>
	public ObstacleLightMeter(IList<IObstacle> obstacles, RayPrimitive[] prims, (int First, int Count)[] ranges, string regexFilter, int sensorBudget, float canopyDepth, float bandHeight)
	{
		CanopyDepth_m = canopyDepth;
		BandHeight_m = MathF.Max(0.05f, bandHeight);

		//Pass one: which obstacles, and how their area is distributed over height.
		var selected = new List<(int Index, string Name, float Area, float[] Edges, List<Vector3>[] Geometry, float[] Areas)>();
		var totalArea = 0f;
		for (int o = 0; o < obstacles.Count; ++o)
			if (Regex.IsMatch(obstacles[0].Name, regexFilter, RegexOptions.IgnoreCase))
			{
				var (first, count) = ranges[o];
				if (count == 0)
					continue;

				var minY = float.MaxValue;
				var maxY = float.MinValue;
				var objectArea = 0f;
				for (int i = first; i < first + count; ++i)
				{
					ref var prim = ref prims[i];
					var b = prim.P + prim.U;
					var c = prim.P + prim.V;
					minY = MathF.Min(minY, MathF.Min(prim.P.Y, MathF.Min(b.Y, c.Y)));
					maxY = MathF.Max(maxY, MathF.Max(prim.P.Y, MathF.Max(b.Y, c.Y)));
					objectArea += 0.5f * Vector3.Cross(prim.U, prim.V).Length();
				}
				if (objectArea <= 0f)
					continue;

				var height = MathF.Max(BandHeight_m, (maxY - minY) / MaxBandsPerTarget);
				//Snap to a global grid so the same height reads as the same band across objects.
				var gridStart = MathF.Floor(minY / height) * height;
				var bandCount = Math.Max(1, (int)MathF.Ceiling((maxY - gridStart) / height));
				var edges = new float[bandCount + 1];
				for (int b = 0; b <= bandCount; ++b)
					edges[b] = gridStart + b * height;

				var geometry = new List<Vector3>[bandCount];
				var areas = new float[bandCount];
				for (int b = 0; b < bandCount; ++b)
					geometry[b] = [];

				for (int i = first; i < first + count; ++i)
				{
					ref var prim = ref prims[i];
					var a = prim.P;
					var b2 = prim.P + prim.U;
					var c2 = prim.P + prim.V;
					var loY = MathF.Min(a.Y, MathF.Min(b2.Y, c2.Y));
					var hiY = MathF.Max(a.Y, MathF.Max(b2.Y, c2.Y));
					var firstBand = Math.Clamp((int)MathF.Floor((loY - gridStart) / height), 0, bandCount - 1);
					var lastBand = Math.Clamp((int)MathF.Floor((hiY - gridStart) / height), 0, bandCount - 1);
					for (int b = firstBand; b <= lastBand; ++b)
						areas[b] += ClipToBand(a, b2, c2, edges[b], edges[b + 1], geometry[b]);
				}

				selected.Add((o, obstacles[o].Name, objectArea, edges, geometry, areas));
				totalArea += objectArea;
			}

		//Pass two: sensor budgets and placement.
		TargetArray = new Target[selected.Count];
		var sensors = new List<Sensor>(Math.Max(sensorBudget, 1024));
		var sensorAreas = new List<float>(Math.Max(sensorBudget, 1024));

		for (int t = 0; t < selected.Count; ++t)
		{
			var (index, name, objectArea, edges, bandGeometry, bandAreas) = selected[t];
			var bandCount = bandGeometry.Length;
			var firstSensor = new int[bandCount];
			var sensorCount = new int[bandCount];

			var min = new Vector3(float.MaxValue);
			var max = new Vector3(float.MinValue);

			for (int b = 0; b < bandCount; ++b)
			{
				var geometry = bandGeometry[b];
				firstSensor[b] = sensors.Count;
				if (bandAreas[b] <= 0f || geometry.Count < 3)
					continue;

				var budget = totalArea > 0f
					? Math.Clamp((int)MathF.Round(sensorBudget * bandAreas[b] / totalArea), MinSensorsPerBand, MaxSensorsPerBand)
					: MinSensorsPerBand;
				sensorCount[b] = budget;

				//Area weighted prefix sum over the band's clipped triangles, so sensors land
				//uniformly on the surface no matter how unevenly it is tessellated.
				var triangleCount = geometry.Count / 3;
				var cumulative = new float[triangleCount];
				var running = 0f;
				for (int i = 0; i < triangleCount; ++i)
				{
					var a = geometry[3 * i];
					running += 0.5f * Vector3.Cross(geometry[3 * i + 1] - a, geometry[3 * i + 2] - a).Length();
					cumulative[i] = running;
					min = Vector3.Min(min, Vector3.Min(a, Vector3.Min(geometry[3 * i + 1], geometry[3 * i + 2])));
					max = Vector3.Max(max, Vector3.Max(a, Vector3.Max(geometry[3 * i + 1], geometry[3 * i + 2])));
				}

				var areaPerSensor = bandAreas[b] / budget;
				for (int s = 0; s < budget; ++s)
				{
					var pick = (s + 0.5f) / budget * running;
					var tri = Array.BinarySearch(cumulative, pick);
					if (tri < 0)
						tri = Math.Min(triangleCount - 1, ~tri);
					var a = geometry[3 * tri];
					var u = geometry[3 * tri + 1] - a;
					var v = geometry[3 * tri + 2] - a;
					RaySampling.Sample2D(s, budget, RaySampling.Hash((uint)index, (uint)((b << 20) + s)), out var u1, out var u2);
					sensors.Add(new Sensor(RaySampling.TrianglePoint(a, u, v, u1, u2), Vector3.Normalize(Vector3.Cross(u, v))));
					sensorAreas.Add(areaPerSensor);
				}
			}

			TargetArray[t] = new Target
			{
				Name = name,
				Group = 1 + index, //group 0 is the terrain
				Area_m2 = objectArea,
				BoundsMin = min,
				BoundsMax = max,
				BandEdges = edges,
				BandFirstSensor = firstSensor,
				BandSensorCount = sensorCount,
			};
		}

		Sensors = [.. sensors];
		SensorArea = [.. sensorAreas];
		SensorExposed = new bool[Sensors.Length];
		SensorSide = new string[Sensors.Length];
		SensorBeamOpen = new float[Sensors.Length];
		SensorBeam = new float[Sensors.Length];
		SensorDiffuseOpen = new float[Sensors.Length];
		SensorDiffuse = new float[Sensors.Length];
		SensorBeamHourFraction = new float[Sensors.Length];
		SensorGapDiffuse = new float[Sensors.Length];
		SensorLeafArea = new float[Sensors.Length];
		SensorCrossings = new int[Sensors.Length];
		StepTotals = new Totals[TargetArray.Length];
	}

	/// <summary>
	/// Clips a triangle to the slab <c>[y0, y1]</c>, appends the result fan-triangulated to
	/// <paramref name="output"/> and returns its area. Sutherland-Hodgman against two planes, so the
	/// result has at most five vertices and keeps the input winding.
	/// </summary>
	static float ClipToBand(Vector3 a, Vector3 b, Vector3 c, float y0, float y1, List<Vector3> output)
	{
		Span<Vector3> polygon = stackalloc Vector3[8];
		Span<Vector3> scratch = stackalloc Vector3[8];
		polygon[0] = a;
		polygon[1] = b;
		polygon[2] = c;

		var count = ClipHalfSpace(polygon, 3, scratch, y0, keepAbove: true);
		if (count < 3)
			return 0f;
		count = ClipHalfSpace(scratch, count, polygon, y1, keepAbove: false);
		if (count < 3)
			return 0f;

		var area = 0f;
		for (int i = 2; i < count; ++i)
		{
			output.Add(polygon[0]);
			output.Add(polygon[i - 1]);
			output.Add(polygon[i]);
			area += 0.5f * Vector3.Cross(polygon[i - 1] - polygon[0], polygon[i] - polygon[0]).Length();
		}
		return area;
	}

	static int ClipHalfSpace(ReadOnlySpan<Vector3> input, int count, Span<Vector3> output, float plane, bool keepAbove)
	{
		var written = 0;
		for (int i = 0; i < count; ++i)
		{
			var current = input[i];
			var next = input[(i + 1) % count];
			var currentIn = keepAbove ? current.Y >= plane : current.Y <= plane;
			var nextIn = keepAbove ? next.Y >= plane : next.Y <= plane;

			if (currentIn)
				output[written++] = current;
			if (currentIn != nextIn)
			{
				var span = next.Y - current.Y;
				var t = MathF.Abs(span) < 1e-12f ? 0f : (plane - current.Y) / span;
				output[written++] = Vector3.Lerp(current, next, Math.Clamp(t, 0f, 1f));
			}
		}
		return written;
	}

	/// <summary>
	/// One-time pass that settles everything static: which way each sensor really faces, whether it
	/// can see the sky at all, and the orientation and height patch it therefore belongs to. All of
	/// it depends on static geometry only, so measuring it once per run is both cheaper and more
	/// stable than deriving it from whatever the current step happened to sample.
	/// </summary>
	void Prepare(RayScene scene)
	{
		if (Prepared || Sensors.Length == 0)
			return;
		Prepared = true;

		//The facade axis of each object is reliable; the sign of it is not, so it is settled per
		//facade rather than per object - see FacadeSigns.
		Parallel.For(0, TargetArray.Length, t => TargetArray[t].Outward = FacadeAxis(TargetArray[t]));
		FacadeSigns(scene);
		Parallel.For(0, TargetArray.Length, t => OrientSensors(scene, TargetArray[t]));

		for (int t = 0; t < TargetArray.Length; ++t)
			BuildPatches(TargetArray[t]);

		for (int t = 0; t < TargetArray.Length; ++t)
		{
			SelectionArea_m2 += TargetArray[t].Area_m2;
			SelectionExposedArea_m2 += TargetArray[t].ExposedArea_m2;
			SelectionEnclosedArea_m2 += TargetArray[t].EnclosedArea_m2;
		}
	}

	/// <summary>
	/// Settles, for one object, which way each of its sensors faces and which side of the object it
	/// belongs to.
	/// </summary>
	/// <remarks>
	/// The winding of an imported mesh cannot be trusted - a solid may be wound inwards throughout -
	/// so the light-receiving side has to be derived. An earlier version asked each sensor
	/// individually which of its two sides saw more sky, and on this geometry that failed: the trays
	/// of a facade system are stacked 0.25 m apart and are 0.259 m tall, so a rim sits in a slot
	/// narrower than the question, both sides saw one or two rays out of thirty-two, and the answer
	/// was noise. It labelled upward rims "down" and called every tray fully exposed.
	///
	/// So the object decides first, and the sensor second:
	/// <list type="number">
	/// <item>The facade plane comes from the object's own surface normals, as the dominant axis of
	/// the area-weighted tensor sum of n (x) n. That product is sign-invariant, which is exactly
	/// what is needed when the winding is meaningless.</item>
	/// <item>Its sign - which way is out of the building - is a vote over every sensor of the object
	/// at once, so hundreds of rays decide it instead of two.</item>
	/// <item>Each facade-parallel sensor then faces away from the building, and is <c>outer</c> or
	/// <c>inner</c> by how far out it sits. Only perpendicular rims fall back to comparing their two
	/// sides, and only when one side wins by a clear margin.</item>
	/// </list>
	/// </remarks>
	/// <summary>The unsigned facade axis of one object, from the area-weighted tensor sum of
	/// n (x) n over its sensors. That product is sign-invariant, which is what is needed when the
	/// mesh winding is meaningless.</summary>
	Vector3 FacadeAxis(Target target)
	{
		var first = target.BandFirstSensor;
		var count = target.BandSensorCount;
		var xx = 0f; var yy = 0f; var zz = 0f; var xy = 0f; var xz = 0f; var yz = 0f;
		for (int b = 0; b < count.Length; ++b)
			for (int i = first[b]; i < first[b] + count[b]; ++i)
			{
				var n = Sensors[i].Normal;
				var a = SensorArea[i];
				xx += a * n.X * n.X; yy += a * n.Y * n.Y; zz += a * n.Z * n.Z;
				xy += a * n.X * n.Y; xz += a * n.X * n.Z; yz += a * n.Y * n.Z;
			}
		return DominantAxis(xx, yy, zz, xy, xz, yz);
	}

	/// <summary>
	/// Points every object's facade axis away from the building, deciding the sign once per facade
	/// rather than once per object.
	/// </summary>
	/// <remarks>
	/// A single object cannot answer this. Asked per tray, the vote put two neighbours in the same
	/// row facing opposite ways, which swapped their inner and outer labels and made the enclosed
	/// area jump from under one percent to twenty-eight between near-identical objects: a tray has
	/// only a handful of sensors whose outward ray actually escapes, and the rest are ambiguous in a
	/// stack this tight. Objects sharing a facade share an axis, though, so they are grouped by it
	/// and the vote is taken over the whole group, where the outer panels that do escape carry it.
	/// </remarks>
	void FacadeSigns(RayScene scene)
	{
		const float parallelEnough = 0.94f; //within 20 degrees
		var axes = new List<Vector3>();
		var members = new List<List<int>>();

		for (int t = 0; t < TargetArray.Length; ++t)
		{
			var axis = TargetArray[t].Outward;
			if (axis.LengthSquared() <= 1e-12f)
				continue;
			var placed = false;
			for (int g = 0; g < axes.Count; ++g)
				if (MathF.Abs(Vector3.Dot(axis, axes[g])) >= parallelEnough)
				{
					members[g].Add(t);
					placed = true;
					break;
				}
			if (!placed)
			{
				axes.Add(axis);
				members.Add([t]);
			}
		}

		Parallel.For(0, axes.Count, g =>
		{
			var axis = axes[g];
			var forward = 0;
			var backward = 0;
			foreach (var t in members[g])
			{
				var target = TargetArray[t];
				//Align each member to the group before voting, or members wound the other way
				//would cancel the very signal being counted.
				var local = Vector3.Dot(target.Outward, axis) >= 0f ? axis : -axis;
				var first = target.BandFirstSensor;
				var count = target.BandSensorCount;
				for (int b = 0; b < count.Length; ++b)
					for (int i = first[b]; i < first[b] + count[b]; ++i)
					{
						var p = Sensors[i].Position;
						if (!scene.AnyHit(p + local * Epsilon, local, Epsilon, FarDistance, RayScene.MaskStatic))
							++forward;
						if (!scene.AnyHit(p - local * Epsilon, -local, Epsilon, FarDistance, RayScene.MaskStatic))
							++backward;
					}
			}

			var outward = backward > forward ? -axis : axis;
			foreach (var t in members[g])
			{
				var target = TargetArray[t];
				target.Outward = Vector3.Dot(target.Outward, outward) >= 0f ? outward : -outward;
			}
		});
	}

	void OrientSensors(RayScene scene, Target target)
	{
		var first = target.BandFirstSensor;
		var count = target.BandSensorCount;
		var axis = target.Outward;
		var hasPlane = axis.LengthSquared() > 1e-12f;

		//3. Where the facade-parallel sensors sit along it, so outer and inner can be told apart.
		var depthMin = float.MaxValue;
		var depthMax = float.MinValue;
		if (hasPlane)
			for (int b = 0; b < count.Length; ++b)
				for (int i = first[b]; i < first[b] + count[b]; ++i)
					if (MathF.Abs(Vector3.Dot(Sensors[i].Normal, axis)) >= ParallelCosine)
					{
						var d = Vector3.Dot(Sensors[i].Position, axis);
						depthMin = MathF.Min(depthMin, d);
						depthMax = MathF.Max(depthMax, d);
					}
		var split = depthMax - depthMin >= MinSideSeparation ? 0.5f * (depthMin + depthMax) : float.MinValue;

		var enclosed = 0f;
		for (int b = 0; b < count.Length; ++b)
			for (int i = first[b]; i < first[b] + count[b]; ++i)
			{
				var sensor = Sensors[i];
				var parallel = hasPlane && MathF.Abs(Vector3.Dot(sensor.Normal, axis)) >= ParallelCosine;

				if (parallel)
				{
					//Both the front panel and the trough wall behind it receive light on the side
					//facing away from the building; only their depth distinguishes them. A face
					//whose outward side is walled in - the room side of an exterior wall - fails
					//the exposure test below and is reported as enclosed.
					Sensors[i] = new Sensor(sensor.Position, axis);
					SensorSide[i] = Vector3.Dot(sensor.Position, axis) > split ? OuterSide : InnerSide;
					SensorExposed[i] = SkyAccess(scene, sensor.Position, axis, ExposureRays, (uint)i)
						>= MinSkyAccess * ExposureRays;
				}
				else
				{
					//A rim or a return. Its two sides are genuinely different, so compare them - but
					//only believe the winner when it wins clearly, or a slot decides it by noise.
					var front = SkyAccess(scene, sensor.Position, sensor.Normal, ExposureRays, (uint)i);
					var back = SkyAccess(scene, sensor.Position, -sensor.Normal, ExposureRays, (uint)i);
					if (back > front + EdgeMargin)
					{
						Sensors[i] = new Sensor(sensor.Position, -sensor.Normal);
						front = back;
					}
					SensorSide[i] = EdgeSide;
					SensorExposed[i] = front >= MinSkyAccess * ExposureRays;
				}

				if (!SensorExposed[i])
					enclosed += SensorArea[i];
			}
		target.EnclosedArea_m2 = enclosed;
	}

	/// <summary>
	/// Dominant eigenvector of a symmetric 3x3 tensor, by power iteration from a fixed start so the
	/// result is reproducible. Returns zero when no axis dominates, which is the honest answer for a
	/// compact object that has no facade plane.
	/// </summary>
	static Vector3 DominantAxis(float xx, float yy, float zz, float xy, float xz, float yz)
	{
		var trace = xx + yy + zz;
		if (trace <= 1e-9f)
			return Vector3.Zero;

		//Start along the largest diagonal entry: never orthogonal to the dominant eigenvector of a
		//positive semi-definite tensor, so one start suffices.
		var v = xx >= yy && xx >= zz ? Vector3.UnitX : (yy >= zz ? Vector3.UnitY : Vector3.UnitZ);
		for (int k = 0; k < 64; ++k)
		{
			var next = new Vector3(
				xx * v.X + xy * v.Y + xz * v.Z,
				xy * v.X + yy * v.Y + yz * v.Z,
				xz * v.X + yz * v.Y + zz * v.Z);
			var length = next.Length();
			if (length <= 1e-12f)
				return Vector3.Zero;
			next /= length;
			var moved = (next - v).LengthSquared();
			v = next;
			if (moved < 1e-12f)
				break;
		}

		//How much of the total area actually lies along it. A cube spreads evenly over three axes
		//and has no facade; a panel or a channel concentrates on one.
		var along = xx * v.X * v.X + yy * v.Y * v.Y + zz * v.Z * v.Z
			+ 2f * (xy * v.X * v.Y + xz * v.X * v.Z + yz * v.Y * v.Z);
		return along / trace >= 0.5f ? v : Vector3.Zero;
	}

	/// <summary>How many of <paramref name="rays"/> cosine-weighted directions around
	/// <paramref name="normal"/> escape the static scene.</summary>
	static int SkyAccess(RayScene scene, Vector3 position, Vector3 normal, int rays, uint key)
	{
		var origin = position + normal * Epsilon;
		RaySampling.OrthonormalBasis(normal, out var tangent, out var bitangent);
		var open = 0;
		for (int j = 0; j < rays; ++j)
		{
			RaySampling.Sample2D(j, rays, RaySampling.Hash(key, 0x5bd1e995u), out var u1, out var u2);
			var d = RaySampling.CosineHemisphere(normal, tangent, bitangent, u1, u2);
			if (!scene.AnyHit(origin, d, Epsilon, FarDistance, RayScene.MaskStatic))
				++open;
		}
		return open;
	}

	/// <summary>Groups an object's exposed sensors into orientation classes and, inside each, into
	/// the height bands they were placed in.</summary>
	void BuildPatches(Target target)
	{
		var bandCount = target.BandSensorCount.Length;
		//[side][orientation class][band] -> sensor indices. Side is part of the key because
		//compass orientation alone merges a tray's front panel with the trough wall behind it.
		var sides = new[] { OuterSide, InnerSide, EdgeSide };
		var buckets = new List<int>[sides.Length][][];
		var classArea = new float[sides.Length][];
		var classNormal = new Vector3[sides.Length][];
		var classDepth = new float[sides.Length][];
		for (int k = 0; k < sides.Length; ++k)
		{
			buckets[k] = new List<int>[ClassCount][];
			classArea[k] = new float[ClassCount];
			classNormal[k] = new Vector3[ClassCount];
			classDepth[k] = new float[ClassCount];
		}

		var exposedArea = 0f;
		var objectNormal = Vector3.Zero;
		var centroid = Vector3.Zero;

		for (int b = 0; b < bandCount; ++b)
		{
			var first = target.BandFirstSensor[b];
			for (int i = first; i < first + target.BandSensorCount[b]; ++i)
			{
				if (!SensorExposed[i])
					continue;
				var area = SensorArea[i];
				var normal = Sensors[i].Normal;
				var cls = Classify(normal);
				var side = Array.IndexOf(sides, SensorSide[i] ?? OuterSide);
				if (side < 0)
					side = 0;

				buckets[side][cls] ??= new List<int>[bandCount];
				(buckets[side][cls][b] ??= []).Add(i);
				classArea[side][cls] += area;
				classNormal[side][cls] += normal * area;
				classDepth[side][cls] += area * (target.Outward.LengthSquared() > 1e-12f
					? Vector3.Dot(Sensors[i].Position, target.Outward) : 0f);

				exposedArea += area;
				objectNormal += normal * area;
				centroid += Sensors[i].Position * area;
			}
		}

		target.ExposedArea_m2 = exposedArea;
		target.Normal = objectNormal.LengthSquared() > 1e-12f ? Vector3.Normalize(objectNormal) : Vector3.UnitY;
		target.Centroid = exposedArea > 0f ? centroid / exposedArea : (target.BoundsMin + target.BoundsMax) * 0.5f;

		//Largest first; Finish reorders by the solar load once the summaries exist.
		var order = new List<(int Side, int Class)>(sides.Length * ClassCount);
		for (int k = 0; k < sides.Length; ++k)
			for (int c = 0; c < ClassCount; ++c)
				if (classArea[k][c] > 0f)
					order.Add((k, c));
		order.Sort((x, y) => classArea[y.Side][y.Class].CompareTo(classArea[x.Side][x.Class]));

		var surfaces = new List<Surface>(order.Count);
		foreach (var (k, c) in order)
		{
			var bands = new List<Band>(bandCount);
			for (int b = 0; b < bandCount; ++b)
			{
				var list = buckets[k][c][b];
				if (list == null || list.Count == 0)
					continue;
				var area = 0f;
				foreach (var i in list)
					area += SensorArea[i];
				bands.Add(new Band
				{
					MinY = target.BandEdges[b],
					MaxY = target.BandEdges[b + 1],
					Area_m2 = area,
					Sensors = [.. list],
				});
			}

			surfaces.Add(new Surface
			{
				Orientation = ClassName(c),
				Side = sides[k],
				Normal = classNormal[k][c].LengthSquared() > 1e-12f
					? Vector3.Normalize(classNormal[k][c]) : Vector3.UnitY,
				Area_m2 = classArea[k][c],
				Depth_m = classArea[k][c] > 0f ? classDepth[k][c] / classArea[k][c] : 0f,
				Bands = [.. bands],
			});
		}
		target.Surfaces = [.. surfaces];

		//The placement scaffolding is only needed until the patches exist.
		target.BandFirstSensor = [];
		target.BandSensorCount = [];
	}

	/// <summary>
	/// Measures every sensor for one simulation step and folds the result into the running day of
	/// every band, surface and object. <paramref name="staticShading"/> is updated in place with the
	/// fraction of the open-sky load that still reaches each obstacle, which feeds the indirect light
	/// of the next step.
	/// </summary>
	/// <param name="localStepTime">Start of the step in local time at the site. Days are closed on
	/// its calendar date, because that is the day an architect means.</param>
	public void Measure(RayScene scene, in SkyDome diffuse, SkyDome[] suns, int sunCount,
						in SurfaceRadianceModel model, int diffuseSamples, float hoursPerTick,
						DateTime localStepTime, float[] staticShading)
	{
		if (Sensors.Length == 0)
			return;
		Prepare(scene);

		var samples = Math.Max(1, diffuseSamples);
		var depth = CanopyDepth_m;
		var localModel = model;
		var localDiffuse = diffuse;

		Parallel.ForEach(Partitioner.Create(0, Sensors.Length), range =>
		{
			for (int i = range.Item1; i < range.Item2; ++i)
			{
				if (!SensorExposed[i])
					continue;

				var sensor = Sensors[i];
				var n = sensor.Normal;
				var origin = sensor.Position + n * Epsilon;
				var key = RaySampling.Hash((uint)i, (uint)localStepTime.Ticks, 0x9e3779b9u);

				//Direct beam, sub-sampled over the step because the sun sweeps a long arc in it.
				float beamOpen = 0f, beam = 0f, beamLitSubSteps = 0f;
				for (int k = 0; k < sunCount; ++k)
				{
					ref readonly var sun = ref suns[k];
					var cosIncidence = Vector3.Dot(n, sun.SunDirection);
					if (sun.DirectNormal <= 0f || cosIncidence <= 1e-3f)
						continue;
					if (scene.AnyHit(origin, sun.SunDirection, Epsilon, FarDistance, RayScene.MaskStatic))
						continue; //the building itself shades this patch, vegetation aside
					var load = sun.DirectNormal * cosIncidence;
					beamOpen += load;
					beamLitSubSteps += 1f;
					if (!scene.AnyHit(origin, sun.SunDirection, Epsilon, FarDistance, RayScene.MaskPlants))
						beam += load;
				}

				//Diffuse sky and one bounce of everything it lights, cosine sampled over the
				//outward hemisphere. Tracing the building and the plants separately yields the
				//with-vegetation and the open-sky load from the same set of directions.
				RaySampling.OrthonormalBasis(n, out var tangent, out var bitangent);
				float sumOpen = 0f, sumVeg = 0f, freeOfFoliage = 0f;
				for (int j = 0; j < samples; ++j)
				{
					RaySampling.Sample2D(j, samples, key, out var u1, out var u2);
					var d = RaySampling.CosineHemisphere(n, tangent, bitangent, u1, u2);

					var hasStatic = scene.ClosestHit(origin, d, Epsilon, FarDistance, RayScene.MaskStatic, out var staticHit);
					var hasPlant = scene.ClosestHit(origin, d, Epsilon, FarDistance, RayScene.MaskPlants, out var plantHit);

					var radianceOpen = hasStatic ? localModel.Radiance(in staticHit, in localDiffuse) : localDiffuse.Radiance(d);
					sumOpen += radianceOpen;

					if (hasPlant && (!hasStatic || plantHit.T < staticHit.T))
						sumVeg += localModel.Radiance(in plantHit, in localDiffuse);
					else
					{
						sumVeg += radianceOpen;
						freeOfFoliage += 1f;
					}
				}

				var scale = MathF.PI / samples;
				var subSteps = MathF.Max(1, sunCount);
				SensorBeamOpen[i] = beamOpen / subSteps;
				SensorBeam[i] = beam / subSteps;
				SensorDiffuseOpen[i] = scale * sumOpen;
				SensorDiffuse[i] = scale * sumVeg;
				SensorBeamHourFraction[i] = beamLitSubSteps / subSteps;
				SensorGapDiffuse[i] = freeOfFoliage / samples;
				SensorCrossings[i] = scene.CountFoliageCrossings(origin, n, Epsilon, depth, out var leafArea);
				SensorLeafArea[i] = leafArea;
			}
		});

		var date = localStepTime.Date;
		Parallel.For(0, TargetArray.Length, t =>
		{
			var target = TargetArray[t];
			var objectTotals = default(Totals);

			foreach (var surface in target.Surfaces)
			{
				var surfaceTotals = default(Totals);
				foreach (var band in surface.Bands)
				{
					var bandTotals = default(Totals);
					foreach (var i in band.Sensors)
					{
						var a = SensorArea[i];
						bandTotals.Weight += a;
						bandTotals.BeamOpen += SensorBeamOpen[i] * a;
						bandTotals.Beam += SensorBeam[i] * a;
						bandTotals.DiffuseOpen += SensorDiffuseOpen[i] * a;
						bandTotals.Diffuse += SensorDiffuse[i] * a;
						bandTotals.BeamHours += SensorBeamHourFraction[i] * a;
						bandTotals.LeafArea += SensorLeafArea[i] * a;
						bandTotals.Projected += SensorCrossings[i] * a;
						bandTotals.Gap += SensorGapDiffuse[i] * a;
					}
					Fold(ref band.Open, band.Days, in bandTotals, date, hoursPerTick);
					surfaceTotals.Add(in bandTotals);
				}
				Fold(ref surface.Open, surface.Days, in surfaceTotals, date, hoursPerTick);
				objectTotals.Add(in surfaceTotals);
			}
			Fold(ref target.Open, target.Days, in objectTotals, date, hoursPerTick);
			StepTotals[t] = objectTotals;

			var open = objectTotals.BeamOpen + objectTotals.DiffuseOpen;
			var veg = objectTotals.Beam + objectTotals.Diffuse;
			staticShading[target.Group] = open > 1e-3 ? (float)Math.Clamp(veg / open, 0.0, 1.0) : 1f;
		});

		//Sequentially, so the selection-wide series does not depend on how the work was scheduled.
		var selection = default(Totals);
		for (int t = 0; t < StepTotals.Length; ++t)
			selection.Add(in StepTotals[t]);
		Fold(ref SelectionOpen, SelectionDays, in selection, date, hoursPerTick);
	}

	/// <summary>Area-weighted sums of one step for one level. Adding a child's totals to a parent's
	/// is what makes every level's area weighting exact and identical.</summary>
	struct Totals
	{
		public double Weight;
		public double BeamOpen, Beam, DiffuseOpen, Diffuse;
		public double BeamHours, LeafArea, Projected, Gap;

		public void Add(in Totals other)
		{
			Weight += other.Weight;
			BeamOpen += other.BeamOpen;
			Beam += other.Beam;
			DiffuseOpen += other.DiffuseOpen;
			Diffuse += other.Diffuse;
			BeamHours += other.BeamHours;
			LeafArea += other.LeafArea;
			Projected += other.Projected;
			Gap += other.Gap;
		}
	}

	/// <summary>Folds one step into a level's running day, closing the previous one first when the
	/// date has turned over. The destination list is passed in because an accumulator does not know
	/// which level owns it.</summary>
	static void Fold(ref Accumulator open, List<ObstacleLightDay> days, in Totals totals, DateTime date, float hoursPerTick)
	{
		if (totals.Weight <= 0.0)
			return;
		if (open.HasData && open.Date != date)
		{
			days.Add(Close(in open));
			open = default;
		}
		open.Date = date;
		open.HasData = true;
		++open.Steps;

		var weight = totals.Weight;
		var openIrradiance = (totals.BeamOpen + totals.DiffuseOpen) / weight;
		var vegIrradiance = (totals.Beam + totals.Diffuse) / weight;
		var leafArea = (float)(totals.LeafArea / weight);

		open.DaylightHours += hoursPerTick;
		open.BeamHours += hoursPerTick * totals.BeamHours / weight;
		open.BeamOpen += hoursPerTick * totals.BeamOpen / weight;
		open.Beam += hoursPerTick * totals.Beam / weight;
		open.DiffuseOpen += hoursPerTick * totals.DiffuseOpen / weight;
		open.Diffuse += hoursPerTick * totals.Diffuse / weight;
		open.LeafAreaSum += leafArea;
		open.ProjectedSum += totals.Projected / weight;
		open.GapSum += totals.Gap / weight;
		open.Weight += 1.0;
		open.LeafAreaMax = MathF.Max(open.LeafAreaMax, leafArea);
		open.PeakOpen = MathF.Max(open.PeakOpen, (float)openIrradiance);
		open.PeakReduction = MathF.Max(open.PeakReduction, (float)(openIrradiance - vegIrradiance));
	}

	static ObstacleLightDay Close(in Accumulator a)
	{
		var totalOpen = a.BeamOpen + a.DiffuseOpen;
		var total = a.Beam + a.Diffuse;
		var weight = a.Weight > 0 ? a.Weight : 1.0;
		var gap = (float)(a.GapSum / weight);
		return new ObstacleLightDay(
			a.Date,
			a.Steps,
			(float)a.DaylightHours,
			(float)a.BeamHours,
			(float)a.BeamOpen,
			(float)a.Beam,
			(float)a.DiffuseOpen,
			(float)a.Diffuse,
			totalOpen > 1e-6 ? (float)(1.0 - total / totalOpen) : 0f,
			a.BeamOpen > 1e-6 ? (float)(a.Beam / a.BeamOpen) : 1f,
			a.DiffuseOpen > 1e-6 ? (float)(a.Diffuse / a.DiffuseOpen) : 1f,
			gap,
			(float)(a.LeafAreaSum / weight),
			a.LeafAreaMax,
			(float)(a.ProjectedSum / weight),
			MathF.Max(0f, -MathF.Log(MathF.Max(gap, 1e-3f)) / BeerLambertExtinction),
			a.PeakOpen,
			a.PeakReduction);
	}

	/// <summary>
	/// Closes the day still in progress at every level and computes the summaries. Must be called
	/// before the results are read; calling it twice is harmless.
	/// </summary>
	public void Finish()
	{
		for (int t = 0; t < TargetArray.Length; ++t)
		{
			var target = TargetArray[t];
			foreach (var surface in target.Surfaces)
			{
				foreach (var band in surface.Bands)
				{
					FlushInto(ref band.Open, band.Days);
					band.Summary = ObstacleLightSummary.From(band.Days);
				}
				FlushInto(ref surface.Open, surface.Days);
				surface.Summary = ObstacleLightSummary.From(surface.Days);
			}
			FlushInto(ref target.Open, target.Days);
			target.Summary = ObstacleLightSummary.From(target.Days);

			//Order by the solar load the orientation actually carries, not by its area. A wall
			//imported as a slab has two faces of identical area, so area leaves the choice of
			//"the dominant facade" to a tie-break - and it picked the shaded north side of a
			//planted south wall. Total open-sky energy is what makes a facade matter thermally.
			Array.Sort(target.Surfaces, (x, y) =>
				(y.Summary.TotalOpenSky_Wh_m2 * y.Area_m2).CompareTo(x.Summary.TotalOpenSky_Wh_m2 * x.Area_m2));
		}

		FlushInto(ref SelectionOpen, SelectionDays);
		SelectionSummary = ObstacleLightSummary.From(SelectionDays);
	}

	static void FlushInto(ref Accumulator open, List<ObstacleLightDay> days)
	{
		if (!open.HasData)
			return;
		days.Add(Close(in open));
		open = default;
	}
}
