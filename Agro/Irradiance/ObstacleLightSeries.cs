using System;

namespace Agro;

/// <summary>
/// Light and foliage readings on one facade patch over one reporting period: a week, a calendar
/// year or the whole run.
/// </summary>
/// <remarks>
/// <para>
/// Irradiations are sums over the period's daylight steps, already integrated over each step, so
/// they are energies per unit area. Transmittances and the shaded fraction are <em>irradiance
/// weighted</em>: ratios of those sums, never means of per-step ratios. A plain mean lets a dim
/// step just after dawn, where the foliage happens to block nothing, count as much as noon, and
/// flatters the canopy by tens of percent.
/// </para>
/// <para>
/// The leaf area indices are means over the period's daylight steps weighted by hours, so a week
/// derived from its steps, a year derived from the same steps and a total derived from the years
/// all agree.
/// </para>
/// </remarks>
public readonly record struct ObstacleLightPeriod(
	/// <summary>Hours during which the sky was bright enough to trace. Zero means no reading.</summary>
	float DaylightHours,
	/// <summary>Direct plus diffuse irradiation the patch would receive with no vegetation, Wh/m².</summary>
	float OpenSky_Wh_m2,
	/// <summary>Irradiation the vegetation kept off one square metre, Wh/m². Slightly negative is
	/// physical in low sun, when a bright canopy reflects more onto the wall than the sky it hides.</summary>
	float Avoided_Wh_m2,
	/// <summary><c>1 - received / openSky</c>: the share of the solar gain the vegetation keeps off.</summary>
	float ShadedFraction,
	/// <summary>Share of the direct beam that gets through the foliage, weighted by beam load.</summary>
	float BeamTransmittance,
	/// <summary>Share of the diffuse and inter-reflected load that gets through.</summary>
	float DiffuseTransmittance,
	/// <summary>Total one-sided leaf area per unit patch area within the canopy depth, mean over the
	/// period's daylight hours.</summary>
	float LeafAreaIndex,
	/// <summary>Highest per-step value the period saw: the developed canopy rather than its mean.</summary>
	float LeafAreaIndexMax,
	/// <summary>Leaf area projected onto the patch per unit patch area.</summary>
	float ProjectedLeafAreaIndex,
	/// <summary>Beer-Lambert inversion of the diffuse gap fraction, what a canopy analyser held in
	/// front of the wall would report.</summary>
	float LeafAreaIndexFromGaps,
	/// <summary>Largest irradiance the vegetation removed at any moment of the period, W/m².</summary>
	float PeakReduction_W_m2)
{
	/// <summary>True when nothing was measured in the period.</summary>
	public bool IsEmpty => DaylightHours <= 0f;
	public float Received_Wh_m2 => OpenSky_Wh_m2 - Avoided_Wh_m2;
}

/// <summary>
/// The periodic readings of one level: one record per week of the run, one per calendar year, and
/// the total. The arrays are dense and aligned with <see cref="PeriodCalendar"/>, so a week with no
/// daylight step is an empty record rather than a gap.
/// </summary>
public sealed class ObstacleLightSeries
{
	public ObstacleLightPeriod[] Weeks { get; }
	public ObstacleLightPeriod[] Years { get; }
	public ObstacleLightPeriod Total { get; }

	internal ObstacleLightSeries(ObstacleLightPeriod[] weeks, ObstacleLightPeriod[] years, ObstacleLightPeriod total)
	{
		Weeks = weeks;
		Years = years;
		Total = total;
	}
}
