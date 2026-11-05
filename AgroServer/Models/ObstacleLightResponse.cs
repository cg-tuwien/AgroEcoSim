using Agro;
using Agro.Vegetation;
using AgentsSystem;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgroServer.Models;

/// <summary>
/// The leaf area index measurement and the tray vegetation survey of one run, as one document.
/// </summary>
/// <remarks>
/// <para>
/// One type for the socket and for the file in the run store, so the two cannot describe the same
/// measurement differently - the previous pair did, and the stored file claimed its series were
/// missing when they were not. The socket copy is budgeted: above
/// <see cref="WireWeeklyRecordBudget"/> the per-obstacle and per-surface weekly series are dropped,
/// then the annual ones, and <see cref="Complete"/> and <see cref="Omitted"/> say so. The stored
/// copy is never budgeted.
/// </para>
/// <para>
/// Weekly, annual and whole-run records only: the study asked for weeks, and a season of daily
/// records over sixteen hundred trays was tens of megabytes nobody read. Series are dense arrays
/// aligned with <see cref="Periods"/>, so index is week; a week without a daylight step is null.
/// </para>
/// </remarks>
public class ObstacleLightDocument
{
    public const string FormatTag = "greenfdt.leaf-area-index/2";

    /// <summary>
    /// Non-empty weekly records the socket copy may carry over every obstacle and surface before
    /// the per-obstacle series are dropped in favour of totals. About 200 bytes a record, so this is
    /// some four megabytes, the same order the daily budget allowed. A facade of four hundred trays
    /// exceeds it after about two months; a handful of named walls never will.
    /// </summary>
    public const int WireWeeklyRecordBudget = 20_000;

    public string Format { get; set; } = FormatTag;
    ///<summary>When this document was built, UTC.</summary>
    public string Generated { get; set; }
    ///<summary>Which run this measures; null on the synchronous request path, which stores nothing.</summary>
    public string RunId { get; set; }
    ///<summary>False when a series was dropped for size; see <see cref="Omitted"/>.</summary>
    public bool Complete { get; set; }
    ///<summary>Paths of the series that were left out, e.g. <c>obstacles[].weeks</c>. Empty when complete.</summary>
    public string[] Omitted { get; set; } = [];
    public ObstacleLightMetaResponse Meta { get; set; }
    ///<summary>Units and meaning of every field, so the file can be read without this source.</summary>
    public Dictionary<string, string> Definitions { get; set; }
    public ObstacleLightPeriodsResponse Periods { get; set; }
    ///<summary>The whole selection as one patch; null when the filter matched nothing.</summary>
    public ObstacleLightSelectionResponse Selection { get; set; }
    public ObstacleLightObstacleResponse[] Obstacles { get; set; } = [];
    ///<summary>The vegetation of every tray as one patch; null when there is no tray to survey.</summary>
    public VegetationPatchResponse Facade { get; set; }
    public VegetationTrayResponse[] Trays { get; set; } = [];

