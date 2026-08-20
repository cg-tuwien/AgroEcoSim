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
	const float PiV = 3f * 0.001f * 0.1f / Pi4;
	const float Third = 1f/3f;

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

	readonly Vector2 mVegetativeTemperature;

	/// <summary>
	/// Threshold to transform to a full plant
	/// </summary>
	public readonly float GerminationThreshold;

	/// <summary>
	/// Ratio ∈ [0, 1] of the required energy to start growing roots and stems
	/// </summary>
	public readonly float GerminationProgress => Water_g / GerminationThreshold;
	public readonly int SoilIndex;

	public SeedAgent(int soilIndex, Vector3 center, float radius, Vector2 vegetativeTemperature, float energy = -1f)
	{
		SoilIndex = soilIndex;
		Center = center;
		Radius = radius;
		if (energy < 0f)
			Water_g = radius * radius * radius * 100f;
		else
			Water_g = energy;

		GerminationThreshold = Water_g * 500f + 100f * radius;
		mVegetativeTemperature = vegetativeTemperature;
	}

	public void Tick(IFormation _formation, int formationID, uint timestep)
	{
		var plant = (PlantFormation2)_formation;
		var world = plant.World;
		Water_g -= Radius * Radius * Radius * world.HoursPerTick; //life support
		if (Water_g <= 0) //energy depleted
		{
			Water_g = 0f;
			plant.SeedDeath();
			Debug.WriteLine($"Seed death: {formationID} at {world.Timestep} in field {SoilIndex}");
		}
		else
		{
			if (Water_g >= GerminationThreshold) //GERMINATION
			{
				//Debug.WriteLine($"GERMINATION at {timestep}");
				var initialYawAngle = plant.RNG.NextFloat(-MathF.PI, MathF.PI);
				var initialYaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, initialYawAngle);
				plant.UGBirth(new UnderGroundAgent(plant, timestep, -1, initialYaw * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.5f * MathF.PI), Water_g * 0.4f, initialResources: 1f, initialProduction: 1f));

				var baseStemOrientation = initialYaw * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f * MathF.PI);
                var rizomOrientation = initialYaw * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.25f * MathF.PI);
                var rizome = new AboveGroundAgent(plant, -1, OrganTypes.Stem, rizomOrientation, Water_g * 0.4f, initialResources: 1f, initialProduction: 1f, length: 0.001f, radius: 0.0025f)
                {
                    IsRizome = true
                };
                rizome.RizomeInfo.test3 = true;
                var rizomeIndex = plant.AG.Birth(rizome);
                var meristem = new AboveGroundAgent(plant, rizomeIndex, OrganTypes.Meristem, baseStemOrientation, Water_g * 0.4f, initialResources: 1f, initialProduction: 1f);
				var meristemIndex = plant.AG.Birth(meristem); //base stem

				if (plant.Parameters.LateralsPerNode > 0)
					AboveGroundAgent.CreateFirstLeaves(meristem, plant, 0, meristemIndex);

				plant.SeedDeath();
				Water_g = 0f;
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
					soil.RequestWater(source, waterRequest_g, plant, SoilIndex);
				}
			}
		}
	}

	void IncWater(float amount)
	{
		Debug.Assert(amount >= 0f);
		Water_g += amount;
		Radius = MathF.Pow(Radius * Radius * Radius + amount * PiV, Third); //use the rest for growth
	}
}
