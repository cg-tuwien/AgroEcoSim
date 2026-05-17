using System.Numerics;

namespace Agro.Species;

public static class Geranium_Macrorrhizum
{
    public static SpeciesSettings Init() =>new() {
        Name = "Geranium Macrorrhizum",
        Behavior = Behavior.Herbaceous,
        LeafLength = 0.04f,
        LeafLengthVar = 0.0f,
        LeafRadius = 0.02f,
        LeafPitchVar = 5f * (MathF.PI / 180f),
        LeafPitch = 85 * (MathF.PI / 180f),
        PetioleLength = 0.060f,
        PetioleLengthVar = 0.01f,
        PetioleRadius = 0.003f,
        PetioleRadiusVar = 0.0001f,

        LeafGrowthTime = 24 * 7,
        Height = 0.3f,

        NodeDistance = 0f,
        NodeDistanceVar = 0f,

        LateralsPerNode = 2,
        LateralPitch = 15 * (MathF.PI / 180f),
        LateralPitchVar = 10 * (MathF.PI / 180f),
        LateralRoll = 40f * (MathF.PI / 180f),
        LateralRollVar = 5f * (MathF.PI / 180f),

        MaxLeaveAge = 1.5f * 365f,
        pNewCrown = 0.70f,
        crownPitch = 0.38f,
        growthFactor = 0.25f,

        MaxRadius = 0.0035f,           
        pExpandRizome = 0.012f,       
        RizomeMaxDepth = 3,
        RizomeLength = 0.045f,
        RizomeRadius = 0.0035f,
        pFloweringSeaonns = [0.055f, 0.004f, 0.002f, 0.0f],
        FlowerSettings = new FlowerSettings
        {
                PedalLength = 0.016f,
                PedalRadius = 0.007f,
                PedalLengthVar = 0.002f,
                pedalGrowthTime = 180f,      
                pedalGrowthTimeVar = 36f,
                pedalColor = new Vector3(210, 100, 160), 

                BudLength = 0.010f,
                BudRadius = 0.005f,
                BudBloomAge = 216u,      
                FlowerMaxAge = 840u,      

                PetiolLength = 0.012f,
                PetiolRadius = 0.0006f,
                PetiolLengthVar = 0.006f,
                PetiolRadiusVar = 0.0002f,
                petiolSegments = 1,

                stemLength = 0.115f,
                stemLengthVar = 0.025f,
                fStemRadius = 0.0012f,
                fStemRadiusVar = 0.0002f,
                bStemLength = 0.05f,
                bStemRadius = 0.0010f,
                bStemLengthVar = 0.015f,
                bStemRadiusVar = 0.0002f,

                flowerDebth = 2,
                flowerBaseDebth = 3,
                LateralsPerNode = 2,
                FlowersPerInternode = 2,
                clusterSize = 2,
                clusterAngle = 0.20f,     
                deterministic = true,
                continous = false,
                internodeFlower = true,
                internodeFlowerWithStem = true,
                floralDepthFactor = 1,
                pFlowerDebth = 1.0f,

                LateralAngle = 50,
                LateralRoll = 15,
                BaseLaterals = 2,        
                BaseLateralAngle = 55,
                BaseLateralRoll = 10,
                pFlowerBaseDebth = 0.9f,

                LeafLength = 0.030f,
                LeafRadius = 0.015f,
                LeafLengthVar = 0.005f,
                LeafRadiusVar = 0.003f,
                LeavePetioleLength = 0.003f,    
                LeavePetioleRadius = 0.0005f,

                growthTime = 150f,
            },
    };
}