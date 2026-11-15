using System.Numerics;

namespace Agro.Species;
/// <summary>Narrow upright serviceberry cultivar; clonal phenotype, not seed genetics.</summary>
public static class Amelanchier_Alnifolia_Obelisk
{
    public static SpeciesSettings Init()
    {
        var s = TreeSpecies.Create("Amelanchier alnifolia 'Obelisk'", "Obelisk serviceberry");
        s.Height = 5f;
        s.TreeCrownRadius = .85f;
        s.TreeCrownHabit = TreeCrownHabit.Columnar;
        s.TreeAnnualExtension = .4f;
        s.TreeAnnualRadiusGrowth = .003f;
        s.TreeMatureAgeYears = 18f;
        s.TreeStemCount = 3;
        s.TreeFloweringAgeYears = 2f;
        s.TreeFlowersPerCluster = 7;
        s.TreeBranchProbability = .20f;
        s.TreeShortShootBranchProbability = .20f;
        s.TreeShortShootNodes = 3;
        s.NodeDistance = .055f;
        s.NodeDistanceVar = .01f;
        s.LateralPitch = .40f;
        s.TreeBudBreakSeason = .075f;
        s.LeafLength = .06f;
        s.LeafLengthVar = .01f;
        s.LeafRadius = .022f;
        s.LeafRadiusVar = .003f;
        s.PetioleLength = .013f;
        s.PetioleLengthVar = .004f;
        s.PetioleRadius = .0009f;
        s.PetioleRadiusVar = .00015f;
        s.LeafMorphology = TreeLeafMorphologies.Amelanchier;
        // Bronze young shoots, red-brown twigs, smooth slate-grey bark on older stems.
        s.StemColors = new StemColorModel(
            young: new(139, 120, 55), mature: new(132, 63, 36),
            old: new(125, 122, 118), dead: new(154, 134, 106), oldRadius: .015f);
        // Bronze young leaves, mid green in summer, orange-red in autumn (CIELAB).
        s.LeafPhenology = TreeSpecies.Phenology(new(54, 8, 36), new(40, -26, 34), new(56, 33, 62));
        s.FloweringSeasonStart = .09f;
        s.FloweringSeasonEnd = .17f;
        s.FlowerSettings = TreeSpecies.Flowers(.009f, .0035f, new(248, 247, 233));
        s.FruitSettings = new()
        {
            Enabled = true,
            FruitType = FruitType.Pome,
            InitialLength = .002f,
            InitialRadius = .001f,
            MatureLength = .012f,
            MatureRadius = .006f,
            GrowthTimeHours = 24f * 55f,
            RipeningTimeHours = 24f * 14f,
            FruitRetentionDays = 25f,
            UnripeColor = new(.4f, .55f, .22f),
            RipeningColor = new(.68f, .18f, .23f),
            MatureColor = new(.13f, .08f, .20f),
            OverripeColor = new(.09f, .05f, .13f),
            FruitPerFlowerProbability = .65f,
            PollinationRequired = false,
        };
        return s;
    }
}
