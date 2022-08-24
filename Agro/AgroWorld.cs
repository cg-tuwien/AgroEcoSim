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

namespace Agro;

[StructLayout(LayoutKind.Auto)]
public readonly struct WeatherStats
{
    /// <summary>
    /// Rainfall in gramm
    /// </summary>
    /// <summary>
    /// Factor of sky coverage where 0 means clear sky and 1 means fully covered
    /// </summary>
    public readonly float Precipitation;
    public readonly float SkyCoverage;

    public WeatherStats(float precipitation, float skyCoverage)
    {
        Precipitation = precipitation;
        SkyCoverage = skyCoverage;
    }
}

public static class AgroWorld {
    public static uint TicksPerHour = 1;
    //public const int TotalHours = 24 * 365 * 10;
    public static uint TotalHours = 24 * 31 * 1;

    //public static readonly Vector3 FieldSize = new(6f, 4f, 2f);
    //public const float FieldResolution = 0.1f;

    public static Vector3 FieldSize = new(20f, 10f, 3f); //2D size and the last component is depth
    public static float FieldResolution = 0.5f;

    public const float Latitude = 48.208333f;
    public const float Longitude = 16.3725f;
    public const float Elevation = 188; //meters above sea level


    //clouds_coverage, precipitation
    static readonly WeatherStats[] Weather;

    static readonly int[] DaysPerMonth = new[]{31,28,31,30,31,30,31,31,30,31,30,31};

    public static uint TimestepsTotal = TicksPerHour * TotalHours;

