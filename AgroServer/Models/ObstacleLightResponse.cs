using Agro;
using AgentsSystem;
using System.Numerics;

namespace AgroServer.Models;

/// <summary>
/// What the measurement was, so that an exported file stands on its own.
/// </summary>
public class ObstacleLightMetaResponse
{
    ///<summary>Substring that selected the obstacles.</summary>
    public string Filter { get; set; }
    ///<summary>Which light model produced the numbers.</summary>
    public string Renderer { get; set; }
    public float Latitude { get; set; }
    public float Longitude { get; set; }
    /// <summary>Metres above sea level; it scales the direct beam the surfaces received.</summary>
    public float Altitude { get; set; }
    /// <summary>
    /// Rotation of the scene about the up axis, degrees. Recorded because it moves every sun
    /// position relative to the measured surfaces: two documents from the same model at
    /// different azimuths carry substantially different beam hours, and without this field
    /// nothing in either file says which orientation it describes.
    /// </summary>
    public float Azimuth { get; set; }
    ///<summary>IANA time zone the daily dates are in.</summary>
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
    ///<summary>True when the per-obstacle daily series were left out to keep the payload sane;
    ///the summaries and the selection-wide series are always present.</summary>
    public bool DailySeriesOmitted { get; set; }
    ///<summary>Broadband shortwave albedo assumed for foliage and for obstacle surfaces, which the
    ///inter-reflected part of the load depends on.</summary>
    public float FoliageAlbedo { get; set; }
    public float ObstacleAlbedo { get; set; }

    /// <summary>
    /// Describes the measurement, whether or not it produced anything. It is emitted even when the
    /// filter matched nothing, because "your filter is wrong" and "the measurement never ran" look
    /// identical from the client otherwise.
    /// </summary>
    public static ObstacleLightMetaResponse From(AgroWorld world, ObstacleLightMeter meter,
        IReadOnlyList<IObstacle> obstacles) => new()
    {
        Filter = world.ObstacleLightSelection.Filter,
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
    };
}

/// <summary>One day of readings, as sent to the client. A flat record so a series of them is a
/// compact array of objects and every field is self-describing.</summary>
public class ObstacleLightDayResponse
{
    ///<summary>Local calendar date at the site, yyyy-MM-dd.</summary>
    public string Date { get; set; }
    ///<summary>Simulation steps that contributed.</summary>
    public int Steps { get; set; }
    ///<summary>Hours the sky was bright enough to trace.</summary>
    public float DaylightHours { get; set; }
    ///<summary>Hours the direct beam reached this patch at all, vegetation aside.</summary>
    public float DirectBeamHours { get; set; }
    ///<summary>Direct beam irradiation with no vegetation, Wh/m².</summary>
    public float BeamOpenSky { get; set; }
    ///<summary>Direct beam irradiation actually received, Wh/m².</summary>
    public float Beam { get; set; }
    ///<summary>Diffuse and inter-reflected irradiation with no vegetation, Wh/m².</summary>
    public float DiffuseOpenSky { get; set; }
    ///<summary>Diffuse and inter-reflected irradiation actually received, Wh/m².</summary>
    public float Diffuse { get; set; }
    ///<summary>Total irradiation actually received, Wh/m².</summary>
    public float Total { get; set; }
    ///<summary>Total irradiation with no vegetation, Wh/m².</summary>
    public float TotalOpenSky { get; set; }
    ///<summary>Irradiation the vegetation kept off one square metre, Wh/m².</summary>
    public float Avoided { get; set; }
    ///<summary>The same over the patch's whole exposed area, kWh.</summary>
    public float AvoidedTotal_kWh { get; set; }
    ///<summary>Fraction of the day's irradiation the vegetation keeps off.</summary>
    public float ShadedFraction { get; set; }
    ///<summary>Share of the direct beam that gets through, weighted by beam load.</summary>
    public float BeamTransmittance { get; set; }
    ///<summary>Share of the diffuse load that gets through.</summary>
    public float DiffuseTransmittance { get; set; }
    ///<summary>Geometric fraction of the cosine-weighted sky hemisphere free of foliage.</summary>
    public float GapFractionDiffuse { get; set; }
    ///<summary>Total one-sided leaf area per unit patch area, mean over the day.</summary>
    public float LeafAreaIndex { get; set; }
    ///<summary>Highest value the day reached.</summary>
    public float LeafAreaIndexMax { get; set; }
    ///<summary>Leaf area projected onto the patch per unit patch area.</summary>
    public float ProjectedLeafAreaIndex { get; set; }
    ///<summary>Beer-Lambert inversion of the diffuse gap fraction.</summary>
    public float LeafAreaIndexFromGaps { get; set; }
    ///<summary>Highest open-sky irradiance of the day, W/m².</summary>
    public float PeakOpenSky { get; set; }
    ///<summary>Largest irradiance the vegetation removed at any moment, W/m².</summary>
    public float PeakReduction { get; set; }