    /// <summary>
    /// Snapshots the meter and the survey. Returns null when nothing was asked for: no obstacle
    /// filter and no imported tray system.
    /// </summary>
    /// <param name="weeklyRecordBudget">Null keeps every series; a number applies the wire budget.</param>
    public static ObstacleLightDocument From(AgroWorld world, string runId, int? weeklyRecordBudget)
    {
        var meter = world.ObstacleLight;
        var survey = world.Vegetation;
        var filter = world.ObstacleLightSelection.Filter;
        if (filter == null && !survey.Imported)
            return null;

        meter?.Finish();
        survey.Finish();
        var calendar = world.Periods;

        var document = new ObstacleLightDocument
        {
            Generated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            RunId = runId,
            Meta = ObstacleLightMetaResponse.From(world, meter, world.ObstacleList),
            Definitions = ObstacleLightDefinitions.All,
            Periods = ObstacleLightPeriodsResponse.From(calendar),
        };

        if (meter != null)
        {
            //One weekly record per week with daylight, for the object and for each of its
            //facade-parallel surfaces. Counted before anything is built, so the decision is one
            //decision rather than a different one per obstacle.
            var weekly = 0L;
            var annual = 0L;
            foreach (var target in meter.Targets)
            {
                weekly += NonEmpty(target.Series.Weeks);
                annual += target.Series.Years.Length;
                foreach (var surface in target.Surfaces)
                    if (surface.Series != null)
                    {
                        weekly += NonEmpty(surface.Series.Weeks);
                        annual += surface.Series.Years.Length;
                    }
            }
            var keepWeeks = weeklyRecordBudget == null || weekly <= weeklyRecordBudget;
            var keepYears = keepWeeks || annual <= weeklyRecordBudget;
            var omitted = new List<string>();
            if (!keepWeeks) omitted.AddRange(["obstacles[].weeks", "obstacles[].surfaces[].weeks"]);
            if (!keepYears) omitted.AddRange(["obstacles[].years", "obstacles[].surfaces[].years"]);

            document.Selection = ObstacleLightSelectionResponse.From(meter);
            document.Obstacles = new ObstacleLightObstacleResponse[meter.Targets.Count];
            for (int t = 0; t < document.Obstacles.Length; ++t)
                document.Obstacles[t] = ObstacleLightObstacleResponse.From(meter.Targets[t], keepWeeks, keepYears);
            document.Omitted = [.. omitted];
        }

        if (survey.Imported || survey.Trays.Count > 0)
        {
            document.Facade = VegetationPatchResponse.From(survey);
            document.Trays = new VegetationTrayResponse[survey.Trays.Count];
            for (int t = 0; t < document.Trays.Length; ++t)
                document.Trays[t] = VegetationTrayResponse.From(survey.Trays[t]);
        }

        document.Complete = document.Omitted.Length == 0;
        return document;
    }

    static int NonEmpty(ObstacleLightPeriod[] weeks)
    {
        var count = 0;
        foreach (var week in weeks)
            if (!week.IsEmpty)
                ++count;
        return count;
    }

    /// <summary>The document as a JSON element, camelCase and rounded exactly as the stored file
    /// is, for embedding in a payload whose own serializer must not be given a float converter.</summary>
    public static JsonElement? ToElement(ObstacleLightDocument document) => document == null
        ? null
        : JsonSerializer.SerializeToElement(document, ObstacleLightJsonContext.Default.ObstacleLightDocument);

    internal static float[] Xyz(Vector3 v) => [v.X, v.Y, v.Z];
    internal static string Date(DateTime d) => d.ToString("yyyy-MM-dd");
}

/// <summary>What the measurement was, so that an exported file stands on its own.</summary>
public class ObstacleLightMetaResponse
{
    ///<summary>The obstacle name filter as requested.</summary>
    public string Filter { get; set; }
    ///<summary>"regex" or "substring": the rule actually applied. Substring when the pattern did not parse.</summary>
    public string FilterMode { get; set; }
    ///<summary>Why the regular expression was not used, or null.</summary>
    public string FilterError { get; set; }
    ///<summary>Which light model produced the numbers.</summary>
    public string Renderer { get; set; }
    public float Latitude { get; set; }
    public float Longitude { get; set; }
    /// <summary>Metres above sea level; it scales the direct beam the surfaces received.</summary>
    public float Altitude { get; set; }
    /// <summary>Rotation of the scene about the up axis, degrees. It moves every sun position relative
    /// to the measured surfaces, so without it nothing in the file says which orientation it describes.</summary>
    public float Azimuth { get; set; }
    ///<summary>IANA time zone the dates are in.</summary>
    public string TimeZone { get; set; }
    ///<summary>First simulated day, local.</summary>
    public string InitialDate { get; set; }
    public int HoursPerTick { get; set; }
    public int TotalHours { get; set; }
    ///<summary>How far in front of a surface foliage counted, metres.</summary>
    public float CanopyDepth { get; set; }
    ///<summary>Height of one band, metres.</summary>
    public float BandHeight { get; set; }
    ///<summary>Measurement points actually placed.</summary>
    public int Sensors { get; set; }
    ///<summary>Obstacles the scene offered the filter.</summary>
    public int ObstaclesAvailable { get; set; }
    ///<summary>How many of them the filter matched. Zero with a non-empty filter means the filter
    ///is wrong, not that the measurement failed to run - the distinction the HUD reports.</summary>
    public int ObstaclesMatched { get; set; }
    ///<summary>A few of the available names, so a mistyped filter can be corrected without
    ///going back to the source model.</summary>
    public string[] ObstacleNameSamples { get; set; }
    ///<summary>Broadband shortwave albedo assumed for foliage and for obstacle surfaces, which the
    ///inter-reflected part of the load depends on.</summary>
    public float FoliageAlbedo { get; set; }
    public float ObstacleAlbedo { get; set; }
    ///<summary>Which days were actually measured. Both the meter and the survey are carried across
    ///a checkpoint, so a resumed run continues the count from the original start date.</summary>
    public ObstacleLightCoverageResponse Coverage { get; set; }
    ///<summary>Per species present, the one-sided outline area over the rectangle Length x 2 Radius
    ///that the light model stands a blade in, one factor per blade slice. The tray leaf areas use
    ///the outline; the ray-traced leaf area indices use the rectangle.</summary>
    public ObstacleLightSpeciesResponse[] Species { get; set; } = [];

