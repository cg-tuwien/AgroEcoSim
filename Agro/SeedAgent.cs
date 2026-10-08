using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using AgentsSystem;

namespace Agro;

/// <summary>
/// Plant seed, approximated by a sphere
/// </summary>
[StructLayout(LayoutKind.Auto)]
public struct SeedAgent : IAgent
{
	[StructLayout(LayoutKind.Auto)]
	[Message]
	public readonly struct WaterInc : IMessage<SeedAgent>
	{
		/// <summary>
		/// Water volume in gramm
		/// </summary>
		public readonly float Amount_g;
		public WaterInc(float amount_g) => Amount_g = amount_g;
		public bool Valid => Amount_g > 0f;
		public Transaction Type => Transaction.Increase;
        public void Receive(ref SeedAgent dstAgent, uint timestep) => dstAgent.IncWater(Amount_g);
    }

	const float Pi4 = MathF.PI * 4f;
	// Additive-volume proxy for imbibition: water density is 1e6 g/m³.
	// This is not a species-specific swelling constitutive law.
	const double WaterToRadiusCubed = 3d / (4d * Math.PI * 1e6);

	/// <summary>
	/// Sphere center
	/// </summary>
	internal readonly Vector3 Center;
	/// <summary>
	/// Seed sphere radius
	/// </summary>
	public float Radius { get; private set; }
	/// <summary>
	/// Amount of water currrently stored (in gramms)
	/// </summary>
	public float Water_g { get; private set; }

	/// <summary>Stored reserve in model energy units, independent of hydration.</summary>
	public float Energy { get; private set; }

	readonly Vector2 mVegetativeTemperature;

	/// <summary>
	/// Threshold to transform to a full plant
	/// </summary>
	public readonly float GerminationThreshold;

	/// <summary>
	/// Hydration progress ∈ [0, 1] toward the germination threshold
	/// </summary>
	public readonly float GerminationProgress => Math.Clamp(Water_g / GerminationThreshold, 0f, 1f);
	public readonly int SoilIndex;

	/// <summary>Creates an initial propagule with independent water and energy pools.</summary>
	/// <param name="soilIndex">Soil field containing the seed.</param>
	/// <param name="center">Initial position in simulation coordinates.</param>
	/// <param name="radius">Initial spherical-envelope radius in metres.</param>
	/// <param name="vegetativeTemperature">Lower and full-uptake temperatures in degrees Celsius.</param>
	/// <param name="energy">Legacy initial-water argument in grams; negative selects
	/// historical initial hydration. The name is retained for source compatibility.</param>
	/// <param name="reserveEnergy">Initial reserve in custom model energy units. The
	/// default preserves the size-based nominal legacy budget, without water overshoot.
	/// It is not a measured seed carbohydrate mass.</param>
	public SeedAgent(int soilIndex, Vector3 center, float radius, Vector2 vegetativeTemperature,
		float energy = -1f, float? reserveEnergy = null)
	{
		if (!float.IsFinite(radius) || radius <= 0f) throw new ArgumentOutOfRangeException(nameof(radius));
		if (!float.IsFinite(energy)) throw new ArgumentOutOfRangeException(nameof(energy));
		if (reserveEnergy.HasValue && (!float.IsFinite(reserveEnergy.Value) || reserveEnergy.Value < 0f))
			throw new ArgumentOutOfRangeException(nameof(reserveEnergy));
		SoilIndex = soilIndex;
		Center = center;
		Radius = radius;
		if (energy < 0f)
			Water_g = radius * radius * radius * 100f;
		else
			Water_g = energy;

		GerminationThreshold = Water_g * 500f + 100f * radius;
		Energy = reserveEnergy ?? (radius * radius * radius * 100f * 500f + 100f * radius);
		if (!float.IsFinite(GerminationThreshold) || !float.IsFinite(Energy))
			throw new ArgumentOutOfRangeException(nameof(radius), "Seed initialization exceeds the finite model range.");
		mVegetativeTemperature = vegetativeTemperature;
	}

