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

	public double UsefulnessTotal { get; set; }

	public List<GatherDataBase> Gathering = new();
	public List<GatherEfficiency> Efficiencies = new();

	public float[]? ReceivedEnergy;
	public float[]? ReceivedWater;

	internal void DistributeWaterByRequirement(float factor)
	{
		ReceivedWater = new float[Gathering.Count];
		for(int i = 0; i < ReceivedWater.Length; ++i)
			ReceivedWater[i] = Gathering[i].PhotosynthWater * factor;
	}

	internal double Weights4EnergyDistributionByRequirement(bool positiveEfficiency)
	{
		if (positiveEfficiency)
		{
			var weightsTotal = 0.0;
			for(int i = 0; i < Efficiencies.Count; ++i)
				weightsTotal = Gathering[i].LifesupportEnergy * Efficiencies[i].ProductionEfficiency;
			return weightsTotal;
		}
		else
			return EnergyRequirementPerTick;
	}

	internal double Weights4EnergyDistributionByStorage(bool positiveEfficiency)
	{
		var weightsTotal = 0.0;
		if (positiveEfficiency)
			for(int i = 0; i < Efficiencies.Count; ++i)
				weightsTotal += (Gathering[i].CapacityEnergy - Gathering[i].LifesupportEnergy) * Efficiencies[i].ResourceEfficiency;
		else
			for(int i = 0; i < Efficiencies.Count; ++i)
				weightsTotal += Gathering[i].CapacityEnergy - Gathering[i].LifesupportEnergy;

		return weightsTotal;
	}

	internal void DistributeEnergyByStorage(float factor, bool positiveEfficiency)
	{
		ReceivedEnergy = new float[Gathering.Count];
		if (positiveEfficiency)
			for(int i = 0; i < ReceivedEnergy.Length; ++i)
			{
				var w = (Gathering[i].CapacityEnergy - Gathering[i].LifesupportEnergy) * Efficiencies[i].ResourceEfficiency;
				ReceivedEnergy[i] = Gathering[i].LifesupportEnergy + w * factor;
			}
		else
			for(int i = 0; i < ReceivedEnergy.Length; ++i)
				ReceivedEnergy[i] = Gathering[i].LifesupportEnergy + (Gathering[i].CapacityEnergy - Gathering[i].LifesupportEnergy) * factor;
	}

	internal void DistributeEnergyByRequirement(float factor, bool positiveEfficiency)
	{
		//factor is energyAvailableTotal / energyRequirementTotal
		ReceivedEnergy = new float[Gathering.Count];

		if (positiveEfficiency)
		{
			for(int i = 0; i < ReceivedEnergy.Length; ++i)
				ReceivedEnergy[i] = Gathering[i].LifesupportEnergy * Efficiencies[i].ProductionEfficiency * factor; //in sum over all i: LifeSupportEnergy[i] / energyRequirementTotal yields 1
		}
		else
		{
			for(int i = 0; i < ReceivedEnergy.Length; ++i)
				ReceivedEnergy[i] = Gathering[i].LifesupportEnergy * factor; //in sum over all i: LifeSupportEnergy[i] / energyRequirementTotal yields 1
		}
	}

	internal void DistributeWaterByStorage(float factor)
	{
		ReceivedWater = new float[Gathering.Count];
		for(int i = 0; i < ReceivedWater.Length; ++i)
			ReceivedWater[i] = Gathering[i].PhotosynthWater + (Gathering[i].CapacityWater - Gathering[i].PhotosynthWater) * factor;
	}
}