    static AgroWorld()
    {
        var sunnyDays = new float[]{3.2f, 3.3f, 5.8f, 7.6f, 8.2f, 8.6f, 11.8f, 12.6f, 10.5f, 9.7f, 4.3f, 4.1f}; //clouds factor 0 to 0.25
        var cloudyDays = new float[]{12.3f, 12.6f, 14.6f, 15.3f, 17.2f, 16.6f, 15.1f, 13.7f, 13.2f, 13.6f, 12.4f, 12.1f}; //clouds factor 0.25 to 0.5
        var dullDays = new float[]{15.5f, 12.4f, 10.6f, 7.1f, 5.6f, 4.8f, 4f, 4.6f, 6.3f, 7.7f, 13.3f, 14.8f}; //clouds factor 0.5 to 1

        //dry, 0-2mm, 2-5mm, 5-10mm, 10-20mm, 20-50mm, 50-100mm
        var dailyPrecipitation = new[] {
            new float[]{21.6f, 4.9f, 2.8f, 1.2f, 0.6f, 0f, 0f},
            new float[]{19.1f, 5f, 2f, 1.4f, 0.5f, 0.1f, 0f},
            new float[]{21.4f, 5.1f, 2.5f, 1.3f, 0.5f, 0.2f, 0f},
            new float[]{21f, 4.9f, 2.4f, 1f, 0.6f, 0.1f, 0f},
            new float[]{20.2f, 5.7f, 2.6f, 1.5f, 0.7f, 0.3f, 0f},
            new float[]{19f, 5.5f, 2.9f, 1.4f, 0.8f, 0.4f, 0f},
            new float[]{20.3f, 5.8f, 2.2f, 1.4f, 1.1f, 0.1f, 0.1f},
            new float[]{22.3f, 4.7f, 2f, 1f, 0.7f, 0.3f, 0f},
            new float[]{22.1f, 3.7f, 2f, 1f, 0.7f, 0.3f, 0.1f},
            new float[]{24f, 3.5f, 1.8f, 1.1f, 0.5f, 0.1f, 0f},
            new float[]{20.8f, 4.5f, 2.6f, 1.4f, 0.5f, 0.1f, 0f},
            new float[]{20.9f, 5.5f, 2.6f, 1.3f, 0.5f, 0.1f, 0f}
        };

        var precipitationMM = new []{29, 28, 33, 30, 43, 46, 44, 37, 42, 26, 31, 31};

        var maxTemperature_10_5Days = new []{0.7, 0.8, 0.1, 0, 0, 0, 0, 0, 0, 0, 0, 0.4};
        var maxTemperature_5_0Days = new []{7.5, 4.7, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0.7, 5.2};
        var maxTemperature0_5Days = new[]{11.6, 8.4, 4.5, 0.3, 0, 0, 0, 0, 0, 0.1, 5.2, 12.8};
        var maxTemperature5_10Days = new[]{8.9, 8.7, 9.6, 2.9, 0.1, 0, 0, 0, 0, 2.8, 10.9, 10.5};
        var maxTemperature10_15Days = new []{2.2, 4.9, 10.4, 9.7, 3, 0.5, 0, 0.2, 2.2, 10, 10, 1.9};
        var maxTemperature15_20Days = new []{0, 0.7, 4.8, 10.9, 10.9, 6.8, 2.8, 2.5, 9.2, 9.9, 3, 0.2};
        var maxTemperature20_25Days = new []{0, 0, 0.6, 5.5, 12, 11.8, 9.9, 9.3, 11.4, 7.2, 0.2, 0};
        var maxTemperature25_30Days = new []{0, 0, 0, 0.7, 4.7, 8.3, 11.9, 12.4, 6.6, 0.9, 0, 0};
        var maxTemperature30_35Days = new []{0, 0, 0, 0, 0.3, 2.5, 5.8, 5.8, 0.5, 0, 0, 0};
        var maxTemperature35_40Days = new []{0, 0, 0, 0, 0, 0.1, 0.5, 0.9, 0, 0, 0, 0};

        var avgHotDayTemperature = new float[]{10, 12, 19, 24, 28, 32, 34, 34, 29, 25, 17, 11};
        var avgHighTemperature = new float[]{3, 5, 10, 16, 21, 24, 26, 26, 22, 16, 9, 4};
        var avgLowTemperature = new float []{-2, -2, 1, 5, 9, 12, 14, 14, 11, 6, 2, -1};
        var avgColdNightTemperature = new float[]{-10, -8, -5, -2, 2 ,5, 9, 8, 5, -2, -4, -8};

        Weather = new WeatherStats[TimestepsTotal];
        var tsRemain = TimestepsTotal;
        var tsCounter = 0U;
        var month = 0;
        while (tsRemain > 0)
        {
            var monthly = PlanCloudsSingleMonth(month + 1, DaysPerMonth[month], sunnyDays[month], cloudyDays[month], dullDays[month], precipitationMM[month], dailyPrecipitation[month]);
            var monthlyLength = (uint)Math.Min(Weather.Length - tsCounter, monthly.Length); //shorten if not the whole month is taken
            Array.Copy(monthly, 0, Weather, tsCounter, monthlyLength);
            month = month < 11 ? month + 1 : 0;
            tsRemain -= monthlyLength;
            tsCounter += monthlyLength;
        }
    }

    internal static float GetWater(uint timestep) => Weather[timestep].Precipitation;
    internal static float GetTemperature(uint timestep) => 20;
    internal static float GetAmbientLight(uint timestep) => 1f - Weather[timestep].SkyCoverage;

    internal static readonly Pcg RNG = new(42);

