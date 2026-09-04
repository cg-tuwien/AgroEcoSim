using Utils;

namespace Agro;

public class ImportedObjData
{
    public string[] Vertices { get; set; }
    //public string[] Normals { get; set; }
    public Dictionary<string, string[]> Faces { get; set; }
    public Dictionary<string, string> Materials { get; set; }
}

public class ImportedObjDataV2
{
    public string[] Vertices { get; set; }
    //public string[] Normals { get; set; }
    public Dictionary<string, (List<Vector3i> Triangles, List<int[]> Polygons)> Faces { get; set; }
}

public class SimulationRequest
{
    ///<summary>
    ///Number of simulation steps per hour (default: 1)
    ///</summary>
    ///<example>1</example>
    public int? HoursPerTick { get; init; }
    ///<summary>
    ///Number of simulation hours (default: 744 == 31 days)
    ///</summary>
    ///<example>744</example>
    public int? TotalHours { get; init; }
    ///<summary>
    ///Field size in meters. Note "D" is depth. (default: 1x1x1)
    ///</summary>
    public Utils.Json.Vector3XDZ? FieldSize { get; init; }
    ///<summary>
    ///Size of a soil voxel in meters (default: 0.5)
    ///</summary>
    ///<example>0.5</example>
    public float? FieldResolution { get; init; }
    ///<summary>
    ///Seed number that controls all pseudo-random decisions in the simulation. (default: 42)
    ///</summary>
    ///<example>42</example>
    public ulong? Seed { get; init; }

    /// <summary>
    /// Date to start the simulation
    /// </summary>
    public DateTime? InitialDate { get; init; }

    ///<summary>
    ///A list of seeds to be planted (default: a single centered plant)
    ///</summary>
    public SpeciesSettings[]? Species { get; init; }

    ///<summary>
    ///A list of seeds to be planted (default: a single centered plant)
    ///</summary>
    public PlantRequest[]? Plants { get; init; }

    ///<summary>
    ///A list of obstacles (default: null)
    ///</summary>
    public ObstacleRequest[]? Obstacles { get; init; }

    ///<summary>
    ///If true, returns also the geometry for frontend visualization (default: false)
    ///</summary>
    public bool? RequestGeometry { get; init; }

    ///<summary>
    ///Selects the renderer, 0 = no renderer, constant light; 1 = Mitsuba3, 2 = Tamashii,
    ///3 = the internal CPU light model. Modes 1 and 2 fall back to mode 3 when their renderer does
    ///not answer. (default: 0)
    ///</summary>
    public byte? RenderMode { get; init; }

    /// <summary>
    /// Hemisphere rays per sensor and simulation step for the internal CPU light model. Higher
    /// values trade simulation speed for less noise in the per-leaf irradiance. (default: 16)
    /// </summary>
    ///<example>16</example>
    public int? LightSamples { get; init; }

    /// <summary>
    /// Case-insensitive substring of the obstacle names whose leaf area index and solar load should
    /// be measured every simulation step, for example "Wand" to select the facades of an imported
    /// building. Nothing is measured when this is not set. Only effective with the internal light
    /// model (render mode 3, or 1 and 2 after a fallback).
    /// </summary>
    ///<example>Wand</example>
    public string? LeafAreaIndexObstacles { get; init; }

    /// <summary>
    /// Total number of measurement points spread over all selected obstacles, in proportion to their
    /// area. The cost of the measurement follows this number rather than the triangle count.
    /// (default: 16384)
    /// </summary>
    ///<example>16384</example>
    public int? LeafAreaIndexSensors { get; init; }

    /// <summary>
    /// How far in front of a selected obstacle, in metres, foliage still counts towards its leaf
    /// area index. (default: 3)
    /// </summary>
    ///<example>3</example>
    public float? LeafAreaIndexDepth { get; init; }

    /// <summary>
    /// Number of samples to render per pixel (usually a power of two between 64 and 4096)
    /// </summary>
    public ushort? SamplesPerPixel { get; init; }

    ///<summary>
    ///If true, the backend will send a preview after each simulation step. Otherwise the previews are sent layzily. (default: false)
    ///</summary>
    public bool? ExactPreview { get; init; }

    /// <summary>
    /// If true, the backend will send the roots geometry along with the shoots. Attention, involves large data volumes. (default: false)
    /// </summary>
    public bool? DownloadRoots { get; init; }

    /// <summary>
    /// File data of the scene model
    /// </summary>
    public string? FieldModelKey { get; init; }
    /// <summary>
    /// File name of the scene model
    /// </summary>
    public string? FieldModelPath { get; init; }
    /// <summary>
    /// Regex that matches the names of all pots in the scene where plants can be seeded
    /// </summary>
    public string? FieldItemRegex { get; init; }
    /// <summary>
    /// Deciedes whether the regex matches object names or materials
    /// </summary>
    public bool? FieldItemRegexMaterial { get; init; }
}