    internal static ObstacleLightDayResponse From(in ObstacleLightDay d, float exposedArea) => new()
    {
        Date = d.Date.ToString("yyyy-MM-dd"),
        Steps = d.Steps,
        DaylightHours = d.DaylightHours,
        DirectBeamHours = d.DirectBeamHours,
        BeamOpenSky = d.BeamOpenSky_Wh_m2,
        Beam = d.Beam_Wh_m2,
        DiffuseOpenSky = d.DiffuseOpenSky_Wh_m2,
        Diffuse = d.Diffuse_Wh_m2,
        Total = d.Total_Wh_m2,
        TotalOpenSky = d.TotalOpenSky_Wh_m2,
        Avoided = d.Avoided_Wh_m2,
        AvoidedTotal_kWh = d.Avoided_Wh_m2 * exposedArea * 1e-3f,
        ShadedFraction = d.ShadedFraction,
        BeamTransmittance = d.BeamTransmittance,
        DiffuseTransmittance = d.DiffuseTransmittance,
        GapFractionDiffuse = d.GapFractionDiffuse,
        LeafAreaIndex = d.LeafAreaIndex,
        LeafAreaIndexMax = d.LeafAreaIndexMax,
        ProjectedLeafAreaIndex = d.ProjectedLeafAreaIndex,
        LeafAreaIndexFromGaps = d.LeafAreaIndexFromGaps,
        PeakOpenSky = d.PeakOpenSky_W_m2,
        PeakReduction = d.PeakReduction_W_m2,
    };
}

/// <summary>Whole-period totals, same fields and same weighting rules as a day.</summary>
public class ObstacleLightSummaryResponse
{
    public int Days { get; set; }
    public float DaylightHours { get; set; }
    public float DirectBeamHours { get; set; }
    public float BeamOpenSky { get; set; }
    public float Beam { get; set; }
    public float DiffuseOpenSky { get; set; }
    public float Diffuse { get; set; }
    public float Total { get; set; }
    public float TotalOpenSky { get; set; }
    public float Avoided { get; set; }
    ///<summary>Solar energy the vegetation kept off this patch over the whole period, kWh.</summary>
    public float AvoidedTotal_kWh { get; set; }
    public float ShadedFraction { get; set; }
    public float BeamTransmittance { get; set; }
    public float DiffuseTransmittance { get; set; }
    public float GapFractionDiffuse { get; set; }
    ///<summary>Mean over the period, weighted by daylight hours.</summary>
    public float LeafAreaIndex { get; set; }
    ///<summary>Highest value reached, that is the developed canopy.</summary>
    public float LeafAreaIndexMax { get; set; }
    ///<summary>Value on the last measured day.</summary>
    public float LeafAreaIndexFinal { get; set; }
    public float ProjectedLeafAreaIndex { get; set; }
    public float LeafAreaIndexFromGaps { get; set; }
    public float PeakOpenSky { get; set; }
    public float PeakReduction { get; set; }