    static int[] DistributeSunAndClouds(int totalSun, int totalClouds)
    {
        if (totalSun <= 0f)
            return new int[] { -totalClouds };
        else if (totalClouds <= 0f)
            return new int[] { totalSun };
        else
        {
            var sunSegments = (int)RNG.NextUInt(1, (uint)Math.Min(totalSun, totalClouds), maxExclusive: false);
            var cloudsAtStart = sunSegments == 1 || RNG.NextUInt(10) >= 2 ? 1 : 0;
            var cloudsAtEnd = sunSegments > 1 && totalClouds > sunSegments && RNG.NextUInt(10) >= 2 ? 1 : 0;

            var cloudSegments = sunSegments + cloudsAtStart + cloudsAtEnd - 1;
            //Debug.WriteLine($"totalSun: {totalSun}, totalClouds: {totalClouds}, sunSegments: {sunSegments}, cloudSegments: {cloudSegments}, cloudsStart: {cloudsAtStart}, cloudsEnd:, {cloudsAtEnd}");

            if (cloudSegments > 0)
            {
                var cloudIntervals = new int[cloudSegments];
                Array.Fill(cloudIntervals, -1);
                var cloudIndices = RNG.NextInts(totalClouds - cloudSegments, 0, cloudSegments);
                for (int i = 0; i < cloudIndices.Length; ++i)
                    --cloudIntervals[cloudIndices[i]];
                var sunIntervals = new int[sunSegments];
                Array.Fill(sunIntervals, 1);
                var sunIndices = RNG.NextInts(totalSun - sunSegments, 0, sunSegments);
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
                return new int[] { totalSun };
        }
    }
    static readonly float[] precipitationLowIntervals = new float[] {0, 0, 2, 5, 10, 20, 50};
    static readonly float[] precipitationHighIntervals = new float[] {0, 2, 5, 10, 20, 50, 100};
    static WeatherStats[] PlanCloudsSingleMonth(int month, int daysPerMonth, float sunnyDays, float cloudDays, float dullDays, float precipitation, float[] maxPrecipitationStats)
    {
        var hoursInMonth = daysPerMonth * 24;
        var sunHoursTarget = Math.Max(0, (int)Math.Round(RNG.NextNormal(sunnyDays * 24, 0.15 * sunnyDays * 24)));
        var dullHoursTarget = Math.Max(0, (int)Math.Round(RNG.NextNormal(dullDays * 24, 0.15 * dullDays * 24)));
        var cloudHoursTarget = Math.Max(0, hoursInMonth - sunHoursTarget - dullHoursTarget);
        var dullHours = 0;
        var dullIntervals = new List<uint>();
        var lowDull = 1;
        var highDull = Math.Min(24*10, hoursInMonth - sunHoursTarget - cloudHoursTarget); //10 days
        while (dullHours < dullHoursTarget)
        {
            var interval = Math.Min(RNG.Next(lowDull, highDull, maxExclusive: false), RNG.Next(lowDull, highDull, maxExclusive: false));
            var limit = 21 - Math.Abs(7 - month);
            for(int i = 0; i <  limit; ++i)
                interval = Math.Min(interval, RNG.Next(lowDull, highDull, maxExclusive: false));
            if (dullHours + interval > dullHoursTarget)
                interval = dullHoursTarget - dullHours;
            Debug.Assert(interval >= 0);
            dullIntervals.Add((uint)interval);
            dullHours += interval;
        }

        var startDull = RNG.Next(100) >= 50;
        var endDull = RNG.Next(100) >= 50;

        var sun_cloud_intervalsLength = dullIntervals.Count + (startDull ? 0 : 1) + (endDull ? 0 : 1) - 1;
        var sunIntervalsRaw = RNG.NextDoublesScaled(sun_cloud_intervalsLength, hoursInMonth - dullHours - cloudHoursTarget);
        var sunIntervals = new int[sun_cloud_intervalsLength];
        var sunHours = 0;
        for (int i = 0; i < sun_cloud_intervalsLength; ++i)
        {
            var tmp = (int)Math.Round(sunIntervalsRaw[i]);
            sunIntervals[i] = tmp;
            sunHours += tmp;
        }

        var cloudIntervalsRaw = RNG.NextDoublesScaled(sun_cloud_intervalsLength, hoursInMonth - dullHours - sunHoursTarget);
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
                var ei = RNG.Next(elegibleSunIndexes.Count);
                var sidx = elegibleSunIndexes[ei];
                --sunIntervals[sidx];
                if (sunIntervals[sidx] <= 1)
                    elegibleSunIndexes.RemoveAt(ei);
            }
            else
            {
                var ei = RNG.Next(elegibleCloudIndexes.Count);
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
                ++sunIntervals[RNG.Next(sunIntervals.Length)];
            else
                ++cloudIntervals[RNG.Next(cloudIntervals.Length)];
            sunTurn = !sunTurn;
        }

        var sun_cloud_intervals = new List<int[]>();
        for(int i = 0; i < sunIntervals.Length; ++i)
            sun_cloud_intervals.Add(DistributeSunAndClouds(sunIntervals[i], cloudIntervals[i]));

        var targetPrecipitation = (float)Math.Max(0, RNG.NextNormal(precipitation, 0.25 * precipitation));
        //normalize maxPrecipitationStats
        var maxPrecipitationSum = maxPrecipitationStats.Sum();
        var precipitationStrengthPrefixSum = new float[maxPrecipitationStats.Length];
        precipitationStrengthPrefixSum[0] = maxPrecipitationStats[0] / maxPrecipitationSum;
        for(int i = 1; i < maxPrecipitationStats.Length; ++i)
            precipitationStrengthPrefixSum[i] = precipitationStrengthPrefixSum[i - 1] + maxPrecipitationStats[i] / maxPrecipitationSum;

        var randomVector = RNG.NextFloats(dullIntervals.Count);
        var rainInDulls = new float[randomVector.Length];
        var rainInDullsSum = 0.0f;
        for(int i = 0; i < randomVector.Length; ++i)
        {
            var bin = Array.BinarySearch(precipitationStrengthPrefixSum, 0, precipitationStrengthPrefixSum.Length, randomVector[i]);
            bin = bin >= 0 ? bin : ~bin;
            var r = RNG.NextFloat(precipitationLowIntervals[bin], precipitationHighIntervals[bin]) * dullIntervals[i];
            rainInDulls[i] = r;
            rainInDullsSum += r;
        }

        //scale to reach the target AND convert  mm / m² to gramms (= 1000000 mm³ = 1 l and 1 l = 1000 gramms)
        rainInDullsSum = 1e3f * targetPrecipitation / rainInDullsSum;
        for(int i = 0; i < rainInDulls.Length; ++i)
            rainInDulls[i] *= rainInDullsSum;

        // Debug.WriteLine($"m: {month}, h: {hoursInMonth} = {cloudIntervals.Sum() + sunIntervals.Sum() + dullIntervals.Sum()} = {dullIntervals.Sum() + sun_cloud_intervals.Sum(x => x.Sum(y => Math.Abs(y)))}");
        // Debug.WriteLine($"sun: {sunHoursTarget} -> {sunIntervals.Sum()}, intervals: {sunIntervals.Length}");
        // Debug.WriteLine($"clouds: {cloudHoursTarget} -> {cloudIntervals.Sum()}, intervals: {cloudIntervals.Length}");
        // Debug.WriteLine($"dull: {dullHoursTarget} -> {dullIntervals.Sum()}, intervals: {dullIntervals.Count}");
        // Debug.WriteLine($"rain: {targetPrecipitation} -> {rainInDulls.Sum() / 1e3}, intervals: {rainInDulls.Length}");
        //Debug.WriteLine($"sci: [{String.Join(", ", sun_cloud_intervals)}]");

        //[sky_coverage (factor), precipitation (gramm), temperature (°C), sun_energy (W/hm²)] #wind_speed (km/h), humidity (?)
        var result = new WeatherStats[TicksPerHour * hoursInMonth];
        int hi = 0, di = 0, si = 0;
        var dullTurn = startDull;
        var resultLimit = dullIntervals.Count + sun_cloud_intervalsLength;
        for(int i = 0; i < resultLimit; ++i)
        {
            if (dullTurn)
            {
                var cloudDistribution = RNG.NextFloats((int)(TicksPerHour * dullIntervals[di]), 0.5f, 1.0f);
                var rainDistribution = new float[cloudDistribution.Length];
                Array.Copy(cloudDistribution, rainDistribution, cloudDistribution.Length);
                var rainPeriodCharacter = RNG.NextFloat(0.5f, 0.9f);
                for(int j = 0; j < rainDistribution.Length; ++j)
                    if (rainDistribution[j] < RNG.NextFloat(0.5f, rainPeriodCharacter))
                        rainDistribution[j] = 0;
                //assure at least 1 positive element
                var secureIndex = RNG.Next(rainDistribution.Length);
                rainDistribution[secureIndex] = cloudDistribution[secureIndex];
                var rdSum = rainDistribution.Sum();
                if (rdSum > 0)
                    for(int j = 0; j < rainDistribution.Length; ++j)
                        rainDistribution[j] *= rainInDulls[di] / rdSum;

                for(int j = 0; j < cloudDistribution.Length; ++j)
                    result[hi + j] = new WeatherStats(cloudDistribution[j], rainDistribution[j]);

                hi += (int)(dullIntervals[di] * TicksPerHour);
                ++di;
            }
            else
            {
                foreach(var s in sun_cloud_intervals[si])
                {
                    var sca = Math.Abs(s);
                    var (low,high) = s > 0 ? (0f, 0.25f) : (0.25f, 0.5f);
                    for (int j = 0; j < sca; ++j)
                        result[hi+j] = new WeatherStats(RNG.NextFloat(low, high), 0f);
                    hi += (int)(sca * TicksPerHour);
                }
                ++si;
            }
            dullTurn = !dullTurn;
        }

        return result;
    }


