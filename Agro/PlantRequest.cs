using System.Numerics;

namespace Agro;

///<summary>
///Per plant request data
///</summary>
public class PlantRequest
{
    #if !GODOT
    ///<summary>
    ///Position of the plant seed (OpenGL-like coordinates); Use X,Y,Z for its components, e.g. { "X": 1. "Y": 2, "Z": 3 }
    ///</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(Utils.Json.Vector3JsonConverter))]
    [System.Text.Json.Serialization.JsonPropertyName("P")]
    #endif
    public Vector3? Position { get; set; }
}