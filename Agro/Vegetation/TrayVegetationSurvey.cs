using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using AgentsSystem;

namespace Agro.Vegetation;

/// <summary>
/// The foliage of one tray over one reporting period, as a microclimate model describes vegetation:
/// one-sided leaf area, the mean sine of the leaf tilt, the mean equivalent leaf diameter and the
/// thickness of the vegetation layer. Means over the period's daily surveys; the leaf angle and the
/// diameter are weighted by leaf area.
/// </summary>
public readonly record struct VegetationPeriod(
	/// <summary>Daily surveys that contributed. Zero means no reading.</summary>
	int Days,
	/// <summary>One-sided leaf area of every blade rooted in the tray, m², mean over the days.</summary>
	float LeafArea_m2,
	/// <summary>Area-weighted mean of sin(tilt from the horizontal) = sqrt(1 - n_y²) per blade:
	/// 0 for horizontal foliage, 1 for vertical.</summary>
	float LeafAngleSine,
	/// <summary>Area-weighted mean of 2 sqrt(A / pi), the diameter of the disc with one blade's area,
	/// metres. Whole blades, not mesh triangles.</summary>
	float LeafDiameter_m,
	/// <summary>Thickness of the vegetation layer measured outward from the tray's back wall,
	/// metres: the farthest any leaf reaches, as a bounding box would give it. Mean over the days.</summary>
	float Thickness_m,
	/// <summary>Mean number of blades.</summary>
	float Leaves,
	/// <summary>Largest daily leaf area of the period, m².</summary>
	float LeafAreaMax_m2,
	/// <summary>Leaf area on the last surveyed day of the period, m².</summary>
	float LeafAreaFinal_m2,
	/// <summary>Largest daily thickness of the period, metres.</summary>
	float ThicknessMax_m,
	/// <summary>One-sided area of blades that died of drought and still hang on the plant, m², mean
	/// over the days. They shade but no longer live; the geometric fields cover living blades only.</summary>
	float DeadLeafArea_m2,
	// Whole ordinary foliage blades removed during the period, summed rather than averaged.
	// Includes age replacement and stress shedding; excludes floral bracts.
	long LeavesShed = 0)
{
	public bool IsEmpty => Days == 0 && LeavesShed == 0;
}

/// <summary>Weekly, annual and whole-run readings of one tray, dense and aligned with
/// <see cref="PeriodCalendar"/>.</summary>
public sealed class VegetationSeries
{
	public VegetationPeriod[] Weeks { get; }
	public VegetationPeriod[] Years { get; }
	public VegetationPeriod Total { get; }

	internal VegetationSeries(VegetationPeriod[] weeks, VegetationPeriod[] years, VegetationPeriod total)
	{
		Weeks = weeks;
		Years = years;
		Total = total;
	}
}

/// <summary>
/// Surveys the foliage once per local day and aggregates it per tray and per period.
/// </summary>
/// <remarks>
/// <para>
/// Geometric, not ray traced, and independent of the light model: a walk over every leaf organ,
/// reading its outline area (<see cref="LaminaArea"/>), the normal of its lamina and the reach of
/// its corners. The whole facade costs a few milliseconds a day. Nothing here feeds back into the
/// simulation, and the per-plant partial sums are merged in formation order, so the result is as
/// reproducible as the run.
/// </para>
/// <para>
/// A plant belongs to the tray its soil sits in: <see cref="PlantFormation2.SoilIndex"/> names the
/// imported soil object, and the tray is that name without its <c>_S</c> section suffix - the one
/// convention the facade model follows. A plain box field is one tray named <c>field</c>.
/// </para>
/// <para>
/// The thickness is measured along the facade axis, the dominant normal of the tray meshes, pointing
/// away from the building. The meshes give the axis but not its sign - their winding cannot be
/// trusted - so the sign is settled on the first day there is any foliage, by which side of the
/// trays the leaves are on. Plants grow out of a tray, never into the wall. Without any tray mesh the
/// axis is up and the thickness is the canopy height above the seeding level.
/// </para>
/// <para>
/// Checkpointed with the light meter: the accumulators, the dates and the settled facade sign are
/// written, everything else is rebuilt by <see cref="Setup"/> from the same request, so a resumed
/// run continues the weeks from the original start date.
/// </para>
/// </remarks>
public sealed class TrayVegetationSurvey
{
	/// <summary>Everything the survey knows about one tray.</summary>
	public sealed class Tray
	{
		public string Name { get; internal init; } = string.Empty;
		/// <summary>Imported soil objects of this tray, in soil order.</summary>
		public List<string> Soils { get; } = [];
		/// <summary>Plants seeded in it, alive or not.</summary>
		public int Plants { get; internal set; }
		/// <summary>Plants per species name.</summary>
		public SortedDictionary<string, int> Species { get; } = new(StringComparer.Ordinal);
		/// <summary>True when an obstacle of the same name supplied the tray's geometry.</summary>
		public bool HasMesh { get; internal set; }
		/// <summary>Extent of the tray along the facade, metres.</summary>
		public float Width_m { get; internal set; }
		/// <summary>Vertical extent, metres.</summary>
		public float Height_m { get; internal set; }
		/// <summary>Extent along the facade axis, metres: how far the tray stands out from its back wall.</summary>
		public float Depth_m { get; internal set; }
		/// <summary>Centre of the tray's bounding box, world coordinates.</summary>
		public Vector3 Centroid { get; internal set; }
		/// <summary>Set by <see cref="Finish"/>.</summary>
		public VegetationSeries? Series { get; internal set; }
		/// <summary>The most recent daily survey of this tray, a one-day record. Live during the run.</summary>
		public VegetationPeriod LastDay { get; internal set; }

