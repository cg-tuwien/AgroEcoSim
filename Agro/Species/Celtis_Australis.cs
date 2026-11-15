using System.Numerics;

namespace Agro.Species;
/// <summary>European nettle tree. JRC European Atlas and NSW Flora; see TREE_SPECIES.md.</summary>
public static class Celtis_Australis
{
    public static SpeciesSettings Init()
    {
        var s = TreeSpecies.Create("Celtis australis", "European nettle tree");
        s.Height = 18f;
        s.TreeCrownRadius = 6f;
        s.TreeMatureAgeYears = 45f;
        s.TreeAnnualExtension = .8f;
        s.TreeAnnualRadiusGrowth = .006f;
        s.TreeClearTrunkHeight = 1.5f;
        s.TreeFloweringAgeYears = 8f;
        // Provisional architectural profile; independent of flowering and size attenuation.
        s.TreeApicalControlStartYears = 8f;
        s.TreeApicalControlEndYears = 18f;
        // Provisional pose from reference photographs, separate from source-backed blade dimensions.
        s.TreeLeafDeclinationDegrees = 45f;
        s.TreeLeafDeclinationVariationDegrees = 15f;
        // Trial nodal resolution guided by annual-shoot leaf-area/length data.
        // These are not direct internode measurements. Activation compensates
        // the higher site density; conditional branching stays unchanged.
        s.NodeDistance = .03f;
        s.NodeDistanceVar = .006f;
        s.TreeBranchProbability = .30f;
        s.TreeAxillaryFlushProbability = .03f / .08f;
        s.TreeSympodialRenewal = true;
        s.TreeDorsiventralShoots = true;
        s.TreeLateralUnitArchDegrees = 50f;
        s.TreeShortShootNodes = 4;
        s.TreeShortShootLength = .035f;
        s.TreeFlowersPerCluster = 1;
        s.TreeMaleFlowersPerCluster = 3;
        s.LeafLength = .075f;
        s.LeafLengthVar = .015f;
        s.LeafRadius = .0175f;
        s.LeafRadiusVar = .004f;
        s.PetioleLength = .012f;
        s.PetioleLengthVar = .003f;
        s.PetioleRadius = .001f;
        s.PetioleRadiusVar = .0002f;
        s.LeafMorphology = TreeLeafMorphologies.Celtis;
        // Downy greenish-grey shoots, grey-brown twigs, and the smooth pale grey, beech-like
        // bark the species is known for (the viewers' former #929087), reached early.
        s.StemColors = new StemColorModel(
            young: new(119, 143, 74), mature: new(118, 105, 81),
            old: new(146, 144, 135), dead: new(154, 134, 106), oldRadius: .02f);
        s.FloweringSeasonStart = .10f;
        s.FloweringSeasonEnd = .20f;
        s.FlowerSettings = TreeSpecies.Flowers(.001f, .0008f, new(150, 166, 85), pedicelLength: .025f);
        s.FruitSettings = new()
        {
            Enabled = true,
            FruitType = FruitType.Drupe,
            InitialLength = .002f,
            InitialRadius = .001f,
            MatureLength = .01f,
            MatureRadius = .005f,
            GrowthTimeHours = 24f * 110f,
            RipeningTimeHours = 24f * 25f,
            FruitRetentionDays = 45f,
            UnripeColor = new(.35f, .53f, .18f),
            RipeningColor = new(.45f, .25f, .15f),
            MatureColor = new(.12f, .08f, .13f),
            OverripeColor = new(.09f, .06f, .09f),
            FruitPerFlowerProbability = .65f,
            PollinationRequired = false,
        };
        return s;
    }
}
