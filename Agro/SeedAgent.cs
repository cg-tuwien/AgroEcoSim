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
		/// Water volume in m³
		/// </summary>
		public readonly float Amount;
		public WaterInc(float amount) => Amount = amount;
		public bool Valid => Amount > 0f;
		public Transaction Type => Transaction.Increase;
        public void Receive(ref SeedAgent dstAgent, uint timestep) => dstAgent.IncWater(Amount);
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
	/// Amount of energy currrently stored
	/// </summary>
	public float Water { get; private set; }

	readonly Vector2 mVegetativeTemperature;

	/// <summary>
	/// Threshold to transform to a full plant
	/// </summary>
	public readonly float GerminationThreshold;

	/// <summary>
	/// Ratio ∈ [0, 1] of the required energy to start growing roots and stems
	/// </summary>
	public float GerminationProgress => Water / GerminationThreshold;

	public SeedAgent(Vector3 center, float radius, Vector2 vegetativeTemperature, float energy = -1f)
	{
		Center = center;
		Radius = radius;
		if (energy < 0f)
			Water = radius * radius * radius * 100f;
		else
			Water = energy;

		GerminationThreshold = Water * 500f + 100f*radius;
		mVegetativeTemperature = vegetativeTemperature;
	}

	public void Tick(IFormation _formation, int formationID, uint timestep)
	{
		var plant = (PlantFormation2)_formation;
		var world = plant.World;
		Water -= Radius * Radius * Radius * world.HoursPerTick; //life support
		if (Water <= 0) //energy depleted
		{
			Water = 0f;
			plant.SeedDeath();
		}
		else
		{
			if (Water >= GerminationThreshold) //GERMINATION
			{
				Debug.WriteLine($"GERMINATION at {timestep}");
				var initialYawAngle = plant.RNG.NextFloat(-MathF.PI, MathF.PI);
				var initialYaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, initialYawAngle);
				plant.UG.Birth(new UnderGroundAgent(plant, timestep, -1, initialYaw * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.5f * MathF.PI), Water * 0.4f, initialResources: 1f, initialProduction: 1f));

				var baseStemOrientation = initialYaw * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f * MathF.PI);
				var meristem = new AboveGroundAgent(plant, -1, OrganTypes.Meristem, baseStemOrientation, Water * 0.4f, initialResources: 1f, initialProduction: 1f);
				var meristemIndex = plant.AG.Birth(meristem); //base stem

				if (plant.Parameters.LateralsPerNode > 0)
					AboveGroundAgent.CreateFirstLeaves(meristem, plant, 0, meristemIndex);

				plant.SeedDeath();
				Water = 0f;
			}
			else
			{
				var soil = plant.Soil;
				//find all soild cells that the shpere intersects
				var source = soil.IntersectPoint(Center);
				if (source >= 0) //TODO this is a rough approximation taking only the first intersected soil cell
				{
					var soilTemperature = soil.GetTemperature(source);
					var waterRequest = 0f;
					for(int i = 0; i < world.HoursPerTick; ++i)
					{
						var amount = Pi4 * Radius * Radius; //sphere surface is 4πr²
						if (soilTemperature > mVegetativeTemperature.X)
						{
							if (soilTemperature < mVegetativeTemperature.Y)
								amount *= (soilTemperature - mVegetativeTemperature.X) / (mVegetativeTemperature.Y - mVegetativeTemperature.X);

							waterRequest += amount;
						}
					}
					soil.RequestWater(source, waterRequest, plant);
				}
			}
		}
	}

	void IncWater(float amount)
	{
		Debug.Assert(amount >= 0f);
		Water += amount * 0.7f; //store most of the energy, 0.2f are losses
		Radius = MathF.Pow(Radius * Radius * Radius + amount * PiV, Third); //use the rest for growth
	}
}
