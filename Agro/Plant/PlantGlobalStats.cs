using System.Diagnostics;

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
	public double WaterPriorityPerTick { get; set; }

	public List<GatherDataBase> Gathering = [];
	public int Count => Gathering.Count;

	public float[] ReceivedEnergy = new float[8];
	public float[] ReceivedWater = new float[8];
	int PrevCount = 0;
	readonly int[] PrevDiffs = [0, 0, 0, 0];
	byte PrevPointer = 0;

	public GatherDataBase this[int index] => Gathering[index];

	internal double Weights4EnergyDistributionByRequirement()
	{
		var weightsTotal = 0.0;
		for(int i = 0; i < Gathering.Count; ++i)
			weightsTotal += Gathering[i].LifesupportEnergy * Gathering[i].ProductionEfficiency;
		return weightsTotal;
	}

	internal double Weights4EnergyDistributionByStorage()
	{
		var weightsTotal = 0.0;
		for(int i = 0; i < Gathering.Count; ++i)
			weightsTotal += Gathering[i].CapacityEnergy * Gathering[i].ResourcesEfficiency;
		return weightsTotal;
	}

	void ResizeEnergyArray()
	{
		var diff = Gathering.Count - PrevCount;
		PrevCount = Gathering.Count;
		if (diff > 0)
		{
			PrevDiffs[PrevPointer++] = diff;
			if (PrevPointer >= PrevDiffs.Length)
				PrevPointer = 0;
		}

		if (Gathering.Count > ReceivedEnergy.Length)
		{
			var sum = PrevDiffs[0];
			for(int i = 1; i < PrevDiffs.Length; ++i)
				sum += PrevDiffs[i];

			Array.Resize(ref ReceivedEnergy, Gathering.Count + sum * 8); //increase the size more than necessary to spare furthere increases in the next steps
			Array.Resize(ref ReceivedWater, ReceivedEnergy.Length);
		}
	}

	void ResizeWaterArray()
	{
		if (Gathering.Count > ReceivedEnergy.Length)
			ResizeEnergyArray();

		if (ReceivedWater.Length < ReceivedEnergy.Length)
			Array.Resize(ref ReceivedWater, ReceivedEnergy.Length);
	}

	internal void DistributeEnergyByStorage(float factor)
	{
		// if(ReceivedEnergy.Length < Gathering.Count)
		ResizeEnergyArray();
		var limit = Gathering.Count;
		for(int i = 0; i < limit; ++i)
		{
			var w = Gathering[i].CapacityEnergy * Gathering[i].ResourcesEfficiency;
			ReceivedEnergy[i] = Gathering[i].LifesupportEnergy + w * factor;
		}
	}

	internal void DistributeEnergyByRequirement(float factor)
	{
		//factor is energyAvailableTotal / energyRequirementTotal
		ResizeEnergyArray();
		var limit = Gathering.Count;
		for(int i = 0; i < limit; ++i)
			ReceivedEnergy[i] = Gathering[i].LifesupportEnergy * Gathering[i].ProductionEfficiency * factor; //in sum over all i: LifeSupportEnergy[i] / energyRequirementTotal yields 1
	}

	// The photosynthetic forecast has first claim on tissue water. It is not
	// extra storage: a leaf can hold at most its current anatomical capacity.
	internal static float WaterPriority(in GatherDataBase data)
		=> Math.Clamp(data.PhotosynthWater, 0f, Math.Max(0f, data.CapacityWater));

	static float RoundWaterDown(double amount)
	{
		if (amount <= 0d) return 0f;
		var result = (float)amount;
		return (double)result > amount ? MathF.BitDecrement(result) : result;
	}

	internal void DistributeWaterByStorage(double factor)
	{
		ResizeWaterArray();
		var limit = Gathering.Count;
		for(int i = 0; i < limit; ++i)
		{
			var capacity = Math.Max(0f, Gathering[i].CapacityWater);
			var priority = WaterPriority(Gathering[i]);
			ReceivedWater[i] = RoundWaterDown(priority + (capacity - priority) * factor);
		}
	}

	internal void DistributeWaterByRequirement(double factor)
	{
		ResizeWaterArray();
		var limit = Gathering.Count;
		for(int i = 0; i < limit; ++i)
			ReceivedWater[i] = RoundWaterDown(WaterPriority(Gathering[i]) * factor);
	}

	internal double ReceivedWaterSum()
	{
		var total = 0d;
		for (int i = 0; i < Gathering.Count; ++i)
			total += ReceivedWater[i];
		return total;
	}

    internal float ReceivedEnergySum()
    {
        var result = 0f;
		var limit = Gathering.Count;
		for(int i = 0; i < limit; ++i)
			result += ReceivedEnergy[i];
		return result;
    }
}
