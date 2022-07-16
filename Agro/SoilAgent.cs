using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using AgentsSystem;
using Utils;

namespace Agro;

[StructLayout(LayoutKind.Auto)]
public partial struct SoilAgent : IAgent
{
	public const float FieldCellSurface = AgroWorld.FieldResolution * AgroWorld.FieldResolution;

	internal const float SoilDiffusionCoefPerTick = 0.1f / AgroWorld.TicksPerHour;
	internal const float GravitationDiffusionCoefPerTick = 0.1f * SoilDiffusionCoefPerTick;

	public float Water { get; private set; }
	public float Steam { get; private set; }
	public float Temperature { get; private set; }

	public const float WaterCapacityRatio = 0.5f;

	[JsonIgnore]
	public float WaterCapacity => FieldCellSurface * AgroWorld.FieldResolution * WaterCapacityRatio;

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
						formation.Send(formationID, new Water_PullFrom(formation, lateralFlow[i], coords + LateralNeighborhood[i]));
					}
				//Water -= sideDiffusion;
			}

			formation.Send(formationID, new Water_PullFrom(formation, downDiffusion, coords.X, coords.Y, coords.Z + 1));
				//Water -= downDiffusion;
		}
		else if (Water > 0f)
		{
			formation.Send(formationID, new Water_PullFrom(formation, Water, coords.X, coords.Y, coords.Z + 1));
				//Water = 0f;
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