	public void Tick(IFormation _formation, int formationID, uint timestep)
	{
		var plant = (PlantFormation2)_formation;
		var world = plant.World;
		// A seed hydrated at the boundary germinates before another interval's loss.
		// Otherwise a request capped at the threshold could never germinate.
		if (Water_g < GerminationThreshold)
			Water_g -= Radius * Radius * Radius * world.HoursPerTick; // Empirical hydration loss, g/h.
		if (Water_g <= 0) // Dehydration, not carbon starvation.
		{
			Water_g = 0f;
			Energy = 0f;
			plant.SeedDeath();
			Debug.WriteLine($"Seed death: {formationID} at {world.Timestep} in field {SoilIndex}");
		}
		else
		{
			if (Water_g >= GerminationThreshold) //GERMINATION
			{
				//Debug.WriteLine($"GERMINATION at {timestep}");
				const float rootReserveFraction = 0.3f;
				const float rhizomeReserveFraction = 0.3f;
				const float aerialReserveFraction = 1f - rootReserveFraction - rhizomeReserveFraction;
				var seedReserve = Energy;
				var seedWater = Water_g;
				var initialYawAngle = plant.RNG.NextFloat(-MathF.PI, MathF.PI);
				var initialYaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, initialYawAngle);
				plant.UGBirth(new UnderGroundAgent(plant, timestep, -1,
					initialYaw * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.5f * MathF.PI),
					seedReserve * rootReserveFraction, initialWater_g: seedWater * rootReserveFraction,
					initialResources: 1f, initialProduction: 1f));

				var baseStemOrientation = initialYaw * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f * MathF.PI);
				var rhizomeOrientation = initialYaw;
				var initialRhizomeRadius = Math.Min(0.0025f, plant.Parameters.RhizomeSegmentRadius);
				var rhizome = new AboveGroundAgent(plant, -1, OrganTypes.Stem, rhizomeOrientation,
					seedReserve * rhizomeReserveFraction,
					radius: initialRhizomeRadius, length: 0.001f, initialResources: 1f, initialProduction: 1f)
				{
					IsRizome = true,
					Water_g = seedWater * rhizomeReserveFraction
				};
				var rhizomeIndex = plant.AG.Birth(rhizome);
				// Each petiole/blade helper already shares resources among its segments.
				var aerialOrganCount = 1 + plant.Parameters.LateralsPerNode * 2;
				var aerialReservePerOrgan = seedReserve * aerialReserveFraction / aerialOrganCount;
				var aerialWaterPerOrgan = seedWater * aerialReserveFraction / aerialOrganCount;
				var meristem = new AboveGroundAgent(plant, rhizomeIndex, OrganTypes.Meristem,
					baseStemOrientation, aerialReservePerOrgan, initialResources: 1f, initialProduction: 1f)
				{ Water_g = aerialWaterPerOrgan };
				// The first crown must emerge from its local soil just as a later rhizome
				// crown does. A compressed internode target alone can finish below the
				// surface and keep every subsequent leaf node crowded into the soil.
				// Set a growth target; do not move the seed or teleport its descendants.
				if (plant.Parameters.Behavior == Behavior.Herbaceous)
				{
					var crownBase = plant.Position + plant.AG.FieldOrigin
						+ Vector3.Transform(Vector3.UnitX, rhizomeOrientation) * rhizome.Length;
					var scene = world.Collisions.StaticScene;
					if (scene.InsideSoil(crownBase, 1f)
						&& scene.ClosestSoilSurface(crownBase, crownBase + Vector3.UnitY * 0.1f,
							0.0001f, out var surface))
						meristem.SetEmergenceTarget(surface.Point.Y - crownBase.Y + plant.Parameters.CrownNodeHeightAboveSoil);
				}
				var meristemIndex = plant.AG.Birth(meristem); //base stem

				if (plant.Parameters.LateralsPerNode > 0)
					AboveGroundAgent.CreateLeavesFromReserve(meristem, plant, 0f, meristemIndex,
						1f, 1f, aerialReservePerOrgan, aerialWaterPerOrgan);

				plant.SeedDeath();
				Water_g = 0f;
				Energy = 0f;
			}
			else
			{
				var soil = plant.Soil;
				//find all soild cells that the shpere intersects
				var source = soil.IntersectPoint(Center, SoilIndex);
				if (source >= 0) //TODO this is a rough approximation taking only the first intersected soil cell
				{
					var soilTemperature = soil.GetTemperature(source, SoilIndex);
					var waterRequest_g = 0f;
					//for (int i = 0; i < world.HoursPerTick; ++i)
					{
						var amount_g = Pi4 * Radius * Radius * 1e5f * world.HoursPerTick; //sphere surface is 4πr² square meters, 1e5 tansofmrs m² to gramms
						if (soilTemperature > mVegetativeTemperature.X)
						{
							if (soilTemperature < mVegetativeTemperature.Y)
								amount_g *= (soilTemperature - mVegetativeTemperature.X) / (mVegetativeTemperature.Y - mVegetativeTemperature.X);

							waterRequest_g += amount_g;
						}
					}
					// Limit uptake at the request; never discard already delivered water.
					waterRequest_g = MathF.Min(waterRequest_g, MathF.Max(0f, GerminationThreshold - Water_g));
					soil.RequestWater(source, waterRequest_g, plant, SoilIndex);
				}
			}
		}
	}

	void IncWater(float amount)
	{
		Debug.Assert(float.IsFinite(amount) && amount >= 0f);
		Water_g += amount;
		Radius = (float)Math.Cbrt((double)Radius * Radius * Radius + amount * WaterToRadiusCubed);
	}
}
