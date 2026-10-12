using Utils;
using System;
using System.IO;
using System.Diagnostics;
using System.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using AgentsSystem;
using System.Text;
using System.Globalization;
using System.Collections;
using Innovative.SolarCalculator;
using GeoTimeZone;

namespace Agro;

[StructLayout(LayoutKind.Auto)]
public readonly struct WeatherStats
{
	/// <summary>
	/// Rainfall in gramm per m²
	/// </summary>
	public readonly float Precipitation;
	/// <summary>
	/// Factor of sky coverage where 0 means clear sky and 1 means fully covered
	/// </summary>
	public readonly float SkyCoverage;

	public WeatherStats(double skyCoverage, double precipitation_in_g_per_m2)
	{
		SkyCoverage = (float)skyCoverage;
		Precipitation = (float)precipitation_in_g_per_m2;
	}

	public WeatherStats(float skyCoverage, float precipitation_in_g_per_m2)
	{
		Precipitation = precipitation_in_g_per_m2;
		SkyCoverage = skyCoverage;
	}
}

public class AgroWorld : SimulationWorld
{
	public readonly ushort HoursPerTick = 1;
	public readonly byte TicksPerDay = 24;
	//public const int TotalHours = 24 * 365 * 10;
	//public readonly int TotalHours = 24 * 31 * 12;
	public readonly int TotalHours = 24 * 31 * 3; //cca 3 months

	public readonly byte StatsBlockLength = 24;

	//public static readonly Vector3 FieldSize = new(6f, 4f, 2f);
	//public const float FieldResolution = 0.1f;

	/// <summary>
	/// Metric size with X (right), Y (front), Depth (down)
	/// </summary>
	public Vector3 FieldSize = new(1f, 1f, 1f);  //TODO switch this (breaking change) so that y represents height again
	/// <summary>
	/// Target metric size of a cell (same in all directions)
	/// </summary>
	public readonly float FieldResolution = 0.5f;

	/// <summary>Default site: Vienna. These are what a request that says nothing gets.</summary>
	public const float DefaultLatitude = 48.208333f;
	public const float DefaultLongitude = 16.3725f;
	public const float DefaultAltitude = 188f; //meters above sea level
	public const float DefaultAzimuthBase = 0f;
	/// <summary>Most terrain models are exported facing forward, not backward (north).</summary>
	public const TerrainOrientation DefaultAzimuthOffset = TerrainOrientation.Forward;

	/// <summary>Geographic latitude of the world origin, degrees north.</summary>
	public readonly float Latitude = DefaultLatitude;
	/// <summary>Geographic longitude of the world origin, degrees east.</summary>
	public readonly float Longitude = DefaultLongitude;
	/// <summary>Height of the world origin above sea level, in metres.</summary>
	public readonly float Altitude = DefaultAltitude;
	/// <summary>
	/// Rotation of the scene about the up axis before the terrain's own facing is accounted for,
	/// in degrees. Zero puts the scene's +X axis due east; an increasing value turns the scene
	/// clockwise seen from above.
	/// </summary>
	public readonly float AzimuthBase = DefaultAzimuthBase;
	/// <summary>Which way the loaded terrain model faces. See <see cref="TerrainOrientation"/>.</summary>
	public readonly TerrainOrientation AzimuthOffset = DefaultAzimuthOffset;
	/// <summary>
	/// The azimuth the sun is actually computed against: <see cref="AzimuthBase"/> plus the
	/// compensation for <see cref="AzimuthOffset"/>, wrapped into [0, 360).
	/// </summary>
	/// <remarks>
	/// It rotates the SUN, not the geometry: the whole of the maths is subtracting it from the
	/// solar azimuth before the direction vector is built (see <see cref="SkyDome"/>), and
	/// nothing in the scene, the BVH or any export moves. The frontend mirrors this in
	/// ThreeFrontend/src/helpers/Sun.ts.
	/// </remarks>
	public readonly float Azimuth;

	public readonly TimeZoneInfo TimeZone = TimeZoneInfo.Local;

	public readonly IrradianceClient Irradiance;

	/// <summary>The collision phase: hard constraints against obstacles and soil, then foliage.</summary>
	public readonly Collisions.CollisionWorld Collisions = new();

	//clouds_coverage, precipitation
	WeatherStats[] Weather;
	BitArray Daylight;

	static readonly int[] DaysPerMonth = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

	public int TimestepsTotal()
	{
		var ticks = Math.DivRem(TotalHours, HoursPerTick, out var rem);
		return ticks + (rem == 0 ? 0 : 1);
	}

