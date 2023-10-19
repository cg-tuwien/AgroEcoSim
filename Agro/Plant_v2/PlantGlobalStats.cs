using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Timers;
using AgentsSystem;
using glTFLoader.Schema;
using NumericHelpers;
using Utils;

namespace Agro;
public class PlantGlobalStats
{
	public double Energy { get; set; }
	public double Water { get; set; }

	public double EnergyDiff { get; set; }
	public double WaterDiff { get; set; }

	public double EnergyCapacity { get; set; }
	public double WaterCapacity { get; set; }

	public double EnergyRequirementPerTick{ get; set; }
	public double WaterRequirementPerTick { get; set; }

	public List<GatherDataBase> Gathering = new();

	public float[]? ReceivedEnergy;
	public float[]? ReceivedWater;

	internal double Weights4EnergyDistributionByRequirement()
	{
		//if (MaxProduction > 0)
		{
			var weightsTotal = 0.0;
			for(int i = 0; i < Gathering.Count; ++i)
				weightsTotal = Gathering[i].LifesupportEnergy * Gathering[i].ProductionEfficiency;
			return weightsTotal;
		}
		// else
		// 	return EnergyRequirementPerTick;
	}

	internal double Weights4EnergyDistributionByStorage()
	{
		var weightsTotal = 0.0;
		//if (MaxResources > 0)
			for(int i = 0; i < Gathering.Count; ++i)
				weightsTotal += Gathering[i].CapacityEnergy * Gathering[i].ResourcesEfficiency;
		// else
		// 	for(int i = 0; i < Efficiencies.Count; ++i)
		// 		weightsTotal += Gathering[i].CapacityEnergy;

		return weightsTotal;
	}

	internal void DistributeEnergyByStorage(float factor)
	{
		ReceivedEnergy = new float[Gathering.Count];
		//if (MaxResources > 0)
			for(int i = 0; i < ReceivedEnergy.Length; ++i)
			{
				var w = Gathering[i].CapacityEnergy * Gathering[i].ResourcesEfficiency;
				ReceivedEnergy[i] = Gathering[i].LifesupportEnergy + w * factor;
			}
		// else
		// 	for(int i = 0; i < ReceivedEnergy.Length; ++i)
		// 		ReceivedEnergy[i] = Gathering[i].LifesupportEnergy + Gathering[i].CapacityEnergy * factor;
	}

	internal void DistributeEnergyByRequirement(float factor)
	{
		//factor is energyAvailableTotal / energyRequirementTotal
		ReceivedEnergy = new float[Gathering.Count];

		//if (MaxProduction > 0)
		{
			for(int i = 0; i < ReceivedEnergy.Length; ++i)
				ReceivedEnergy[i] = Gathering[i].LifesupportEnergy * Gathering[i].ProductionEfficiency * factor; //in sum over all i: LifeSupportEnergy[i] / energyRequirementTotal yields 1
		}
		// else
		// {
		// 	for(int i = 0; i < ReceivedEnergy.Length; ++i)
		// 		ReceivedEnergy[i] = Gathering[i].LifesupportEnergy * factor; //in sum over all i: LifeSupportEnergy[i] / energyRequirementTotal yields 1
		// }
	}

	internal void DistributeWaterByStorage(float factor)
	{
		ReceivedWater = new float[Gathering.Count];
		for(int i = 0; i < ReceivedWater.Length; ++i)
			ReceivedWater[i] = Gathering[i].PhotosynthWater + Gathering[i].CapacityWater * factor;
	}

	internal void DistributeWaterByRequirement(float factor)
	{
		ReceivedWater = new float[Gathering.Count];
		for(int i = 0; i < ReceivedWater.Length; ++i)
			ReceivedWater[i] = Gathering[i].PhotosynthWater * factor;
	}

}
