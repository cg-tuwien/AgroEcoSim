using System;
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

	public double EnergyRequirement{ get; set; }
	public double WaterRequirement { get; set; }

	public double UsefulnessTotal { get; set; }

	public IList<float> LightEfficiency { get; set; }
	public IList<float> EnergyEfficiency { get; set; }
	public IList<float> LifeSupportEnergy { get; set; }
	public IList<float> PhotosynthWater { get; set; }
	public IList<float> EnergyCapacities { get; set; }
	public IList<float> WaterCapacities { get; set; }

	public float[]? ReceivedEnergy;
	public float[]? ReceivedWater;

	internal void DistributeWaterByRequirement(float factor)
	{
		ReceivedWater = new float[PhotosynthWater.Count];
		for(int i = 0; i < ReceivedWater.Length; ++i)
			ReceivedWater[i] = PhotosynthWater[i] * factor;
	}

	internal double Weights4EnergyDistributionByRequirement(bool positiveEfficiency)
	{
		if (positiveEfficiency)
		{
			var weightsTotal = 0.0;
			for(int i = 0; i < EnergyEfficiency.Count; ++i)
				weightsTotal = LifeSupportEnergy[i] * EnergyEfficiency[i];
			return weightsTotal;
		}
		else
			return EnergyRequirement;
	}

	internal double Weights4EnergyDistributionByStorage(bool positiveEfficiency)
	{
		var weightsTotal = 0.0;
		if (positiveEfficiency)
			for(int i = 0; i < LightEfficiency.Count; ++i)
				weightsTotal += (EnergyCapacities[i] - LifeSupportEnergy[i]) * LightEfficiency[i];
		else
			for(int i = 0; i < LightEfficiency.Count; ++i)
				weightsTotal += EnergyCapacities[i] - LifeSupportEnergy[i];

		return weightsTotal;
	}

	internal void DistributeEnergyByStorage(float factor, bool positiveEfficiency)
	{
		ReceivedEnergy = new float[LifeSupportEnergy.Count];
		if (positiveEfficiency)
			for(int i = 0; i < ReceivedEnergy.Length; ++i)
			{
				var w = (EnergyCapacities[i] - LifeSupportEnergy[i]) * LightEfficiency[i];
				ReceivedEnergy[i] = LifeSupportEnergy[i] + w * factor;
			}
		else
			for(int i = 0; i < ReceivedEnergy.Length; ++i)
				ReceivedEnergy[i] = LifeSupportEnergy[i] + (EnergyCapacities[i] - LifeSupportEnergy[i]) * factor;
	}

	internal void DistributeEnergyByRequirement(float factor, bool positiveEfficiency)
	{
		//factor is energyAvailableTotal / energyRequirementTotal
		ReceivedEnergy = new float[LifeSupportEnergy.Count];

		if (positiveEfficiency)
		{
			for(int i = 0; i < ReceivedEnergy.Length; ++i)
				ReceivedEnergy[i] = LifeSupportEnergy[i] * EnergyEfficiency[i] * factor; //in sum over all i: LifeSupportEnergy[i] / energyRequirementTotal yields 1
		}
		else
		{
			for(int i = 0; i < ReceivedEnergy.Length; ++i)
				ReceivedEnergy[i] = LifeSupportEnergy[i] * factor; //in sum over all i: LifeSupportEnergy[i] / energyRequirementTotal yields 1
		}
	}

	internal void DistributeWaterByStorage(float factor)
	{
		ReceivedWater = new float[PhotosynthWater.Count];
		for(int i = 0; i < ReceivedWater.Length; ++i)
			ReceivedWater[i] = PhotosynthWater[i] + (WaterCapacities[i] - PhotosynthWater[i]) * factor;
	}
}
