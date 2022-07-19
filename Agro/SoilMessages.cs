using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using AgentsSystem;
using Utils;

namespace Agro;

public partial struct SoilAgent : IAgent
{
	[StructLayout(LayoutKind.Auto)]
	[Message]
	public readonly struct WaterInc : IMessage<SoilAgent>
	{
		#if HISTORY_LOG || TICK_LOG
		public readonly static List<SimpleMsgLog> TransactionsHistory = new();
		public static void ClearHistory() => TransactionsHistory.Clear();
		public readonly ulong ID { get; } = Utils.UID.Next();
		#endif

		public readonly float Amount;
		public WaterInc(float amount) => Amount = amount;
		public bool Valid => Amount > 0f;
		public Transaction Type => Transaction.Increase;
		public void Receive(ref SoilAgent dstAgent, uint timestep)
		{
			dstAgent.IncWater(Amount);
			#if HISTORY_LOG || TICK_LOG
			lock(TransactionsHistory) TransactionsHistory.Add(new(timestep, ID, dstAgent.ID, Amount));
			#endif
		}
	}

	[StructLayout(LayoutKind.Auto)]
	[Message]
	public readonly struct Water_PullFrom : IMessage<SoilAgent>
	{
        #if HISTORY_LOG || TICK_LOG
		public readonly static List<PullMsgLog> TransactionsHistory = new();
		public static void ClearHistory() => TransactionsHistory.Clear();
		public readonly ulong ID { get; } = Utils.UID.Next();
		#endif

		public readonly SoilFormation DstFormation;
		public readonly float Amount;
		public readonly Vector3i DstIndex;
		public Water_PullFrom(SoilFormation dstFormation, float amount, Vector3i dstID)
		{
			DstFormation = dstFormation;
			Amount = amount;
			DstIndex = dstID;
		}
		public Water_PullFrom(SoilFormation dstFormation, float amount, int dstX, int dstY, int dstZ)
		{
			DstFormation = dstFormation;
			Amount = amount;
			DstIndex = new Vector3i(dstX, dstY, dstZ);
		}
		public bool Valid => Amount > 0f && DstFormation.CheckCoords(DstIndex);
		public Transaction Type => Transaction.Decrease;
		public void Receive(ref SoilAgent srcAgent, uint timestep)
		{
			var freeCapacity = Math.Max(0f, DstFormation.GetWaterCapacity(DstIndex) - DstFormation.GetWater(DstIndex));
			var water = srcAgent.TryDecWater(Math.Min(Amount, freeCapacity));
			if (water > 0f)
			{
				DstFormation.Send(DstIndex, new WaterInc(water));
				#if HISTORY_LOG || TICK_LOG
				lock(TransactionsHistory) TransactionsHistory.Add(new(timestep, ID, srcAgent.ID, DstFormation.GetID(DstIndex), water));
				#endif
			}
		}
	}

	// [StructLayout(LayoutKind.Auto)]
	// public readonly struct SteamDiffusionMsg : IMessage<SoilAgent>
	// {
	// 	public readonly float Amount;
	// 	public SteamDiffusionMsg(float amount) => Amount = amount;
	// 	public Transaction Type => Transaction.Increase;
	// 	public void Receive(ref SoilAgent agent) => agent.IncSteam(Amount);
	// }

	[StructLayout(LayoutKind.Auto)]
	[Message]
	public readonly struct Water_UG_PullFrom_Soil : IMessage<SoilAgent>
	{
        #if HISTORY_LOG || TICK_LOG
		public readonly static List<PullMsgLog> TransactionsHistory = new();
		public static void ClearHistory() => TransactionsHistory.Clear();
		public readonly ulong ID { get; } = Utils.UID.Next();
		#endif

		/// <summary>
		/// Water volume in m³
		/// </summary>
		public readonly float Amount;
		public readonly PlantSubFormation<UnderGroundAgent> DstFormation;
		public readonly int DstIndex;
		public Water_UG_PullFrom_Soil(PlantSubFormation<UnderGroundAgent> dstFormation, float amount, int dstIndex)
		{
			Amount = amount;
			DstFormation = dstFormation;
			DstIndex = dstIndex;
		}
		public bool Valid => Amount > 0f && DstFormation.CheckIndex(DstIndex);
		public Transaction Type => Transaction.Decrease;
		public void Receive(ref SoilAgent srcAgent, uint timestep)
		{
			var freeCapacity = Math.Max(0f, DstFormation.GetWaterCapacityPerTick(DstIndex) - DstFormation.GetWater(DstIndex));
			var water = srcAgent.TryDecWater(Math.Min(Amount, freeCapacity));
			//Writing actions from other formations must not be implemented directly, but over messages
			if (water > 0f)
			{
				DstFormation.SendProtected(DstIndex, new UnderGroundAgent.WaterInc(water));
				#if HISTORY_LOG || TICK_LOG
				lock(TransactionsHistory) TransactionsHistory.Add(new(timestep, ID, srcAgent.ID, DstFormation.GetID(DstIndex), water));
				#endif
			}
		}
	}

	[StructLayout(LayoutKind.Auto)]
	[Message]
	public readonly struct Water_Seed_PullFrom_Soil : IMessage<SoilAgent>
	{
		#if HISTORY_LOG || TICK_LOG
		public readonly static List<PullMsgLog> TransactionsHistory = new();
		public static void ClearHistory() => TransactionsHistory.Clear();
		public readonly ulong ID { get; } = Utils.UID.Next();
		#endif

		public readonly float Amount;
		public readonly PlantFormation DstFormation;
		//SeedIndex is always 0

		public Water_Seed_PullFrom_Soil(PlantFormation dstFormation, float amount)
		{
			Amount = amount;
			DstFormation = dstFormation;
		}
		public bool Valid => Amount > 0f && DstFormation.SeedAlive;
		public Transaction Type => Transaction.Decrease;
		public void Receive(ref SoilAgent srcAgent, uint timestep)
		{
			var water = srcAgent.TryDecWater(Amount);
			if (water > 0f)
			{
				DstFormation.Send(0, new SeedAgent.WaterInc(water)); //there is always just one seed
				#if HISTORY_LOG || TICK_LOG
				lock(TransactionsHistory) TransactionsHistory.Add(new(timestep, ID, srcAgent.ID, DstFormation.GetID(), water));
				#endif
			}
		}
	}
}
