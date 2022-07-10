using System;
using System.Numerics;
using AgentsSystem;
using Utils;

namespace Agro;

public partial class SoilFormation : Formation3iTransformed<SoilAgent>
{
	public SoilFormation(Vector3i size, Vector3 fieldSize, uint timestep) : base(size.X, size.Y, size.Z)
	{
		const float coldFactor = 0.75f; //earth gets 1 degree colder each x meters (where x is the value of this constant)
		var airTemp = AgroWorld.GetTemperature(timestep);
		var bottomTemp = airTemp > 4f ? Math.Max(4f, airTemp - fieldSize.Z * coldFactor) : Math.Min(4f, airTemp + fieldSize.Z * coldFactor);
		for(var z = 0; z < size.Z; ++z)
		{
			var temp = airTemp + (bottomTemp - airTemp) * z / (size.Z - 1);
			for(var x = 0; x < size.X; ++x)
				for(var y = 0; y < size.Y; ++y)                
					Agents[Index(x, y, z)] = new SoilAgent(0f, 0f, temp);
		}
		
		SetScale(fieldSize);
	}

	public override void DeliverPost()
	{
		base.DeliverPost();

		//Console.WriteLine(Agents.Where((x, i) => Coords(i).Z == 2 && Coords(i).X == 1).Select(x => x.Water).Sum());
	}

	public float GetWater(int index) => ReadTMP ? AgentsTMP[index].Water : Agents[index].Water;

	public float GetTemperature(int index) => ReadTMP ? AgentsTMP[index].Temperature : Agents[index].Temperature;

	public override List<int> IntersectSphere(Vector3 center, float radius) => base.IntersectSphere(new Vector3(center.X, center.Z, -center.Y), radius);
}

