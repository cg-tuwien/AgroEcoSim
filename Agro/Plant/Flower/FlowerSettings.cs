using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static Agro.AboveGroundAgent;

namespace Agro
{
    public class FlowerSettings
    {

        public readonly List<OrganTypes> flowerOrgans = new List<OrganTypes>() { OrganTypes.FlowerStem, OrganTypes.FlowerPadel, OrganTypes.FlowerPetiol, OrganTypes.FlowerMeristem, OrganTypes.FlowerBud
    };

        public FlowerSettings()
        {
        }

        // structure
        public float stemLength { get; set; } = 0.01f;
        public bool continous { get; set; } = false;
        public bool internodeFlower { get; set; } = true;
        public int flowerDebth { get; set; } = 1;

        public int LateralsPerNode { get; set; } = 2;
        public int FlowersPerInternode { get; set; } = 2;
        public float stemLengthVar { get; set; } = 0f;
        public bool deterministic { get; set; } = true;
        public int clusterSize { get; set; } = 1;
        public byte floralDepthFactor { get; set; } = 1;
        public float pFlowerDebth { get; set; } = 3f;
        public int flowerBaseDebth { get; set; } = 10;

        public uint FlowerMaxAge { get; set; }
        public uint BudBloomAge { get; set; }


        public int LateralAngle { get; set; } = 90;
        public int LateralRoll { get; set; }

        public bool internodeFlowerWithStem { get; set; } = true;
        public float LeafLength { get; set; } = 0.01f;
        public float LeafRadius { get; set; } = 0.005f;
        public float PedalLength { get; set; } = 0.005f;
        public float PedalRadius { get; set; } = 0.0025f;
        public float BudLength { get; set; } = 0.0075f;
        public float BudRadius { get; set; } = 0.005f;
        public float PetiolLength { get; set; } = 0.02f;
        public float PetiolRadius { get; set; } = 0.0025f;
        public float LeafLengthVar { get; set; }
        public float LeafRadiusVar { get; set; }
        public float LeavePetioleLength { get; set; } = 0.005f;
        public float LeavePetioleRadius { get; set; } = 0.001f;
        public float fStemRadius { get; set; } = 0.0005f;
        public int BaseLaterals { get; set; } = 1;
        public int BaseLateralAngle { get; set; } = 90;
        public int BaseLateralRoll { get; set; }
        public float pFlowerBaseDebth { get; set; } = 0;
        public int petiolSegments { get; set; } = 1;
        public float fStemRadiusVar { get; set; }
        public float bStemRadius { get; set; }
        public float bStemLength { get; set; }
        public float bStemLengthVar { get; set; }
        public float bStemRadiusVar { get; set; }
        public float PetiolRadiusVar { get; set; }
        public float PetiolLengthVar { get; set; }
        public float PedalLengthVar { get; set; }
        public float growthTime { get; set; }
        public float pedalGrowthTimeVar { get; set; }
        public float pedalGrowthTime { get; set; }
        public LeafPhenology FlowerPhenology { get; set; } = new LeafPhenology(
    expectedAge: 30f * 24f,   // 30-day default lifespan
    colorModel: new ColorModel(
        ColorModel.DefaultLifecycle,   // placeholder; override per species
        ColorModel.DefaultStress,
        ColorModel.DefaultSeasonalSenescence
    )
);
        public float clusterAngle { get; internal set; }
        public float PetalSenescenceDurationH { get; set; } = 168f;
        public bool HasFlowerBaseLeaves { get; set; } = false;
        public float pBaseFlowerHeight { get; set; } = 0;
        public float FlowerLeafPetiolRadius { get; set; } = 0.0005f;
        public float FlowerLeafPetiolLength { get; set; } = 0.001f;
        
    }


}