    public static ObstacleLightMetaResponse From(AgroWorld world, ObstacleLightMeter meter,
        IReadOnlyList<IObstacle> obstacles)
    {
        var filter = world.Irradiance.ObstacleLightFilter;
        var survey = world.Vegetation;
        return new()
        {
            Filter = world.ObstacleLightSelection.Filter,
            FilterMode = filter?.Mode,
            FilterError = filter?.Error,
            Renderer = world.RendererName,
            Latitude = world.Latitude,
            Longitude = world.Longitude,
            Altitude = world.Altitude,
            Azimuth = world.Azimuth,
            TimeZone = world.TimeZone.Id,
            InitialDate = world.InitialTime.ToString("yyyy-MM-dd"),
            HoursPerTick = world.HoursPerTick,
            TotalHours = world.TotalHours,
            CanopyDepth = meter?.CanopyDepth_m ?? 0f,
            BandHeight = meter?.BandHeight_m ?? 0f,
            Sensors = meter?.SensorCount ?? 0,
            ObstaclesAvailable = world.ObstacleLightSelection.Candidates,
            ObstaclesMatched = world.ObstacleLightSelection.Matches,
            ObstacleNameSamples = obstacles is null ? [] : [.. obstacles.Take(8).Select(o => o.Name)],
            FoliageAlbedo = CpuLightSolver.FoliageAlbedo,
            ObstacleAlbedo = CpuLightSolver.ObstacleAlbedo,
            Coverage = new()
            {
                FirstDate = meter?.FirstDate is DateTime first ? ObstacleLightDocument.Date(first) : null,
                LastDate = meter?.LastDate is DateTime last ? ObstacleLightDocument.Date(last) : null,
                LitDays = meter?.LitDays ?? 0,
                SurveyFirstDate = survey.FirstDate is DateTime sf ? ObstacleLightDocument.Date(sf) : null,
                SurveyLastDate = survey.LastSurveyedDate is DateTime sl ? ObstacleLightDocument.Date(sl) : null,
                SurveyedDays = survey.SurveyedDays,
            },
            Species = [.. survey.SpeciesFactors.Select(p => new ObstacleLightSpeciesResponse { Name = p.Key, LeafOutlineAreaRatio = p.Value })],
        };
    }
}

public class ObstacleLightCoverageResponse
{
    ///<summary>Local date of the first and of the last step the light meter measured, or null.</summary>
    public string FirstDate { get; set; }
    public string LastDate { get; set; }
    ///<summary>Distinct local dates with a daylight step measured.</summary>
    public int LitDays { get; set; }
    ///<summary>Local dates of the first and the last vegetation survey, or null.</summary>
    public string SurveyFirstDate { get; set; }
    public string SurveyLastDate { get; set; }
    public int SurveyedDays { get; set; }
}

