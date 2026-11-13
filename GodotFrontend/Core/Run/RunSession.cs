using System.Text.Json;
using Agro;

namespace GodotFrontend.Core.Run;

/// <summary>Where the run is and what weather it sees (RunSite message); the sun, the sky and the clock read it.</summary>
public sealed class SiteInfo
{
    public float Latitude = 48.208333f, Longitude = 16.3725f, Altitude = 188f, AzimuthBase = 0f;
    public int AzimuthOffset = 0;
    public string? TimeZone;
    public float[] Coverage = Array.Empty<float>();

    /// <summary>How the scene sits on the compass: the base azimuth plus the loaded model's orientation offset.</summary>
    public float AzimuthDegrees => (AzimuthBase + ((TerrainOrientation)AzimuthOffset).OffsetDegrees()) % 360f;
}

/// <summary>The run-level facts of what the viewport is showing (the run* and sim* signals of appstate).</summary>
public sealed class RunSession
{
    public string RunId = "";
    /// <summary>true once the server said it stores the run, false once it said 404, null until answered.</summary>
    public bool? RunStored;
    public string ViewingRunId = "";
    public string StopReason = "";
    public bool Computing;
    public string Renderer = "";
    public uint SimStep, SimLength;
    public (uint step, uint total)? Preparation;
    public int SimHoursPerTick = 4;
    public string InitialDate = "2026-02-01";
    public SiteInfo Site = new();
    public JsonElement? ObstacleLight;
    public bool HistoryLimitHit;
    public long HistoryBudget;
    public string LastError = "";

    public bool FramesRecoverable => RunId.Length > 0 && RunStored == true;
}
