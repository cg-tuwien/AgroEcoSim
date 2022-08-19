using System.Numerics;
using AgentsSystem;
using Utils;

namespace Agro;

public static class Initialize
{
    public static SimulationWorld World(FieldImportData? settings = null)
    {
        if (settings?.TicksPerHour.HasValue ?? false)
            AgroWorld.TicksPerHour = settings.TicksPerHour.Value;

        if (settings?.TotalHours.HasValue ?? false)
            AgroWorld.TotalHours = settings.TotalHours.Value;

        if (settings?.FieldResolution.HasValue ?? false)
            AgroWorld.FieldResolution = settings.FieldResolution.Value;

        if (settings?.FieldSize.HasValue ?? false)
            AgroWorld.FieldSize = settings.FieldSize.Value;

        var world = new SimulationWorld();
        world.AddCallback((timestep, formations) => AgroWorld.IrradianceCallback(timestep, formations));
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
                var minVegTemp = AgroWorld.RNG.NextFloat(8f, 10f);
                var pos = settings.Plants[i].P
                    ?? new Vector3(AgroWorld.FieldSize.X * AgroWorld.RNG.NextFloat(), -rnd.NextFloat(0.04f), AgroWorld.FieldSize.Z * AgroWorld.RNG.NextFloat());

                var seed = new SeedAgent(pos,
                                         AgroWorld.RNG.NextFloat(0.02f),
                                         new Vector2(minVegTemp, minVegTemp + AgroWorld.RNG.NextFloat(8f, 14f)));
                plantsFormation[i] = new PlantFormation(soil, seed, rnd);
            }
        }
        else
        {
            const int plantsCount = 100;
            plantsFormation = new PlantFormation[plantsCount];
            for (int i = 0; i < plantsCount; ++i)
            {
                var minVegTemp = AgroWorld.RNG.NextFloat(8f, 10f);
                var seed = new SeedAgent(new Vector3(AgroWorld.FieldSize.X * AgroWorld.RNG.NextFloat(),
                                                        -rnd.NextFloat(0.04f),
                                                        AgroWorld.FieldSize.Z * AgroWorld.RNG.NextFloat()),
                                            AgroWorld.RNG.NextFloat(0.02f),
                                            new Vector2(minVegTemp, minVegTemp + AgroWorld.RNG.NextFloat(8f, 14f)));
                plantsFormation[i] = new PlantFormation(soil, seed, rnd);
            }
        }
        world.AddRange(plantsFormation);

        return world;
    }
}