public class ObstacleLightSpeciesResponse
{
    public string Name { get; set; }
    public float[] LeafOutlineAreaRatio { get; set; }
}

/// <summary>The reporting periods every weekly and annual array is aligned with.</summary>
public class ObstacleLightPeriodsResponse
{
    public string WeekRule { get; set; }
    public string YearRule { get; set; }
    public ObstacleLightWeekResponse[] Weeks { get; set; }
    public ObstacleLightYearResponse[] Years { get; set; }
    public ObstacleLightSpanResponse Total { get; set; }

    internal static ObstacleLightPeriodsResponse From(PeriodCalendar calendar)
    {
        var weeks = new ObstacleLightWeekResponse[calendar.WeekCount];
        for (int i = 0; i < weeks.Length; ++i)
        {
            var (start, end) = calendar.Week(i);
            weeks[i] = new() { Index = i, Start = ObstacleLightDocument.Date(start), End = ObstacleLightDocument.Date(end) };
        }
        var years = new ObstacleLightYearResponse[calendar.YearCount];
        for (int i = 0; i < years.Length; ++i)
        {
            var (year, start, end) = calendar.Year(i);
            years[i] = new() { Year = year, Start = ObstacleLightDocument.Date(start), End = ObstacleLightDocument.Date(end) };
        }
        return new()
        {
            WeekRule = PeriodCalendar.WeekRule,
            YearRule = PeriodCalendar.YearRule,
            Weeks = weeks,
            Years = years,
            Total = new() { Start = ObstacleLightDocument.Date(calendar.Start), End = ObstacleLightDocument.Date(calendar.End) },
        };
    }
}

public class ObstacleLightWeekResponse
{
    public int Index { get; set; }
    ///<summary>First and last local date of the week, inclusive.</summary>
    public string Start { get; set; }
    public string End { get; set; }
}

public class ObstacleLightYearResponse
{
    public int Year { get; set; }
    public string Start { get; set; }
    public string End { get; set; }
}

public class ObstacleLightSpanResponse
{
    public string Start { get; set; }
    public string End { get; set; }
}

/// <summary>One period's light and foliage readings on one patch. A class rather than a struct so
/// a weekly array can hold null for a week without daylight.</summary>
public class ObstacleLightPeriodResponse
{
    ///<summary>Hours the sky was bright enough to trace. Zero means no reading.</summary>
    public float DaylightHours { get; set; }
    ///<summary>Direct plus diffuse irradiation with the vegetation removed, kWh/m².</summary>
    public float OpenSky_kWh_m2 { get; set; }
    ///<summary>Irradiation the vegetation kept off one square metre, kWh/m².</summary>
    public float Avoided_kWh_m2 { get; set; }
    ///<summary>1 - received / openSky: the share of the solar gain the vegetation keeps off.</summary>
    public float ShadedFraction { get; set; }
    public float BeamTransmittance { get; set; }
    public float DiffuseTransmittance { get; set; }
    ///<summary>Total one-sided leaf area per unit patch area, mean over the period's daylight hours.</summary>
    public float LeafAreaIndex { get; set; }
    public float ProjectedLeafAreaIndex { get; set; }
    public float LeafAreaIndexFromGaps { get; set; }
    ///<summary>Largest irradiance the vegetation removed at any moment, W/m².</summary>
    public float PeakReduction_W_m2 { get; set; }
    ///<summary>Highest per-step value of the period. Annual and whole-run records only.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? LeafAreaIndexMax { get; set; }

    internal static ObstacleLightPeriodResponse From(in ObstacleLightPeriod p, bool withMax) => new()
    {
        DaylightHours = p.DaylightHours,
        OpenSky_kWh_m2 = p.OpenSky_Wh_m2 * 1e-3f,
        Avoided_kWh_m2 = p.Avoided_Wh_m2 * 1e-3f,
        ShadedFraction = p.ShadedFraction,
        BeamTransmittance = p.BeamTransmittance,
        DiffuseTransmittance = p.DiffuseTransmittance,
        LeafAreaIndex = p.LeafAreaIndex,
        ProjectedLeafAreaIndex = p.ProjectedLeafAreaIndex,
        LeafAreaIndexFromGaps = p.LeafAreaIndexFromGaps,
        PeakReduction_W_m2 = p.PeakReduction_W_m2,
        LeafAreaIndexMax = withMax ? p.LeafAreaIndexMax : null,
    };