    internal static ObstacleLightSummaryResponse From(in ObstacleLightSummary s, float exposedArea) => new()
    {
        Days = s.Days,
        DaylightHours = s.DaylightHours,
        DirectBeamHours = s.DirectBeamHours,
        BeamOpenSky = s.BeamOpenSky_Wh_m2,
        Beam = s.Beam_Wh_m2,
        DiffuseOpenSky = s.DiffuseOpenSky_Wh_m2,
        Diffuse = s.Diffuse_Wh_m2,
        Total = s.Total_Wh_m2,
        TotalOpenSky = s.TotalOpenSky_Wh_m2,
        Avoided = s.Avoided_Wh_m2,
        AvoidedTotal_kWh = s.Avoided_Wh_m2 * exposedArea * 1e-3f,
        ShadedFraction = s.ShadedFraction,
        BeamTransmittance = s.BeamTransmittance,
        DiffuseTransmittance = s.DiffuseTransmittance,
        GapFractionDiffuse = s.GapFractionDiffuse,
        LeafAreaIndex = s.LeafAreaIndex,
        LeafAreaIndexMax = s.LeafAreaIndexMax,
        LeafAreaIndexFinal = s.LeafAreaIndexFinal,
        ProjectedLeafAreaIndex = s.ProjectedLeafAreaIndex,
        LeafAreaIndexFromGaps = s.LeafAreaIndexFromGaps,
        PeakOpenSky = s.PeakOpenSky_W_m2,
        PeakReduction = s.PeakReduction_W_m2,
    };
}

/// <summary>
/// One height slice of one orientation of an obstacle: the unit everything is measured on.
/// </summary>
/// <remarks>Only a summary, no daily series. The vertical gradient is what a band answers, and it is
/// a shape rather than a time series; the daily curve belongs to the surface and the object, and
/// carrying it for every band as well multiplies the file by the number of bands for no new
/// information.</remarks>
public class ObstacleLightBandResponse
{
    ///<summary>Lower edge of the band in world coordinates, metres.</summary>
    public float MinY { get; set; }
    ///<summary>Upper edge, metres.</summary>
    public float MaxY { get; set; }
    ///<summary>Surface area of the slice, m².</summary>
    public float Area { get; set; }
    public ObstacleLightSummaryResponse Summary { get; set; }
}

/// <summary>
/// One orientation of an obstacle - a facade, a roof, a reveal - and the level to quote whenever an
/// intensive reading matters.
/// </summary>
public class ObstacleLightSurfaceResponse
{
    ///<summary>"N".."NW" for the eight compass sectors, "up" or "down" for near-horizontal.</summary>
    public string Orientation { get; set; }
    ///<summary>Compass bearing it faces, degrees clockwise from north.</summary>
    public float AzimuthDegrees { get; set; }
    ///<summary>Tilt from horizontal, degrees: 0 faces up, 90 is vertical.</summary>
    public float TiltDegrees { get; set; }
    ///<summary>Surface area of this orientation, m².</summary>
    public float Area { get; set; }
    public ObstacleLightSummaryResponse Summary { get; set; }
    public ObstacleLightDayResponse[] Days { get; set; }
    ///<summary>Height slices, bottom to top.</summary>
    public ObstacleLightBandResponse[] Bands { get; set; }
}

/// <summary>
/// The whole selection as one patch. Always present, and the series to plot when the per-obstacle
/// ones were dropped for size.
/// </summary>
public class ObstacleLightSelectionResponse
{
    ///<summary>Total area of every measured obstacle, m².</summary>
    public float Area { get; set; }
    ///<summary>The part of it that carries readings, m².</summary>
    public float ExposedArea { get; set; }
    public int Obstacles { get; set; }
    public ObstacleLightSummaryResponse Summary { get; set; }
    public ObstacleLightDayResponse[] Days { get; set; }

