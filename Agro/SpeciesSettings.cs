using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text.Json.Serialization;
using AgentsSystem;
using glTFLoader.Schema;
using NumericHelpers;
using Utils;

namespace Agro;

public class SpeciesSettings
{
    ///<summary>
    /// Species name
    ///</summary>
    [JsonPropertyName("N")]
    public string Name { get; init; }

    ///<summary>
    /// Standard height of the plant (in meters)
    ///</summary>
    [JsonPropertyName("H")]
    public float Height { get; init; }

    ///<summary>
    /// Standard height of the plant (in meters)
    ///</summary>
    [JsonPropertyName("ND")]
    public float NodeDistance { get; init; }

    ///<summary>
    /// Variance of the plant height (in meters)
    ///</summary>
    [JsonPropertyName("NDv")]
    public float NodeDistanceVar { get; init; }

    ///<summary>
    /// For monopodial branching select 1, for dichotomous select 0 and for anisotomous select sth. between
    ///</summary>
    [JsonPropertyName("BMF")]
    public float MonopodialFactor { get; init; }

    private float dominanceFactor;
    public float[] DominanceFactors;

    ///<summary>
    /// Dominance factor reduces (or boosts) growth of lateral branches. Multiplies with each recursion level.
    ///</summary>
    [JsonPropertyName("BDF")]
    public float DominanceFactor { get => dominanceFactor; init {
        dominanceFactor = value;
        const int factors = 16;
        DominanceFactors = new float[factors + 1];
        DominanceFactors[0] = 1;
        DominanceFactors[1] = 1;
        DominanceFactors[2] = dominanceFactor;
        for(int i = 3; i < factors; ++i)
            DominanceFactors[i] = MathF.Pow(dominanceFactor, i);
    } }

    ///<summary>
    /// Number of lateral branches emerging from a node
    ///</summary>
    [JsonPropertyName("BLN")]
    public int LateralsPerNode { get; init; }

    ///<summary>
    /// Angular offset (along growth axis) to the previous node, in radians
    ///</summary>
    [JsonPropertyName("BR")]
    public float LateralRoll { get; init; }

    ///<summary>
    /// Variance of the angular offset (along growth axis) to the previous node, in radians
    ///</summary>
    [JsonPropertyName("BRv")]
    public float LateralRollVar { get; init; }

    ///<summary>
    /// Angle of the lateral to its parent, in radians
    ///</summary>
    [JsonPropertyName("BP")]
    public float LateralPitch { get; init; }

    ///<summary>
    /// Variance of the angle of the lateral to its parent, in radians
    ///</summary>
    [JsonPropertyName("BPv")]
    public float LateralPitchVar { get; init; }


    [JsonPropertyName("TB")]
    public float TwigsBending { get; init; }
    [JsonPropertyName("TBL")]
    public float TwigsBendingLevel { get; init; }

    [JsonPropertyName("TBA")]
    public float TwigsBendingApical { get; set; }

    [JsonPropertyName("SG")]
    public float ShootsGravitaxis { get; set; }

    /// <summary>
    /// Standard wood growth time (in hours)
    /// </summary>
    [JsonPropertyName("WGT")]
    public float WoodGrowthTime { get; set; }
    /// <summary>
    /// Variance of the wood growth time (in hours)
    /// </summary>
    [JsonPropertyName("WGTv")]
    public float WoodGrowthTimeVar { get; set; }

    ///<summary>
    /// Maximum branch level that supports leaves (here level denotes the max. subtree depth)
    ///</summary>
    [JsonPropertyName("LV")]
    public int LeafLevel { get; init; }

    ///<summary>
    /// Standard leaf length along its main axis (in meters)
    ///</summary>
    [JsonPropertyName("LL")]
    public float LeafLength { get; init; }

    ///<summary>
    /// Variance of the leaf length along its main axis (in meters)
    ///</summary>
    [JsonPropertyName("LLv")]
    public float LeafLengthVar { get; init; }

    ///<summary>
    /// Standard leaf radius, i.e. the span perpendicular to its main axis (in meters)
    ///</summary>
    [JsonPropertyName("LR")]
    public float LeafRadius { get; init; }