    ///<summary>Dense weekly array; an empty week is null.</summary>
    internal static ObstacleLightPeriodResponse[] Weekly(ObstacleLightSeries series)
    {
        var result = new ObstacleLightPeriodResponse[series.Weeks.Length];
        for (int i = 0; i < result.Length; ++i)
            result[i] = series.Weeks[i].IsEmpty ? null : From(in series.Weeks[i], withMax: false);
        return result;
    }

    internal static ObstacleLightPeriodResponse[] Annual(ObstacleLightSeries series)
    {
        var result = new ObstacleLightPeriodResponse[series.Years.Length];
        for (int i = 0; i < result.Length; ++i)
            result[i] = From(in series.Years[i], withMax: true);
        return result;
    }
}

/// <summary>One height slice of one orientation of an obstacle: the unit everything is measured on.
/// Whole-run total only - the vertical gradient is a shape, not a time series.</summary>
public class ObstacleLightBandResponse
{
    ///<summary>Lower and upper edge of the band in world coordinates, metres.</summary>
    public float MinY { get; set; }
    public float MaxY { get; set; }
    ///<summary>Surface area of the slice, m².</summary>
    public float Area { get; set; }
    public ObstacleLightPeriodResponse Total { get; set; }
}

/// <summary>One orientation and side of an obstacle: the level to quote for an intensive reading.</summary>
public class ObstacleLightSurfaceResponse
{
    ///<summary>"N".."NW" for the eight compass sectors, "up" or "down" for near-horizontal.</summary>
    public string Orientation { get; set; }
    ///<summary>"outer": the facade face; "inner": a face set back from it, the back wall of a tray
    ///seen through its opening; "edge": a rim or return perpendicular to the facade.</summary>
    public string Side { get; set; }
    ///<summary>Compass bearing it faces, degrees clockwise from north.</summary>
    public float AzimuthDegrees { get; set; }
    ///<summary>Tilt from horizontal, degrees: 0 faces up, 90 is vertical.</summary>
    public float TiltDegrees { get; set; }
    ///<summary>Surface area, m².</summary>
    public float Area { get; set; }
    ///<summary>How far this side sits out from the object's facade plane, metres.</summary>
    public float Depth { get; set; }
    ///<summary>Weekly series; null for an edge side and when dropped for size.</summary>
    public ObstacleLightPeriodResponse[] Weeks { get; set; }
    public ObstacleLightPeriodResponse[] Years { get; set; }
    public ObstacleLightPeriodResponse Total { get; set; }
    ///<summary>Height slices, bottom to top.</summary>
    public ObstacleLightBandResponse[] Bands { get; set; }
}

/// <summary>The whole selection as one patch. Its series are never dropped.</summary>
public class ObstacleLightSelectionResponse
{
    ///<summary>Total area of every measured obstacle, m².</summary>
    public float Area { get; set; }
    ///<summary>The part of it that carries readings, m².</summary>
    public float ExposedArea { get; set; }
    ///<summary>The part whose light-receiving side is enclosed by the building, m².</summary>
    public float EnclosedArea { get; set; }
    public int Obstacles { get; set; }
    public ObstacleLightPeriodResponse[] Weeks { get; set; }
    public ObstacleLightPeriodResponse[] Years { get; set; }
    public ObstacleLightPeriodResponse Total { get; set; }

    internal static ObstacleLightSelectionResponse From(ObstacleLightMeter meter)
    {
        var series = meter.SelectionSeries;
        return new()
        {
            Area = meter.SelectionArea_m2,
            ExposedArea = meter.SelectionExposedArea_m2,
            EnclosedArea = meter.SelectionEnclosedArea_m2,
            Obstacles = meter.Targets.Count,
            Weeks = ObstacleLightPeriodResponse.Weekly(series),
            Years = ObstacleLightPeriodResponse.Annual(series),
            Total = ObstacleLightPeriodResponse.From(series.Total, withMax: true),
        };
    }
}