		internal readonly List<int> SoilIndices = [];
		/// <summary>Mesh vertices, or the seeding positions when there is no mesh.</summary>
		internal Vector3[] Points = [];
		/// <summary>Extent of <see cref="Points"/> along the unsigned facade axis.</summary>
		internal float AlongMin, AlongMax;
		internal Series Acc = null!;
	}

	/// <summary>One day's survey of one tray or of the whole facade.</summary>
	internal struct Sample
	{
		public double LeafArea, AreaSine, AreaDiameter, DeadLeafArea;
		public int Blades;
		public float Thickness;
	}

	/// <summary>Sums over the days of one period.</summary>
	internal struct Accumulator
	{
		public int Days;
		public double LeafArea, AreaSine, AreaDiameter, Blades, Thickness, DeadLeafArea;
		public float LeafAreaMax, LeafAreaFinal, ThicknessMax;
		public long LeavesShed;

		public void Fold(in Sample sample)
		{
			++Days;
			LeafArea += sample.LeafArea;
			AreaSine += sample.AreaSine;
			AreaDiameter += sample.AreaDiameter;
			Blades += sample.Blades;
			Thickness += sample.Thickness;
			DeadLeafArea += sample.DeadLeafArea;
			LeafAreaMax = MathF.Max(LeafAreaMax, (float)sample.LeafArea);
			LeafAreaFinal = (float)sample.LeafArea;
			ThicknessMax = MathF.Max(ThicknessMax, sample.Thickness);
		}

		/// <summary>Years are added in order, so the later surveyed year's final value wins.</summary>
		public void Add(in Accumulator other)
		{
			if (other.IsEmpty)
				return;
			Days += other.Days;
			LeafArea += other.LeafArea;
			AreaSine += other.AreaSine;
			AreaDiameter += other.AreaDiameter;
			Blades += other.Blades;
			Thickness += other.Thickness;
			DeadLeafArea += other.DeadLeafArea;
			LeafAreaMax = MathF.Max(LeafAreaMax, other.LeafAreaMax);
			if (other.Days > 0)
				LeafAreaFinal = other.LeafAreaFinal;
			ThicknessMax = MathF.Max(ThicknessMax, other.ThicknessMax);
			LeavesShed += other.LeavesShed;
		}

		public VegetationPeriod Close() => IsEmpty ? default : new VegetationPeriod(
			Days,
			Days > 0 ? (float)(LeafArea / Days) : 0f,
			LeafArea > 0.0 ? (float)(AreaSine / LeafArea) : 0f,
			LeafArea > 0.0 ? (float)(AreaDiameter / LeafArea) : 0f,
			Days > 0 ? (float)(Thickness / Days) : 0f,
			Days > 0 ? (float)(Blades / Days) : 0f,
			LeafAreaMax,
			LeafAreaFinal,
			ThicknessMax,
			Days > 0 ? (float)(DeadLeafArea / Days) : 0f,
			LeavesShed);

