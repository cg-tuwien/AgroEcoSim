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
        DominanceFactors = new float[factors];
        DominanceFactors[0] = 1;
        DominanceFactors[1] = dominanceFactor;
        for(int i = 2; i < factors; ++i)
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

    public float AuxinsProduction { get; init; } = 1000f;

    public float CytokininsProduction { get; init; }

    public float AuxinsReach { get; private set; } = 1f;

    public float CytokininsReach { get; private set; }


    public float DensityDryWood = 700; //in kg/m³
	public float DensityDryStem = 200; //in kg/m³

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

            Initialized = true;
        }
    }
}