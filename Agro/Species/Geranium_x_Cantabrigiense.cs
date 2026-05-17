using System.Numerics;

namespace Agro.Species;

// Sources
// - RHS, "Geranium × cantabrigiense 'Biokovo'" (to 20 cm tall, flowers 2.5 cm wide)
// - Missouri Botanical Garden Plant Finder
//   (hybrid of G. macrorrhizum × G. dalmaticum; 15-25 cm tall; 7-lobed leaves 9 cm wide;
//    5-petaled white/pale-pink flowers ~2 cm with pink stamens)
// - Gardenia.net "Biokovo" profile (15-30 cm tall; ~2.5 cm flowers, deep pink stamens)
public static class Geranium_x_Cantabrigiense
{
    const float DegToRad = MathF.PI / 180f;

    public static SpeciesSettings Init() => new()
    {
        Name = "Geranium × cantabrigiense",
        Aka = "Cambridge Cranesbill",
        Behavior = Behavior.Herbaceous,

        Height = 0.25f,

        LeafLength = 0.06f,
        LeafLengthVar = 0.012f,
        LeafRadius = 0.035f,       
        LeafRadiusVar = 0.006f,
        LeafPitch = 22f * DegToRad,
        LeafPitchVar = 7f * DegToRad,
        LeafColor = "4c7641",    

        LeafGrowthTime = 24f * 7f * 2f,
        LeafGrowthTimeVar = 24f * 3f,
        MaxLeaveAge = 24f * 180f,   

        PetioleLength = 0.06f,
        PetioleLengthVar = 0.012f,
        PetioleRadius = 0.0015f,
        PetioleRadiusVar = 0.0002f,

        pNewCrown = 0.50f,           
        crownPitch = 0.16f,
        growthFactor = 0.40f,
        MaxRadius = 0.003f,

        LateralsPerNode = 2,
        NodeDistance = 0f,          
        NodeDistanceVar = 0.0025f,
        DominanceFactor = 0.7f,

        RizomeMaxDepth = 4,
        RizomeLength = 0.035f,
        RizomeRadius = 0.0022f,
        pExpandRizome = 0.0009f,          
        SympodialStartAgeHours = 24f * 50f,
        FloweringStartAgeHours = 24f * 55f,
        FloweringEndAgeHours = 24f * 110f,
        pChaningSeaonns = [0.018f, 0.002f, 0.009f, 0.0f],
        pFloweringSeaonns = [0.055f, 0.004f, 0.002f, 0.0f],  
        WoodElasticModulus = 1.2e8f,
        GreenElasticModulus = 5e6f,         
        DepthFactor = 0.01f,

        
        FlowerSettings = new FlowerSettings
        {
            PedalLength = 0.011f,
            PedalRadius = 0.005f,
            PedalLengthVar = 0.002f,
            pedalGrowthTime = 50f,
            pedalGrowthTimeVar = 5f,
            pedalColor = new Vector3(235, 180, 195), 

            BudLength = 0.005f,
            BudRadius = 0.005f,
            BudBloomAge = 240u,   
            FlowerMaxAge = 720u,   

            PetiolLength = 0.015f,
            PetiolRadius = 0.0005f,
            PetiolLengthVar = 0.005f,
            PetiolRadiusVar = 0.0002f,
            petiolSegments = 1,

            stemLength = 0.12f,
            stemLengthVar = 0.03f,
            fStemRadius = 0.001f,
            fStemRadiusVar = 0.0002f,
            bStemLength = 0.04f,
            bStemRadius = 0.0008f,
            bStemLengthVar = 0.01f,
            bStemRadiusVar = 0.0001f,

            flowerDebth = 1,
            flowerBaseDebth = 2,
            LateralsPerNode = 2,
            FlowersPerInternode = 2,
            clusterSize = 3,     
            clusterAngle = 0.15f,
            deterministic = true,
            continous = false,
            internodeFlower = true,
            internodeFlowerWithStem = true,
            floralDepthFactor = 1,
            pFlowerDebth = 1.0f,

            LateralAngle = 55,
            LateralRoll = 10,
            BaseLaterals = 1,
            BaseLateralAngle = 60,
            BaseLateralRoll = 5,
            pFlowerBaseDebth = 0.8f,

            LeafLength = 0.020f,
            LeafRadius = 0.010f,
            LeafLengthVar = 0.004f,
            LeafRadiusVar = 0.002f,
            LeavePetioleLength = 0.005f,
            LeavePetioleRadius = 0.0004f,

            growthTime = 120f,

        },

        BaseLeafColor = new Vector3(0.298f, 0.463f, 0.255f), 
        OldLeafColor = new Vector3(0.627f, 0.251f, 0.188f),  
    };
}