		public bool IsEmpty => Days == 0 && LeafArea == 0.0 && AreaSine == 0.0 && AreaDiameter == 0.0 && Blades == 0.0
			&& Thickness == 0.0 && LeafAreaMax == 0f && LeafAreaFinal == 0f && ThicknessMax == 0f && DeadLeafArea == 0.0 && LeavesShed == 0;

		public void Write(BinaryWriter writer)
		{
			writer.Write(Days);
			writer.Write(LeafArea);
			writer.Write(AreaSine);
			writer.Write(AreaDiameter);
			writer.Write(Blades);
			writer.Write(Thickness);
			writer.Write(LeafAreaMax);
			writer.Write(LeafAreaFinal);
			writer.Write(ThicknessMax);
			writer.Write(DeadLeafArea);
			writer.Write(LeavesShed);
		}

		public static Accumulator Read(BinaryReader reader) => new()
		{
			Days = reader.ReadInt32(),
			LeafArea = reader.ReadDouble(),
			AreaSine = reader.ReadDouble(),
			AreaDiameter = reader.ReadDouble(),
			Blades = reader.ReadDouble(),
			Thickness = reader.ReadDouble(),
			LeafAreaMax = reader.ReadSingle(),
			LeafAreaFinal = reader.ReadSingle(),
			ThicknessMax = reader.ReadSingle(),
			DeadLeafArea = reader.ReadDouble(),
			LeavesShed = reader.ReadInt64(),
		};
	}

	/// <summary>Weekly and annual accumulators of one tray; the total is the sum of the years.</summary>
	internal sealed class Series
	{
		readonly Accumulator[] Weeks;
		readonly Accumulator[] Years;

		public Series(PeriodCalendar calendar)
		{
			Weeks = new Accumulator[calendar.WeekCount];
			Years = new Accumulator[calendar.YearCount];
		}

		public void Fold(int week, int year, in Sample sample)
		{
			Weeks[Math.Min(week, Weeks.Length - 1)].Fold(in sample);
			Years[Math.Min(year, Years.Length - 1)].Fold(in sample);
		}

		public void RecordShedding(int week, int year, long count)
		{
			Weeks[Math.Min(week, Weeks.Length - 1)].LeavesShed += count;
			Years[Math.Min(year, Years.Length - 1)].LeavesShed += count;
		}

		public VegetationSeries Close()
		{
			var weeks = new VegetationPeriod[Weeks.Length];
			for (int i = 0; i < weeks.Length; ++i)
				weeks[i] = Weeks[i].Close();
			var years = new VegetationPeriod[Years.Length];
			var total = default(Accumulator);
			for (int i = 0; i < years.Length; ++i)
			{
				years[i] = Years[i].Close();
				total.Add(in Years[i]);
			}
			return new VegetationSeries(weeks, years, total.Close());
		}

		/// <summary>Sparse: the array length, then the index and record of every non-empty bucket, so a
		/// checkpoint from a horizon of another length can be laid onto this one.</summary>
		public void Write(BinaryWriter writer)
		{
			WriteBuckets(writer, Weeks);
			WriteBuckets(writer, Years);
		}

		public void Read(BinaryReader reader)
		{
			ReadBuckets(reader, Weeks, "week");
			ReadBuckets(reader, Years, "year");
		}

		static void WriteBuckets(BinaryWriter writer, Accumulator[] buckets)
		{
			writer.Write(buckets.Length);
			var filled = 0;
			foreach (var bucket in buckets)
				if (!bucket.IsEmpty)
					++filled;
			writer.Write(filled);
			for (int i = 0; i < buckets.Length; ++i)
				if (!buckets[i].IsEmpty)
				{
					writer.Write(i);
					buckets[i].Write(writer);
				}
		}

		static void ReadBuckets(BinaryReader reader, Accumulator[] buckets, string unit)
		{
			var length = reader.ReadInt32();
			var filled = reader.ReadInt32();
			Array.Clear(buckets);
			for (int k = 0; k < filled; ++k)
			{
				var index = reader.ReadInt32();
				var record = Accumulator.Read(reader);
				if ((uint)index >= (uint)length)
					throw new InvalidDataException($"A {unit} record at index {index} lies outside the {length} {unit}s the checkpoint declares.");
				if (index >= buckets.Length)
					throw new InvalidDataException($"The checkpoint holds a {unit} record at index {index} and this run's horizon has only {buckets.Length} {unit}s. A resumed run must reach every date already surveyed.");
				buckets[index] = record;
			}
		}
	}

