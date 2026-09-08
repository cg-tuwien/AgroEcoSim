using Utils;

namespace Agro;

/// <summary>Species probabilities are init-only; their conversion depends only on tick length.
/// Keep the exact Pcg arithmetic and leave every random draw at its original call site.</summary>
internal readonly struct PlantTickProbabilities
{
    internal readonly float CrownBud, RhizomeAxillarySeason, RhizomeBasal, RhizomeAxillary;
    readonly (float flower, float crown) PreFlower, Flowering, PostFlower, Dormant;

    internal PlantTickProbabilities(SpeciesSettings species, int hoursPerTick)
    {
        CrownBud = SpeciesSettings.SeasonalProbabilityPerTick(species.CrownBudActivationProbabilityPerSeason,
            species.CrownBudBreakWindowHours, hoursPerTick);
        RhizomeAxillarySeason = SpeciesSettings.SeasonalProbabilityPerTick(species.RhizomeAxillaryBudActivationProbabilityPerSeason,
            species.RhizomeBudBreakWindowHours, hoursPerTick);
        RhizomeBasal = Pcg.AccumulatedProbability(species.RhizomeBasalBudActivationProbabilityPerHour, hoursPerTick);
        RhizomeAxillary = Pcg.AccumulatedProbability(species.RhizomeAxillaryBudActivationProbabilityPerHour, hoursPerTick);
        PreFlower = (Pcg.AccumulatedProbability(species.PreFloweringCommitmentProbabilityPerHour, hoursPerTick),
            Pcg.AccumulatedProbability(species.PreFloweringCrownContinuationProbabilityPerHour, hoursPerTick));
        Flowering = (Pcg.AccumulatedProbability(species.FloweringCommitmentProbabilityPerHour, hoursPerTick),
            Pcg.AccumulatedProbability(species.FloweringCrownContinuationProbabilityPerHour, hoursPerTick));
        PostFlower = (Pcg.AccumulatedProbability(species.PostFloweringCommitmentProbabilityPerHour, hoursPerTick),
            Pcg.AccumulatedProbability(species.PostFloweringCrownContinuationProbabilityPerHour, hoursPerTick));
        Dormant = (Pcg.AccumulatedProbability(species.DormantFloweringCommitmentProbabilityPerHour, hoursPerTick),
            Pcg.AccumulatedProbability(species.DormantCrownContinuationProbabilityPerHour, hoursPerTick));
    }

    internal (float flower, float crown) ForPhase(SeasonalPhase phase) => phase switch
    {
        SeasonalPhase.PreFlower => PreFlower,
        SeasonalPhase.Flowering => Flowering,
        SeasonalPhase.PostFlower => PostFlower,
        SeasonalPhase.ResetPending => Dormant,
        _ => default,
    };
}