/// <summary>Leaf area index and solar load of one obstacle.</summary>
public class ObstacleLightObstacleResponse
{
    ///<summary>Name of the source object, as imported.</summary>
    public string Name { get; set; }
    ///<summary>Total surface area, both faces of a solid included, m².</summary>
    public float Area { get; set; }
    ///<summary>Share of it that can see the sky past the building itself, m².</summary>
    public float ExposedArea { get; set; }
    ///<summary>Share whose light-receiving side is enclosed and carries no reading, m².</summary>
    public float EnclosedArea { get; set; }
    ///<summary>Orientation of the surface carrying the most solar load: the facade the object is
    ///mostly about. A label, not an average.</summary>
    public string DominantOrientation { get; set; }
    public float AzimuthDegrees { get; set; }
    public float TiltDegrees { get; set; }
    ///<summary>Area-weighted centre of the exposed surface, world coordinates.</summary>
    [JsonConverter(typeof(ExactFloatArrayConverter))]
    public float[] Centroid { get; set; }
    [JsonConverter(typeof(ExactFloatArrayConverter))]
    public float[] BoundsMin { get; set; }
    [JsonConverter(typeof(ExactFloatArrayConverter))]
    public float[] BoundsMax { get; set; }
    ///<summary>Whole-object weekly series; null when dropped for size. Its energies are exact, its
    ///intensive readings mix orientations.</summary>
    public ObstacleLightPeriodResponse[] Weeks { get; set; }
    public ObstacleLightPeriodResponse[] Years { get; set; }
    public ObstacleLightPeriodResponse Total { get; set; }
    ///<summary>Orientations and sides present, the one carrying the most solar load first.</summary>
    public ObstacleLightSurfaceResponse[] Surfaces { get; set; }

    internal static ObstacleLightObstacleResponse From(ObstacleLightMeter.Target target, bool keepWeeks, bool keepYears)
    {
        var surfaces = new ObstacleLightSurfaceResponse[target.Surfaces.Length];
        for (int s = 0; s < surfaces.Length; ++s)
        {
            var surface = target.Surfaces[s];
            var bands = new ObstacleLightBandResponse[surface.Bands.Length];
            for (int b = 0; b < bands.Length; ++b)
            {
                var band = surface.Bands[b];
                bands[b] = new()
                {
                    MinY = band.MinY,
                    MaxY = band.MaxY,
                    Area = band.Area_m2,
                    Total = ObstacleLightPeriodResponse.From(band.Total, withMax: true),
                };
            }
            surfaces[s] = new()
            {
                Orientation = surface.Orientation,
                Side = surface.Side,
                AzimuthDegrees = surface.AzimuthDegrees,
                TiltDegrees = surface.TiltDegrees,
                Area = surface.Area_m2,
                Depth = surface.Depth_m,
                Weeks = surface.Series != null && keepWeeks ? ObstacleLightPeriodResponse.Weekly(surface.Series) : null,
                Years = surface.Series != null && keepYears ? ObstacleLightPeriodResponse.Annual(surface.Series) : null,
                Total = ObstacleLightPeriodResponse.From(surface.Total, withMax: true),
                Bands = bands,
            };
        }

        return new()
        {
            Name = target.Name,
            Area = target.Area_m2,
            ExposedArea = target.ExposedArea_m2,
            EnclosedArea = target.EnclosedArea_m2,
            DominantOrientation = surfaces.Length > 0 ? surfaces[0].Orientation : string.Empty,
            AzimuthDegrees = surfaces.Length > 0 ? surfaces[0].AzimuthDegrees : 0f,
            TiltDegrees = surfaces.Length > 0 ? surfaces[0].TiltDegrees : 0f,
            Centroid = ObstacleLightDocument.Xyz(target.Centroid),
            BoundsMin = ObstacleLightDocument.Xyz(target.BoundsMin),
            BoundsMax = ObstacleLightDocument.Xyz(target.BoundsMax),
            Weeks = keepWeeks ? ObstacleLightPeriodResponse.Weekly(target.Series) : null,
            Years = keepYears ? ObstacleLightPeriodResponse.Annual(target.Series) : null,
            Total = ObstacleLightPeriodResponse.From(target.Series.Total, withMax: true),
            Surfaces = surfaces,
        };
    }
}