	/// <summary>Sums of one plant's leaves for one day, before they are merged into its tray.</summary>
	struct Partial
	{
		public double LeafArea, AreaSine, AreaDiameter, DeadLeafArea;
		public int Blades;
		/// <summary>Extent of the leaf corners along the unsigned facade axis.</summary>
		public float AlongMax, AlongMin;
		/// <summary>Sum of area times leaf position along the axis, which decides the axis' sign.</summary>
		public double AreaAlong;
	}

	const int MaxBladeSlices = 16;
	/// <summary>An axis this close to vertical makes the tray a planter rather than a facade
	/// element, and up is then the only sensible outward direction.</summary>
	const float VerticalCosine = 0.7f;

	bool Ready;
	int[] TrayOfFormation = [];
	long[] RecordedShedTotals = [];
	Tray[] TrayArray = [];
	Partial[] Partials = [];
	Sample[] Samples = [];
	Series FacadeAcc = null!;
	PeriodCalendar Calendar = null!;
	Vector3 Axis = Vector3.UnitY;
	bool SignKnown;
	bool Dirty = true;
	DateTime? LastDate;

	public IReadOnlyList<Tray> Trays => TrayArray;
	/// <summary>True when the terrain is an imported tray system, i.e. when there is a tray to
	/// report rather than one plain field.</summary>
	public bool Imported { get; private set; }
	/// <summary>Plants seeded over all trays.</summary>
	public int Plants { get; private set; }
	/// <summary>Unit normal of the facade pointing away from the building, once known; up when the
	/// trays stand on the ground or there is no tray mesh at all.</summary>
	public Vector3 Outward { get; private set; } = Vector3.UnitY;
	/// <summary>Whether <see cref="Outward"/> has been settled. False until the first day with foliage.</summary>
	public bool OutwardKnown => SignKnown;
	/// <summary>The whole facade as one patch: leaf areas summed, angle and diameter area-weighted
	/// over every tray, thickness the largest of any tray. Set by <see cref="Finish"/>.</summary>
	public VegetationSeries? FacadeSeries { get; private set; }
	/// <summary>Outline area factors per species present, by species name, for the document.</summary>
	public SortedDictionary<string, float[]> SpeciesFactors { get; } = new(StringComparer.Ordinal);
	public DateTime? FirstDate { get; private set; }
	public DateTime? LastSurveyedDate => LastDate;
	public int SurveyedDays { get; private set; }

	/// <summary>Simulation callback. Surveys geometry on the first step of every local date and
	/// records physical shedding every step in the period containing that step's midpoint.</summary>
	public void Tick(SimulationWorld world, uint timestep, IList<IFormation> formations, IList<IObstacle> obstacles)
	{
		if (world is not AgroWorld agro)
			return;
		var local = TimeZoneInfo.ConvertTimeFromUtc(agro.GetTime(timestep), agro.TimeZone);
		var date = PeriodCalendar.StepDate(local, agro.HoursPerTick);
		var formationList = formations as IReadOnlyList<IFormation> ?? [.. formations];
		if (!Ready)
			Setup(agro, formationList, obstacles as IReadOnlyList<IObstacle> ?? [.. obstacles]);
		if (LastDate != date)
		{
			Survey(formationList, date);
			FirstDate ??= date;
			LastDate = date;
			++SurveyedDays;
			Dirty = true;
		}
		RecordShedding(formationList, date);
	}

	void RecordShedding(IReadOnlyList<IFormation> formations, DateTime date)
	{
		var week = Calendar.WeekIndex(date);
		var year = Calendar.YearIndex(date);
		for (int f = 0; f < TrayOfFormation.Length; ++f)
		{
			var t = TrayOfFormation[f];
			if (t < 0 || formations[f] is not PlantFormation2 plant)
				continue;
			var total = plant.AG.FoliageBladesShed;
			var count = total - RecordedShedTotals[f];
			RecordedShedTotals[f] = total;
			if (count <= 0)
				continue;
			TrayArray[t].Acc.RecordShedding(week, year, count);
			FacadeAcc.RecordShedding(week, year, count);
			TrayArray[t].LastDay = TrayArray[t].LastDay with { LeavesShed = TrayArray[t].LastDay.LeavesShed + count };
			Dirty = true;
		}
	}

