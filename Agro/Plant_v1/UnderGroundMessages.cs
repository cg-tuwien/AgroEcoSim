using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AgentsSystem;
using System.Buffers;

namespace Agro;

public partial struct UnderGroundAgent : IAgent
{
    [StructLayout(LayoutKind.Auto)]
    [Message]
    public readonly struct WaterInc : IMessage<UnderGroundAgent>
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
        public void Receive(ref UnderGroundAgent dstAgent, uint timestep, byte stage)
        {
            dstAgent.IncWater(Amount);
			#if HISTORY_LOG || TICK_LOG
			lock(MessagesHistory) MessagesHistory.Add(new(timestep, stage, ID, dstAgent.ID, Amount));
			#endif
        }
    }

    [StructLayout(LayoutKind.Auto)]
    [Message]
    public readonly struct WaterDec : IMessage<UnderGroundAgent>
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
        public void Receive(ref UnderGroundAgent dstAgent, uint timestep, byte stage)
        {
            dstAgent.TryDecWater(Amount);
			#if HISTORY_LOG || TICK_LOG
			lock(MessagesHistory) MessagesHistory.Add(new(timestep, stage, ID, dstAgent.ID, -Amount));
			#endif
        }
    }

    [StructLayout(LayoutKind.Auto)]
    [Message]
    public readonly struct EnergyInc : IMessage<UnderGroundAgent>
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
        public void Receive(ref UnderGroundAgent dstAgent, uint timestep, byte stage)
        {
            dstAgent.IncEnergy(Amount);
			#if HISTORY_LOG || TICK_LOG
			lock(MessagesHistory) MessagesHistory.Add(new(timestep, stage, ID, dstAgent.ID, Amount));
			#endif
        }
    }

    [StructLayout(LayoutKind.Auto)]
    [Message]
    public readonly struct EnergyDec : IMessage<UnderGroundAgent>
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
        public void Receive(ref UnderGroundAgent dstAgent, uint timestep, byte stage)
        {
            dstAgent.TryDecEnergy(Amount);
			#if HISTORY_LOG || TICK_LOG
			lock(MessagesHistory) MessagesHistory.Add(new(timestep, stage, ID, dstAgent.ID, -Amount));
			#endif
        }
    }

    // [StructLayout(LayoutKind.Auto)]
    // [Message]
    // public readonly struct Energy_PullFrom: IMessage<UnderGroundAgent>
    // {
    //     #if HISTORY_LOG || TICK_LOG
	// 	public readonly static List<PullMsgLog> MessagesHistory = new();
    //     public static void ClearHistory() => MessagesHistory.Clear();
	// 	public readonly ulong ID { get; } = Utils.UID.Next();
	// 	#endif

    //     public readonly float Amount;
    //     public readonly PlantSubFormation<UnderGroundAgent> DstFormation;
    //     public readonly int DstIndex;
    //     public Energy_PullFrom(PlantSubFormation<UnderGroundAgent> dstFormation, float amount, int dstIndex)
    //     {
    //         Amount = amount;
    //         DstFormation = dstFormation;
    //         DstIndex = dstIndex;
    //     }
    //     public bool Valid => Amount > 0f && DstFormation.CheckIndex(DstIndex);
    //     public Transaction Type => Transaction.Decrease;

    //     public void Receive(ref UnderGroundAgent srcAgent, uint timestep)
    //     {
    //         var freeCapacity = Math.Max(0f, DstFormation.GetEnergyCapacity(DstIndex) - DstFormation.GetEnergy(DstIndex));
    //         var energy = srcAgent.TryDecEnergy(Math.Min(freeCapacity, Amount));
    //         if (energy > 0f)
    //         {
    //             DstFormation.SendProtected(DstIndex, new EnergyInc(energy));
    //             #if HISTORY_LOG || TICK_LOG
    //             lock(MessagesHistory) MessagesHistory.Add(new(timestep, ID, srcAgent.ID, DstFormation.GetID(DstIndex), energy));
    //             #endif
    //         }
    //     }
    // }

    [StructLayout(LayoutKind.Auto)]
    [Message]
    public readonly struct Energy_PullFrom_AG: IMessage<AboveGroundAgent>
    {
        #if HISTORY_LOG || TICK_LOG
		public readonly static List<PullMsgLog> MessagesHistory = new();
        public static void ClearHistory() => MessagesHistory.Clear();
		public readonly ulong ID { get; } = Utils.UID.Next();
		#endif

        public readonly float Amount;
        public readonly PlantSubFormation<UnderGroundAgent> DstFormation;
        public readonly int DstIndex;
        public Energy_PullFrom_AG(PlantSubFormation<UnderGroundAgent> dstFormation, float amount, int dstIndex)
        {
            Amount = amount;
            DstFormation = dstFormation;
            DstIndex = dstIndex;
        }
        public bool Valid => Amount > 0f && DstFormation.CheckIndex(DstIndex);
        public Transaction Type => Transaction.Decrease;

        public void Receive(ref AboveGroundAgent srcAgent, uint timestep, byte stage)
        {
            var freeCapacity = Math.Max(0f, DstFormation.GetEnergyCapacity(DstIndex) - DstFormation.GetEnergy(DstIndex));
            var energy = srcAgent.TryDecEnergy(Math.Min(Amount, freeCapacity));
            if (energy > 0f)
            {
                DstFormation.SendProtected(DstIndex, new EnergyInc(energy));
                #if HISTORY_LOG || TICK_LOG
                lock(MessagesHistory) MessagesHistory.Add(new(timestep, stage, ID, srcAgent.ID, DstFormation.GetID(DstIndex), energy));
			    #endif
            }
        }
    }

    // [StructLayout(LayoutKind.Auto)]
    // [Message]
    // public readonly struct Water_PullFrom : IMessage<UnderGroundAgent>
    // {
	// 	#if HISTORY_LOG || TICK_LOG
	// 	public readonly static List<PullMsgLog> MessagesHistory = new();
    //     public static void ClearHistory() => MessagesHistory.Clear();
	// 	public readonly ulong ID { get; } = Utils.UID.Next();
	// 	#endif

    //     /// <summary>
    //     /// Water volume in m³
    //     /// </summary>
    //     public readonly float Amount;
    //     public readonly PlantSubFormation<UnderGroundAgent> DstFormation;
    //     public readonly int DstIndex;
    //     public Water_PullFrom(PlantSubFormation<UnderGroundAgent> dstFormation, float amount, int dstIndex)
    //     {
    //         Amount = amount;
    //         DstFormation = dstFormation;
    //         DstIndex = dstIndex;
    //     }
    //     public bool Valid => Amount > 0f && DstFormation.CheckIndex(DstIndex);
    //     public Transaction Type => Transaction.Decrease;

    //     public void Receive(ref UnderGroundAgent srcAgent, uint timestep)
    //     {
    //         var freeCapacity = Math.Max(0f, DstFormation.GetWaterTotalCapacity(DstIndex) - DstFormation.GetWater(DstIndex));
    //         var water = srcAgent.TryDecWater(Math.Min(freeCapacity, Amount));
    //         if (water > 0f)
    //         {
    //             DstFormation.SendProtected(DstIndex, new WaterInc(water));
	// 		    #if HISTORY_LOG || TICK_LOG
	// 		    lock(MessagesHistory) MessagesHistory.Add(new(timestep, ID, srcAgent.ID, DstFormation.GetID(DstIndex), water));
	// 		    #endif
    //         }
    //     }
    // }

    [StructLayout(LayoutKind.Auto)]
    [Message]
    public readonly struct Water_AG_PullFrom_UG : IMessage<UnderGroundAgent>
    {
		#if HISTORY_LOG || TICK_LOG
		public readonly static List<PullMsgLog> MessagesHistory = new();
        public static void ClearHistory() => MessagesHistory.Clear();
		public readonly ulong ID { get; } = Utils.UID.Next();
		#endif

        public readonly float Amount;
        public readonly PlantSubFormation<AboveGroundAgent> DstFormation;
        public readonly int DstIndex;
        public Water_AG_PullFrom_UG(PlantSubFormation<AboveGroundAgent> dstFormation, float amount, int dstIndex)
        {
            Amount = amount;
            DstFormation = dstFormation;
            DstIndex = dstIndex;
        }
        public bool Valid => Amount > 0f && DstFormation.CheckIndex(DstIndex);
        public Transaction Type => Transaction.Decrease;

        public void Receive(ref UnderGroundAgent srcAgent, uint timestep, byte stage)
        {
            var freeCapacity = Math.Max(0f, DstFormation.GetWaterTotalCapacity(DstIndex) - DstFormation.GetWater(DstIndex));
            var water = srcAgent.TryDecWater(Math.Min(Amount, freeCapacity));
            if (water > 0f)
            {
                DstFormation.SendProtected(DstIndex, new AboveGroundAgent.WaterInc(water));
			    #if HISTORY_LOG || TICK_LOG
			    lock(MessagesHistory) MessagesHistory.Add(new(timestep, stage, ID, srcAgent.ID, DstFormation.GetID(DstIndex), water));
                #endif
            }
        }
    }
}
