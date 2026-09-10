using System;
using System.Collections.Generic;

namespace Agro;

/// <summary>
/// One day of light and foliage readings on one facade patch.
/// </summary>
/// <remarks>
/// <para>
/// A day rather than a simulation step, because that is the unit the question is asked in: solar
/// gain is an energy per day, and per-step series over a season and a few hundred facade objects run
/// to hundreds of megabytes without answering anything a daily total does not.
/// </para>
/// <para>
/// Irradiations are sums over the day's daylight steps and are already integrated over each step, so
/// they are energies per unit area. Transmittances are <em>irradiance weighted</em>: they are ratios
/// of those sums, never means of per-step ratios. That distinction is not cosmetic - a plain mean
/// lets a dim step just after dawn, where the foliage happens to block nothing, count as much as
/// noon, and it flatters the canopy by tens of percent.
/// </para>
/// </remarks>
public readonly record struct ObstacleLightDay(
	/// <summary>Local calendar date at the site.</summary>
	DateTime Date,
	/// <summary>Simulation steps that contributed.</summary>
	int Steps,
	/// <summary>Hours of the day during which the sky was bright enough to trace.</summary>
	float DaylightHours,
	/// <summary>Hours during which the direct beam reached this patch at all, vegetation aside.
	/// Zero on a facade the sun never faces, or under full overcast.</summary>
	float DirectBeamHours,
	/// <summary>Direct beam irradiation the patch would receive with no vegetation, Wh/m².</summary>
	float BeamOpenSky_Wh_m2,
	/// <summary>Direct beam irradiation it actually receives, Wh/m².</summary>
	float Beam_Wh_m2,
	/// <summary>Diffuse and inter-reflected irradiation with no vegetation, Wh/m².</summary>
	float DiffuseOpenSky_Wh_m2,
	/// <summary>Diffuse and inter-reflected irradiation it actually receives, Wh/m². Includes what
	/// the foliage itself reflects onto the wall, which is why it is not simply the open value
	/// times a transmittance.</summary>
	float Diffuse_Wh_m2,
	/// <summary>Fraction of the day's total irradiation the vegetation keeps off, that is
	/// <c>1 - total / totalOpenSky</c>. The single number a shading study asks for. Slightly
	/// negative days are possible in low sun, when a bright canopy reflects more onto the wall than
	/// the sky it hides.</summary>
	float ShadedFraction,
	/// <summary>Share of the direct beam that gets through the foliage, weighted by beam load. Equal
	/// to the beam gap fraction, since a blocked beam contributes nothing.</summary>
	float BeamTransmittance,
	/// <summary>Share of the diffuse and inter-reflected load that gets through.</summary>
	float DiffuseTransmittance,
	/// <summary>Geometric fraction of the cosine-weighted sky hemisphere free of foliage. Unlike
	/// the transmittances this counts rays, not energy, which is what a Beer-Lambert inversion
	/// needs.</summary>
	float GapFractionDiffuse,
	/// <summary>Total one-sided leaf area per unit patch area, averaged over the day's steps.</summary>
	float LeafAreaIndex,
	/// <summary>Highest value the day saw, which is the one to quote for a growing canopy.</summary>
	float LeafAreaIndexMax,
	/// <summary>Leaf area projected onto the patch per unit patch area.</summary>
	float ProjectedLeafAreaIndex,
	/// <summary>Beer-Lambert inversion of <see cref="GapFractionDiffuse"/>, what a canopy analyser
	/// held in front of the wall would report.</summary>
	float LeafAreaIndexFromGaps,
	/// <summary>Highest open-sky irradiance of the day, W/m². Sizes the unshaded peak load.</summary>
	float PeakOpenSky_W_m2,
	/// <summary>Largest irradiance the vegetation removed at any moment of the day, W/m². This is
	/// the number a peak cooling load calculation wants.</summary>
	float PeakReduction_W_m2)
{
	public float Total_Wh_m2 => Beam_Wh_m2 + Diffuse_Wh_m2;
	public float TotalOpenSky_Wh_m2 => BeamOpenSky_Wh_m2 + DiffuseOpenSky_Wh_m2;
	/// <summary>Irradiation the vegetation kept off one square metre, Wh/m².</summary>
	public float Avoided_Wh_m2 => TotalOpenSky_Wh_m2 - Total_Wh_m2;
}