    ///<summary>
    /// Variance of the leaf radius, i.e. the span perpendicular to its main axis (in meters)
    ///</summary>
    [JsonPropertyName("LRv")]
    public float LeafRadiusVar { get; init; }

    ///<summary>
    /// Standard growth time of a leaf (in hours)
    ///</summary>
    [JsonPropertyName("LGT")]
    public float LeafGrowthTime { get; init; }

    ///<summary>
    /// Variance of the growth time of a leaf (in hours)
    ///</summary>
    [JsonPropertyName("LGTv")]
    public float LeafGrowthTimeVar { get; init; }


    ///<summary>
    /// Standard leaf pitch angle wrt. to its petiole (in radians)
    ///</summary>
    [JsonPropertyName("LP")]
    public float LeafPitch { get; init; }

    ///<summary>
    /// Variance of the leaf pitch angle wrt. to its petiole (in radians)
    ///</summary>
    [JsonPropertyName("LPv")]
    public float LeafPitchVar { get; init; }

    ///<summary>
    /// Standard petiole length (in meters)
    ///</summary>
    [JsonPropertyName("PL")]
    public float PetioleLength { get; init; }

    ///<summary>
    /// Variance of the petiole length (in meters)
    ///</summary>
    [JsonPropertyName("PLv")]
    public float PetioleLengthVar { get; init; }

    ///<summary>
    /// Standard petiole radius (in meters)
    ///</summary>
    [JsonPropertyName("PR")]
    public float PetioleRadius { get; init; }

    ///<summary>
    /// Variance of the petiole radius (in meters)
    ///</summary>
    [JsonPropertyName("PRv")]
    public float PetioleRadiusVar { get; init; }

    ///<summary>
    /// Density of the roots system (affects branching probabiilty). Valued 1 to 100 (with one being the most dense)
    ///</summary>
    [JsonPropertyName("RS")]
    public float RootsSparsity { get; init; }

    ///<summary>
    /// Correction factor to point the roots growth downwards
    ///</summary>
    [JsonPropertyName("RG")]
    public float RootsGravitaxis { get; init; }

    // public float RootRadiusGrowthPerH { get; init; }
    // public float RootLengthGrowthPerH { get; init; }

    //public int FirstFruitHour { get; init; }


    [JsonPropertyName("AP")]
    public float AuxinsProduction { get; init; }

    public float CytokininsProduction { get; init; }

    [JsonPropertyName("AR")]
    public float AuxinsReach { get; init; }

    public float CytokininsReach { get; init; }

    public float AuxinsThreshold => 1f;

    public float DensityDryWood = 700; //in kg/m³
	public float DensityDryStem = 200; //in kg/m³

    [JsonPropertyName("WEM")]
    public float WoodElasticModulus { get; init; } = 1e10f; // in Pa

    [JsonPropertyName("GEM")]
    public float GreenElasticModulus { get; init; } = 1e8f; // in Pa

    public float PetioleCoverThreshold { get; private set; } = float.MaxValue;

    public static SpeciesSettings Avocado;

    static SpeciesSettings()
    {
        //Just gueesing
        Avocado = new() {
            Name = "Persea americana",
            LeafLength = 0.2f,
            LeafRadius = 0.04f,
            PetioleLength = 0.05f,
            PetioleRadius = 0.007f,
            // RootLengthGrowthPerH = 0.023148148f,
            // RootRadiusGrowthPerH = 0.00297619f,
            LeafGrowthTime = 720,
            Height = 12f,
            //FirstFruitHour = 113952,
        };
    }

    private bool Initialized = false;

    public void Init(int hoursPerTick)
    {
        if (!Initialized)
        {
            // AuxinsDegradationPerTick = AuxinsReach * hoursPerTick;
            // CytokininsDegradationPerTick = CytokininsReach * hoursPerTick;
            TwigsBendingApical = TwigsBendingApical * TwigsBendingLevel;
            ShootsGravitaxis *= 0.4f;
            Initialized = true;

            PetioleCoverThreshold = MathF.Cos(MathF.PI * 0.5f - LateralPitch) * PetioleLength * 0.25f;

            //BUG with petiole -> stem and not meristem
            //Remove length factor at apex distribution for the current segment
            //Bending suddenly does not work
            //BUG water distribution
        }
    }
}