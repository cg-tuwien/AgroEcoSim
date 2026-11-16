using System.Text.Json;
using System.Text.Json.Serialization;

namespace GodotFrontend.Core.Net.Dto;

/// <summary>AgroServer/Models/RunResponse.cs, camelCased by ASP.NET on the way out.</summary>
public sealed class RunDto
{
    public string Id { get; set; } = "";
    /// <summary>running | complete | aborted</summary>
    public string Status { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public int HoursPerTick { get; set; }
    public uint TotalSteps { get; set; }
    public uint StoredFrames { get; set; }
    public uint LastStep { get; set; }
    public int TotalHours { get; set; }
    public string InitialDay { get; set; } = "";
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public ulong Seed { get; set; }
    public byte RenderMode { get; set; }
    public uint Plants { get; set; }
    public uint Trays { get; set; }
    public uint Obstacles { get; set; }
    public uint Species { get; set; }
    public bool Analytics { get; set; }
    public bool Deterministic { get; set; }
    public long BytesOnDisk { get; set; }
    public long AuxBytesOnDisk { get; set; }
    public string? FieldModelName { get; set; }
    public bool HasFieldModel { get; set; }
    public bool HasTerrain { get; set; }
    public bool HasObstacleLight { get; set; }
    public bool HasCheckpoint { get; set; }
    public string? ParentRunId { get; set; }
    public uint? BranchStep { get; set; }
    public string? StopReason { get; set; }
    /// <summary>Null for a server that predates run protection.</summary>
    public bool? Protected { get; set; }

    public long TotalBytes => BytesOnDisk + AuxBytesOnDisk;
    public bool IsRunning => Status == "running";
}

public sealed class RunSiteDto
{
    public float Latitude { get; set; }
    public float Longitude { get; set; }
    public float Altitude { get; set; }
    public float AzimuthBase { get; set; }
    public int AzimuthOffset { get; set; }
    public string TimeZone { get; set; } = "";
    /// <summary>Cloud cover per timestep as raw little-endian float32 (base64 on the wire, decoded by the JSON layer).</summary>
    public byte[] SkyCoverage { get; set; } = Array.Empty<byte>();

    public float[] Coverage()
    {
        var n = SkyCoverage.Length / 4;
        var f = new float[n];
        Buffer.BlockCopy(SkyCoverage, 0, f, 0, n * 4);
        return f;
    }
}

public sealed class PreviewDto
{
    public byte[] Scene { get; set; } = Array.Empty<byte>();
    public string Renderer { get; set; } = "";
    public uint Step { get; set; }
}

public sealed class PlantDto
{
    public float V { get; set; }
}

public sealed class ResultDto
{
    public List<PlantDto>? Plants { get; set; }
    public byte[]? Scene { get; set; }
    public string Renderer { get; set; } = "";
    public string? Debug { get; set; }
    public List<long>? StepTimes { get; set; }
    public uint? CompletedSteps { get; set; }
    public uint TicksPerMillisecond { get; set; }
    public JsonElement? ObstacleLight { get; set; }
    public string? StopReason { get; set; }
}

public sealed class StoreEventDto
{
    public long Seq { get; set; }
    public DateTime Utc { get; set; }
    public string Level { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? RunId { get; set; }
    public long Bytes { get; set; }
    public string Message { get; set; } = "";
}

public sealed class StoreStatusDto
{
    public bool Enabled { get; set; }
    public string Root { get; set; } = "";
    public string DisabledReason { get; set; } = "";
    public string MountPoint { get; set; } = "";
    public string FileSystem { get; set; } = "";
    public string VolumeName { get; set; } = "";
    public bool AnonymousVolume { get; set; }
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public long MinFreeBytes { get; set; }
    public long RetentionBytes { get; set; }
    public long ModelRetentionBytes { get; set; }
    public long StoredBytes { get; set; }
    public long ModelBytes { get; set; }
    public int Runs { get; set; }
    public int LiveRuns { get; set; }
    public double SweepMinutes { get; set; }
    public string[] Warnings { get; set; } = Array.Empty<string>();
    public StoreEventDto[] Events { get; set; } = Array.Empty<StoreEventDto>();
}

public sealed class DeleteDto
{
    public bool Deleted { get; set; }
    public string Reason { get; set; } = "";
}

/// <summary>GET run/{id}/index: the stored steps with their sizes and the checkpoint revision headers.</summary>
public sealed record RunIndex(uint[] Steps, uint[] Sizes, string Revision, uint RestartStep)
{
    public static RunIndex Decode(byte[] buf, string? revision, string? restart)
    {
        var whole = buf.Length / 8;
        var steps = new uint[whole];
        var sizes = new uint[whole];
        for (var i = 0; i < whole; ++i)
        {
            steps[i] = BitConverter.ToUInt32(buf, i * 8);
            sizes[i] = BitConverter.ToUInt32(buf, i * 8 + 4);
        }
        return new RunIndex(steps, sizes, revision ?? "0", uint.TryParse(restart, out var r) ? r : 0);
    }
}
