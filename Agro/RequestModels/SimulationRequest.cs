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

    ///<summary>
    ///Foliage-against-foliage collisions, within a plant and between neighbouring plants.
    ///(default: true)
    ///</summary>
    ///<remarks>
    ///Collisions with obstacles and with the soil are hard constraints and are always resolved; this
    ///switch governs only the best-effort foliage work. Turning it off is a real trade. Measured on
    ///the green facade at 1,882 plants over 120 days: with it on, 1.13% of organs end inside the soil
    ///and 3.23% point downwards, at 68.5 ms per step; with it off, 0.95% and 0.64% at 37.5 ms. The
    ///foliage pass costs about two thirds of the collision phase, and at that density it does not
    ///reduce burial - that is the hard passes' work - while its response is what tips organs over.
    ///It still reduces interpenetration between neighbours, which is what it is for.
    ///</remarks>
    ///<example>true</example>
    public bool? SoftCollisions { get; init; }

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
    /// Height of the horizontal bands each selected obstacle is measured in, in metres. Foliage on a
    /// facade develops from the planting level upwards, so a wall reported as a single number hides
    /// that gradient. Set it larger than the tallest obstacle to get one band per object.
    /// (default: 0.5)
    /// </summary>
    ///<example>0.5</example>
    public float? LeafAreaIndexBandHeight { get; init; }

    /// <summary>
    /// Number of samples to render per pixel (usually a power of two between 64 and 4096)
    /// </summary>
    public ushort? SamplesPerPixel { get; init; }

    ///<summary>
    ///If true, the backend will send a preview after each simulation step. Otherwise the previews are sent layzily. (default: false)
    ///</summary>
    /// <summary>
    /// Run the simulation sequentially so that it is reproducible.
    ///
    /// Off by default: the parallel path is faster and most runs do not need reproducibility.
    /// Required for branching to mean anything - see SimulationWorld.Deterministic.
    /// </summary>
    public bool? Deterministic { get; init; }

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

    /// <summary>
    /// The stored run this one continues or was forked from, recorded for lineage.
    ///
    /// Lineage only - it changes nothing about how the simulation runs. There is no checkpoint of
    /// live world state anywhere in this project, so a child does not resume its parent: it is
    /// computed again from this request. That is exact for a <see cref="Deterministic"/> parent
    /// and merely similar for any other, which is what the recorded flag is for.
    /// </summary>
    public string? ParentRunId { get; init; }

    /// <summary>
    /// Build the terrain from the field model stored with this run.
    ///
    /// Without it a stored run cannot be re-run at all once its facade is gone from the upload
    /// cache, which takes five minutes: the model reaches the simulation through
    /// <c>POST /terrain</c> and a per-connection cache, and <see cref="FieldModelKey"/> has never
    /// been set by anything. The run store keeps the OBJ source, so continuing or forking a run
    /// months later costs the user nothing and re-uploads nothing.
    ///
    /// Ignored when the connection has already uploaded a model of its own - an explicit upload is
    /// always the more recent intent.
    /// </summary>
    public string? FieldModelRunId { get; init; }
}