/// <summary>The foliage of one tray, or of the whole facade, over one period.</summary>
public class VegetationPeriodResponse
{
    ///<summary>Daily surveys that contributed. Zero means no reading.</summary>
    public int Days { get; set; }
    ///<summary>One-sided leaf area, m², mean over the days.</summary>
    public float LeafArea_m2 { get; set; }
    ///<summary>Area-weighted mean of sin(tilt): 0 horizontal foliage, 1 vertical.</summary>
    public float LeafAngleSine { get; set; }
    ///<summary>Area-weighted mean equivalent-circle diameter of a whole blade, metres.</summary>
    public float LeafDiameter_m { get; set; }
    ///<summary>Thickness of the vegetation layer outward from the back wall, metres, mean over the days.</summary>
    public float Thickness_m { get; set; }
    ///<summary>Mean number of blades.</summary>
    public float Leaves { get; set; }
    ///<summary>Total ordinary foliage blades physically removed during this period, counted once
    ///per whole blade. Excludes floral bracts and blades that remain attached after dying.</summary>
    public long LeavesShed { get; set; }
    ///<summary>One-sided area of blades killed by drought and still attached, m², mean over the days.
    ///The geometric foliage fields describe living blades only; LeavesShed counts removals.</summary>
    public float DeadLeafArea_m2 { get; set; }
    ///<summary>Annual and whole-run records only.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? LeafAreaMax_m2 { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? LeafAreaFinal_m2 { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? ThicknessMax_m { get; set; }

    internal static VegetationPeriodResponse From(in VegetationPeriod p, bool withExtremes) => new()
    {
        Days = p.Days,
        LeafArea_m2 = p.LeafArea_m2,
        LeafAngleSine = p.LeafAngleSine,
        LeafDiameter_m = p.LeafDiameter_m,
        Thickness_m = p.Thickness_m,
        Leaves = p.Leaves,
        LeavesShed = p.LeavesShed,
        DeadLeafArea_m2 = p.DeadLeafArea_m2,
        LeafAreaMax_m2 = withExtremes ? p.LeafAreaMax_m2 : null,
        LeafAreaFinal_m2 = withExtremes ? p.LeafAreaFinal_m2 : null,
        ThicknessMax_m = withExtremes ? p.ThicknessMax_m : null,
    };

    internal static VegetationPeriodResponse[] Weekly(VegetationSeries series)
    {
        var result = new VegetationPeriodResponse[series.Weeks.Length];
        for (int i = 0; i < result.Length; ++i)
            result[i] = series.Weeks[i].IsEmpty ? null : From(in series.Weeks[i], withExtremes: false);
        return result;
    }

    internal static VegetationPeriodResponse[] Annual(VegetationSeries series)
    {
        var result = new VegetationPeriodResponse[series.Years.Length];
        for (int i = 0; i < result.Length; ++i)
            result[i] = From(in series.Years[i], withExtremes: true);
        return result;
    }
}

/// <summary>Every tray as one patch: leaf areas summed, angle and diameter area-weighted over all
/// trays, thickness the largest of any tray.</summary>
public class VegetationPatchResponse
{
    public int Trays { get; set; }
    public int Plants { get; set; }
    ///<summary>Unit normal of the facade pointing away from the building, world coordinates, along
    ///which the thickness is measured. Up when the trays stand on the ground or have no mesh.</summary>
    [JsonConverter(typeof(ExactFloatArrayConverter))]
    public float[] Outward { get; set; }
    public VegetationPeriodResponse[] Weeks { get; set; }
    public VegetationPeriodResponse[] Years { get; set; }
    public VegetationPeriodResponse Total { get; set; }

