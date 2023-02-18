using System.Numerics;
using AgentsSystem;
using Utils;

namespace Agro;

public static class Initialize
{
	public static SimulationWorld World(SimulationRequest? settings = null)
	{
		if (settings?.HoursPerTick.HasValue ?? false)
			AgroWorld.HoursPerTick = settings.HoursPerTick.Value;

		if (settings?.TotalHours.HasValue ?? false)
			AgroWorld.TotalHours = settings.TotalHours.Value;

		if (settings?.FieldResolution.HasValue ?? false)
			AgroWorld.FieldResolution = settings.FieldResolution.Value;

		if (settings?.FieldSize.HasValue ?? false)
			AgroWorld.FieldSize = settings.FieldSize.Value;

		if (settings?.Seed.HasValue ?? false)
			AgroWorld.InitRNG(settings.Seed.Value);

		AgroWorld.Init();
		var world = new SimulationWorld();

		world.AddCallback(IrradianceClient.Tick);
		//var soil = new SoilFormation(new Vector3i(AgroWorld.FieldSize / AgroWorld.FieldResolution), AgroWorld.FieldSize, 0);
		var soil = new SoilFormationNew(new Vector3i(AgroWorld.FieldSize / AgroWorld.FieldResolution), AgroWorld.FieldSize, Vector3.Zero);
		world.Add(soil);

		PlantFormation2[] plantsFormation;
		var rnd = AgroWorld.RNG;
		if (settings?.Plants != null)
		{
			var plantsCount = settings.Plants.Length;
			plantsFormation = new PlantFormation2[plantsCount];
			for (int i = 0; i < plantsCount; ++i)
			{
				var minVegTemp = rnd.NextFloat(8f, 10f);
				var pos = settings.Plants[i].Position
					?? new Vector3(AgroWorld.FieldSize.X * rnd.NextFloat(), -rnd.NextFloat(0.04f), AgroWorld.FieldSize.Y * rnd.NextFloat());
				pos.Y += soil.GetHeight(pos.X, pos.Z);

				var seed = new SeedAgent(pos,
										 rnd.NextFloat(0.02f),
										 new Vector2(minVegTemp, minVegTemp + rnd.NextFloat(8f, 14f)));
				plantsFormation[i] = new PlantFormation2(PlantSettings.Avocado, soil, seed, rnd);
			}
		}
		else
		{
			const int plantsCount = 1;
			plantsFormation = new PlantFormation2[plantsCount];
			for (int i = 0; i < plantsCount; ++i)
			{
				var minVegTemp = rnd.NextFloat(8f, 10f);
				var seed = new SeedAgent(new Vector3(AgroWorld.FieldSize.X * rnd.NextFloat(),
														-rnd.NextFloat(0.04f),
														AgroWorld.FieldSize.Y * rnd.NextFloat()), //Y because Z is depth
											rnd.NextFloat(0.02f),
											new Vector2(minVegTemp, minVegTemp + rnd.NextFloat(8f, 14f)));
				plantsFormation[i] = new PlantFormation2(PlantSettings.Avocado, soil, seed, rnd);
			}
		}
		world.AddRange(plantsFormation);

		if (settings?.Obstacles != null)
			foreach(var obstacle in settings.Obstacles)
			switch(obstacle.Type.ToLower())
			{
				case "wall":
					world.Add(new Wall(obstacle.Length ?? (obstacle.Radius.HasValue ? obstacle.Radius.Value * 2f : 1f), obstacle.Height ?? 1f, obstacle.Thickness ?? 0.1f, obstacle.Position ?? Vector3.Zero, obstacle.Orientation ?? 0f));
					break;
				case "umbrella":
					world.Add(new Umbrella(obstacle.Radius ?? (obstacle.Length.HasValue ? obstacle.Length.Value * 0.5f : 1f), obstacle.Height ?? 1f, obstacle.Thickness ?? 0.1f, obstacle.Position ?? Vector3.Zero));
				break;
				default: throw new ArgumentException($"There is no such obstacle type as {obstacle.Type}.");
			}

		return world;
	}
}
