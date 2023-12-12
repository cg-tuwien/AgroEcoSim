// using System;
// using System.Collections.Generic;
// using System.Numerics;
// using System.Runtime.InteropServices;
// using AgentsSystem;
// using Utils;

// namespace Agro;

// public partial struct SoilAgent : IAgent
// {
// 	[StructLayout(LayoutKind.Auto)]
// 	[Message]
// 	public readonly struct WaterInc : IMessage<SoilAgent>
// 	{
// 		public readonly float Amount;
// 		public WaterInc(float amount) => Amount = amount;
// 		public bool Valid => Amount > 0f;
// 		public Transaction Type => Transaction.Increase;
//         public void Receive(ref SoilAgent dstAgent, uint timestep) => dstAgent.IncWater(Amount);
//     }

// 	[StructLayout(LayoutKind.Auto)]
// 	[Message]
// 	public readonly struct Water_PullFrom : IMessage<SoilAgent>
// 	{
// 		public readonly SoilFormation DstFormation;
// 		public readonly float Amount;
// 		public readonly Vector3i DstIndex;
// 		public Water_PullFrom(SoilFormation dstFormation, float amount, Vector3i dstID)
// 		{
// 			DstFormation = dstFormation;
// 			Amount = amount;
// 			DstIndex = dstID;
// 		}
// 		public Water_PullFrom(SoilFormation dstFormation, float amount, int dstX, int dstY, int dstZ)
// 		{
// 			DstFormation = dstFormation;
// 			Amount = amount;
// 			DstIndex = new Vector3i(dstX, dstY, dstZ);
// 		}
// 		public bool Valid => Amount > 0f && DstFormation.Contains(DstIndex);
// 		public Transaction Type => Transaction.Decrease;
// 		public void Receive(ref SoilAgent srcAgent, uint timestep)
// 		{
// 			//var freeCapacity = Math.Max(0f, DstFormation.GetWaterCapacity(DstIndex) - DstFormation.GetWater(DstIndex));
// 			//var water = srcAgent.TryDecWater(Math.Min(Amount, freeCapacity));
// 			var water = srcAgent.TryDecWater(Amount);
// 			if (water > 0f)
// 				DstFormation.Send(DstIndex, new WaterInc(water));
// 		}
// 	}

// 	// [StructLayout(LayoutKind.Auto)]
// 	// public readonly struct SteamDiffusionMsg : IMessage<SoilAgent>
// 	// {
// 	// 	public readonly float Amount;
// 	// 	public SteamDiffusionMsg(float amount) => Amount = amount;
// 	// 	public Transaction Type => Transaction.Increase;
// 	// 	public void Receive(ref SoilAgent agent) => agent.IncSteam(Amount);
// 	// }

// 	[StructLayout(LayoutKind.Auto)]
// 	[Message]
// 	public readonly struct Water_UG_PullFrom_Soil2 : IMessage<SoilAgent>
// 	{
// 		/// <summary>
// 		/// Water volume in m³
// 		/// </summary>
// 		public readonly float Amount;
// 		public readonly PlantSubFormation2<UnderGroundAgent2> DstFormation;
// 		public readonly int DstIndex;
// 		public Water_UG_PullFrom_Soil2(PlantSubFormation2<UnderGroundAgent2> dstFormation, float amount, int dstIndex)
// 		{
// 			Amount = amount;
// 			DstFormation = dstFormation;
// 			DstIndex = dstIndex;
// 		}
// 		public bool Valid => Amount > 0f && DstFormation.CheckIndex(DstIndex);
// 		public Transaction Type => Transaction.Decrease;
// 		public void Receive(ref SoilAgent srcAgent, uint timestep)
// 		{
// 			//var freeCapacity = Math.Max(0f, DstFormation.GetWaterTotalCapacity(DstIndex) - DstFormation.GetWater(DstIndex));
// 			//var water = srcAgent.TryDecWater(Math.Min(Amount, freeCapacity));
// 			var water = srcAgent.TryDecWater(Amount);
// 			//Writing actions from other formations must not be implemented directly, but over messages
// 			if (water > 0f)
// 				DstFormation.SendProtected(DstIndex, new UnderGroundAgent2.WaterInc(water));
// 		}
// 	}

// 	[StructLayout(LayoutKind.Auto)]
// 	[Message]
// 	public readonly struct Water_Seed_PullFrom_Soil : IMessage<SoilAgent>
// 	{
// 		public readonly float Amount;
// 		public readonly IPlantFormation DstFormation;
// 		//SeedIndex is always 0

// 		public Water_Seed_PullFrom_Soil(IPlantFormation dstFormation, float amount)
// 		{
// 			Amount = amount;
// 			DstFormation = dstFormation;
// 		}
// 		public bool Valid => Amount > 0f && DstFormation.SeedAlive;
// 		public Transaction Type => Transaction.Decrease;
// 		public void Receive(ref SoilAgent srcAgent, uint timestep)
// 		{
// 			var water = srcAgent.TryDecWater(Amount);
// 			if (water > 0f)
// 				DstFormation.Send(0, new SeedAgent.WaterInc(water)); //there is always just one seed
// 		}
// 	}
// }
