using System;
using System.Numerics;
using Innovative.SolarCalculator;

namespace Agro;

/// <summary>
/// Sun and sky radiance for one instant, in broadband shortwave units
/// (irradiances in W/m², radiances in W/m²/sr).
/// </summary>
/// <remarks>
/// The chain is deliberately assembled from small, individually citable steps so that every
/// number can be checked against the literature:
/// <list type="number">
/// <item>solar position from the NOAA algorithm (the <c>SolarCalculator</c> package that
/// <see cref="AgroWorld.Init"/> already uses for the daylight bitmap),</item>
/// <item>clear-sky beam transmittance after Meinel &amp; Meinel (1976),
/// <c>tau_b = 0.7^(m^0.678)</c>, with the Kasten &amp; Young (1989) relative optical air mass,</item>
/// <item>clear-sky diffuse transmittance after Liu &amp; Jordan, <c>tau_d = 0.271 - 0.294 tau_b</c>,</item>
/// <item>cloud attenuation of the global horizontal irradiance after Kasten &amp; Czeplak (1980),
/// <c>GHI = GHI_clear (1 - 0.75 c^3.4)</c>, driven by the sky coverage that
/// <see cref="AgroWorld"/> already simulates hour by hour,</item>
/// <item>the beam/diffuse split of that GHI from the clearness index after Erbs et al. (1982),</item>
/// <item>the angular distribution of the diffuse part from the CIE general sky formula, blended
/// between the standard clear (type 12) and the standard overcast (type 1) gradation and
/// indicatrix pairs by the same cloud cover.</item>
/// </list>
/// Only the relative <em>shape</em> is taken from the CIE model: it is normalised so that its
/// cosine-weighted hemispherical integral reproduces the diffuse horizontal irradiance from
/// step 5. The absolute result therefore never depends on the shape constants, they only
/// redistribute a known total over the hemisphere.
/// </remarks>
public readonly struct SkyDome
{
	/// <summary>Unit vector from the scene towards the sun. Y is up; north is -Z and east is +X,
	/// the convention the Mitsuba adapter uses, so shadows keep pointing the same way.</summary>
	public readonly Vector3 SunDirection;
	/// <summary>Beam irradiance on a plane normal to <see cref="SunDirection"/>, in W/m².</summary>
	public readonly float DirectNormal;
	/// <summary>Diffuse irradiance on a horizontal plane, in W/m².</summary>
	public readonly float DiffuseHorizontal;
	/// <summary>Global (beam + diffuse) irradiance on a horizontal plane, in W/m².</summary>
	public readonly float GlobalHorizontal;
	/// <summary>Radiance of the ground seen by a ray that leaves the scene downwards, in W/m²/sr.
	/// Used only where no terrain triangle was hit.</summary>
	public readonly float GroundRadiance;

	//Absolute multipliers of the two normalised CIE shapes; their sum integrates to DiffuseHorizontal.
	readonly float ClearScale;
	readonly float OvercastScale;

	/// <summary>Broadband shortwave albedo of the soil. Bare and vegetated ground both sit close
	/// to 0.2 in the shortwave; see e.g. Oke, Boundary Layer Climates.</summary>
	public const float GroundAlbedo = 0.2f;

	//CIE general sky, standard clear (type 12): gradation a = -1, b = -0.32; indicatrix c = 10, d = -3, e = 0.45.
	const float ClearB = -0.32f;
	const float ClearC = 10f;
	const float ClearD = -3f;
	const float ClearE = 0.45f;
	//CIE general sky, standard overcast (type 1): gradation a = 4, b = -0.7; isotropic indicatrix.
	const float OvercastA = 4f;
	const float OvercastB = -0.7f;

	static readonly float ClearIndicatrixOffset = MathF.Exp(ClearD * MathF.PI * 0.5f);

	SkyDome(Vector3 sunDirection, float directNormal, float diffuseHorizontal, float globalHorizontal,
			float clearScale, float overcastScale)
	{
		SunDirection = sunDirection;
		DirectNormal = directNormal;
		DiffuseHorizontal = diffuseHorizontal;
		GlobalHorizontal = globalHorizontal;
		GroundRadiance = GroundAlbedo * globalHorizontal / MathF.PI;
		ClearScale = clearScale;
		OvercastScale = overcastScale;
	}

	/// <summary>True while there is enough light to bother tracing.</summary>
	public bool IsLit => GlobalHorizontal > 0.5f;

	/// <summary>
	/// The same dome with every magnitude scaled. Used to give a multi-hour timestep a dome whose
	/// totals are the average over the step while its geometry stays that of the brightest instant
	/// inside it, which is what the diffuse gather should see.
	/// </summary>
	public SkyDome Scaled(float factor) => new(SunDirection, DirectNormal * factor, DiffuseHorizontal * factor,
		GlobalHorizontal * factor, ClearScale * factor, OvercastScale * factor);

	/// <summary>Radiance arriving from direction <paramref name="d"/> (a unit vector pointing away
	/// from the scene). Directions below the horizon return the ground radiance, which is what a
	/// ray that escaped without hitting any terrain triangle should see.</summary>
	public float Radiance(Vector3 d)
	{
		if (d.Y <= 0f)
			return GroundRadiance;
		var cosGamma = Math.Clamp(Vector3.Dot(d, SunDirection), -1f, 1f);
		return ClearScale * ClearShape(d.Y, cosGamma) + OvercastScale * OvercastShape(d.Y);
	}

	/// <summary>Irradiance on a plane with unit normal <paramref name="n"/>, assuming an
	/// unobstructed sky: the classic isotropic (Liu &amp; Jordan) transposition of beam, diffuse
	/// and ground-reflected components. Used to estimate the radiosity of building and terrain
	/// triangles without tracing them.</summary>
	public float PlaneIrradiance(Vector3 n)
	{
		var cosIncidence = Vector3.Dot(n, SunDirection);
		var beam = cosIncidence > 0f ? DirectNormal * cosIncidence : 0f;
		var cosTilt = n.Y; //n is a unit vector, so this is cos of the tilt from horizontal
		return beam
			+ DiffuseHorizontal * 0.5f * (1f + cosTilt)
			+ GlobalHorizontal * GroundAlbedo * 0.5f * (1f - cosTilt);
	}

	static float ClearShape(float cosZenith, float cosGamma)
	{
		var gradation = 1f - MathF.Exp(ClearB / MathF.Max(cosZenith, 1e-4f));
		var gamma = FastAcos(cosGamma);
		var indicatrix = 1f + ClearC * (MathF.Exp(ClearD * gamma) - ClearIndicatrixOffset) + ClearE * cosGamma * cosGamma;
		return gradation * indicatrix;
	}

	static float OvercastShape(float cosZenith) => 1f + OvercastA * MathF.Exp(OvercastB / MathF.Max(cosZenith, 1e-4f));

	/// <summary>Nvidia's classic polynomial arc cosine, ~1e-5 absolute error. The indicatrix needs
	/// an angle rather than its cosine and this runs once per sky lookup, so it is worth avoiding
	/// <see cref="MathF.Acos"/> here.</summary>
	static float FastAcos(float x)
	{
		var negate = x < 0f;
		var a = MathF.Abs(x);
		var ret = -0.0187293f * a + 0.0742610f;
		ret = ret * a - 0.2121144f;
		ret = ret * a + 1.5707288f;
		ret *= MathF.Sqrt(MathF.Max(0f, 1f - a));
		return negate ? MathF.PI - ret : ret;
	}

	/// <summary>Fixed cosine-distributed directions over the upper hemisphere, used to normalise
	/// the two CIE shapes. A deterministic Hammersley set keeps the normalisation, and hence the
	/// whole simulation, reproducible.</summary>
	static readonly Vector3[] NormalizationDirections = BuildNormalizationDirections(128);

	static Vector3[] BuildNormalizationDirections(int count)
	{
		var result = new Vector3[count];
		for (int i = 0; i < count; ++i)
		{
			var u1 = (i + 0.5f) / count;
			var u2 = RaySampling.RadicalInverse2(i);
			var r = MathF.Sqrt(u1);
			var phi = 2f * MathF.PI * u2;
			result[i] = new Vector3(r * MathF.Cos(phi), MathF.Sqrt(MathF.Max(0f, 1f - u1)), r * MathF.Sin(phi));
		}
		return result;
	}

	/// <summary>Builds the dome for one instant.</summary>
	/// <param name="utc">Instant in UTC.</param>
	/// <param name="latitude">Degrees north.</param>
	/// <param name="longitude">Degrees east.</param>
	/// <param name="skyCoverage">Cloud cover, 0 = clear .. 1 = fully overcast.</param>
	public static SkyDome Sample(DateTime utc, float latitude, float longitude, float skyCoverage)
	{
		var solar = new SolarTimes(utc, 0, latitude, longitude);
		var elevation = (float)solar.SolarElevation.Radians;
		var sinElevation = MathF.Sin(elevation);
		if (sinElevation <= 0.001f) //below (or level with) the horizon: no beam and no sky brightness worth tracing
			return new SkyDome(Vector3.UnitY, 0f, 0f, 0f, 0f, 0f);

		//North is -Z and east is +X. SolarAzimuth is measured clockwise from north (NOAA).
		var azimuth = (float)solar.SolarAzimuth.Radians;
		var horizontal = MathF.Cos(elevation);
		var sunDirection = new Vector3(horizontal * MathF.Sin(azimuth), sinElevation, -horizontal * MathF.Cos(azimuth));

		//Extraterrestrial normal irradiance including the orbital eccentricity factor.
		var dayAngle = 2f * MathF.PI * (utc.DayOfYear - 1) / 365f;
		var extraterrestrial = 1361f * (1f + 0.033f * MathF.Cos(dayAngle));

		//Kasten & Young relative optical air mass.
		var elevationDegrees = elevation * (180f / MathF.PI);
		var airMass = 1f / (sinElevation + 0.50572f * MathF.Pow(elevationDegrees + 6.07995f, -1.6364f));

		var beamTransmittance = MathF.Pow(0.7f, MathF.Pow(airMass, 0.678f));
		var diffuseTransmittance = MathF.Max(0f, 0.271f - 0.294f * beamTransmittance);
		var clearSkyGlobal = extraterrestrial * sinElevation * (beamTransmittance + diffuseTransmittance);

		var coverage = Math.Clamp(skyCoverage, 0f, 1f);
		var global = clearSkyGlobal * (1f - 0.75f * MathF.Pow(coverage, 3.4f));
		if (global <= 0.5f)
			return new SkyDome(sunDirection, 0f, 0f, 0f, 0f, 0f);

		//Erbs et al. diffuse fraction from the clearness index.
		var clearness = Math.Clamp(global / (extraterrestrial * sinElevation), 0f, 1f);
		float diffuseFraction;
		if (clearness <= 0.22f)
			diffuseFraction = 1f - 0.09f * clearness;
		else if (clearness <= 0.8f)
		{
			var k2 = clearness * clearness;
			diffuseFraction = 0.9511f - 0.1604f * clearness + 4.388f * k2 - 16.638f * k2 * clearness + 12.336f * k2 * k2;
		}
		else
			diffuseFraction = 0.165f;
		diffuseFraction = Math.Clamp(diffuseFraction, 0.05f, 1f);

		var diffuseHorizontal = global * diffuseFraction;
		var directNormal = MathF.Max(0f, (global - diffuseHorizontal) / sinElevation);

		//Normalise both CIE shapes so that each integrates to one unit of diffuse horizontal
		//irradiance: integral(shape * cos) dOmega = pi * mean(shape) over cosine-distributed directions.
		var dirs = NormalizationDirections;
		double clearSum = 0.0, overcastSum = 0.0;
		for (int i = 0; i < dirs.Length; ++i)
		{
			var d = dirs[i];
			clearSum += ClearShape(d.Y, Math.Clamp(Vector3.Dot(d, sunDirection), -1f, 1f));
			overcastSum += OvercastShape(d.Y);
		}
		var clearIntegral = MathF.PI * (float)(clearSum / dirs.Length);
		var overcastIntegral = MathF.PI * (float)(overcastSum / dirs.Length);

		var clearScale = clearIntegral > 1e-6f ? (1f - coverage) * diffuseHorizontal / clearIntegral : 0f;
		var overcastScale = overcastIntegral > 1e-6f ? coverage * diffuseHorizontal / overcastIntegral : 0f;

		return new SkyDome(sunDirection, directNormal, diffuseHorizontal, global, clearScale, overcastScale);
	}
}
