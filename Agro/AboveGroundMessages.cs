using System;
using System.Linq;
using System.Diagnostics;
using System.Numerics;
using AgentsSystem;
using System.Runtime.InteropServices;

namespace Agro;

public partial struct AboveGroundAgent : IAgent
{
	[StructLayout(LayoutKind.Auto)]
	public readonly struct WaterInc : IMessage<AboveGroundAgent>
	{
		public readonly float Amount;
		public WaterInc(float amount) => Amount = amount;
		public Transaction Type => Transaction.Increase;
		public void Receive(ref AboveGroundAgent dstAgent) => dstAgent.IncWater(Amount);
	}

	[StructLayout(LayoutKind.Auto)]
	public readonly struct EnergyInc : IMessage<AboveGroundAgent>
	{
		public readonly float Amount;
		public EnergyInc(float amount) => Amount = amount;
		public Transaction Type => Transaction.Increase;
		public void Receive(ref AboveGroundAgent agent) => agent.IncEnergy(Amount);
	}

	[StructLayout(LayoutKind.Auto)]
	public readonly struct Water_AG_PullFrom_AG : IMessage<AboveGroundAgent>
	{
		public readonly float Amount;
		public readonly PlantFormation DstFormation;
		public readonly int DstIndex;
		public Water_AG_PullFrom_AG(PlantFormation dstFormation, float amount, int dstIndex)
		{
			Amount = amount;
			DstFormation = dstFormation;
			DstIndex = dstIndex;
		}
		public Transaction Type => Transaction.Decrease;

		public void Receive(ref AboveGroundAgent srcAgent)
		{
			var freeCapacity = Math.Max(0f, DstFormation.GetWaterCapacityPerTick_AG(DstIndex) - DstFormation.GetWater_AG(DstIndex));
			var water = srcAgent.TryDecWater(Math.Min(Amount, freeCapacity));
			if (water > 0) DstFormation.Send(DstIndex, new WaterInc(water));
		}
	}

	[StructLayout(LayoutKind.Auto)]
	public readonly struct Energy_AG_PullFrom_AG: IMessage<AboveGroundAgent>
	{
		public readonly float Amount;
		public readonly PlantFormation DstFormation;
		public readonly int DstIndex;
		public Energy_AG_PullFrom_AG(PlantFormation dstFormation, float amount, int dstIndex)
		{
			Amount = amount;
			DstFormation = dstFormation;
			DstIndex = dstIndex;
		}
		public Transaction Type => Transaction.Decrease;

		public void Receive(ref AboveGroundAgent agent)
		{
			var freeCapacity = Math.Max(0f, DstFormation.GetEnergyCapacity_AG(DstIndex) - DstFormation.GetEnergy_AG(DstIndex));
			var energy = agent.TryDecEnergy(Math.Min(Amount, freeCapacity));
			if (energy > 0) DstFormation.Send(DstIndex, new EnergyInc(energy));
		}
	}
}