namespace Agro;

public class SimulationRequest
{
    ///<summary>
    ///Number of simulation steps per hour (default: 1)
    ///</summary>
    ///<example>1</example>
    public int? HoursPerTick { get; set; }
    ///<summary>
    ///Number of simulation hours (default: 744 == 31 days)
    ///</summary>
    ///<example>744</example>
    public int? TotalHours { get; set; }
    ///<summary>
    ///Field size in meters. Note "D" is depth. (default: 1x1x1)
    ///</summary>
    public Utils.Json.Vector3XDZ? FieldSize { get; set; }
    ///<summary>
    ///Size of a soil voxel in meters (default: 0.5)
    ///</summary>
    ///<example>0.5</example>
    public float? FieldResolution { get; set; }
    ///<summary>
    ///Seed number that controls all pseudo-random decisions in the simulation. (default: 42)
    ///</summary>
    ///<example>42</example>
    public ulong? Seed { get; set; }

    ///<summary>
    ///A list of seeds to be planted (default: a single centered plant)
    ///</summary>
    public PlantRequest[]? Plants { get; set; }

    ///<summary>
    ///A list of obstacles (default: null)
    ///</summary>
    public ObstacleRequest[]? Obstacles { get; set; }

    ///<summary>
    ///If true, returns also the geometry for frontend visualization (default: false)
    ///</summary>
    public bool? RequestGeometry { get; set; }

    ///<summary>
    ///If true, the global illumination is switched off, usually for debugging purposes (default: false)
    ///</summary>
    public bool? ConstantLights { get; set; }
}