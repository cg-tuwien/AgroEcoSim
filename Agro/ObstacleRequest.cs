using System.Text.Json.Serialization;

namespace Agro;

///<summary>
///Per obstacle request data
///</summary>
public class ObstacleRequest
{
    ///<summary>
    ///Obstacle type: either wall or umbrella (required)
    ///</summary>
    ///<example>wall</example>
    [JsonPropertyName("T")]
    public string? Type { get; set; }

    ///<summary>
    ///Rotation of the wall in radians with 0 being aligned with X axis; for an umbrella it has no effect (default: 0)
    ///</summary>
    [JsonPropertyName("O")]
    public float? Orientation { get; set; }

    ///<summary>
    ///Thickness (depth) of the wall; for an umbrella the diameter of the pole (default: 0.1)
    ///</summary>
    ///<example>0.1</example>
    [JsonPropertyName("D")]
    public float? Thickness { get; set; }

    ///<summary>
    ///Length of the wall; for an umbrella it's active only if R is not set, then it represents its diameter (default: 1)
    ///</summary>
    ///<example>1</example>
    [JsonPropertyName("L")]
    public float? Length { get; set; }

    ///<summary>
    ///Radius of the umbrella; for a wall it's active only if L is not set, then it represents half of its length (default: 0.5)
    ///</summary>
    ///<example>0.5</example>
    [JsonPropertyName("R")]
    public float? Radius { get; set; }

    ///<summary>
    ///Height of the obstacle (default: 1)
    ///</summary>
    ///<example>1</example>
    [JsonPropertyName("H")]
    public float? Height { get; set; }

    ///<summary>
    ///Position of the obstacle (OpenGL-like coordinates, the anchor point is bottom-center); Use X,Y,Z for its components, e.g. { "X": 1. "Y": 2, "Z": 3 } (default: 0,0,0)
    ///</summary>
    //The converter was useful for System.Numerics.Vector3 but Swagger doesn't support including it among the examples.
    //[JsonConverter(typeof(Utils.Json.Vector3JsonConverter))]
    [JsonPropertyName("P")]
    public Utils.Json.Vector3XYZ? Position { get; set; }
}