    internal static VegetationPatchResponse From(TrayVegetationSurvey survey)
    {
        var series = survey.FacadeSeries;
        return new()
        {
            Trays = survey.Trays.Count,
            Plants = survey.Plants,
            Outward = ObstacleLightDocument.Xyz(survey.Outward),
            Weeks = series == null ? [] : VegetationPeriodResponse.Weekly(series),
            Years = series == null ? [] : VegetationPeriodResponse.Annual(series),
            Total = series == null ? null : VegetationPeriodResponse.From(series.Total, withExtremes: true),
        };
    }
}

/// <summary>The vegetation of one tray: what a microclimate model needs to describe its canopy.</summary>
public class VegetationTrayResponse
{
    ///<summary>Imported object name of the tray; the soils inside it carry the same name with an _S part.</summary>
    public string Name { get; set; }
    public string[] Soils { get; set; }
    ///<summary>Plants seeded in it, alive or not.</summary>
    public int Plants { get; set; }
    ///<summary>Plants per species name.</summary>
    public Dictionary<string, int> Species { get; set; }
    ///<summary>True when the tray's own mesh supplied its extents and back wall.</summary>
    public bool HasMesh { get; set; }
    ///<summary>Extents of the tray mesh, metres: along the facade, vertical, and outward.</summary>
    public float Width_m { get; set; }
    public float Height_m { get; set; }
    public float Depth_m { get; set; }
    [JsonConverter(typeof(ExactFloatArrayConverter))]
    public float[] Centroid { get; set; }
    public VegetationPeriodResponse[] Weeks { get; set; }
    public VegetationPeriodResponse[] Years { get; set; }
    public VegetationPeriodResponse Total { get; set; }

    internal static VegetationTrayResponse From(TrayVegetationSurvey.Tray tray)
    {
        var series = tray.Series;
        return new()
        {
            Name = tray.Name,
            Soils = [.. tray.Soils],
            Plants = tray.Plants,
            Species = new(tray.Species),
            HasMesh = tray.HasMesh,
            Width_m = tray.Width_m,
            Height_m = tray.Height_m,
            Depth_m = tray.Depth_m,
            Centroid = ObstacleLightDocument.Xyz(tray.Centroid),
            Weeks = series == null ? [] : VegetationPeriodResponse.Weekly(series),
            Years = series == null ? [] : VegetationPeriodResponse.Annual(series),
            Total = series == null ? null : VegetationPeriodResponse.From(series.Total, withExtremes: true),
        };
    }
}

/// <summary>
/// Writes floats with four significant digits. A measurement to one part in ten thousand is more
/// than the ray count justifies, and the digits beyond it are what gzip cannot compress.
/// </summary>
public sealed class SignificantFloatConverter : JsonConverter<float>
{
    const int Digits = 4;

    public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetSingle();

    public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options)
    {
        if (!float.IsFinite(value))
        {
            //JSON has no NaN; null is the honest spelling of "no number" rather than a zero
            //that would read as a measurement.
            writer.WriteNullValue();
            return;
        }
        writer.WriteNumberValue(Round(value));
    }

    internal static double Round(float value)
    {
        if (value == 0f)
            return 0.0;
        double d = value;
        var decimals = Digits - 1 - (int)Math.Floor(Math.Log10(Math.Abs(d)));
        if (decimals >= 0)
            return Math.Round(d, Math.Min(decimals, 15));
        var scale = Math.Pow(10.0, -decimals);
        return Math.Round(d / scale) * scale;
    }
}

/// <summary>Full-precision float arrays, for coordinates: four significant digits would put a
/// facade thirty metres from the origin to the centimetre.</summary>
public sealed class ExactFloatArrayConverter : JsonConverter<float[]>
{
    public override float[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new List<float>();
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            result.Add(reader.GetSingle());
        return [.. result];
    }

    public override void Write(Utf8JsonWriter writer, float[] value, JsonSerializerOptions options)
    {
        if (value == null) { writer.WriteNullValue(); return; }
        writer.WriteStartArray();
        foreach (var v in value)
            if (float.IsFinite(v)) writer.WriteNumberValue(v); else writer.WriteNullValue();
        writer.WriteEndArray();
    }
}
