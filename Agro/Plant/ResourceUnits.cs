namespace Agro;

/// <summary>
/// Experimental carbohydrate accounting used when construction costs are enabled.
/// Energy is grams of glucose equivalent. Irradiance is Wh/m² over the current tick;
/// the photosynthetic yield is an empirical shortwave conversion, not a measured
/// Bergenia quantum efficiency. Water here is only the stoichiometric reactant,
/// not transpiration or the plant's separate hydraulic demand.
/// </summary>
internal static class ResourceUnits
{
    // 0.005 g/Wh = 1.3889 g/MJ of incident shortwave radiation. This retains
    // the previous light response while giving its output a declared unit.
    internal const float GlucosePerWh = AboveGroundAgent.mPhotoEfficiency;

    // 6 H2O (108 g) are consumed to form one glucose (180 g) in the net reaction.
    internal const float WaterPerGlucose = 0.6f;

    // Provisional reference at 25 C: 15 mg glucose / g dry matter / day. The
    // literature reports a broad range and does not measure this cultivar.
    internal const float MaintenancePerDryGramPerHour = 0.015f / 24f;

    internal static bool Enabled(SpeciesSettings species) => species.ConstructionEnergyPerDryGram > 0f;

    internal static float PhotosynthesizedGlucose(float irradianceWhPerSquareMetre,
        float areaSquareMetres, float availableWaterGrams, bool glucoseAccounting)
    {
        var byLight = MathF.Max(0f, irradianceWhPerSquareMetre * areaSquareMetres) * GlucosePerWh;
        var waterPerGlucose = glucoseAccounting ? WaterPerGlucose : 1f;
        return MathF.Min(byLight, MathF.Max(0f, availableWaterGrams) / waterPerGlucose);
    }

    internal static float WaterDebit(float glucoseGrams, bool glucoseAccounting)
        => glucoseGrams * (glucoseAccounting ? WaterPerGlucose : 1f);

    internal static float MaintenancePerHour(float volumeCubicMetres, float woodRatio, SpeciesSettings species)
        => volumeCubicMetres * ConstructionBudget.Density(species, woodRatio) * MaintenancePerDryGramPerHour;

    /// <summary>
    /// Allocation weight for tissue that must be built before it can gather light.
    /// This is a nominal one-hour construction demand, not newly created energy or
    /// a growth-rate multiplier. Actual growth still pays ConstructionBudget.Fund.
    /// </summary>
    internal static float ConstructionSinkPerHour(in AboveGroundAgent organ, SpeciesSettings species)
    {
        if (!Enabled(species)) return 0f;

        float targetLength, targetRadius, nominalHours;
        switch (organ.Organ)
        {
            case OrganTypes.Meristem when species.Behavior == Behavior.Herbaceous && !organ.IsRizome:
                targetLength = MathF.Max(organ.Length, organ.GetLengthVar());
                targetRadius = MathF.Max(organ.Radius, species.ShootMaximumRadius);
                // Match the nominal 1 mm/h axial and 0.02 mm/h radial rates in
                // Herbaceous.Tick, including its species growth-rate scale.
                var rate = species.HerbaceousGrowthRateScale;
                if (rate <= 0f) return 0f;
                nominalHours = MathF.Max(targetLength / (0.001f * rate),
                    targetRadius / (0.00002f * rate));
                break;
            case OrganTypes.Leaf:
                targetLength = MathF.Max(organ.Length,
                    (species.LeafLength + organ.GetLengthVar()) / species.LeafSegmentCount);
                targetRadius = MathF.Max(organ.Radius, species.LeafRadius + organ.GetRadiusVar());
                nominalHours = species.LeafGrowthTime;
                break;
            case OrganTypes.Petiole:
                targetLength = MathF.Max(organ.Length,
                    (species.PetioleLength + organ.GetLengthVar()) / species.PetioleSegmentCount);
                targetRadius = MathF.Max(organ.Radius, species.PetioleRadius + organ.GetRadiusVar());
                nominalHours = species.LeafGrowthTime;
                break;
            default:
                return 0f;
        }

        var blade = organ.Organ == OrganTypes.Leaf;
        var targetVolume = ConstructionBudget.Volume(new(targetLength, targetRadius), blade);
        var remainingVolume = Math.Max(0d, targetVolume - organ.Volume());
        return CostPerHour(remainingVolume, nominalHours, organ.WoodRatio(), species);
    }

    internal static float RootConstructionSinkPerHour(float length, float radius, float targetLength,
        float woodRatio, SpeciesSettings species)
    {
        if (!Enabled(species) || length >= targetLength) return 0f;
        // Root tips have no photosynthesis. Route carbon against the remaining
        // longitudinal tissue at their current radius; no guessed adult diameter.
        // The existing root axial coefficient is 0.2 mm/h before its volume
        // and environment factors, so this is a conservative nominal duration.
        var remainingVolume = 4d * Math.Max(0d, targetLength - length) * radius * radius;
        return CostPerHour(remainingVolume, targetLength / 0.0002f, woodRatio, species);
    }

    static float CostPerHour(double remainingVolume, float nominalHours, float woodRatio, SpeciesSettings species)
    {
        if (!float.IsFinite(nominalHours) || nominalHours <= 0f) return 0f;
        var cost = ConstructionBudget.Cost(remainingVolume, species, woodRatio);
        return float.IsFinite(cost) ? cost / MathF.Max(1f, nominalHours) : 0f;
    }

}