/// <summary>
/// Whole-run totals for one facade patch. Every field is the aggregate of the
/// <see cref="ObstacleLightDay"/> records over the simulated period, by the same weighting rules.
/// </summary>
public readonly record struct ObstacleLightSummary(
	int Days,
	float DaylightHours,
	float DirectBeamHours,
	float BeamOpenSky_Wh_m2,
	float Beam_Wh_m2,
	float DiffuseOpenSky_Wh_m2,
	float Diffuse_Wh_m2,
	float ShadedFraction,
	float BeamTransmittance,
	float DiffuseTransmittance,
	float GapFractionDiffuse,
	/// <summary>Mean over the period.</summary>
	float LeafAreaIndex,
	/// <summary>Highest value reached, that is the developed canopy.</summary>
	float LeafAreaIndexMax,
	/// <summary>Value on the last measured day.</summary>
	float LeafAreaIndexFinal,
	float ProjectedLeafAreaIndex,
	float LeafAreaIndexFromGaps,
	float PeakOpenSky_W_m2,
	float PeakReduction_W_m2)
{
	public float Total_Wh_m2 => Beam_Wh_m2 + Diffuse_Wh_m2;
	public float TotalOpenSky_Wh_m2 => BeamOpenSky_Wh_m2 + DiffuseOpenSky_Wh_m2;
	public float Avoided_Wh_m2 => TotalOpenSky_Wh_m2 - Total_Wh_m2;

	internal static ObstacleLightSummary From(IReadOnlyList<ObstacleLightDay> days)
	{
		if (days.Count == 0)
			return default;

		double beamOpen = 0, beam = 0, diffOpen = 0, diff = 0, daylight = 0, beamHours = 0;
		double laiSum = 0, projSum = 0, gapSum = 0, gapWeight = 0;
		float laiMax = 0, peakOpen = 0, peakReduction = 0;
		for (int i = 0; i < days.Count; ++i)
		{
			var d = days[i];
			beamOpen += d.BeamOpenSky_Wh_m2;
			beam += d.Beam_Wh_m2;
			diffOpen += d.DiffuseOpenSky_Wh_m2;
			diff += d.Diffuse_Wh_m2;
			daylight += d.DaylightHours;
			beamHours += d.DirectBeamHours;
			//Weighted by the day's daylight hours so a short winter day does not count as a
			//long summer one when the canopy is what is being averaged.
			var weight = MathF.Max(d.DaylightHours, 1e-3f);
			laiSum += d.LeafAreaIndex * weight;
			projSum += d.ProjectedLeafAreaIndex * weight;
			gapSum += d.GapFractionDiffuse * weight;
			gapWeight += weight;
			laiMax = MathF.Max(laiMax, d.LeafAreaIndexMax);
			peakOpen = MathF.Max(peakOpen, d.PeakOpenSky_W_m2);
			peakReduction = MathF.Max(peakReduction, d.PeakReduction_W_m2);
		}

		var totalOpen = beamOpen + diffOpen;
		var total = beam + diff;
		var gap = gapWeight > 0 ? (float)(gapSum / gapWeight) : 1f;

		return new ObstacleLightSummary(
			days.Count,
			(float)daylight,
			(float)beamHours,
			(float)beamOpen,
			(float)beam,
			(float)diffOpen,
			(float)diff,
			totalOpen > 1e-6 ? (float)(1.0 - total / totalOpen) : 0f,
			beamOpen > 1e-6 ? (float)(beam / beamOpen) : 1f,
			diffOpen > 1e-6 ? (float)(diff / diffOpen) : 1f,
			gap,
			gapWeight > 0 ? (float)(laiSum / gapWeight) : 0f,
			laiMax,
			days[^1].LeafAreaIndex,
			gapWeight > 0 ? (float)(projSum / gapWeight) : 0f,
			MathF.Max(0f, -MathF.Log(MathF.Max(gap, 1e-3f)) / ObstacleLightMeter.BeerLambertExtinction),
			peakOpen,
			peakReduction);
	}
}
