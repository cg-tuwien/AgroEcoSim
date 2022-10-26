using System;
using System.Numerics;
using System.Collections.Generic;
using AgentsSystem;
using Utils;

namespace Agro;

public partial class SoilFormation : Formation3iTransformed<SoilAgent>
{
	readonly int TrueSizeZ;

	public override byte Stages => 1;
	public SoilFormation(Vector3i size, Vector3 fieldSize, uint timestep) : base(size.X, size.Y, size.Z + 1)
	{
		TrueSizeZ = size.Z;
		const float coldFactor = 0.75f; //earth gets 1 degree colder each x meters (where x is the value of this constant)
		var airTemp = AgroWorld.GetTemperature(timestep);
		var bottomTemp = airTemp > 4f ? Math.Max(4f, airTemp - fieldSize.Z * coldFactor) : Math.Min(4f, airTemp + fieldSize.Z * coldFactor);
		for(var z = 0; z <= size.Z; ++z)
		{
			var temp = airTemp + (bottomTemp - airTemp) * z / TrueSizeZ;
			for(var x = 0; x < size.X; ++x)
				for(var y = 0; y < size.Y; ++y)
					Agents[Index(x, y, z)] = new SoilAgent(0f, 0f, temp);
		}

		SetScale(fieldSize);
	}

	// public override void DeliverPost(uint timestep, byte stage)
	// {
	// 	base.DeliverPost(timestep, stage);
	// 	Console.WriteLine(Agents.Where((x, i) => Coords(i).Z == 2 && Coords(i).X == 1).Select(x => x.Water).Sum());
	// }

	public float GetWater(int index) => index >= 0 && index < Agents.Length
		? (ReadTMP ? AgentsTMP[index].Water : Agents[index].Water)
		: 0f;

	public float GetWater(Vector3i index) => GetWater(Index(index));

	public float GetWaterCapacity(int index) => index >= 0 && index < Agents.Length
		? (ReadTMP ? AgentsTMP[index].WaterMaxCapacity : Agents[index].WaterMaxCapacity)
		: 0f;

	public float GetWaterCapacity(Vector3i index) => GetWaterCapacity(Index(index));

	public float GetTemperature(int index) => ReadTMP ? AgentsTMP[index].Temperature : Agents[index].Temperature;

	public int SoilIndex(Vector3i coords) => coords.X + coords.Y * SizeX + (coords.Z + 1) * SizeXY;

	public override List<int> IntersectSphere(Vector3 center, float radius)
	{
		center = new Vector3(center.X, center.Z, -center.Y);
		if (Flags.HasFlag(TransformFlags.Translated))
			center -= Translation;

		if (Flags.HasFlag(TransformFlags.Scaled))
			center *= new Vector3(SizeX, SizeY, TrueSizeZ) / Scale;

		var iCenter = new Vector3i(center);

		if (iCenter.X >= 0 && iCenter.Y >= 0 && iCenter.Z >= 0 && iCenter.X < SizeX && iCenter.Y < SizeY && iCenter.Z < TrueSizeZ)
			return new List<int>{SoilIndex(iCenter)};
		else
			return new();
	}
}