    internal static ObstacleLightSelectionResponse From(ObstacleLightMeter meter)
    {
        var days = new ObstacleLightDayResponse[meter.SelectionDays.Count];
        for (int i = 0; i < days.Length; ++i)
            days[i] = ObstacleLightDayResponse.From(meter.SelectionDays[i], meter.SelectionExposedArea_m2);
        return new ObstacleLightSelectionResponse
        {
            Area = meter.SelectionArea_m2,
            ExposedArea = meter.SelectionExposedArea_m2,
            Obstacles = meter.Targets.Count,
            Summary = ObstacleLightSummaryResponse.From(meter.SelectionSummary, meter.SelectionExposedArea_m2),
            Days = days,
        };
    }
}

/// <summary>
/// Leaf area index and solar load of one obstacle over the simulated period.
/// </summary>
/// <remarks>
/// Days rather than simulation steps, because a season of per-step records over a few hundred facade
/// objects runs to hundreds of megabytes without answering anything a daily total does not. Every
/// irradiation is an energy per unit area over the day; every transmittance is a ratio of those
/// energies, never a mean of per-step ratios.
/// </remarks>
public class ObstacleLightResponse
{
    ///<summary>Name of the source object, as imported.</summary>
    public string Name { get; set; }
    ///<summary>Total surface area, both faces of a solid included, m².</summary>
    public float Area { get; set; }
    ///<summary>Share of it that can see the sky past the building itself, m².</summary>
    public float ExposedArea { get; set; }
    ///<summary>Orientation of the surface carrying the most solar load, which is the facade the
    ///object is mostly about. The mean over every face of a closed solid is meaningless, so this is
    ///a label, not an average.</summary>
    public string DominantOrientation { get; set; }
    ///<summary>Compass bearing of that surface, degrees clockwise from north.</summary>
    public float AzimuthDegrees { get; set; }
    ///<summary>Its tilt from horizontal, degrees: 0 faces up, 90 is a vertical wall.</summary>
    public float TiltDegrees { get; set; }
    ///<summary>Area-weighted centre of the exposed surface, world coordinates.</summary>
    public float[] Centroid { get; set; }
    public float[] BoundsMin { get; set; }
    public float[] BoundsMax { get; set; }
    ///<summary>Whole-object roll-up. Its energies are exact; its intensive readings mix
    ///orientations, so quote a surface where that matters.</summary>
    public ObstacleLightSummaryResponse Summary { get; set; }
    public ObstacleLightDayResponse[] Days { get; set; }
    ///<summary>Orientations present, the one carrying the most solar load first.</summary>
    public ObstacleLightSurfaceResponse[] Surfaces { get; set; }

    static float[] Xyz(Vector3 v) => [v.X, v.Y, v.Z];

    /// <summary>
    /// Day records the payload may carry across every obstacle before the per-obstacle series are
    /// dropped in favour of summaries.
    /// </summary>
    /// <remarks>
    /// A facade system imported as sixteen hundred trays would otherwise produce around a hundred
    /// megabytes of JSON - one daily series per tray plus one per orientation of each - which no
    /// browser is going to receive over a socket and nobody asked for. The summaries and the
    /// selection-wide daily series survive regardless, and those answer the question; narrowing the
    /// filter is what buys back the per-obstacle detail.
    /// </remarks>
    //Measured at roughly 740 bytes of JSON per record, so this is about six megabytes - already
    //generous for a socket message the browser has to hold. Sixteen hundred trays exceed it after
    //two days and fall back to summaries; a handful of named walls never will.
    const int DailyRecordBudget = 8_000;

