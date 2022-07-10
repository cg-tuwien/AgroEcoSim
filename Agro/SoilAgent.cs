using System;
using System.Numerics;
using System.Runtime.InteropServices;
using AgentsSystem;
using Utils;

namespace Agro;

[StructLayout(LayoutKind.Auto)]
public readonly struct WaterDiffusionMsg : IMessage<SoilAgent>
{
	public readonly float Amount;
	public WaterDiffusionMsg(float amount) => Amount = amount;
	public void Receive(ref SoilAgent agent) => agent.IncWater(Amount);
}

[StructLayout(LayoutKind.Auto)]
public readonly struct SteamDiffusionMsg : IMessage<SoilAgent>
{
	public readonly float Amount;
	public SteamDiffusionMsg(float amount) => Amount = amount;
	public void Receive(ref SoilAgent agent) => agent.IncSteam(Amount);
}

[StructLayout(LayoutKind.Auto)]
public struct SoilAgent : IAgent
{
	public const float FieldCellSurface = AgroWorld.FieldResolution * AgroWorld.FieldResolution;

	internal const float SoilDiffusionCoefPerTick = 0.1f / AgroWorld.TicksPerHour;
	internal const float GravitationDiffusionCoefPerTick = 0.1f * SoilDiffusionCoefPerTick;

	float mWater;
	float mSteam;
	float mTemperature;

	public float Water => mWater;

	public float Steam => mSteam;
	public float Temperature => mTemperature;

	internal static readonly Vector3i[] LateralNeighborhood = new []{ new Vector3i(-1, 0, 0), new Vector3i(1, 0, 0), new Vector3i(0, -1, 0), new Vector3i(0, 1, 0) };

	public SoilAgent(float water, float steam, float temperature)
	{
		mWater = water;
		mSteam = steam;
		mTemperature = temperature;
	}

	public void Tick(SimulationWorld world, IFormation _formation, int formationID, uint timestep)
	{
		var formation = (SoilFormation)_formation;
		var coords = formation.Coords(formationID);        
		
		if (coords.Z == 0)
			mWater += AgroWorld.GetWater(timestep) * FieldCellSurface;

		if (mWater > 1e-3f)
		{
			float LateralDiffusionCoef = (coords.Z == 0 ? 0.4f : 0.03f) * SoilDiffusionCoefPerTick;

			var downDiffusion = mWater * GravitationDiffusionCoefPerTick;
			var sideDiffusion = mWater * LateralDiffusionCoef;

			var lateralFlow = new float[LateralNeighborhood.Length];
			var lateralSum = 0f;
			bool anyLateral = false;
			for(int i = 0; i < LateralNeighborhood.Length; ++i)
				if (formation.TryGet(coords + LateralNeighborhood[i], out var neighbor))
				{
					if (neighbor.Water < mWater)
					{
						var diff = mWater - neighbor.Water;
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
				mWater -= sideDiffusion;
			}

			if (formation.Send(coords.X, coords.Y, coords.Z + 1, new WaterDiffusionMsg(downDiffusion)))
				mWater -= downDiffusion;
		}
		else if (mWater > 0f)
		{
			if (formation.Send(coords.X, coords.Y, coords.Z + 1, new WaterDiffusionMsg(mWater)))
				mWater = 0f;
		}

	}
	public void IncWater(float amount) => mWater += amount;
	public void IncSteam(float amount) => mSteam += amount;

	internal float TryDecWater(float amount)
	{
		if (mWater > amount)
		{
			mWater -= amount;
			return amount;
		}
		else
		{
			var w = mWater;
			mWater = 0f;
			return w;
		}
	}
}
