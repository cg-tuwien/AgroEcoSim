using System;
using System.Numerics;
using System.Runtime.InteropServices;
using AgentsSystem;
using Utils;

namespace Agro;

[StructLayout(LayoutKind.Auto)]
public struct SoilAgent : IAgent
{
	[StructLayout(LayoutKind.Auto)]
	public readonly struct WaterDiffusionMsg : IMessage<SoilAgent>
	{
		public readonly float Amount;
		public WaterDiffusionMsg(float amount) => Amount = amount;
		public Transaction Type => Transaction.Increase;
		public void Receive(ref SoilAgent agent) => agent.IncWater(Amount);
	}

	[StructLayout(LayoutKind.Auto)]
	public readonly struct SteamDiffusionMsg : IMessage<SoilAgent>
	{
		public readonly float Amount;
		public SteamDiffusionMsg(float amount) => Amount = amount;
		public Transaction Type => Transaction.Increase;
		public void Receive(ref SoilAgent agent) => agent.IncSteam(Amount);
	}

	[StructLayout(LayoutKind.Auto)]
	public readonly struct Water_UG_PullFrom_Soil : IMessage<SoilAgent>
	{
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
		public Transaction Type => Transaction.Decrease;
		public void Receive(ref SoilAgent srcAgent)
		{
			var freeCapacity = Math.Max(0f, DstFormation.GetWaterCapacityPerTick(DstIndex) - DstFormation.GetWater(DstIndex));
			var water = srcAgent.TryDecWater(Math.Min(Amount, freeCapacity));
			//Writing actions from other formations must not be implemented directly, but over messages
			if (water > 0) DstFormation.SendProtected(DstIndex, new UnderGroundAgent.WaterInc(water));
		}
	}

	[StructLayout(LayoutKind.Auto)]
	public readonly struct SeedWaterRequestToSoilMsg : IMessage<SoilAgent>
	{
		public readonly float Amount;
		public readonly PlantFormation Formation;
		public readonly int Index;
		public SeedWaterRequestToSoilMsg(float amount, PlantFormation formation, int index)
		{
			Amount = amount;
			Formation = formation;
			Index = index;
		}
		public Transaction Type => Transaction.Decrease;
		public void Receive(ref SoilAgent agent)
		{
			var w = agent.TryDecWater(Amount);
			if (w > 0) Formation.Send(Index, new SoilWaterToSeedMsg(w));
		}
	}

	public const float FieldCellSurface = AgroWorld.FieldResolution * AgroWorld.FieldResolution;

	internal const float SoilDiffusionCoefPerTick = 0.1f / AgroWorld.TicksPerHour;
	internal const float GravitationDiffusionCoefPerTick = 0.1f * SoilDiffusionCoefPerTick;

	public float Water { get; private set; }
	public float Steam { get; private set; }
	public float Temperature { get; private set; }

	internal static readonly Vector3i[] LateralNeighborhood = new []{ new Vector3i(-1, 0, 0), new Vector3i(1, 0, 0), new Vector3i(0, -1, 0), new Vector3i(0, 1, 0) };

	public SoilAgent(float water, float steam, float temperature)
	{
		Water = water;
		Steam = steam;
		Temperature = temperature;
	}

	public void Tick(SimulationWorld world, IFormation _formation, int formationID, uint timestep)
	{
		var formation = (SoilFormation)_formation;
		var coords = formation.Coords(formationID);        
		
		if (coords.Z == 0)
			Water += AgroWorld.GetWater(timestep) * FieldCellSurface;

		if (Water > 1e-3f)
		{
			float LateralDiffusionCoef = (coords.Z == 0 ? 0.4f : 0.03f) * SoilDiffusionCoefPerTick;

			var downDiffusion = Water * GravitationDiffusionCoefPerTick;
			var sideDiffusion = Water * LateralDiffusionCoef;

			var lateralFlow = new float[LateralNeighborhood.Length];
			var lateralSum = 0f;
			bool anyLateral = false;
			for(int i = 0; i < LateralNeighborhood.Length; ++i)
				if (formation.TryGet(coords + LateralNeighborhood[i], out var neighbor))
				{
					if (neighbor.Water < Water)
					{
						var diff = Water - neighbor.Water;
						lateralFlow[i] = diff;
						lateralSum += diff;
						anyLateral = true;
					}
				}

			if (anyLateral)
			{
				lateralSum = sideDiffusion / lateralSum;
				for(int i = 0; i < LateralNeighborhood.Length; ++i)
					if (lateralFlow[i] > 0f)
					{
						lateralFlow[i] *= lateralSum;
						formation.Send(coords + LateralNeighborhood[i], new WaterDiffusionMsg(lateralFlow[i]));
					}
				Water -= sideDiffusion;
			}

			if (formation.Send(coords.X, coords.Y, coords.Z + 1, new WaterDiffusionMsg(downDiffusion)))
				Water -= downDiffusion;
		}
		else if (Water > 0f)
		{
			if (formation.Send(coords.X, coords.Y, coords.Z + 1, new WaterDiffusionMsg(Water)))
				Water = 0f;
		}

	}
	void IncWater(float amount) => Water += amount;
	void IncSteam(float amount) => Steam += amount;

	float TryDecWater(float amount)
	{
		if (Water > amount)
		{
			Water -= amount;
			return amount;
		}
		else
		{
			var w = Water;
			Water = 0f;
			return w;
		}
	}

	///////////////////////////
	#region LOG
	///////////////////////////
	#if HISTORY_LOG
	public readonly ulong ID { get; } = Utils.UID.Next();
	#endif
	#endregion
}
