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
		ISoilFormation soil;
		if (settings.FieldModelData?.Faces?.Count > 0)
			soil = new SoilFormationsList(world, settings.FieldModelData, 0.001f, settings.FieldItemRegex, world.FieldResolution);
		else
			soil = new SoilFormationRegularVoxels(world, new Vector3i(world.FieldSize / world.FieldResolution), world.FieldSize);
		world.Add(soil);
		world.Soil = soil;

		PlantFormation2[] plantsFormation;
		var rnd = world.RNG;
		if (settings?.Plants != null)
		{
			var plantsCount = settings.Plants.Length;
			plantsFormation = new PlantFormation2[plantsCount];
			for (int i = 0; i < plantsCount; ++i)
			{
				var minVegTemp = rnd.NextFloat(8f, 10f);
				var soilIndex = (int)rnd.NextUInt((uint)world.Soil.FieldsCount);

				var pos = settings.Plants[i].Position ?? world.Soil.GetRandomSeedPosition(rnd, soilIndex);
				pos.Y -= soil.GetMetricGroundDepth(pos.X, pos.Z, soilIndex);

				var seed = new SeedAgent(soilIndex, pos,
										 rnd.NextPositiveFloat(0.02f),
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
				var soilIndex = (int)rnd.NextUInt((uint)world.Soil.FieldsCount);
				var pos = new Vector3(world.FieldSize.X * rnd.NextFloat(),
									-rnd.NextPositiveFloat(0.04f),
									world.FieldSize.Y * rnd.NextFloat()); //Y because Z is depth
				var seed = new SeedAgent(soilIndex, pos,
											rnd.NextPositiveFloat(0.02f),
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