    /// <summary>Snapshots the meter. Closes the day still in progress first, so this is safe to call
    /// exactly once at the end of a run.</summary>
    /// <param name="unbudgeted">
    /// Ignore <see cref="DailyRecordBudget"/> and keep every daily series. For the copy written to
    /// the run store and fetched over REST, where the payload is a file the browser saves rather
    /// than a socket message it has to hold - the budget is a property of the wire, not of the
    /// measurement.
    /// </param>
    public static List<ObstacleLightResponse> From(ObstacleLightMeter meter, out bool dailySeriesOmitted,
                                                   bool unbudgeted = false)
    {
        dailySeriesOmitted = false;
        if (meter == null)
            return null;
        meter.Finish();

        //One series per obstacle plus one per orientation of it.
        var projected = 0L;
        foreach (var target in meter.Targets)
        {
            projected += target.Days.Count;
            foreach (var surface in target.Surfaces)
                projected += surface.Days.Count;
        }
        var withDays = unbudgeted || projected <= DailyRecordBudget;
        dailySeriesOmitted = !withDays;

        var result = new List<ObstacleLightResponse>(meter.Targets.Count);
        foreach (var target in meter.Targets)
        {
            var days = withDays ? new ObstacleLightDayResponse[target.Days.Count] : [];
            for (int i = 0; i < days.Length; ++i)
                days[i] = ObstacleLightDayResponse.From(target.Days[i], target.ExposedArea_m2);

            var surfaces = new ObstacleLightSurfaceResponse[target.Surfaces.Length];
            for (int s = 0; s < surfaces.Length; ++s)
            {
                var surface = target.Surfaces[s];
                var surfaceDays = withDays ? new ObstacleLightDayResponse[surface.Days.Count] : [];
                for (int i = 0; i < surfaceDays.Length; ++i)
                    surfaceDays[i] = ObstacleLightDayResponse.From(surface.Days[i], surface.Area_m2);

                var bands = new ObstacleLightBandResponse[surface.Bands.Length];
                for (int b = 0; b < bands.Length; ++b)
                {
                    var band = surface.Bands[b];
                    bands[b] = new ObstacleLightBandResponse
                    {
                        MinY = band.MinY,
                        MaxY = band.MaxY,
                        Area = band.Area_m2,
                        Summary = ObstacleLightSummaryResponse.From(band.Summary, band.Area_m2),
                    };
                }

                surfaces[s] = new ObstacleLightSurfaceResponse
                {
                    Orientation = surface.Orientation,
                    AzimuthDegrees = surface.AzimuthDegrees,
                    TiltDegrees = surface.TiltDegrees,
                    Area = surface.Area_m2,
                    Summary = ObstacleLightSummaryResponse.From(surface.Summary, surface.Area_m2),
                    Days = surfaceDays,
                    Bands = bands,
                };
            }

            result.Add(new ObstacleLightResponse
            {
                Name = target.Name,
                Area = target.Area_m2,
                ExposedArea = target.ExposedArea_m2,
                DominantOrientation = surfaces.Length > 0 ? surfaces[0].Orientation : string.Empty,
                AzimuthDegrees = surfaces.Length > 0 ? surfaces[0].AzimuthDegrees : 0f,
                TiltDegrees = surfaces.Length > 0 ? surfaces[0].TiltDegrees : 0f,
                Centroid = Xyz(target.Centroid),
                BoundsMin = Xyz(target.BoundsMin),
                BoundsMax = Xyz(target.BoundsMax),
                Summary = ObstacleLightSummaryResponse.From(target.Summary, target.ExposedArea_m2),
                Days = days,
                Surfaces = surfaces,
            });
        }
        return result;
    }
}

/// <summary>
/// The whole measurement as one document, for the run store.
///
/// Exists because the socket copy is deliberately incomplete: above <c>DailyRecordBudget</c> every
/// per-obstacle daily series is dropped, which is right for a message the browser must hold in one
/// piece and wrong for the file a facade study actually wants. This one is written to disk and
/// served over REST, so its size is the browser's own business - it saves it without parsing it.
///
/// Deliberately carries no definitions block. That lives in the frontend, in the small document a
/// person opens first, and duplicating it here would give two copies to disagree.
/// </summary>
public class ObstacleLightDocumentResponse
{
    ///<summary>Which run this measures, so a saved file can be traced back.</summary>
    public string RunId { get; set; }
    public ObstacleLightMetaResponse Meta { get; set; }
    public ObstacleLightSelectionResponse Selection { get; set; }
    ///<summary>Every obstacle, every daily series present.</summary>
    public List<ObstacleLightResponse> Obstacles { get; set; }
}
