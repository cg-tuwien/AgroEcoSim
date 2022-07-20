using System.Numerics;
using AgentsSystem;
using Utils;

namespace Agro;

public static class Initialize
{
    public static SimulationWorld World()
    {
        var world = new SimulationWorld();
        var soil = new SoilFormation(new Vector3i(AgroWorld.FieldSize / AgroWorld.FieldResolution), AgroWorld.FieldSize, 0);
        world.Add(soil);

        const int plantsCount = 100;
        var rnd = AgroWorld.RNG;
        var plantsFormation = new PlantFormation[plantsCount];
        for (int x = 0; x < plantsCount; ++x)
        {
            var minVegTemp = AgroWorld.RNG.NextFloat(8f, 10f);
            var seed = new SeedAgent(new Vector3(AgroWorld.FieldSize.X * AgroWorld.RNG.NextFloat(),
                                                    -rnd.NextFloat(0.04f),
                                                    AgroWorld.FieldSize.Y * AgroWorld.RNG.NextFloat()),
                                        AgroWorld.RNG.NextFloat(0.02f),
                                        new Vector2(minVegTemp, minVegTemp + AgroWorld.RNG.NextFloat(8f, 14f)));
            plantsFormation[x] = new PlantFormation(soil, seed, rnd);
        }
        world.AddRange(plantsFormation);

        return world;
    }
}