    static readonly List<float> Irradiances = new();
    static readonly List<int> SkipPlants = new();

    static readonly List<Vector3> IrradiancePoints = new();
    //static List<Vector3i> IrradianceTriangles = new();
    //static List<int> IrradianceSurfaceSize = new();
    //static List<int> IrradianceGroupOffsets = new();
    static readonly Dictionary<IFormation, int> IrradianceFormationOffsets = new ();

/*
#INDEXED DATA
uint32 entitiesCount
    #foreach ENTITY
    uint32 surfacesCount
        #foreach SURFACE (for now, each surface is an irradiancemeter)
        uint8 trianglesCount
        #foreach TRIANGLE
            uint32 index0
            uint32 index1
            uint32 index2
#POINTS DATA
uint32 pointsCount
    #foreach POINT
    float32 x
    float32 y
    float32 z
*/

    public static void IrradianceCallback(uint timestep, IList<IFormation> formations)
    {
        return;
        SkipPlants.Clear();
        for(int i = 0; i < formations.Count; ++i)
            if (!(formations[i] is PlantFormation plant && plant.AG.Alive))
                SkipPlants.Add(i);

        int offsetCounter = 0;
        if (SkipPlants.Count < formations.Count)
        {
            Irradiances.Clear();
            IrradianceFormationOffsets.Clear();
            IrradiancePoints.Clear();

            // var objFileName = $"t{timestep}.obj";
            // using var objStream = File.Open(objFileName, FileMode.Create);
            // using var objWriter = new StreamWriter(objStream, Encoding.UTF8);
            // var obji = new StringBuilder();
            // objWriter.WriteLine("o Field");

            var meshFileName = $"t{timestep}.mesh";
#if GODOT
            var meshFileFullPath = Path.Combine("agroeco-mts3", meshFileName);
#else
            var meshFileFullPath = Path.Combine("..", "agroeco-mts3", meshFileName);
#endif
            using (var meshStream = File.Open(meshFileFullPath, FileMode.Create))
            {
                using var writer = new BinaryWriter(meshStream, Encoding.UTF8, false);
                writer.WriteU32(formations.Count - SkipPlants.Count); //WRITE NUMBER OF PLANTS in this system
                var skipPointer = 0;
                for(int pi = 0; pi < formations.Count; ++pi)
                {
                    if (skipPointer < SkipPlants.Count && SkipPlants[skipPointer] == pi)
                        ++skipPointer;
                    else
                    {
                        var plant = formations[pi] as PlantFormation;
                        var ag = plant.AG;
                        var count = ag.Count;

                        IrradianceFormationOffsets.Add(ag, offsetCounter);
                        offsetCounter += count;

                        //IrradianceSurfacesPerPlant.Add(count);
                        writer.WriteU32(count); //WRITE NUMBER OF SURFACES in this plant

                        for(int i = 0; i < count; ++i)
                        {
                            var organ = ag.GetOrgan(i);
                            var center = ag.GetBaseCenter(i);
                            var scale = ag.GetScale(i);
                            var halfRadiusX = new Vector3(0f, 0f, scale.Z * 0.5f);
                            var orientation = ag.GetDirection(i);
                            //var length = ag.GetLength(i) * 0.5f; //x0.5f because its the radius of the cube!
                            var lengthVector = new Vector3(scale.X, 0f, 0f);

                            //sprite.Transform = new Transform(basis, (Formation.GetBaseCenter(index) + stableScale).ToGodot());
                            //sprite.Scale = (Formation.GetScale(index) * 0.5f).ToGodot();
                            switch(organ)
                            {
                                case OrganTypes.Leaf:
                                {
                                    writer.WriteU8(2); //WRITE NUMBER OF TRIANGLES in this surface

                                    var p = IrradiancePoints.Count;
                                    IrradiancePoints.Add(center + Vector3.Transform(-halfRadiusX, orientation));
                                    IrradiancePoints.Add(center + Vector3.Transform(halfRadiusX, orientation));
                                    IrradiancePoints.Add(center + Vector3.Transform(lengthVector + halfRadiusX, orientation));
                                    IrradiancePoints.Add(center + Vector3.Transform(lengthVector - halfRadiusX, orientation));

                                    //var t = IrradianceTriangles.Count;
                                    writer.WriteU32(p);
                                    writer.WriteU32(p + 1);
                                    writer.WriteU32(p + 2);
                                    writer.WriteU32(p);
                                    writer.WriteU32(p + 2);
                                    writer.WriteU32(p + 3);

                                    // obji.AppendLine(OF(p, p+1, p+2));
                                    // obji.AppendLine(OF(p, p+2, p+3));

                                    //IrradianceTriangles.Add(new (p, p + 1, p + 2));
                                    //IrradianceTriangles.Add(new (p, p + 2, p + 3));

                                    //IrradianceGroupOffsets.Add(IrradianceGroups.Count);
                                    //IrradianceSurfaceSize.Add(2);
                                    //IrradianceGroups.Add(t + 1);
                                }
                                break;
                                case OrganTypes.Stem:
                                {
                                    var halfRadiusY = new Vector3(0f, scale.Y * 0.5f, 0f);
                                    writer.WriteU8(8); //WRITE NUMBER OF TRIANGLES in this surface
                                    var p = IrradiancePoints.Count;
                                    IrradiancePoints.Add(center + Vector3.Transform(- halfRadiusX - halfRadiusY, orientation));
                                    IrradiancePoints.Add(center + Vector3.Transform(halfRadiusX - halfRadiusY, orientation));
                                    IrradiancePoints.Add(center + Vector3.Transform(halfRadiusX + halfRadiusY, orientation));
                                    IrradiancePoints.Add(center + Vector3.Transform(-halfRadiusX + halfRadiusY, orientation));
                                    IrradiancePoints.Add(center + Vector3.Transform(lengthVector - halfRadiusX - halfRadiusY, orientation));
                                    IrradiancePoints.Add(center + Vector3.Transform(lengthVector + halfRadiusX - halfRadiusY, orientation));
                                    IrradiancePoints.Add(center + Vector3.Transform(lengthVector + halfRadiusX + halfRadiusY, orientation));
                                    IrradiancePoints.Add(center + Vector3.Transform(lengthVector - halfRadiusX + halfRadiusY, orientation));

                                    //front face
                                    writer.WriteU32(p);
                                    writer.WriteU32(p + 1);
                                    writer.WriteU32(p + 5);
                                    writer.WriteU32(p);
                                    writer.WriteU32(p + 5);
                                    writer.WriteU32(p + 4);
                                    //right face
                                    writer.WriteU32(p + 1);
                                    writer.WriteU32(p + 2);
                                    writer.WriteU32(p + 6);
                                    writer.WriteU32(p + 1);
                                    writer.WriteU32(p + 6);
                                    writer.WriteU32(p + 5);
                                    //back face
                                    writer.WriteU32(p + 2);
                                    writer.WriteU32(p + 3);
                                    writer.WriteU32(p + 7);
                                    writer.WriteU32(p + 2);
                                    writer.WriteU32(p + 7);
                                    writer.WriteU32(p + 6);
                                    //left face
                                    writer.WriteU32(p + 3);
                                    writer.WriteU32(p);
                                    writer.WriteU32(p + 4);
                                    writer.WriteU32(p + 3);
                                    writer.WriteU32(p + 4);
                                    writer.WriteU32(p + 7);

                                    // obji.AppendLine(OF(p, p+1, p+5));
                                    // obji.AppendLine(OF(p, p+5, p+4));

                                    // obji.AppendLine(OF(p+1, p+2, p+6));
                                    // obji.AppendLine(OF(p+1, p+6, p+5));

                                    // obji.AppendLine(OF(p+2, p+3, p+7));
                                    // obji.AppendLine(OF(p+2, p+7, p+6));

                                    // obji.AppendLine(OF(p+3, p, p+4));
                                    // obji.AppendLine(OF(p+3, p+4, p+7));
                                }
                                break;
                                case OrganTypes.Shoot:
                                {
                                    var halfRadiusY = new Vector3(0f, scale.Y * 0.5f, 0f);
                                    writer.WriteU8(2); //WRITE NUMBER OF TRIANGLES in this surface
                                    var p = IrradiancePoints.Count;
                                    IrradiancePoints.Add(center + Vector3.Transform(-halfRadiusX - halfRadiusY, orientation));
                                    IrradiancePoints.Add(center + Vector3.Transform(halfRadiusX - halfRadiusY, orientation));
                                    IrradiancePoints.Add(center + Vector3.Transform(halfRadiusX + halfRadiusY, orientation));
                                    IrradiancePoints.Add(center + Vector3.Transform(-halfRadiusX + halfRadiusY, orientation));

                                    writer.WriteU32(p);
                                    writer.WriteU32(p + 1);
                                    writer.WriteU32(p + 2);
                                    writer.WriteU32(p);
                                    writer.WriteU32(p + 2);
                                    writer.WriteU32(p + 3);

                                    // obji.AppendLine(OF(p, p+1, p+2));
                                    // obji.AppendLine(OF(p, p+2, p+3));
                                }
                                break;
                                default: throw new NotImplementedException();
                            }
                        }
                    }
                }

                writer.Write((uint)IrradiancePoints.Count);
                for(int i = 0; i < IrradiancePoints.Count; ++i)
                {
                    var p = IrradiancePoints[i];
                    writer.Write(p.X);
                    writer.Write(p.Y);
                    writer.Write(p.Z);
                    //objWriter.WriteLine($"v {p.X} {p.Y} {p.Z}");
                }

                //objWriter.WriteLine(obji.ToString());
            }

            SW.Start();
            var processInfo = new ProcessStartInfo("python3")
            {
                CreateNoWindow = true,
#if GODOT
                WorkingDirectory = "agroeco-mts3",
#else
                WorkingDirectory = Path.Combine("..", "agroeco-mts3"),
#endif
                Arguments = $"render.py {meshFileName} {Latitude} {Longitude} {(new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc) + TimeSpan.FromHours(timestep / (double)TotalHours)).ToString("o", CultureInfo.InvariantCulture)}"
            };

            Process.Start(processInfo).WaitForExit();
            SW.Stop();

#if GODOT
            var irrFileName = Path.Combine("agroeco-mts3", $"t{timestep}.irrbin");
#else
            var irrFileName = Path.Combine("..", "agroeco-mts3", $"t{timestep}.irrbin");
#endif
            using var irrStream = File.Open(irrFileName, FileMode.Open);
            using var reader = new BinaryReader(irrStream);

            for(int p = 0; p < offsetCounter; ++p)
                Irradiances.Add(reader.ReadSingle());

            File.Delete(meshFileFullPath);
            File.Delete(irrFileName);
            //File.Delete(objFileName);
        }
    }

    internal static Stopwatch SW = new();

    static string OF(int a, int b, int c) => $"f {a+1} {b+1} {c+1}";

    internal static float GetIrradiance(IFormation formation, int agentIndex) => IrradianceFormationOffsets.TryGetValue(formation, out var offset) ? Irradiances[offset + agentIndex] : 1f;
}

public static class BinaryWriterExtensions
{
    public static void WriteU8(this BinaryWriter writer, int value) => writer.Write((byte)value);
    public static void WriteU32(this BinaryWriter writer, int value) => writer.Write((uint)value);
    public static void WriteU32(this BinaryWriter writer, uint value) => writer.Write(value);
}