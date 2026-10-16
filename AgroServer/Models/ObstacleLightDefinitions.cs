namespace AgroServer.Models;

/// <summary>
/// Units and meaning of every field of the leaf area index document, embedded in it.
/// </summary>
/// <remarks>
/// Worth the bytes: "leaf area index" has no single definition and the variants below differ by a
/// factor of two on the same canopy, so a file that does not say which one it carries invites the
/// reader to assume the wrong one. Kept here, on the side that writes both the socket copy and the
/// stored file, so there is exactly one text to disagree with itself.
/// </remarks>
public static class ObstacleLightDefinitions
{
    public static readonly Dictionary<string, string> All = new()
    {
        ["document"] = "Leaf area index and solar load measured on the selected obstacles (obstacles, selection), "
            + "and the geometry of the foliage per planting tray (trays, facade). Weekly, annual and whole-run "
            + "records only. Every weeks[] and years[] array is dense and aligned with periods.weeks[] and "
            + "periods.years[]: the record at index i belongs to periods.weeks[i]. A null entry is a period "
            + "with no reading. The totals of every object are always present; complete says whether any "
            + "series was dropped for size and omitted lists which.",
        ["periods"] = "weeks: 7-day blocks counted from the local start date, the last one clipped to the run. "
            + "years: local calendar years, the first and last clipped to the run. Dates are local to meta.timeZone "
            + "and inclusive. A simulation step is credited to the date of its midpoint.",
        ["coverage"] = "Local dates actually measured. Neither measurement is checkpointed: a resumed run covers "
            + "only the days after the resume, and the weeks before it are null.",
        ["levels"] = "An obstacle holds surfaces (one per compass orientation and side), each holding height bands. "
            + "An obstacle's energies are exact; its intensive readings average over orientations, so quote a "
            + "surface for those. Surfaces on the facade-parallel sides (outer, inner) carry weekly and annual "
            + "series; rims and returns (edge) and bands carry a total only.",
        ["side"] = "outer: the facade face of the object. inner: a face set back from it - for a planting tray, "
            + "the back wall seen through the opening, i.e. the tray's projection onto the facade. edge: a rim "
            + "or return perpendicular to the facade. Both outer and inner face away from the building.",
        ["orientation"] = "Compass sector the surface faces: N, NE, E, SE, S, SW, W, NW, or up / down when within "
            + "30 degrees of horizontal.",
        ["area"] = "m². Total triangle area of the object, every face of a solid included.",
        ["exposedArea"] = "m². The part that can see the sky past the building itself; only it carries readings. "
            + "A sensor counts in full once at least 4 of 64 hemisphere rays escape the static scene.",
        ["enclosedArea"] = "m². The part whose light-receiving side is walled in by the building and carries no reading.",
        ["daylightHours"] = "h. Hours of the period during which the sky was bright enough to trace. Zero means no reading.",
        ["openSky_kWh_m2"] = "kWh/m². Direct plus diffuse and inter-reflected irradiation the surface would receive "
            + "with the vegetation removed, summed over the period.",
        ["avoided_kWh_m2"] = "kWh/m². openSky minus what was actually received: the irradiation the vegetation kept "
            + "off one square metre. Multiply by exposedArea for the energy kept off the whole object. Slightly "
            + "negative in low sun is physical, when a bright canopy reflects more onto the surface than the sky "
            + "it hides.",
        ["shadedFraction"] = "1 - received / openSky, irradiance weighted: the share of the solar gain the greening "
            + "layer keeps off, i.e. what a building energy model would apply as a shading multiplier. Only the "
            + "plants shade here - the building's own shadow is removed from both sides of the ratio.",
        ["beamTransmittance"] = "Share of the direct beam that reaches the surface through the foliage, weighted by "
            + "beam load. Every plant organ blocks the beam.",
        ["diffuseTransmittance"] = "Share of the diffuse and inter-reflected load that reaches the surface.",
        ["leafAreaIndex"] = "m²/m². Total one-sided leaf area per unit surface area, counted along the surface normal "
            + "up to canopyDepth in front of it, leaves only, through any intervening tray or wall. The classic "
            + "definition; roughly 2 to 8 for a developed living wall. Mean over the period's daylight hours. "
            + "Measured with the light model's rectangular leaf proxy - see species.leafOutlineAreaRatio.",
        ["projectedLeafAreaIndex"] = "m²/m². Leaf area PROJECTED onto the surface per unit surface area. Equals "
            + "leafAreaIndex for leaves parallel to the wall and about half of it for randomly oriented ones.",
        ["leafAreaIndexFromGaps"] = "m²/m². Beer-Lambert inversion of the diffuse gap fraction, -ln(P)/0.93, assuming "
            + "a spherical leaf angle distribution. What a canopy analyser held in front of the wall would report; "
            + "falls below leafAreaIndex where leaves clump.",
        ["leafAreaIndexMax"] = "m²/m². Highest per-step value of the period, i.e. the developed canopy rather than "
            + "its mean. Annual and whole-run records only.",
        ["peakReduction_W_m2"] = "W/m². Largest irradiance the vegetation removed at any moment of the period. The "
            + "number a peak cooling load wants.",
        ["aggregation"] = "Irradiations are sums over the period. Transmittances and shaded fractions are ratios of "
            + "those sums, never means of per-step ratios - a plain mean lets a dim step just after dawn count as "
            + "much as noon. Leaf area indices are means over daylight hours; peaks and maxima are maxima. A year "
            + "is folded from the same steps as its weeks and the total from the years, so the levels agree.",
        ["trays"] = "The foliage of every plant rooted in each planting tray, surveyed once per local day from the "
            + "geometry and averaged over the period. Independent of the light model. A tray is the imported "
            + "object whose soils carry its name with an _S part; a plain field is one tray named field.",
        ["facade"] = "Every tray as one patch: leafArea summed, leafAngleSine and leafDiameter weighted by leaf area "
            + "over all trays, thickness the largest of any tray. outward is the unit normal of the facade, "
            + "pointing away from the building, along which the thickness is measured.",
        ["leafArea_m2"] = "m². One-sided area of every blade in the tray, using the species' blade outline, mean "
            + "over the period's days. Petals are not leaves. Divide by your own facade cell area for a leaf "
            + "area index; divide by the volume of the vegetation layer for a leaf area density.",
        ["leafAngleSine"] = "Area-weighted mean over the blades of sin(alpha) = sqrt(1 - n_y²), alpha the tilt of "
            + "the blade from the horizontal and n its unit normal: 0 for horizontal foliage, 1 for vertical. The "
            + "sine is taken per blade and then averaged, never the other way round.",
        ["leafDiameter_m"] = "m. Area-weighted mean over the blades of 2 sqrt(A / pi), the diameter of the disc with "
            + "the same one-sided area as the whole blade.",
        ["thickness_m"] = "m. Thickness of the vegetation layer, measured from the tray's back wall outward along "
            + "the facade normal to the farthest leaf corner, as a bounding box would give it; mean over the "
            + "period's days. Without a tray mesh it is the canopy height above the seeding level.",
        ["leaves"] = "Mean number of blades. A segmented blade counts once.",
        ["leafAreaMax_m2"] = "m². Largest daily leaf area of the period. Annual and whole-run records only.",
        ["leafAreaFinal_m2"] = "m². Leaf area on the last surveyed day of the period. Annual and whole-run records only.",
        ["thicknessMax_m"] = "m. Largest daily thickness of the period. Annual and whole-run records only.",
        ["days"] = "Daily surveys that contributed to a vegetation record. Zero means no reading.",
        ["width_m, height_m, depth_m"] = "m. Extents of the tray mesh along the facade, vertically, and outward "
            + "from the back wall. Zero without a mesh.",
        ["species.leafOutlineAreaRatio"] = "One-sided area of the species' blade outline over the rectangle "
            + "Length x 2 Radius the light model stands a blade in, one factor per blade slice. The tray leaf "
            + "areas use the outline; the ray-traced leaf area indices use the rectangle, so they read higher "
            + "by one over this factor.",
        ["precision"] = "Readings are written with four significant digits; coordinates in full.",
    };
}