	/// <summary>The tray an imported soil belongs to: the name without its last <c>_S</c> part.</summary>
	public static string TrayNameOf(string soilName)
	{
		var cut = soilName.LastIndexOf('_');
		if (cut <= 0 || cut + 1 >= soilName.Length)
			return soilName;
		var last = soilName.AsSpan(cut + 1);
		return last[0] == 'S' && last.Length > 1 && char.IsDigit(last[1]) ? soilName[..cut] : soilName;
	}

	void Setup(AgroWorld world, IReadOnlyList<IFormation> formations, IReadOnlyList<IObstacle> obstacles)
	{
		Ready = true;
		Calendar = world.Periods;
		var list = world.Soil as SoilFormationsList;
		Imported = list != null;

		var trays = new List<Tray>();
		var byName = new Dictionary<string, int>(StringComparer.Ordinal);
		var seedPoints = new List<List<Vector3>>();
		TrayOfFormation = new int[formations.Count];
		RecordedShedTotals = new long[formations.Count];
		Array.Fill(TrayOfFormation, -1);
		for (int f = 0; f < formations.Count; ++f)
		{
			if (formations[f] is not PlantFormation2 plant)
				continue;
			var soilName = list?.SoilName(plant.SoilIndex) ?? "field";
			var trayName = list != null ? TrayNameOf(soilName) : soilName;
			if (!byName.TryGetValue(trayName, out var index))
			{
				index = trays.Count;
				byName.Add(trayName, index);
				trays.Add(new Tray { Name = trayName });
				seedPoints.Add([]);
			}
			var tray = trays[index];
			if (!tray.SoilIndices.Contains(plant.SoilIndex))
			{
				tray.SoilIndices.Add(plant.SoilIndex);
				tray.Soils.Add(soilName);
			}
			++tray.Plants;
			++Plants;
			var species = plant.Parameters;
			tray.Species[species.Name] = tray.Species.TryGetValue(species.Name, out var n) ? n + 1 : 1;
			SpeciesFactors.TryAdd(species.Name, LaminaArea.OutlineFactors(species));
			seedPoints[index].Add(plant.Position + plant.Soil.GetFieldOrigin(plant.SoilIndex));
			TrayOfFormation[f] = index;
		}

		//The tray meshes: the facade axis from all of them, the back plane and extents per tray.
		var meshes = new Dictionary<string, IObstacle>(StringComparer.Ordinal);
		foreach (var obstacle in obstacles)
			meshes.TryAdd(obstacle.Name, obstacle);
		var xx = 0f; var yy = 0f; var zz = 0f; var xy = 0f; var xz = 0f; var yz = 0f;
		var vertices = new List<Vector3>();
		var indices = new List<int>();
		var anyMesh = false;
		for (int t = 0; t < trays.Count; ++t)
		{
			var tray = trays[t];
			if (meshes.TryGetValue(tray.Name, out var mesh))
			{
				vertices.Clear();
				indices.Clear();
				mesh.AppendTriangles(vertices, indices);
				if (vertices.Count >= 3)
				{
					for (int i = 0; i + 2 < indices.Count; i += 3)
					{
						var a = vertices[indices[i]];
						var cross = Vector3.Cross(vertices[indices[i + 1]] - a, vertices[indices[i + 2]] - a);
						var area2 = cross.Length();
						if (area2 <= 1e-12f)
							continue;
						var normal = cross / area2;
						var w = 0.5f * area2;
						xx += w * normal.X * normal.X; yy += w * normal.Y * normal.Y; zz += w * normal.Z * normal.Z;
						xy += w * normal.X * normal.Y; xz += w * normal.X * normal.Z; yz += w * normal.Y * normal.Z;
					}
					tray.Points = [.. vertices];
					tray.HasMesh = true;
					anyMesh = true;
				}
			}
			if (!tray.HasMesh)
				tray.Points = [.. seedPoints[t]];
		}

		var axis = anyMesh ? ObstacleLightMeter.DominantAxis(xx, yy, zz, xy, xz, yz) : Vector3.Zero;
		if (axis.LengthSquared() <= 1e-12f)
		{
			Axis = Vector3.UnitY;
			SettleSign(Vector3.UnitY);
		}
		else if (MathF.Abs(axis.Y) >= VerticalCosine)
		{
			Axis = axis;
			SettleSign(axis.Y >= 0f ? axis : -axis);
		}
		else
			Axis = axis;

		var across = MathF.Abs(Axis.Y) < 0.99f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitY, Axis)) : Vector3.UnitX;
		foreach (var tray in trays)
		{
			var min = new Vector3(float.MaxValue);
			var max = new Vector3(float.MinValue);
			float alongMin = float.MaxValue, alongMax = float.MinValue, acrossMin = float.MaxValue, acrossMax = float.MinValue;
			foreach (var p in tray.Points)
			{
				min = Vector3.Min(min, p);
				max = Vector3.Max(max, p);
				var along = Vector3.Dot(p, Axis);
				alongMin = MathF.Min(alongMin, along);
				alongMax = MathF.Max(alongMax, along);
				var side = Vector3.Dot(p, across);
				acrossMin = MathF.Min(acrossMin, side);
				acrossMax = MathF.Max(acrossMax, side);
			}
			if (tray.Points.Length == 0)
			{
				min = max = Vector3.Zero;
				alongMin = alongMax = acrossMin = acrossMax = 0f;
			}
			tray.AlongMin = alongMin;
			tray.AlongMax = alongMax;
			tray.Centroid = (min + max) * 0.5f;
			tray.Width_m = tray.HasMesh ? acrossMax - acrossMin : 0f;
			tray.Height_m = tray.HasMesh ? max.Y - min.Y : 0f;
			tray.Depth_m = tray.HasMesh ? alongMax - alongMin : 0f;
			tray.Acc = new Series(Calendar);
		}

		TrayArray = [.. trays];
		Partials = new Partial[formations.Count];
		Samples = new Sample[trays.Count];
		FacadeAcc = new Series(Calendar);
	}

	void SettleSign(Vector3 outward)
	{
		Outward = outward;
		SignKnown = true;
	}

	void Survey(IReadOnlyList<IFormation> formations, DateTime date)
	{
		if (Partials.Length < formations.Count)
			Array.Resize(ref Partials, formations.Count);
		var axis = Axis;
		Parallel.For(0, formations.Count, f =>
		{
			Partials[f] = default;
			if (f < TrayOfFormation.Length && TrayOfFormation[f] >= 0 && formations[f] is PlantFormation2 plant && plant.AG.Alive)
				Partials[f] = MeasurePlant(plant, axis);
		});

		//The sign of the facade axis, decided by where the foliage is relative to the trays.
		if (!SignKnown)
		{
			var vote = 0.0;
			var area = 0.0;
			for (int f = 0; f < formations.Count; ++f)
			{
				var tray = TrayOfFormation.Length > f ? TrayOfFormation[f] : -1;
				if (tray < 0 || Partials[f].LeafArea <= 0.0)
					continue;
				var centre = 0.5f * (TrayArray[tray].AlongMin + TrayArray[tray].AlongMax);
				vote += Partials[f].AreaAlong - Partials[f].LeafArea * centre;
				area += Partials[f].LeafArea;
			}
			if (area > 0.0)
				SettleSign(vote >= 0.0 ? axis : -axis);
		}
		var positive = Vector3.Dot(Outward, axis) >= 0f;

		Array.Clear(Samples);
		for (int f = 0; f < formations.Count; ++f)
		{
			var t = TrayOfFormation.Length > f ? TrayOfFormation[f] : -1;
			if (t < 0)
				continue;
			ref readonly var partial = ref Partials[f];
			ref var sample = ref Samples[t];
			sample.LeafArea += partial.LeafArea;
			sample.AreaSine += partial.AreaSine;
			sample.AreaDiameter += partial.AreaDiameter;
			sample.DeadLeafArea += partial.DeadLeafArea;
			sample.Blades += partial.Blades;
			if (partial.Blades > 0 && SignKnown)
			{
				var tray = TrayArray[t];
				var reach = positive ? partial.AlongMax - tray.AlongMin : tray.AlongMax - partial.AlongMin;
				sample.Thickness = MathF.Max(sample.Thickness, MathF.Max(0f, reach));
			}
		}

		var week = Calendar.WeekIndex(date);
		var year = Calendar.YearIndex(date);
		var facade = default(Sample);
		for (int t = 0; t < TrayArray.Length; ++t)
		{
			TrayArray[t].Acc.Fold(week, year, in Samples[t]);
			var day = default(Accumulator);
			day.Fold(in Samples[t]);
			TrayArray[t].LastDay = day.Close();
			facade.LeafArea += Samples[t].LeafArea;
			facade.AreaSine += Samples[t].AreaSine;
			facade.AreaDiameter += Samples[t].AreaDiameter;
			facade.DeadLeafArea += Samples[t].DeadLeafArea;
			facade.Blades += Samples[t].Blades;
			facade.Thickness = MathF.Max(facade.Thickness, Samples[t].Thickness);
		}
		FacadeAcc.Fold(week, year, in facade);
	}

	static Partial MeasurePlant(PlantFormation2 plant, Vector3 axis)
	{
		var ag = plant.AG;
		var factors = LaminaArea.OutlineFactors(plant.Parameters);
		var segmented = factors.Length > 1;
		Dictionary<int, double>? blades = segmented ? [] : null;
		var result = new Partial { AlongMax = float.MinValue, AlongMin = float.MaxValue };

		var count = ag.Count;
		var timestep = plant.World.Timestep;
		var droughtResponse = plant.DroughtResponse;
		for (int i = 0; i < count; ++i)
		{
			if (ag.GetOrgan(i) != OrganTypes.Leaf)
				continue;
			var dead = droughtResponse && ag.GetAgent(i).IsDeadAttached(timestep);
			var length = ag.GetLength(i);
			var halfWidth = ag.GetBaseRadius(i);
			if (!(length > 0f) || !(halfWidth > 0f))
				continue;

			//A segmented blade is a chain of leaf organs; its slice index is its depth in the chain
			//and the chain's first organ identifies the blade.
			var slice = 0;
			var root = i;
			if (segmented)
				for (var p = ag.GetParent(i); p >= 0 && slice < MaxBladeSlices && ag.GetOrgan(p) == OrganTypes.Leaf; p = ag.GetParent(p))
				{
					++slice;
					root = p;
				}
			var area = (double)factors[Math.Min(slice, factors.Length - 1)] * length * 2f * halfWidth;
			if (dead)
			{
				result.DeadLeafArea += area;
				continue;
			}

			var orientation = ag.GetDirection(i);
			var normal = Vector3.Transform(Vector3.UnitY, orientation);
			var sine = MathF.Sqrt(MathF.Max(0f, 1f - normal.Y * normal.Y));
			result.LeafArea += area;
			result.AreaSine += area * sine;

			if (segmented)
				blades![root] = blades.TryGetValue(root, out var sum) ? sum + area : area;
			else
			{
				++result.Blades;
				result.AreaDiameter += area * 2.0 * Math.Sqrt(area / Math.PI);
			}

			//Reach along the facade axis: the four corners of the rectangle the blade stands in.
			var origin = ag.GetBaseCenterWorld(i) + ag.GetBaseOffset(i);
			var along = Vector3.Transform(Vector3.UnitX, orientation) * length;
			var across = Vector3.Transform(Vector3.UnitZ, orientation) * halfWidth;
			var baseAlong = Vector3.Dot(origin, axis);
			var tipAlong = baseAlong + Vector3.Dot(along, axis);
			var acrossAlong = MathF.Abs(Vector3.Dot(across, axis));
			result.AlongMax = MathF.Max(result.AlongMax, MathF.Max(baseAlong, tipAlong) + acrossAlong);
			result.AlongMin = MathF.Min(result.AlongMin, MathF.Min(baseAlong, tipAlong) - acrossAlong);
			result.AreaAlong += area * (baseAlong + 0.5 * (tipAlong - baseAlong));
		}

		if (blades != null)
			foreach (var bladeArea in blades.Values)
			{
				++result.Blades;
				result.AreaDiameter += bladeArea * 2.0 * Math.Sqrt(bladeArea / Math.PI);
			}
		return result;
	}

	/// <summary>
	/// Writes what a resumed run cannot rebuild: every tray's accumulators and latest day, the facade's,
	/// the settled outward direction and the dates. Trays are keyed by name. Everything else -
	/// trays, their geometry, the facade axis, the species factors - is a function of the request
	/// and is rebuilt by <see cref="Setup"/>.
	/// </summary>
	internal void WriteState(BinaryWriter writer)
	{
		writer.Write(Ready);
		if (!Ready)
			return;
		writer.Write(TrayArray.Length);
		foreach (var tray in TrayArray)
		{
			writer.Write(tray.Name);
			tray.Acc.Write(writer);
			WritePeriod(writer, tray.LastDay);
		}
		FacadeAcc.Write(writer);
		writer.Write(SignKnown);
		writer.Write(Outward.X);
		writer.Write(Outward.Y);
		writer.Write(Outward.Z);
		ObstacleLightMeter.WriteDate(writer, FirstDate);
		ObstacleLightMeter.WriteDate(writer, LastDate);
		writer.Write(SurveyedDays);
		writer.Write(RecordedShedTotals.Length);
		foreach (var count in RecordedShedTotals)
			writer.Write(count);
	}

	/// <summary>Restores what <see cref="WriteState"/> wrote onto a world rebuilt from the same
	/// request, running <see cref="Setup"/> first so the trays exist. Refuses a tray set that differs.</summary>
	internal void ReadState(BinaryReader reader, AgroWorld world)
	{
		if (!reader.ReadBoolean())
			return; //checkpointed before the first step; the first one sets up as a fresh run would
		if (!Ready)
			Setup(world, world.FormationList, world.ObstacleList);
		var count = reader.ReadInt32();
		if (count != TrayArray.Length)
			throw new InvalidDataException(
				$"The checkpoint surveys {count} trays and this run has {TrayArray.Length}. The field model has changed since the run was saved.");
		var byName = new Dictionary<string, Tray>(StringComparer.Ordinal);
		foreach (var tray in TrayArray)
			byName[tray.Name] = tray;
		for (int t = 0; t < count; ++t)
		{
			var name = reader.ReadString();
			if (!byName.TryGetValue(name, out var tray))
				throw new InvalidDataException($"The checkpoint surveys a tray \"{name}\" that this run does not have. The field model has changed since the run was saved.");
			tray.Acc.Read(reader);
			tray.LastDay = ReadPeriod(reader);
		}
		FacadeAcc.Read(reader);
		var signKnown = reader.ReadBoolean();
		var outward = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
		//After Setup, which settles the sign itself for a vertical or absent axis; a facade's sign was
		//voted on the first day with foliage, which the resumed run cannot repeat.
		if (signKnown)
			SettleSign(outward);
		FirstDate = ObstacleLightMeter.ReadDate(reader);
		LastDate = ObstacleLightMeter.ReadDate(reader);
		SurveyedDays = reader.ReadInt32();
		var formations = reader.ReadInt32();
		if (formations != RecordedShedTotals.Length)
			throw new InvalidDataException("The checkpoint's foliage shedding counters belong to a different formation list.");
		for (int f = 0; f < formations; ++f)
			RecordedShedTotals[f] = reader.ReadInt64();
		Dirty = true;
	}

	static void WritePeriod(BinaryWriter writer, in VegetationPeriod period)
	{
		writer.Write(period.Days);
		writer.Write(period.LeafArea_m2);
		writer.Write(period.LeafAngleSine);
		writer.Write(period.LeafDiameter_m);
		writer.Write(period.Thickness_m);
		writer.Write(period.Leaves);
		writer.Write(period.LeafAreaMax_m2);
		writer.Write(period.LeafAreaFinal_m2);
		writer.Write(period.ThicknessMax_m);
		writer.Write(period.DeadLeafArea_m2);
		writer.Write(period.LeavesShed);
	}

	static VegetationPeriod ReadPeriod(BinaryReader reader) => new(
		reader.ReadInt32(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
		reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadInt64());

	/// <summary>Closes every tray and the facade. Idempotent; free when nothing was surveyed since.</summary>
	public void Finish()
	{
		if (!Dirty)
			return;
		Dirty = false;
		if (!Ready)
			return;
		foreach (var tray in TrayArray)
			tray.Series = tray.Acc.Close();
		FacadeSeries = FacadeAcc.Close();
	}
}
