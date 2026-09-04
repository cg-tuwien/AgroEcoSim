using System;
using System.Numerics;

namespace Agro;

/// <summary>
/// Turns a ray hit into the radiance leaving that surface towards the sensor, which is how the
/// internal light model gets indirect illumination without tracing secondary bounces.
/// </summary>
/// <remarks>
/// Two different approximations meet here, chosen because both are stable and cost nothing:
/// <list type="bullet">
/// <item>Buildings and terrain reflect an analytic estimate. Their orientation is known at the hit,
/// so the isotropic-sky plane-of-array irradiance of <see cref="SkyDome.PlaneIrradiance"/> gives a
/// directionally correct first bounce, scaled by the shading factor that the obstacle meter
/// measured for that object in the previous step (1 where nothing was measured). The ground
/// reflected term inside that formula already carries a second bounce.</item>
/// <item>Vegetation reflects the mean irradiance its own plant received in the previous step.
/// Indirect light does not need per-organ resolution, and a per-plant scalar sidesteps the question
/// of whether organ indices survived the births and deaths of the last step.</item>
/// </list>
/// Because the estimate for each step is built from the previous one, the effective path length
/// grows as the simulation runs, in the spirit of progressive radiosity.
/// </remarks>
public readonly struct SurfaceRadianceModel
{
	readonly float[] StaticAlbedo;
	readonly float[] StaticShading;
	readonly float[] PlantRadiosity;
	readonly int FirstPlantGroup;

	public SurfaceRadianceModel(float[] staticAlbedo, float[] staticShading, float[] plantRadiosity, int firstPlantGroup)
	{
		StaticAlbedo = staticAlbedo;
		StaticShading = staticShading;
		PlantRadiosity = plantRadiosity;
		FirstPlantGroup = firstPlantGroup;
	}

	/// <summary>Radiance leaving the hit surface, in W/m²/sr.</summary>
	public float Radiance(in RayHit hit, in SkyDome dome)
	{
		if (hit.IsPlant)
		{
			var plant = hit.Group - FirstPlantGroup;
			return plant >= 0 && plant < PlantRadiosity.Length ? PlantRadiosity[plant] : 0f;
		}
		if (hit.Group < 0 || hit.Group >= StaticAlbedo.Length)
			return 0f;
		return StaticAlbedo[hit.Group] * StaticShading[hit.Group] * dome.PlaneIrradiance(hit.Normal) / MathF.PI;
	}
}
