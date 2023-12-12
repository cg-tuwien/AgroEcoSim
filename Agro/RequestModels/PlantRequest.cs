using System.Text.Json.Serialization;

namespace Agro;

///<summary>
///Per plant request data
///</summary>
public class PlantRequest
{
    [JsonPropertyName("S")]
    public string? SpeciesName { get; set; }

    ///<summary>
    ///Position of the plant seed (OpenGL-like coordinates); Use X,Y,Z for its components, e.g. { "X": 1. "Y": 2, "Z": 3 } [default: 0,0,0]
    ///</summary>
    ///<example>{"X":0, "Y":0, "Z":0}</example>
    //The converter was useful for System.Numerics.Vector3 but Swagger doesn't support including it among the examples.
    //[System.Text.Json.Serialization.JsonConverter(typeof(Utils.Json.Vector3JsonConverter))]
    [JsonPropertyName("P")]
    public Utils.Json.Vector3XYZ? Position { get; set; }
    //public System.Numerics.Vector3? Position { get; set; }
}