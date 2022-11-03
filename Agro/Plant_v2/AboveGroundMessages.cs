using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Numerics;
using AgentsSystem;
using System.Runtime.InteropServices;

namespace Agro;

public partial struct AboveGroundAgent2 : IPlantAgent
{
	[StructLayout(LayoutKind.Auto)]
	[Message]
	public readonly struct WaterInc : IMessage<AboveGroundAgent2>
	{
		#if HISTORY_LOG || TICK_LOG
		public readonly static List<SimpleMsgLog> MessagesHistory = new();
		public static void ClearHistory() => MessagesHistory.Clear();
		public readonly ulong ID { get; } = Utils.UID.Next();
		#endif

		public readonly float Amount;
		public WaterInc(float amount) => Amount = amount;
		public bool Valid => Amount > 0f;
		public Transaction Type => Transaction.Increase;
		public void Receive(ref AboveGroundAgent2 dstAgent, uint timestep, byte stage)
		{
			dstAgent.IncWater(Amount);
			#if HISTORY_LOG || TICK_LOG
			lock(MessagesHistory) MessagesHistory.Add(new(timestep, stage, ID, dstAgent.ID, Amount));
			#endif
		}
	}

	[StructLayout(LayoutKind.Auto)]
	[Message]
	public readonly struct WaterDec : IMessage<AboveGroundAgent2>
	{
		#if HISTORY_LOG || TICK_LOG
		public readonly static List<SimpleMsgLog> MessagesHistory = new();
		public static void ClearHistory() => MessagesHistory.Clear();
		public readonly ulong ID { get; } = Utils.UID.Next();
		#endif

		public readonly float Amount;
		public WaterDec(float amount) => Amount = amount;
		public bool Valid => Amount > 0f;
		public Transaction Type => Transaction.Increase;
		public void Receive(ref AboveGroundAgent2 dstAgent, uint timestep, byte stage)
		{
			dstAgent.TryDecWater(Amount);
			#if HISTORY_LOG || TICK_LOG
			lock(MessagesHistory) MessagesHistory.Add(new(timestep, stage, ID, dstAgent.ID, -Amount));
			#endif
		}
	}

	[StructLayout(LayoutKind.Auto)]
	[Message]
	public readonly struct EnergyInc : IMessage<AboveGroundAgent2>
	{
		#if HISTORY_LOG || TICK_LOG
		public readonly static List<SimpleMsgLog> MessagesHistory = new();
		public static void ClearHistory() => MessagesHistory.Clear();
		public readonly ulong ID { get; } = Utils.UID.Next();
		#endif

		public readonly float Amount;
		public EnergyInc(float amount) => Amount = amount;
		public bool Valid => Amount > 0f;
		public Transaction Type => Transaction.Increase;
		public void Receive(ref AboveGroundAgent2 dstAgent, uint timestep, byte stage)
		{
			dstAgent.IncEnergy(Amount);
			#if HISTORY_LOG || TICK_LOG
			lock(MessagesHistory) MessagesHistory.Add(new(timestep, stage, ID, dstAgent.ID, Amount));
			#endif
		}
	}

	[StructLayout(LayoutKind.Auto)]
	[Message]
	public readonly struct EnergyDec : IMessage<AboveGroundAgent2>
	{
		#if HISTORY_LOG || TICK_LOG
		public readonly static List<SimpleMsgLog> MessagesHistory = new();
		public static void ClearHistory() => MessagesHistory.Clear();
		public readonly ulong ID { get; } = Utils.UID.Next();
		#endif

		public readonly float Amount;
		public EnergyDec(float amount) => Amount = amount;
		public bool Valid => Amount > 0f;
		public Transaction Type => Transaction.Increase;
		public void Receive(ref AboveGroundAgent2 dstAgent, uint timestep, byte stage)
		{
			dstAgent.IncEnergy(Amount);
			#if HISTORY_LOG || TICK_LOG
			lock(MessagesHistory) MessagesHistory.Add(new(timestep, stage, ID, dstAgent.ID, -Amount));
			#endif
		}
	}

	// [StructLayout(LayoutKind.Auto)]
	// [Message]
	// public readonly struct Water_PullFrom : IMessage<AboveGroundAgent>
	// {
	// 	#if HISTORY_LOG || TICK_LOG
	// 	public readonly static List<PullMsgLog> MessagesHistory = new();
	// 	public static void ClearHistory() => MessagesHistory.Clear();
	// 	public readonly ulong ID { get; } = Utils.UID.Next();
	// 	#endif

	// 	public readonly float Amount;
	// 	public readonly PlantSubFormation<AboveGroundAgent> DstFormation;
	// 	public readonly int DstIndex;
	// 	public Water_PullFrom(PlantSubFormation<AboveGroundAgent> dstFormation, float amount, int dstIndex)
	// 	{
	// 		Amount = amount;
	// 		DstFormation = dstFormation;
	// 		DstIndex = dstIndex;
	// 	}
	// 	public bool Valid => Amount > 0f && DstFormation.CheckIndex(DstIndex);
	// 	public Transaction Type => Transaction.Decrease;

	// 	public void Receive(ref AboveGroundAgent srcAgent, uint timestep)
	// 	{
	// 		var freeCapacity = Math.Max(0f, DstFormation.GetWaterTotalCapacity(DstIndex) - DstFormation.GetWater(DstIndex));
	// 		var water = srcAgent.TryDecWater(Math.Min(Amount, freeCapacity));
	// 		if (water > 0f)
	// 		{
	// 			DstFormation.SendProtected(DstIndex, new WaterInc(water));
	// 			#if HISTORY_LOG || TICK_LOG
	// 			lock(MessagesHistory) MessagesHistory.Add(new(timestep, ID, srcAgent.ID, DstFormation.GetID(DstIndex), water));
	// 			#endif
	// 		}
	// 	}
	// }

	// [StructLayout(LayoutKind.Auto)]
	// [Message]
	// public readonly struct Energy_PullFrom: IMessage<AboveGroundAgent>
	// {
	// 	#if HISTORY_LOG || TICK_LOG
	// 	public readonly static List<PullMsgLog> MessagesHistory = new();
	// 	public static void ClearHistory() => MessagesHistory.Clear();
	// 	public readonly ulong ID { get; } = Utils.UID.Next();
	// 	#endif

	// 	public readonly float Amount;
	// 	public readonly PlantSubFormation<AboveGroundAgent> DstFormation;
	// 	public readonly int DstIndex;
	// 	public Energy_PullFrom(PlantSubFormation<AboveGroundAgent> dstFormation, float amount, int dstIndex)
	// 	{
	// 		Amount = amount;
	// 		DstFormation = dstFormation;
	// 		DstIndex = dstIndex;
	// 	}
	// 	public bool Valid => Amount > 0f && DstFormation.CheckIndex(DstIndex);
	// 	public Transaction Type => Transaction.Decrease;

	// 	public void Receive(ref AboveGroundAgent srcAgent, uint timestep)
	// 	{
	// 		var freeCapacity = Math.Max(0f, DstFormation.GetEnergyCapacity(DstIndex) - DstFormation.GetEnergy(DstIndex));

	// 		var energy = srcAgent.TryDecEnergy(Math.Min(Amount, freeCapacity));
	// 		if (energy > 0f)
	// 		{
	// 			DstFormation.SendProtected(DstIndex, new EnergyInc(energy));
	// 			#if HISTORY_LOG || TICK_LOG
	// 			lock(MessagesHistory) MessagesHistory.Add(new(timestep, ID, srcAgent.ID, DstFormation.GetID(DstIndex), energy));
	// 			#endif
	// 		}
	// 	}
	// }
}