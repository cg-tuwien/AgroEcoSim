using Agro;
using Utils.Json;

namespace GodotFrontend.Core.Model;

/// <summary>A seed row: species, tray-local position, tray index and signed age offset (helpers/Seed.ts).</summary>
public sealed class SeedModel
{
    public string Species = "";
    public double Px, Py, Pz;
    public int Field;
    public double StartOffsetHours;

    public PlantRequest ToRequest() => new()
    {
        SpeciesName = Species,
        Position = new Vector3XYZ { X = (float)Px, Y = (float)Py, Z = (float)Pz },
        SoilIndex = Field,
        StartOffsetHours = StartOffsetHours,
    };
}

/// <summary>
/// Everything the SimulationRequest is built from (appstate.requestBody), with the web UI's defaults. The species
/// list holds real Agro SpeciesSettings instances; the request serializer sends only their settable members.
/// </summary>
public sealed class RequestState
{
    public int HoursPerTick = 4;
    public int TotalHours = 1440;
    public float FieldResolution = 0.05f;
    public float FieldSizeX = 10, FieldSizeD = 4, FieldSizeZ = 10;
    /// <summary>Exact unsigned 64-bit seed kept as decimal text.</summary>
    public string InitNumber = "42";
    public bool Randomize;
    public string InitialDate = "2026-02-01";
    public List<DailyAirTemperature>? DailyAirTemperatures;
    public float Latitude = 48.208333f, Longitude = 16.3725f, Altitude = 188f, AzimuthBase = 0f;
    public TerrainOrientation AzimuthOffset = TerrainOrientation.Forward;
    public List<SpeciesSettings> Species = new();
    public List<SeedModel> Seeds = new();
    public List<ObstacleRequest> Obstacles = new();
    public byte RenderMode = 3;
    public ushort SamplesPerPixel = 2048;
    public string LeafAreaIndexObstacles = "";
    public int LeafAreaIndexSensors = 32768;
    public float LeafAreaIndexDepth = 3;
    public float LeafAreaIndexBandHeight = 0.5f;
    public bool ExactPreview, DownloadRoots, VirtualRoots, Deterministic;
    public ContactEngineKind ContactEngine = ContactEngineKind.Mixed;
    public string FieldItemRegex = "erde";
    public bool FieldItemRegexMaterial = true;
    public string FieldModelPath = "";
    public string LineageParentRunId = "";
    public string FieldModelRunId = "";

    /// <summary>Degrees the sun is rotated by for this site: base plus the terrain orientation's offset.</summary>
    public float AzimuthDegrees => (AzimuthBase + AzimuthOffset.OffsetDegrees()) % 360f;

    public ulong SeedValue(Random rng)
    {
        if (Randomize) return (ulong)rng.NextInt64(0, 4294967296L);
        return ulong.TryParse(InitNumber.Trim(), out var v) ? v : 42;
    }

    public SimulationRequest ToRequest(Random rng) => new()
    {
        HoursPerTick = HoursPerTick,
        TotalHours = TotalHours,
        FieldResolution = FieldResolution,
        FieldSize = new Vector3XDZ { X = FieldSizeX, D = FieldSizeD, Z = FieldSizeZ },
        Seed = SeedValue(rng),
        InitialDate = Time.SimClock.ParseDate(InitialDate).ToDateTime(TimeOnly.MinValue),
        DailyAirTemperatures = DailyAirTemperatures?.ToArray(),
        Latitude = Latitude,
        Longitude = Longitude,
        Altitude = Altitude,
        AzimuthBase = AzimuthBase,
        AzimuthOffset = AzimuthOffset,
        Species = Species.ToArray(),
        Plants = Seeds.Select(s => s.ToRequest()).ToArray(),
        Obstacles = Obstacles.ToArray(),
        RequestGeometry = true,
        RenderMode = RenderMode,
        SamplesPerPixel = SamplesPerPixel,
        LeafAreaIndexObstacles = string.IsNullOrWhiteSpace(LeafAreaIndexObstacles) ? null : LeafAreaIndexObstacles.Trim(),
        LeafAreaIndexSensors = LeafAreaIndexSensors,
        LeafAreaIndexDepth = LeafAreaIndexDepth,
        LeafAreaIndexBandHeight = LeafAreaIndexBandHeight,
        ExactPreview = ExactPreview,
        DownloadRoots = DownloadRoots,
        VirtualRoots = VirtualRoots,
        FieldItemRegex = FieldItemRegex,
        Deterministic = Deterministic,
        ContactEngine = ContactEngine,
        FieldItemRegexMaterial = FieldItemRegexMaterial,
        FieldModelPath = FieldModelPath,
        ParentRunId = string.IsNullOrEmpty(LineageParentRunId) ? null : LineageParentRunId,
        FieldModelRunId = string.IsNullOrEmpty(FieldModelRunId) ? null : FieldModelRunId,
    };

