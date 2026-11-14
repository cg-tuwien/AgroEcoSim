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
/// <item>Existing vegetation and non-lamina woody organs retain the per-plant mean irradiance
/// approximation. Woody laminae instead carry their own measured light by stable organ identity,
/// avoiding redistribution between bright and dark parts of a large crown. Their summed two-face
/// incident energy is divided between two emitted hemispheres. This is a lumped shortwave model,
/// not separate spectral reflection and transmission on the two leaf surfaces.</item>
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
	readonly float[]? WoodyLaminaRadiosity;
	readonly int FirstWoodyLaminaGroup;

	public SurfaceRadianceModel(float[] staticAlbedo, float[] staticShading, float[] plantRadiosity, int firstPlantGroup)
		: this(staticAlbedo, staticShading, plantRadiosity, firstPlantGroup, null, int.MaxValue) { }

	public SurfaceRadianceModel(float[] staticAlbedo, float[] staticShading, float[] plantRadiosity, int firstPlantGroup,
        float[]? woodyLaminaRadiosity, int firstWoodyLaminaGroup)
	{
		StaticAlbedo = staticAlbedo;
		StaticShading = staticShading;
		PlantRadiosity = plantRadiosity;
		FirstPlantGroup = firstPlantGroup;
		WoodyLaminaRadiosity = woodyLaminaRadiosity;
		FirstWoodyLaminaGroup = firstWoodyLaminaGroup;
	}

	/// <summary>Radiance leaving the hit surface, in W/m²/sr.</summary>
	public float Radiance(in RayHit hit, in SkyDome dome)
	{
		if (hit.IsPlant)
		{
			if (WoodyLaminaRadiosity != null && hit.Group >= FirstWoodyLaminaGroup)
			{
				var leaf = hit.Group - FirstWoodyLaminaGroup;
				return leaf < WoodyLaminaRadiosity.Length ? WoodyLaminaRadiosity[leaf] : 0f;
			}
			var plant = hit.Group - FirstPlantGroup;
			return plant >= 0 && plant < PlantRadiosity.Length ? PlantRadiosity[plant] : 0f;
		}
		if (hit.Group < 0 || hit.Group >= StaticAlbedo.Length)
			return 0f;
		return StaticAlbedo[hit.Group] * StaticShading[hit.Group] * dome.PlaneIrradiance(hit.Normal) / MathF.PI;
	}
    /// <summary>Mean radiance over the complete lighting interval; no extra geometry rays.</summary>
    public float Radiance(in RayHit hit, in SkyInterval sky)
    {
        if (hit.IsPlant)
        {
            if (WoodyLaminaRadiosity != null && hit.Group >= FirstWoodyLaminaGroup)
            {
                var leaf = hit.Group - FirstWoodyLaminaGroup;
                return leaf < WoodyLaminaRadiosity.Length ? WoodyLaminaRadiosity[leaf] : 0f;
            }
            var plant = hit.Group - FirstPlantGroup;
            return plant >= 0 && plant < PlantRadiosity.Length ? PlantRadiosity[plant] : 0f;
        }
        if (hit.Group < 0 || hit.Group >= StaticAlbedo.Length) return 0f;
        return StaticAlbedo[hit.Group] * StaticShading[hit.Group] * sky.PlaneIrradiance(hit.Normal) / MathF.PI;
    }

}
