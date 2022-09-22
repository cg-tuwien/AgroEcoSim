using System.Numerics;
using AgentsSystem;
using Utils;

namespace Agro;

public static class Initialize
{
	public static SimulationWorld World(SimulationRequest? settings = null)
	{
		if (settings?.TicksPerHour.HasValue ?? false)
			AgroWorld.TicksPerHour = settings.TicksPerHour.Value;

		if (settings?.TotalHours.HasValue ?? false)
			AgroWorld.TotalHours = settings.TotalHours.Value;

		if (settings?.FieldResolution.HasValue ?? false)
			AgroWorld.FieldResolution = settings.FieldResolution.Value;

		if (settings?.FieldSize.HasValue ?? false)
			AgroWorld.FieldSize = settings.FieldSize.Value;

		if (settings?.Seed.HasValue ?? false)
			AgroWorld.InitRNG(settings.Seed.Value);

		var world = new SimulationWorld();
		world.AddCallback((timestep, formations) => IrradianceClient.Tick(timestep, formations));
		var soil = new SoilFormation(new Vector3i(AgroWorld.FieldSize / AgroWorld.FieldResolution), AgroWorld.FieldSize, 0);
		world.Add(soil);

		PlantFormation[] plantsFormation;
		var rnd = AgroWorld.RNG;
		if (settings?.Plants != null)
		{
			var plantsCount = settings.Plants.Length;
			plantsFormation = new PlantFormation[plantsCount];
			for (int i = 0; i < plantsCount; ++i)
			{
				var minVegTemp = rnd.NextFloat(8f, 10f);
				var pos = settings.Plants[i].Position
					?? new Vector3(AgroWorld.FieldSize.X * rnd.NextFloat(), -rnd.NextFloat(0.04f), AgroWorld.FieldSize.Z * rnd.NextFloat());

				var seed = new SeedAgent(pos,
										 rnd.NextFloat(0.02f),
										 new Vector2(minVegTemp, minVegTemp + rnd.NextFloat(8f, 14f)));
				plantsFormation[i] = new PlantFormation(soil, seed, rnd);
			}
		}
		else
		{
			const int plantsCount = 1;
			plantsFormation = new PlantFormation[plantsCount];
			for (int i = 0; i < plantsCount; ++i)
			{
				var minVegTemp = rnd.NextFloat(8f, 10f);
				var seed = new SeedAgent(new Vector3(AgroWorld.FieldSize.X * rnd.NextFloat(),
														-rnd.NextFloat(0.04f),
														AgroWorld.FieldSize.Z * rnd.NextFloat()),
											rnd.NextFloat(0.02f),
											new Vector2(minVegTemp, minVegTemp + rnd.NextFloat(8f, 14f)));
				plantsFormation[i] = new PlantFormation(soil, seed, rnd);
			}
		}
		world.AddRange(plantsFormation);

		return world;
	}
}
