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

public class SimulationRequest
{
    ///<summary>
    ///Number of simulation steps per hour (default: 1)
    ///</summary>
    public uint? TicksPerHour { get; set; } = 1;
    ///<summary>
    ///Number of simulation hours (default: 744)
    ///</summary>
    public uint? TotalHours { get; set; } = 744;
    ///<summary>
    ///Field size in meters. Note "D" is depth. (default: 5x2x5)
    ///</summary>
    public Utils.Json.Vector3XDZ? FieldSize { get; set; } = new(){ X = 5, D = 2f, Z = 5 };
    ///<summary>
    ///Size of a soil voxel in meters (default: 0.5)
    ///</summary>
    public float? FieldResolution { get; set; }

    ///<summary>
    ///A list of seeds to be planted
    ///</summary>
    public PlantRequest[]? Plants { get; set; }
}