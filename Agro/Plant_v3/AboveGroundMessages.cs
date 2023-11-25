using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Numerics;
using AgentsSystem;
using System.Runtime.InteropServices;

namespace Agro;

public partial struct AboveGroundAgent3 : IPlantAgent
{
	[StructLayout(LayoutKind.Auto)]
	[Message]
	public readonly struct WaterInc : IMessage<AboveGroundAgent3>
	{
		public readonly float Amount;
		public WaterInc(float amount) => Amount = amount;
		public bool Valid => Amount > 0f;
		public Transaction Type => Transaction.Increase;
        public void Receive(ref AboveGroundAgent3 dstAgent, uint timestep) => dstAgent.IncWater(Amount);
    }

	[StructLayout(LayoutKind.Auto)]
	[Message]
	public readonly struct WaterDec : IMessage<AboveGroundAgent3>
	{
		public readonly float Amount;
		public WaterDec(float amount) => Amount = amount;
		public bool Valid => Amount > 0f;
		public Transaction Type => Transaction.Increase;
        public void Receive(ref AboveGroundAgent3 dstAgent, uint timestep) => dstAgent.TryDecWater(Amount);
    }

	[StructLayout(LayoutKind.Auto)]
	[Message]
	public readonly struct EnergyInc : IMessage<AboveGroundAgent3>
	{
		public readonly float Amount;
		public EnergyInc(float amount) => Amount = amount;
		public bool Valid => Amount > 0f;
		public Transaction Type => Transaction.Increase;
        public void Receive(ref AboveGroundAgent3 dstAgent, uint timestep) => dstAgent.IncEnergy(Amount);
    }

	[StructLayout(LayoutKind.Auto)]
	[Message]
	public readonly struct EnergyDec : IMessage<AboveGroundAgent3>
	{
		public readonly float Amount;
		public EnergyDec(float amount) => Amount = amount;
		public bool Valid => Amount > 0f;
		public Transaction Type => Transaction.Increase;
        public void Receive(ref AboveGroundAgent3 dstAgent, uint timestep) => dstAgent.IncEnergy(Amount);
    }
}
