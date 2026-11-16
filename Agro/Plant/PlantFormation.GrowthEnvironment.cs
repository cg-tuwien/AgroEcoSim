namespace Agro;

public partial class PlantFormation2
{
    // Derived from immutable request settings; neither cache is checkpoint state. Each plant
    // owns its cache, so parallel ticks do not synchronize or repeat date/temperature queries
    // for every organ. A restored world computes it on its first continued tick.
    uint VegetativeTemperatureTimestep = uint.MaxValue;
    float CachedVegetativeTemperatureFactor = 1f;

    internal float VegetativeTemperatureFactor(uint timestep)
    {
        if (!Parameters.VegetativeTemperatureResponseEnabled) return 1f;
        if (VegetativeTemperatureTimestep != timestep)
        {
            CachedVegetativeTemperatureFactor = World.GetVegetativeTemperatureFactor(timestep,
                Parameters.VegetativeGrowthBaseTemperatureC, Parameters.VegetativeGrowthFullTemperatureC);
            VegetativeTemperatureTimestep = timestep;
        }
        return CachedVegetativeTemperatureFactor;
    }

    internal float VegetativeEnvironmentFactor(uint timestep, float previousDayExposure)
    {
        var thermal = VegetativeTemperatureFactor(timestep);
        var reference = Parameters.VegetativeGrowthLightReferenceWhM2;
        if (reference <= 0f) return thermal;
        // An organ that carries no lit leaf of its own has nothing to sense light with: a dormant
        // crown bud on a leafless stretch of rhizome, a node bud whose leaf fell, a crown that
        // lost its foliage over winter. Its exposure is zero, and a zero here is not "dark" but
        // "unknown". Read as dark it made every such hazard zero: no crown rose from the rhizome
        // and no shed leaf was replaced, and a two-year Geranium macrorrhizum held 6 leaves on one
        // crown beside 125 dormant buds. Such an organ takes the plant's best-lit leaf, as a
        // woody shoot does, and a plant with no lit leaf at all develops on its reserves - the
        // thermal response alone - like a propagule that has not surfaced.
        // NewDay leaves the plant's maximum at one when no leaf has completed a day.
        var best = AG.DailyResourceMax;
        if (best <= 1f) return thermal;
        if (previousDayExposure <= 0f) previousDayExposure = best;
        return thermal * Math.Clamp(previousDayExposure / reference, 0f, 1f);
    }

    internal float VegetativeGrowthFactor(uint timestep, float previousDayExposure,
        float previousDayProduction, float dailyProductionMax)
    {
        if (Parameters.VegetativeGrowthLightReferenceWhM2 > 0f)
            return VegetativeEnvironmentFactor(timestep, previousDayExposure);
        // Keep the exact legacy ratio for unconfigured species. Water and reserve checks
        // remain at the growth call sites; absolute exposure is independent of hydration.
        var relativeProduction = dailyProductionMax > 1e-6f
            ? previousDayProduction / dailyProductionMax : 0f;
        return VegetativeTemperatureFactor(timestep) * relativeProduction;
    }

    internal float VegetativeOpportunityProbability(float probabilityPerTick, uint timestep,
        float previousDayExposure)
    {
        var factor = VegetativeEnvironmentFactor(timestep, previousDayExposure);
        if (factor <= 0f || probabilityPerTick <= 0f) return 0f;
        if (factor >= 1f || probabilityPerTick >= 1f) return probabilityPerTick;
        // Scale developmental time (the event hazard), not the already accumulated tick
        // probability. This preserves the 1-hour/4-hour conversion and legacy RNG draws.
        return 1f - MathF.Pow(1f - probabilityPerTick, factor);
    }
}