    /// <summary>Loads a stored run's request back into the editor (GET run/{id}/request).</summary>
    public void FromRequest(SimulationRequest r)
    {
        if (r.HoursPerTick is int h) HoursPerTick = h;
        if (r.TotalHours is int t) TotalHours = t;
        if (r.FieldResolution is float fr) FieldResolution = fr;
        if (r.FieldSize is { } fs) { FieldSizeX = fs.X; FieldSizeD = fs.D; FieldSizeZ = fs.Z; }
        if (r.Seed is ulong seed) InitNumber = seed.ToString();
        Randomize = false;
        if (r.InitialDate is DateTime d) InitialDate = d.ToString("yyyy-MM-dd");
        DailyAirTemperatures = r.DailyAirTemperatures?.ToList();
        if (r.Latitude is float la) Latitude = la;
        if (r.Longitude is float lo) Longitude = lo;
        if (r.Altitude is float al) Altitude = al;
        if (r.AzimuthBase is float ab) AzimuthBase = ab;
        if (r.AzimuthOffset is TerrainOrientation ao) AzimuthOffset = ao;
        if (r.Species is { Length: > 0 } sp) Species = sp.ToList();
        Seeds = (r.Plants ?? Array.Empty<PlantRequest>()).Select(p => new SeedModel
        {
            Species = p.SpeciesName ?? "",
            Px = p.Position?.X ?? 0, Py = p.Position?.Y ?? 0, Pz = p.Position?.Z ?? 0,
            Field = p.SoilIndex, StartOffsetHours = p.StartOffsetHours,
        }).ToList();
        Obstacles = (r.Obstacles ?? Array.Empty<ObstacleRequest>()).ToList();
        if (r.RenderMode is byte rm) RenderMode = rm;
        if (r.SamplesPerPixel is ushort spp) SamplesPerPixel = spp;
        LeafAreaIndexObstacles = r.LeafAreaIndexObstacles ?? "";
        if (r.LeafAreaIndexSensors is int ls) LeafAreaIndexSensors = ls;
        if (r.LeafAreaIndexDepth is float ld) LeafAreaIndexDepth = ld;
        if (r.LeafAreaIndexBandHeight is float lb) LeafAreaIndexBandHeight = lb;
        ExactPreview = r.ExactPreview ?? false;
        DownloadRoots = r.DownloadRoots ?? false;
        VirtualRoots = r.VirtualRoots;
        Deterministic = r.Deterministic ?? false;
        if (r.ContactEngine is ContactEngineKind ce) ContactEngine = ce;
        FieldItemRegex = r.FieldItemRegex ?? FieldItemRegex;
        FieldItemRegexMaterial = r.FieldItemRegexMaterial ?? FieldItemRegexMaterial;
        FieldModelPath = r.FieldModelPath ?? "";
    }

    public void AddDefaultSeed(string species)
    {
        Seeds.Add(new SeedModel { Species = species, Px = FieldSizeX * 0.5, Py = -0.01, Pz = FieldSizeZ * 0.5, Field = 0 });
    }
}
