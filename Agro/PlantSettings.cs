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
    /// For monopodial branching select 1, for dichotomous select 0 and for anisotomous select sth. between
    ///</summary>
    [JsonPropertyName("BMF")]
    public float MonopodialFactor { get; init; }

    ///<summary>
    /// Number of lateral branches emerging from a node
    ///</summary>
    [JsonPropertyName("BLN")]
    public int LateralsPerNode { get; init; }

    ///<summary>
    /// Angular offset (along growth axis) to the previous node, in radians
    ///</summary>
    [JsonPropertyName("BLA")]
    public float LateralAngle { get; init; }

    ///<summary>
    /// Standard leaf length along its main axis (in meters)
    ///</summary>
    [JsonPropertyName("LL")]
    public float LeafLength { get; init; }

    ///<summary>
    /// Standard leaf radius, i.e. the span perpendicular to its main axis (in meters)
    ///</summary>
    [JsonPropertyName("LR")]
    public float LeafRadius { get; init; }

    ///<summary>
    /// Standard growth time of a leaf (in hours)
    ///</summary>
    [JsonPropertyName("LGT")]
    public float LeafGrowthTime { get; init; }


    ///<summary>
    /// Standard leaf pitch angle wrt. to its petiole (in radians)
    ///</summary>
    [JsonPropertyName("LP")]
    public float LeafPitch { get; init; }

    ///<summary>
    /// Standard petiole length (in meters)
    ///</summary>
    [JsonPropertyName("PL")]
    public float PetioleLength { get; init; }

    ///<summary>
    /// Standard petiole radius (in meters)
    ///</summary>
    [JsonPropertyName("PR")]
    public float PetioleRadius { get; init; }
    // public float RootRadiusGrowthPerH { get; init; }
    // public float RootLengthGrowthPerH { get; init; }

    //public int FirstFruitHour { get; init; }

    public float[] DominanceFactors = new[] {1, 0.7f, MathF.Pow(0.7f, 2), MathF.Pow(0.7f, 3), MathF.Pow(0.7f, 4), MathF.Pow(0.7f, 5), MathF.Pow(0.7f, 6), MathF.Pow(0.7f, 7), MathF.Pow(0.7f, 8), MathF.Pow(0.7f, 9), MathF.Pow(0.7f, 10), MathF.Pow(0.7f, 11), MathF.Pow(0.7f, 12)};

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
}