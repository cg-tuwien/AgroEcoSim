using System.Text.Json;
using System.Text.Json.Serialization;
using GodotFrontend.Core.Stereo;

namespace GodotFrontend.Core.Persist;

/// <summary>App preferences, a JSON file under the user data directory. Everything optional; missing keys keep defaults.</summary>
public sealed class Settings
{
    public string? ServerUrl { get; set; }
    public string? LastRunId { get; set; }
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public int? WindowW { get; set; }
    public int? WindowH { get; set; }
    public bool? WindowMaximized { get; set; }
    public float? ContentScale { get; set; }
    public long? HistoryBudgetMb { get; set; }
    public long? FrameCacheMb { get; set; }
    public int? ActiveTab { get; set; }
    public float? SidePanelWidth { get; set; }
    public LightingSettings Lighting { get; set; } = new();
    public StereoSettings Stereo { get; set; } = new();
    public string VisualMapping { get; set; } = "natural";
    public bool ShowLeaves { get; set; } = true;
    public bool ShowRoots { get; set; } = true;
    public bool ShowTerrain { get; set; } = true;
    public bool ShowSeeds { get; set; } = true;
    public bool ShowObstacles { get; set; } = true;
    public bool PerformPicking { get; set; } = true;

    [JsonIgnore] public string Path { get; private set; } = "";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static Settings Load(string path)
    {
        Settings s;
        try { s = File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Options) ?? new() : new(); }
        catch { s = new(); }
        s.Path = path;
        return s;
    }

    public void Save()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, Options));
        }
        catch { /* preferences are best effort */ }
    }
}

/// <summary>The viewport lighting switches (core/Lighting.ts LightingOptions) plus the sky background.</summary>
public sealed class LightingSettings
{
    public bool Daylight { get; set; }
    public bool Shadows { get; set; }
    public bool Occlusion { get; set; }
    public bool Indirect { get; set; }
    public bool SecondBounce { get; set; }
    public bool LeafTranslucency { get; set; }
    public bool Sky { get; set; }
    public float ShadowBiasTexels { get; set; } = 4;
}

/// <summary>The stereo presentation of the viewport. The test pattern is a calibration aid and is never stored.</summary>
public sealed class StereoSettings
{
    public string Mode { get; set; } = "mono";
    public double EyeSeparation { get; set; } = StereoOptions.DefaultEyeSeparation;
    public bool ConvergeOnTarget { get; set; } = true;
    public double Convergence { get; set; } = StereoOptions.DefaultConvergence;
    public bool SwapEyes { get; set; }
}
