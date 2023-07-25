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


    ///<summary>
    /// Standard height of the plant (in meters)
    ///</summary>
    [JsonPropertyName("H")]
    public float Height { get; init; }

    //public int FirstFruitHour { get; init; }

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