	public DateTime InitialTime = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Unspecified);

	public readonly bool VirtualRoots = false;

	/// <summary>Foliage-against-foliage collisions, within a plant and between neighbours. Obstacle
	/// and soil collisions are hard constraints and are not governed by this. Per world rather than
	/// a process-wide static, so two simulations running in one server cannot change each other's.</summary>
	public readonly bool SoftCollisions = true;
	public readonly bool GrowthLightGuidanceEnabled;
	public readonly float CollisionGeometryToleranceM;
	/// <summary>Which contact engine runs: the certified one in <see cref="Collisions.CollisionWorld"/>
	/// or the single-pass <see cref="Collisions.SoftContactEngine"/>. Fixed for the run and
	/// recorded in live checkpoints.</summary>
	public readonly ContactEngineKind ContactEngine;
	/// <summary>
	/// Whether births are staged as developmental transactions that the certified engine admits
	/// geometrically. The soft engine never rejects geometry, so its births go straight to Census;
	/// with collisions disabled staging only survives for the opt-in construction budget.
	/// </summary>
	public bool StagesDevelopment => !(global::Agro.Collisions.CollisionSettings.Enabled && ContactEngine == ContactEngineKind.Soft);
	/// <summary>Operational backend preference. Auto stays on CPU until the CUDA prototype
	/// has demonstrated its end-to-end performance gate on the deployment hardware.</summary>
	public readonly Collisions.CollisionComputeBackend CollisionBackend;

	/// <summary><c>AGRO_CONTACT_ENGINE=Certified|Soft</c> (or 0|1) applies only when the request omits the field.</summary>
	static ContactEngineKind ContactEngineFromEnvironment()
	{
		var value = Environment.GetEnvironmentVariable("AGRO_CONTACT_ENGINE");
		return !string.IsNullOrWhiteSpace(value) && Enum.TryParse<ContactEngineKind>(value.Trim(), ignoreCase: true, out var engine)
			&& Enum.IsDefined(engine) ? engine : ContactEngineKind.Certified;
	}

	public AgroWorld(SimulationRequest? settings = null) : base()
	{
		CollisionGeometryToleranceM = settings?.CollisionGeometryToleranceM ?? .0005f;
		if (!float.IsFinite(CollisionGeometryToleranceM) || CollisionGeometryToleranceM < 0f || CollisionGeometryToleranceM > .0005f)
			throw new ArgumentOutOfRangeException(nameof(SimulationRequest.CollisionGeometryToleranceM),
				"Collision geometry tolerance must be finite and between 0 and 0.0005 metres.");
		CollisionBackend = global::Agro.Collisions.CudaCollisionBackend.RequestedBackend();
		ContactEngine = settings?.ContactEngine ?? ContactEngineFromEnvironment();
		Deterministic = settings?.Deterministic ?? false;

		//The site has to be settled BEFORE the time-zone lookup below and before Init(), both of
		//which read it: the zone is derived from the coordinates, and Init() builds the daylight
		//bitmap from the latitude. Assigning it further down with the rest of the settings would
		//leave the clock on the default site while everything else moved.
		Latitude = settings?.Latitude ?? DefaultLatitude;
		Longitude = settings?.Longitude ?? DefaultLongitude;
		Altitude = settings?.Altitude ?? DefaultAltitude;
		//Validated rather than clamped, following CollisionGeometryToleranceM above: an
		//out-of-range coordinate makes the zone lookup return nonsense and the solar position
		//NaN, and a NaN sun is a run that quietly produces no light rather than an error.
		if (!float.IsFinite(Latitude) || Latitude < -90f || Latitude > 90f)
			throw new ArgumentOutOfRangeException(nameof(SimulationRequest.Latitude),
				"Latitude must be finite and between -90 and 90 degrees.");
		if (!float.IsFinite(Longitude) || Longitude < -180f || Longitude > 180f)
			throw new ArgumentOutOfRangeException(nameof(SimulationRequest.Longitude),
				"Longitude must be finite and between -180 and 180 degrees.");
		if (!float.IsFinite(Altitude))
			throw new ArgumentOutOfRangeException(nameof(SimulationRequest.Altitude),
				"Altitude must be finite.");
		//Wrapped rather than rejected: a rotation is periodic, so 370 degrees is a legitimate
		//way to say 10 and refusing it would be pedantry.
		var azimuthBase = settings?.AzimuthBase ?? DefaultAzimuthBase;
		AzimuthBase = float.IsFinite(azimuthBase) ? ((azimuthBase % 360f) + 360f) % 360f : DefaultAzimuthBase;
		AzimuthOffset = settings?.AzimuthOffset ?? DefaultAzimuthOffset;
		//The terrain's own facing is part of where the sun is, not part of where the site is, so
		//it is folded in here once rather than at each of the four places that read Azimuth.
		Azimuth = ((AzimuthBase + AzimuthOffset.OffsetDegrees()) % 360f + 360f) % 360f;

		var ianaTimeZone = TimeZoneLookup.GetTimeZone(Latitude, Longitude).Result;
		#if WINDOWS
		TimeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneConverter.TZConvert.IanaToWindows(ianaTimeZone));
		#else
		TimeZone = TimeZoneInfo.FindSystemTimeZoneById(ianaTimeZone);
		#endif

		if (settings != null)
		{
			if (settings?.HoursPerTick.HasValue ?? false)
			{
				HoursPerTick = (ushort)settings.HoursPerTick.Value;
				if (HoursPerTick < 24)
					HoursPerTick = HoursPerTick switch { <= 0 => 1, 5 => 4, 7 => 6, 9 or 10 => 8, 11 => 12, >12 and <= 18 => 12, >18 => 24, _ => HoursPerTick };
				else
					HoursPerTick = (ushort)(24 * (HoursPerTick / 24));

				StatsBlockLength = (byte)(23 / HoursPerTick + 1);

				TicksPerDay = (byte)(HoursPerTick >= 24 ? 1 : 24 / HoursPerTick);
			}

			if (settings?.TotalHours.HasValue ?? false)
				TotalHours = settings.TotalHours.Value;

			if (settings?.FieldResolution.HasValue ?? false)
				FieldResolution = settings.FieldResolution.Value;

			if (settings?.FieldSize.HasValue ?? false)
				FieldSize = settings.FieldSize.Value;

			if (settings?.Seed.HasValue ?? false)
				InitRNG(settings.Seed.Value);

			if (settings?.SoftCollisions.HasValue ?? false)
				SoftCollisions = settings.SoftCollisions.Value;

			if (settings?.InitialDate.HasValue ?? false)
				InitialTime = new(settings.InitialDate.Value.Year, settings.InitialDate.Value.Month, settings.InitialDate.Value.Day, 0, 0, 0, DateTimeKind.Unspecified);
		}

		GrowthLightGuidanceEnabled = (settings?.GrowthLightGuidanceEnabled ?? true) && (settings?.RenderMode ?? 0) != 0;
		Irradiance = new IrradianceClient(Latitude, Longitude, settings?.RenderMode ?? 0, settings?.SamplesPerPixel ?? 512,
			settings?.LightSamples ?? 16, settings?.LeafAreaIndexObstacles, settings?.LeafAreaIndexSensors ?? 32768,
			settings?.LeafAreaIndexDepth ?? 3f, settings?.LeafAreaIndexBandHeight ?? 0.5f, Azimuth, Altitude);

		//The external renderers live in their own repository (agroeco-mts3) and compute the sun
		//from the La/Lo headers and the tick time. Az is now sent alongside, but until their
		//sun is corrected by it they will light the scene as if the scene were not rotated -
		//while the CPU solver, the growth guidance and the viewport all use the rotated sun.
		//Said out loud rather than refused: the combination becomes correct the moment the
		//renderer reads the header, and refusing it would block that.
		if (Azimuth != 0f && ((settings?.RenderMode ?? 0) == 1 || (settings?.RenderMode ?? 0) == 2))
			Console.WriteLine($"WARNING: Azimuth is {Azimuth} deg but RenderMode is " +
				$"{settings?.RenderMode}. The external renderer computes its own sun and will " +
				"ignore the rotation unless it reads the Az header, so its irradiance will not " +
				"match the internal solver's.");

		Init();
	}

	public void Init()
	{
		var sunnyDays = new float[]{3.2f, 3.3f, 5.8f, 7.6f, 8.2f, 8.6f, 11.8f, 12.6f, 10.5f, 9.7f, 4.3f, 4.1f}; //clouds factor 0 to 0.25
		var cloudyDays = new float[]{12.3f, 12.6f, 14.6f, 15.3f, 17.2f, 16.6f, 15.1f, 13.7f, 13.2f, 13.6f, 12.4f, 12.1f}; //clouds factor 0.25 to 0.5
		var dullDays = new float[]{15.5f, 12.4f, 10.6f, 7.1f, 5.6f, 4.8f, 4f, 4.6f, 6.3f, 7.7f, 13.3f, 14.8f}; //clouds factor 0.5 to 1

		//dry, 0-2mm, 2-5mm, 5-10mm, 10-20mm, 20-50mm, 50-100mm
		var dailyPrecipitation = new[] {
			new float[]{21.6f, 4.9f, 2.8f, 1.2f, 0.6f, 0f, 0f},
			[19.1f, 5f, 2f, 1.4f, 0.5f, 0.1f, 0f],
			[21.4f, 5.1f, 2.5f, 1.3f, 0.5f, 0.2f, 0f],
			[21f, 4.9f, 2.4f, 1f, 0.6f, 0.1f, 0f],
			[20.2f, 5.7f, 2.6f, 1.5f, 0.7f, 0.3f, 0f],
			[19f, 5.5f, 2.9f, 1.4f, 0.8f, 0.4f, 0f],
			[20.3f, 5.8f, 2.2f, 1.4f, 1.1f, 0.1f, 0.1f],
			[22.3f, 4.7f, 2f, 1f, 0.7f, 0.3f, 0f],
			[22.1f, 3.7f, 2f, 1f, 0.7f, 0.3f, 0.1f],
			[24f, 3.5f, 1.8f, 1.1f, 0.5f, 0.1f, 0f],
			[20.8f, 4.5f, 2.6f, 1.4f, 0.5f, 0.1f, 0f],
			[20.9f, 5.5f, 2.6f, 1.3f, 0.5f, 0.1f, 0f]
		};

		var precipitationMM = new []{29, 28, 33, 30, 43, 46, 44, 37, 42, 26, 31, 31};

		// var maxTemperature_10_5Days = new []{0.7, 0.8, 0.1, 0, 0, 0, 0, 0, 0, 0, 0, 0.4};
		// var maxTemperature_5_0Days = new []{7.5, 4.7, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0.7, 5.2};
		// var maxTemperature0_5Days = new[]{11.6, 8.4, 4.5, 0.3, 0, 0, 0, 0, 0, 0.1, 5.2, 12.8};
		// var maxTemperature5_10Days = new[]{8.9, 8.7, 9.6, 2.9, 0.1, 0, 0, 0, 0, 2.8, 10.9, 10.5};
		// var maxTemperature10_15Days = new []{2.2, 4.9, 10.4, 9.7, 3, 0.5, 0, 0.2, 2.2, 10, 10, 1.9};
		// var maxTemperature15_20Days = new []{0, 0.7, 4.8, 10.9, 10.9, 6.8, 2.8, 2.5, 9.2, 9.9, 3, 0.2};
		// var maxTemperature20_25Days = new []{0, 0, 0.6, 5.5, 12, 11.8, 9.9, 9.3, 11.4, 7.2, 0.2, 0};
		// var maxTemperature25_30Days = new []{0, 0, 0, 0.7, 4.7, 8.3, 11.9, 12.4, 6.6, 0.9, 0, 0};
		// var maxTemperature30_35Days = new []{0, 0, 0, 0, 0.3, 2.5, 5.8, 5.8, 0.5, 0, 0, 0};
		// var maxTemperature35_40Days = new []{0, 0, 0, 0, 0, 0.1, 0.5, 0.9, 0, 0, 0, 0};

		// var avgHotDayTemperature = new float[]{10, 12, 19, 24, 28, 32, 34, 34, 29, 25, 17, 11};
		// var avgHighTemperature = new float[]{3, 5, 10, 16, 21, 24, 26, 26, 22, 16, 9, 4};
		// var avgLowTemperature = new float []{-2, -2, 1, 5, 9, 12, 14, 14, 11, 6, 2, -1};
		// var avgColdNightTemperature = new float[]{-10, -8, -5, -2, 2 ,5, 9, 8, 5, -2, -4, -8};

		var tsTotal = TimestepsTotal();
		Weather = new WeatherStats[tsTotal];
		var tsCounter = 0;
		// Weather planning must not advance the plant/soil random stream.  Its
		// draw count changes with the requested horizon, which otherwise makes a
		// fixed seed grow a different plant merely because it is simulated for
		// longer.
		var weatherRng = RNG.NextRNG();

		var month = 0;
		var timestepHours = 0;
		var timestepPrecipitation = 0.0;
		var timestepCoverage = 0.0;
		while(tsCounter < tsTotal)
		{
			var monthly = PlanCloudsSingleMonth(weatherRng, month + 1, DaysPerMonth[month], sunnyDays[month], cloudyDays[month], dullDays[month], precipitationMM[month], dailyPrecipitation[month]);
			for (var h = 0; h < monthly.Length && tsCounter < tsTotal; ++h)
			{
				timestepPrecipitation += monthly[h].Precipitation;
				timestepCoverage += monthly[h].SkyCoverage;
				++timestepHours;

				if (timestepHours != HoursPerTick)
					continue;

				Weather[tsCounter++] = new WeatherStats(timestepCoverage / timestepHours, timestepPrecipitation);
				timestepHours = 0;
				timestepPrecipitation = 0.0;
				timestepCoverage = 0.0;
			}

			month = month < 11 ? month + 1 : 0;
		}

		if (HoursPerTick < 24 || Math.Abs(Latitude) > 66)
		{
			Daylight = new BitArray(tsTotal);
			//TODO handle signed vs. unsigned
			for (int i = 0; i < tsTotal; ++i)
			{
				var t0 = GetTime((uint)i);
				var t1 = t0.AddHours(HoursPerTick);

				if (HoursPerTick >= 24 && Math.Abs(Latitude) < 66)
					Daylight.Set(i, true);
				else
				{
					var solar0 = new SolarTimes(t0, 0, Latitude, Longitude);
					var solar1 = new SolarTimes(t1, 0, Latitude, Longitude);
					if (!(t1 < solar0.DawnAstronomical || t0 > solar1.DuskAstronomical || (t0 > solar0.DuskAstronomical && t1 < solar1.DawnAstronomical)))
						Daylight.Set(i, true);
				}
			}
		}
	}

	/// <summary>
	/// Starts on 1 March = 0..0.25 (mar-may) spring, 0.25..0.5 (jun-aug) summer, 0.5..0.75 (sep-nov) autumn, 0.75..1 (dec-feb) winter
	/// </summary>
	public float Season { get; private set; }
	public override uint Timestep
	{
		get => base.Timestep;
		protected set
		{
			base.Timestep = value;
			var dayOfYear = GetTime(base.Timestep).DayOfYear - 1;
			Season = (dayOfYear >= 59 ? dayOfYear - 59 : 306 + dayOfYear) / 365f;
		}
	}

	sealed record CalendarOrigin(DateTime Local, DateTime Utc);
	CalendarOrigin? CachedCalendarOrigin;

	internal DateTime GetTime(uint timestep)
	{
		var initial = InitialTime;
		var origin = CachedCalendarOrigin;
		// InitialTime is public and can change between runs. Keep the source with
		// its conversion and publish both together for parallel plant readers.
		if (origin == null || origin.Local != initial || origin.Local.Kind != initial.Kind)
			CachedCalendarOrigin = origin = new(initial, TimeZoneInfo.ConvertTimeToUtc(initial, TimeZone));
		return origin.Utc + TimeSpan.FromHours(timestep * HoursPerTick);
	}
	///<summary>
	///Rainfall in the given timestep in gramm per m²
	///</summary>
	internal float GetWater(uint timestep) => Weather[timestep].Precipitation;
	/// <summary>
	/// Temperature in the given timesttep in degree Celsius
	/// </summary>
	internal float GetTemperature(uint timestep) => 20;
	internal float GetAmbientLight(uint timestep) => 1f - Weather[timestep].SkyCoverage;

	/// <summary>
	/// Cloud cover for every timestep of the run, 0 = clear .. 1 = fully overcast.
	/// </summary>
	/// <remarks>
	/// The whole series, not a per-step lookup, because the whole series exists before step 0
	/// does: <see cref="Init"/> plans it month by month at construction. Sending it once lets the
	/// viewport preview the sky of any step the playhead lands on - including steps whose frames
	/// were evicted and steps an offline export renders without ever touching the playhead -
	/// without adding anything to the per-frame format.
	/// </remarks>
	public float[] SkyCoverageSeries()
	{
		if (Weather == null) return [];
		var result = new float[Weather.Length];
		for (int i = 0; i < Weather.Length; ++i)
			result[i] = Weather[i].SkyCoverage;
		return result;
	}
	internal bool GetDaylight(uint timestep) => Daylight?.Get((int)timestep) ?? true;

	internal Pcg RNG = new(42);
    internal ISoilFormation Soil;

    // Soil transport must finish before roots inspect its water balances. The
    // generic parallel scheduler otherwise races soil writes with plant reads.
    // The inner per-column soil workers have joined before the plant workers start.
    protected override void TickParallel()
    {
        if (Soil == null)
        {
            base.TickParallel();
            return;
        }
        Soil.Tick(Timestep);
        var formations = FormationList;
        Parallel.For(0, formations.Count, i =>
        {
            var formation = formations[i];
            if (!ReferenceEquals(formation, Soil))
                formation.Tick(Timestep);
        });
    }

    // Soil receipts must enter plant inboxes before those inboxes are drained.
    // A second round still handles any messages produced by the plant round.
    public override void DeliverPostParallel()
    {
        if (Soil == null)
        {
            base.DeliverPostParallel();
            return;
        }
        var formations = FormationList;
        int delivered;
        do
        {
            delivered = 0;
            if (Soil.HasUndeliveredPost)
            {
                Soil.DeliverPost(Timestep);
                delivered = 1;
            }
            // A serial scan is cheaper than an empty fork/join: the second round usually finds
            // nothing, and it was costing a barrier over every plant to learn that.
            var anyPost = false;
            for (int i = 0; !anyPost && i < formations.Count; ++i)
                anyPost = !ReferenceEquals(formations[i], Soil) && formations[i].HasUndeliveredPost;
            if (!anyPost) break;
            Parallel.For(0, formations.Count, i =>
            {
                var formation = formations[i];
                if (!ReferenceEquals(formation, Soil) && formation.HasUndeliveredPost)
                {
                    formation.DeliverPost(Timestep);
                    Interlocked.Exchange(ref delivered, 1);
                }
            });
        }
        while (delivered != 0);
    }

    internal void InitRNG(ulong seed) => RNG = new(seed);

	int[] DistributeSunAndClouds(Pcg rng, int totalSun, int totalClouds)
	{
		if (totalSun <= 0f)
			return [-totalClouds];
		else if (totalClouds <= 0f)
			return [totalSun];
		else
		{
			var sunSegments = (int)rng.NextUInt(1, (uint)Math.Min(totalSun, totalClouds), maxExclusive: false);
			var cloudsAtStart = sunSegments == 1 || rng.NextUInt(10) >= 2 ? 1 : 0;
			var cloudsAtEnd = sunSegments > 1 && totalClouds > sunSegments && rng.NextUInt(10) >= 2 ? 1 : 0;

			var cloudSegments = sunSegments + cloudsAtStart + cloudsAtEnd - 1;
			//Debug.WriteLine($"totalSun: {totalSun}, totalClouds: {totalClouds}, sunSegments: {sunSegments}, cloudSegments: {cloudSegments}, cloudsStart: {cloudsAtStart}, cloudsEnd:, {cloudsAtEnd}");

			if (cloudSegments > 0)
			{
				var cloudIntervals = new int[cloudSegments];
				Array.Fill(cloudIntervals, -1);
				var cloudIndices = rng.NextInts(totalClouds - cloudSegments, 0, cloudSegments);
				for (int i = 0; i < cloudIndices.Length; ++i)
					--cloudIntervals[cloudIndices[i]];
				var sunIntervals = new int[sunSegments];
				Array.Fill(sunIntervals, 1);
				var sunIndices = rng.NextInts(totalSun - sunSegments, 0, sunSegments);
				for(int i = 0; i < sunIndices.Length; ++i)
					++sunIntervals[sunIndices[i]];
				var result = new int[sunSegments + cloudSegments];
				if (cloudsAtStart == 1)
					for(int i = 0; i < cloudSegments; ++i)
					{
						var target = i << 1;
						result[target++] = cloudIntervals[i];
						if (i < sunIntervals.Length)
							result[target] = sunIntervals[i];
					}
				else
					for(int i = 0; i < sunSegments; ++i)
					{
						var target = i << 1;
						result[target++] = sunIntervals[i];
						if (i < cloudIntervals.Length)
							result[target] = cloudIntervals[i];
					}
				return result;
			}
			else
				return [totalSun];
		}
	}
	static readonly float[] precipitationLowIntervals = [0, 0, 2, 5, 10, 20, 50];
	static readonly float[] precipitationHighIntervals = [0, 2, 5, 10, 20, 50, 100];
	WeatherStats[] PlanCloudsSingleMonth(Pcg rng, int month, int daysPerMonth, float sunnyDays, float cloudDays, float dullDays, float precipitationMM, float[] maxPrecipitationStats_inDays)
	{
		var hoursInMonth = daysPerMonth * 24;
		var sunHoursTarget = Math.Max(0, (int)Math.Round(rng.NextNormal(sunnyDays * 24, 0.15 * sunnyDays * 24)));
		var dullHoursTarget = Math.Max(0, (int)Math.Round(rng.NextNormal(dullDays * 24, 0.15 * dullDays * 24)));
		var cloudHoursTarget = Math.Max(0, hoursInMonth - sunHoursTarget - dullHoursTarget);
		var dullHours = 0;
		var dullIntervals = new List<int>();
		var lowDull = 1;
		var highDull = Math.Min(24*10, hoursInMonth - sunHoursTarget - cloudHoursTarget); //10 days
		while (dullHours < dullHoursTarget)
		{
			var interval = Math.Min(rng.Next(lowDull, highDull, maxExclusive: false), rng.Next(lowDull, highDull, maxExclusive: false));
			var limit = 21 - Math.Abs(7 - month);
			for(int i = 0; i <  limit; ++i)
				interval = Math.Min(interval, rng.Next(lowDull, highDull, maxExclusive: false));
			if (dullHours + interval > dullHoursTarget)
				interval = dullHoursTarget - dullHours;
			Debug.Assert(interval >= 0);
			dullIntervals.Add(interval);
			dullHours += interval;
		}

		var startDull = rng.Next(100) >= 50;
		var endDull = rng.Next(100) >= 50;

		var sun_cloud_intervalsLength = dullIntervals.Count + (startDull ? 0 : 1) + (endDull ? 0 : 1) - 1;
		var sunIntervalsRaw = rng.NextDoublesScaled(sun_cloud_intervalsLength, hoursInMonth - dullHours - cloudHoursTarget);
		var sunIntervals = new int[sun_cloud_intervalsLength];
		var sunHours = 0;
		for (int i = 0; i < sun_cloud_intervalsLength; ++i)
		{
			var tmp = (int)Math.Round(sunIntervalsRaw[i]);
			sunIntervals[i] = tmp;
			sunHours += tmp;
		}

		var cloudIntervalsRaw = rng.NextDoublesScaled(sun_cloud_intervalsLength, hoursInMonth - dullHours - sunHoursTarget);
		var cloudIntervals = new int[sun_cloud_intervalsLength];
		var cloudHours = 0;
		for (int i = 0; i < sun_cloud_intervalsLength; ++i)
		{
			var tmp = (int)Math.Round(cloudIntervalsRaw[i]);
			cloudIntervals[i] = tmp;
			cloudHours += tmp;
		}

		var sunTurn = true;
		var tmpLimit = sunHours + cloudHours + dullHours - hoursInMonth;
		var elegibleSunIndexes = new List<int>(sunIntervals.Length);
		for(int i = 0; i < sunIntervals.Length; ++i)
			if (sunIntervals[i] > 1)
				elegibleSunIndexes.Add(i);

		var elegibleCloudIndexes = new List<int>(cloudIntervals.Length);
		for(int i = 0; i < cloudIntervals.Length; ++i)
			if (cloudIntervals[i] > 1)
				elegibleCloudIndexes.Add(i);

		for(int i = 0; i < tmpLimit; ++i)
		{
			if (sunTurn)
			{
				var ei = rng.Next(elegibleSunIndexes.Count);
				var sidx = elegibleSunIndexes[ei];
				--sunIntervals[sidx];
				if (sunIntervals[sidx] <= 1)
					elegibleSunIndexes.RemoveAt(ei);
			}
			else
			{
				var ei = rng.Next(elegibleCloudIndexes.Count);
				var ci = elegibleCloudIndexes[ei];
				--cloudIntervals[ci];
				if (cloudIntervals[ci] <= 1)
					elegibleCloudIndexes.RemoveAt(ei);
			}
			sunTurn = !sunTurn;
		}

		tmpLimit = hoursInMonth - dullHours - cloudHours - sunHours;
		for(int i = 0; i < tmpLimit; ++i)
		{
			if (sunTurn)
				++sunIntervals[rng.Next(sunIntervals.Length)];
			else
				++cloudIntervals[rng.Next(cloudIntervals.Length)];
			sunTurn = !sunTurn;
		}

		var sun_cloud_intervals = new List<int[]>();
		for(int i = 0; i < sunIntervals.Length; ++i)
			sun_cloud_intervals.Add(DistributeSunAndClouds(rng, sunIntervals[i], cloudIntervals[i]));

		var targetPrecipitation_inMM = (float)Math.Max(0, rng.NextNormal(precipitationMM, 0.25 * precipitationMM));
		//normalize maxPrecipitationStats
		var maxPrecipitationSum_inDays = maxPrecipitationStats_inDays.Sum();
		var precipitationStrengthPrefixSum = new float[maxPrecipitationStats_inDays.Length];
		precipitationStrengthPrefixSum[0] = maxPrecipitationStats_inDays[0] / maxPrecipitationSum_inDays;
		for(int i = 1; i < maxPrecipitationStats_inDays.Length; ++i)
			precipitationStrengthPrefixSum[i] = precipitationStrengthPrefixSum[i - 1] + maxPrecipitationStats_inDays[i] / maxPrecipitationSum_inDays;

		var randomVector = rng.NextFloats(dullIntervals.Count);
		var rainInDulls = new float[randomVector.Length]; //currently in mm
		var rainInDullsSum_inMM = 0.0f;
		for(int i = 0; i < randomVector.Length; ++i)
		{
			var bin = Array.BinarySearch(precipitationStrengthPrefixSum, 0, precipitationStrengthPrefixSum.Length, randomVector[i]);
			bin = bin >= 0 ? bin : ~bin;
			var r = rng.NextFloat(precipitationLowIntervals[bin], precipitationHighIntervals[bin]) * dullIntervals[i];
			rainInDulls[i] = r;
			rainInDullsSum_inMM += r;
		}

		//scale to reach the target AND convert  mm / m² to gramms (= 1000000 mm³ = 1 l and 1 l = 1000 gramms)
		var rainInDullsSum_inG_per_m2 = 1e3f * targetPrecipitation_inMM / rainInDullsSum_inMM;

		// Debug.WriteLine($"m: {month}, h: {hoursInMonth} = {cloudIntervals.Sum() + sunIntervals.Sum() + dullIntervals.Sum()} = {dullIntervals.Sum() + sun_cloud_intervals.Sum(x => x.Sum(y => Math.Abs(y)))}");
		// Debug.WriteLine($"sun: {sunHoursTarget} -> {sunIntervals.Sum()}, intervals: {sunIntervals.Length}");
		// Debug.WriteLine($"clouds: {cloudHoursTarget} -> {cloudIntervals.Sum()}, intervals: {cloudIntervals.Length}");
		// Debug.WriteLine($"dull: {dullHoursTarget} -> {dullIntervals.Sum()}, intervals: {dullIntervals.Count}");
		// Debug.WriteLine($"rain: {targetPrecipitation} -> {rainInDulls.Sum() / 1e3}, intervals: {rainInDulls.Length}");
		//Debug.WriteLine($"sci: [{String.Join(", ", sun_cloud_intervals)}]");

		//[sky_coverage (factor), precipitation (gramm), temperature (°C), sun_energy (W / hm²)] #wind_speed (km/h), humidity (?)
		var resultHourly = new WeatherStats[hoursInMonth];
		int hi = 0, di = 0, si = 0;
		var dullTurn = startDull;
		var resultLimit = dullIntervals.Count + sun_cloud_intervalsLength;
		for(int i = 0; i < resultLimit; ++i)
		{
			if (dullTurn)
			{
				var cloudDistribution = rng.NextFloats(dullIntervals[di], 0.5f, 1.0f);
				var rainDistribution = new float[cloudDistribution.Length];
				if (rainInDulls[di] > 0)
				{
					Array.Copy(cloudDistribution, rainDistribution, cloudDistribution.Length);
					var rainPeriodCharacter = rng.NextFloat(0.5f, 0.9f);
					for(int j = 0; j < rainDistribution.Length; ++j)
						if (rainDistribution[j] < rng.NextFloat(0.5f, rainPeriodCharacter))
							rainDistribution[j] = 0;
					//assure at least 1 positive element
					var secureIndex = rng.Next(rainDistribution.Length);
					rainDistribution[secureIndex] = cloudDistribution[secureIndex];
					var rdSum = rainDistribution.Sum();
					if (rdSum > 0)
						for(int j = 0; j < rainDistribution.Length; ++j)
							rainDistribution[j] *= rainInDullsSum_inG_per_m2 * rainInDulls[di] / rdSum; //rain in Dulls is in mm/m²; rainDistribution is now in g/m²
				}
				for(int j = 0; j < cloudDistribution.Length; ++j)
					resultHourly[hi++] = new WeatherStats(cloudDistribution[j], rainDistribution[j]);

				++di;
			}
			else
			{
				foreach(var s in sun_cloud_intervals[si])
				{
					var sca = Math.Abs(s);
					var (low,high) = s > 0 ? (0f, 0.25f) : (0.25f, 0.5f);
					for (int j = 0; j < sca; ++j)
						resultHourly[hi++] = new WeatherStats(rng.NextFloat(low, high), 0f);
				}
				++si;
			}
			dullTurn = !dullTurn;
		}

		return resultHourly;
	}

	public static float W2J(float watt, float seconds = 3600) => watt * seconds;

	public static float J2Cal(float joules) => joules / 4.186f;
	public static float Cal2J(float calories) => calories * 4.186f;

	public byte[] SerializeTerrain() => Soil.Serialize();

	//--- checkpointing ----------------------------------------------------------------------------
	//
	//What a resumed run needs that the request does not already give it.
	//
	//The request rebuilds the STRUCTURE identically every time - the same soil, the same plants in
	//the same order, the same obstacles - because Initialize is deterministic in exactly that sense.
	//What it cannot rebuild is everything the simulation has done since: how far the clock has run,
	//how much water is in each cell, where every organ is, and what each plant's generator will say
	//next. That is what travels here, positionally, against a world that has just been rebuilt.
	//
	//Nothing derived is written. The collision BVH, the tree caches, the irradiance client and the
	//weather tables are all functions of the above or of the request, and they are rebuilt on the
	//first tick after a restore - which is why a resumed run has to be given a world built the
	//normal way rather than an empty one.

	/// <summary>The state and geometry semantics this world writes and accepts.</summary>
	// V4 corrects authored leaf widths. V3 contact caches and accepted frames refer to the old
	// geometry and cannot safely resume under the corrected templates; saved V7 playback is unchanged.
	// V5 keeps request coefficients unmodified during initialization. Earlier archives
	// contain already-converted settings without a marker, so rebuilding their live
	// world would apply the conversion twice. Their recorded playback is still valid.
    // V6 adds accepted internal blade/petiole controls and pins their immutable
    // geometry/profile identity. V7 scene playback remains independently readable.
    // V7 integrates the moving direct and diffuse sky at ten-minute midpoints and
    // supports calibrated expansion/contact limits. Old live states would continue
    // under different light and growth semantics; recorded scene playback is unchanged.
    // V8 corrects lamina reserve weights, maintenance horizons, photosynthetic
    // water demand and root growth units. The layout is unchanged, but old live
    // continuations diverge.
    // V9 adds construction escrow and retained root-branch funding intent.
	// V10 separates seed reserves from hydration and corrects germination accounting.
	// V11 debits delivered water from both soil models, saves high-precision cell
	// balances and the tetrahedral virtual reservoir, and introduces opt-in
	// glucose-equivalent accounting. Older live states would continue with a
	// different resource budget; recorded playback remains independently readable.
    // V13 stores independent admission retry state for retained organ births.
    // V14 certifies segmented scape emergence, changes Bergenia's unloaded scape
    // curve and inherits the new search bound in older requests that omitted it.
    // V15 changes foliage rollback: a rejected stationary petiole can yield after
    // rejected neighbours are restored, and a floral increment may be retried
    // against that accepted geometry with a full continuous clearance check.
    // Older live states would take a different growth path; scene playback is independent.
    // V16 certifies attachment sweeps against the minimum exclusion radius of each
    // subinterval, permitting radius growth without freezing the whole path at its
    // starting size. V15 continuations could reject feasible scape growth.
    // V17 captures sampled support-radius growth, but a later contact fallback
    // could still discard its early retry. V18 certifies that retry after final
    // fallback, preserving accepted radii. V19 keeps unfinished floral precursors
    // viable only through their bounded bloom window instead of applying an
    // organ-age cutoff before the successor can develop. Older live states follow
    // different development paths; recorded playback remains independently readable.
    // V20 retries only sampled leaf/petiole growth after final contact fallback and
    // releases an unplaceable first scape once its original spring opportunity ends.
    // Both change continuation semantics without changing the saved-state layout.
    // V21 uses actual interpolated thickness for midpoint collision rejection
    // and endpoint thickness for separating-plane proofs. Sampled floral growth
    // can retain a certified partial increment after the final contact rollback.
    // V22 certifies attachment containment using the same centre-line / blade
    // surface geometry as clipping, avoiding false rejection of neighbour yield.
    // The payload layout is unchanged; accepted continuation paths can differ.
    // V23 aligns live growth-trial pair order with the main contact solver.
    // Equivalent sweeps now share its bounded certificate and continuation policy.
    // The payload layout is unchanged; retry acceptance can differ from V22.
    // V24 distinguishes observed intersections from uncertified sweeps, permits
    // bounded facet-plane certificates after the distance-query budget, and uses
    // a mechanical retry budget independent of biological tick duration. Accepted
    // continuation paths can change; the payload layout and playback do not.
    // V25 recovers sampled floral development through certified waypoints, with
    // both contacting support systems yielding at each smaller growth increment.
    // Only certified prefixes are retained. Payload layout and playback are unchanged.
    // V26 retains multiple continuation witnesses and solves their coupled
    // unilateral tangent constraints. Accepted geometry and retries can differ.
    // V27 adds certified staged evasion and clearance guards to experimental V1.
    // Retry ordering/precision changes can alter continuation; playback is unchanged.
    // V28 solves shared angular limits in the coupled tangent and adds a bounded
    // direct solve for poorly conditioned contact sets. Payload layout is unchanged.
    // V30 aligns interval distance proofs with the existing clearance tolerance
    // and removes unchecked tiny-motion acceptance, including at attachments.
    // Certification decisions can alter continuation; payload/playback are unchanged.
    // V31 measures unrestricted rod distances in relative doubles and preserves
    // exact accepted interpolation endpoints. V32 clips attached rod pairs in
    // double parameter ranges and uses matching exemption-containment predicates.
    // Contact/retry decisions can change; payload layout and playback are unchanged.
    // V33 writes one contact-engine tag byte ahead of the collision payload. The certified
    // engine's payload after that byte is unchanged; the soft engine stores its sparse
    // contact memories instead. A checkpoint resumes only under the engine that wrote it.
	public const byte CheckpointVersion = 33;

	/// <summary>
	/// Everything the next step depends on that the request does not determine.
	///
	/// Safe to call only BETWEEN steps: mid-tick the agent arrays are half-swapped and the postboxes
	/// hold undelivered messages, neither of which is captured here.
	/// </summary>
	public byte[] SerializeState()
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream);

		writer.Write(CheckpointVersion);
		writer.Write(Timestep);
		//The world generator. It is consumed only while the world is being built - the weather table
		//forks a stream from it - so a rebuilt world already has it in the right place. Written
		//anyway, at sixteen bytes, so that the day something draws from it during a run this
		//checkpoint does not quietly become wrong.
		var (worldState, worldIncrement) = RNG.GetState();
		writer.Write(worldState);
		writer.Write(worldIncrement);
		//Counted so a restore can refuse a world of a different shape rather than silently reading
		//one formation's state into another's.
		writer.Write(Count);
        ForEach(formation => { if(formation is PlantFormation2 plant) writer.Write(SceneGeometryAssets.For(plant.Parameters).Id); });

		ForEach(formation =>
		{
			switch (formation)
			{
				case PlantFormation2 plant: plant.WriteState(writer); break;
				case ISoilFormation soil: soil.WriteState(writer); break;
			}
		});

		//Last, because it is keyed against the formations written above.
		Irradiance.WriteState(writer, FormationList);
		Collisions.WriteState(writer, FormationList, ContactEngine);

		return stream.ToArray();
	}

	/// <summary>
	/// Restores a world that has just been built from the same request.
	///
	/// Positional: formations are appended in a fixed order by Initialize - the soil, then the
	/// plants - and nothing ever removes one, so index N in the checkpoint is index N here. A
	/// mismatch in the count means the request and the checkpoint describe different worlds, and
	/// that is refused rather than reconciled.
	/// </summary>
	public void RestoreState(byte[] checkpoint)
	{
		using var stream = new MemoryStream(checkpoint);
		using var reader = new BinaryReader(stream);

		var version = reader.ReadByte();
		if (version != CheckpointVersion)
			throw new InvalidDataException(
				$"This checkpoint is version {version} and this build writes version {CheckpointVersion}. " +
				$"A run saved by a different build of the simulation cannot be resumed.");

		var timestep = reader.ReadUInt32();
		RNG.SetState(reader.ReadUInt64(), reader.ReadUInt64());
		var count = reader.ReadInt32();
		if (count != Count)
			throw new InvalidDataException(
				$"The checkpoint holds {count} formations and this request builds {Count}. " +
				$"The scenario has changed since the run was saved.");

        ForEach(formation =>
        {
            if(formation is PlantFormation2 plant && reader.ReadString()!=SceneGeometryAssets.For(plant.Parameters).Id)
                throw new InvalidDataException("The checkpoint mesh or deformation profile has changed. Recorded playback remains available.");
        });
		ForEach(formation =>
		{
			switch (formation)
			{
				case PlantFormation2 plant: plant.ReadState(reader); break;
				case ISoilFormation soil: soil.ReadState(reader); break;
			}
		});

		Irradiance.ReadState(reader, FormationList);
		Collisions.ReadState(reader, FormationList, ContactEngine);

		if (stream.Position != stream.Length)
			throw new InvalidDataException(
				$"The checkpoint has {stream.Length - stream.Position} bytes left over after every " +
				$"formation was restored, so it does not describe this world.");

		RestoreTimestep(timestep);
		// Gravity runs before the first world callback. Restored rhizomes must see the
		// same static anchors on that first step as an uninterrupted simulation.
		Collisions.EnsureStatic(this);
	}

	/// <summary>
	/// How many soil fields this world has: one for a plain box field, one per tray for an
	/// imported facade.
	///
	/// Exposed because a stored run has to be describable from outside - "1,600 trays" is the
	/// first thing that distinguishes one archived facade run from another, and the soil itself
	/// is internal.
	/// </summary>
	public int SoilFieldsCount => Soil?.FieldsCount ?? 0;

	/// <summary>
	/// Per-obstacle leaf area index and solar load, one sample per simulation step, for the
	/// obstacles selected by <see cref="SimulationRequest.LeafAreaIndexObstacles"/>. Null when no
	/// selection was requested or when no internal light model ran.
	/// </summary>
	public ObstacleLightMeter? ObstacleLight => Irradiance.ObstacleLight;

	/// <summary>What the leaf area index filter was offered and what it matched, so a caller can
	/// tell a mistyped filter from a measurement that never ran.</summary>
	public (string? Filter, int Candidates, int Matches) ObstacleLightSelection => Irradiance.ObstacleLightSelection;
}
