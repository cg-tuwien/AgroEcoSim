using System.Numerics;

namespace Agro.Species;
/// <summary>Grafted globe Norway maple; the rootstock is trained before mature scion development.</summary>
public static class Acer_Platanoides_Globosum
{
    public static SpeciesSettings Init()
    {
        var s = TreeSpecies.Create("Acer platanoides 'Globosum'", "Globe Norway maple");
        s.Height = 6f;
        s.TreeCrownRadius = 3f;
        s.TreeCrownHabit = TreeCrownHabit.Globe;
        s.TreeGraftHeight = 2.2f;
        s.TreeClearTrunkHeight = 2.2f;
        s.TreeAnnualExtension = .48f;
        s.TreeAnnualRadiusGrowth = .004f;
        s.TreeMatureAgeYears = 25f;
        s.TreeFloweringAgeYears = 0f;
        s.TreeFlowersPerCluster = 9;
        s.TreeBranchProbability = .27f;
        s.LateralsPerNode = 2;
        s.LateralRoll = MathF.PI * .5f;
        s.NodeDistance = .065f;
        s.NodeDistanceVar = .01f;
        s.LateralPitch = 1.05f;
        s.TreeBudBreakSeason = .12f;
        s.LeafLength = .12f;
        s.LeafLengthVar = .025f;
        s.LeafRadius = .07f;
        s.LeafRadiusVar = .012f;
        s.PetioleLength = .09f;
        s.PetioleLengthVar = .02f;
        s.PetioleRadius = .0016f;
        s.PetioleRadiusVar = .00025f;
        s.LeafMorphology = TreeLeafMorphologies.Acer;
        s.FloweringSeasonStart = .075f;
        s.FloweringSeasonEnd = .12f;
        s.FlowerSettings = TreeSpecies.Flowers(.003f, .0015f, new(195, 207, 65));
        s.FruitSettings = new()
        {
            Enabled = true,
            FruitType = FruitType.Samara,
            InitialLength = .002f,
            InitialRadius = .001f,
            MatureLength = .008f,
            MatureRadius = .004f,
            GrowthTimeHours = 24f * 125f,
            RipeningTimeHours = 24f * 28f,
            FruitRetentionDays = 50f,
            UnripeColor = new(.48f, .64f, .24f),
            RipeningColor = new(.65f, .52f, .25f),
            MatureColor = new(.55f, .38f, .19f),
            OverripeColor = new(.42f, .29f, .15f),
            FruitPerFlowerProbability = .5f,
            PollinationRequired = false,
            FruitsPerFlower = 2,
            WingsPerFruit = 1,
            WingLength = .035f,
            WingRadius = .006f,
            WingMorphology = TreeLeafMorphologies.SamaraWing,
        };
        return s;
    }
}
