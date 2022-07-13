using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using AgentsSystem;

namespace Agro;

[StructLayout(LayoutKind.Auto)]
public readonly struct SoilWaterToSeedMsg : IMessage<SeedAgent>
{
	/// <summary>
	/// Water volume in m³
	/// </summary>    
	public readonly float Amount;

	public SoilWaterToSeedMsg(float amount) => Amount = amount;
	public Transaction Type => Transaction.Increase;

	public void Receive(ref SeedAgent agent) => agent.IncWater(Amount);
}

/// <summary>
/// Plant seed, approximated by a sphere
/// </summary>
[StructLayout(LayoutKind.Auto)]
public struct SeedAgent : IAgent
{
	const float Pi4 = MathF.PI * 4f;
	const float PiV = 3f * 0.001f * 0.1f / Pi4;
	const float Third = 1f/3f;
	/// <summary>
	/// Sphere center
	/// </summary>
	internal readonly Vector3 Center;
	/// <summary>
	/// Sphere radius
	/// </summary>
	float mRadius;
	/// <summary>
	/// Amount of energy currrently stored
	/// </summary>
	float mEnergyStored;

	readonly Vector2 mVegetativeTemperature;

	/// <summary>
	/// Threshold to transform to a full plant
	/// </summary>
	readonly float mAwakeThreshold;
	/// <summary>
	/// Sphere radius
	/// </summary>
	public float Radius => mRadius;
	/// <summary>
	/// Amount of energy currrently stored
	/// </summary>    
	public float StoredEnergy => mEnergyStored;
	/// <summary>
	/// Ratio ∈ [0, 1] of the required energy to start growing roots and stems
	/// </summary>
	public float EnergyAccumulationProgress => mEnergyStored / mAwakeThreshold;
	
	public SeedAgent(Vector3 center, float radius, Vector2 vegetativeTemperature, float energy = -1f)
	{
		Center = center;
		mRadius = radius;
		if (energy < 0f)
			mEnergyStored = radius * radius * radius * 100f;
		else
			mEnergyStored = energy;
		
		mAwakeThreshold = mEnergyStored * 500f + 100f*radius;
		mVegetativeTemperature = vegetativeTemperature;
	}

	public void Tick(SimulationWorld world, IFormation _formation, int formationID, uint timestep)
	{
		var formation = (PlantFormation)_formation;
		mEnergyStored -= Radius * Radius * Radius / AgroWorld.TicksPerHour; //life support
		if (mEnergyStored <= 0) //energy depleted
		{
			mEnergyStored = 0f;
			formation.SeedDeath();
		}
		else
		{
			if (mEnergyStored >= mAwakeThreshold)
			{
				formation.UnderGroundBirth(new UnderGroundAgent(-1, default, -Vector3.UnitY, mEnergyStored * 0.4f));
				formation.AboveGroundBirth(new AboveGroundAgent(-1, OrganTypes.Stem, default, Vector3.UnitY, 0.0001f, 0.0002f, mEnergyStored * 0.4f));
				formation.AboveGroundBirth(new AboveGroundAgent(0, OrganTypes.Leaf, formation.GetDirection_AG(0), Vector3.UnitY, 0.0001f, 0.0003f, mEnergyStored * 0.2f));
				formation.SeedDeath();
				mEnergyStored = 0f;
			}
			else
			{
				var soil = formation.Soil;
				//find all soild cells that the shpere intersects
				var sources = soil.IntersectSphere(Center, Radius);
				if (sources.Count > 0) //TODO this is a rough approximation taking only the first intersected soil cell
				{
					var amount = Pi4 * mRadius * mRadius; //sphere surface is 4πr²
					var soilTemperature = soil.GetTemperature(sources[0]);
					if (soilTemperature > mVegetativeTemperature.X)
					{
						if (soilTemperature < mVegetativeTemperature.Y)
							amount *= (soilTemperature - mVegetativeTemperature.X) / (mVegetativeTemperature.Y - mVegetativeTemperature.X);
						mEnergyStored += amount * 0.7f; //store most of the energy, 0.2f are losses
						mRadius = MathF.Pow(Radius * Radius * Radius + amount * PiV, Third); //use the rest for growth
						soil.Send(sources[0], new SoilAgent.SeedWaterRequestToSoilMsg(amount / AgroWorld.TicksPerHour, formation, formationID));
					}
				}
			}
		}
	}

	public void IncWater(float amount)
	{
		Debug.Assert(amount >= 0f);
		mEnergyStored += amount * 0.7f; //store most of the energy, 0.2f are losses
		mRadius = MathF.Pow(Radius * Radius * Radius + amount * PiV, Third); //use the rest for growth
	}
}
