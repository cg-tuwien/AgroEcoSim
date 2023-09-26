using System;
using System.Diagnostics;
using System.Numerics;
using AgentsSystem;
using Utils;

namespace Agro;

public static class Initialize
{
	public static AgroWorld World(SimulationRequest? settings = null)
	{
		var world = new AgroWorld(settings);
		world.AddCallback(world.Irradiance.Tick);

		world.StreamExporterFunc = world.Irradiance.ExportToStream;
		world.RendererName = "unknown";
		//var soil = new SoilFormation(new Vector3i(AgroWorld.FieldSize / AgroWorld.FieldResolution), AgroWorld.FieldSize, 0);
		var soil = new SoilFormationNew(world, new Vector3i(world.FieldSize / world.FieldResolution), world.FieldSize, Vector3.Zero);
		world.Add(soil);

		PlantFormation2[] plantsFormation;
		var rnd = world.RNG;
		if (settings?.Plants != null)
		{
			var plantsCount = settings.Plants.Length;
			plantsFormation = new PlantFormation2[plantsCount];
			for (int i = 0; i < plantsCount; ++i)
			{
				var minVegTemp = rnd.NextFloat(8f, 10f);
				var pos = settings.Plants[i].Position
					?? new Vector3(world.FieldSize.X * rnd.NextFloat(), -rnd.NextFloat(0.04f), world.FieldSize.Y * rnd.NextFloat());
				pos.Y -= soil.GetMetricGroundDepth(pos.X, pos.Z);

				var seed = new SeedAgent(pos,
										 rnd.NextFloat(0.02f),
										 new Vector2(minVegTemp, minVegTemp + rnd.NextFloat(8f, 14f)));
				var species = string.IsNullOrEmpty(settings.Plants[i].SpeciesName) ? null : settings.Species?.FirstOrDefault(x => x.Name == settings.Plants[i].SpeciesName);
				plantsFormation[i] = new PlantFormation2(world, species ?? SpeciesSettings.Avocado, soil, seed, rnd, world.HoursPerTick);
			}
		}
		else
		{
			const int plantsCount = 1;
			plantsFormation = new PlantFormation2[plantsCount];
			for (int i = 0; i < plantsCount; ++i)
			{
				var minVegTemp = rnd.NextFloat(8f, 10f);
				var seed = new SeedAgent(new Vector3(world.FieldSize.X * rnd.NextFloat(),
														-rnd.NextFloat(0.04f),
														world.FieldSize.Y * rnd.NextFloat()), //Y because Z is depth
											rnd.NextFloat(0.02f),
											new Vector2(minVegTemp, minVegTemp + rnd.NextFloat(8f, 14f)));
				var species = string.IsNullOrEmpty(settings.Plants[i].SpeciesName) ? null : settings.Species?.FirstOrDefault(x => x.Name == settings.Plants[i].SpeciesName);
				plantsFormation[i] = new PlantFormation2(world, species ?? SpeciesSettings.Avocado, soil, seed, rnd, world.HoursPerTick);
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
