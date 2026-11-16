namespace AgroServer.Models;

public class SimulationResponse
{
    /// <summary>The durable handle, also returned by synchronous simulations.</summary>
    public string? RunId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool StorageFailed { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ExecutionFailed { get; set; }
    ///<summary>
    ///Resulting plants (same ordering as in the request)
    ///</summary>
    public List<PlantResponse> Plants { get; set; }
    ///<summary>
    ///3D Scene of the last time step
    ///</summary>
    public byte[] Scene { get; set; }
    public string Renderer { get; set; }
    public string Debug { get; set; }

    public List<long> StepTimes { get; set; }
    /// <summary>Absolute number of recorded steps completed, excluding preparation and including
    /// steps before a resume. Zero means no recorded frame exists yet.</summary>
    public uint? CompletedSteps { get; set; }
    public uint TicksPerMillisecond { get; set; } = (uint)TimeSpan.TicksPerMillisecond;

    ///<summary>
    ///The leaf area index document - the measurement on the obstacles selected by
    ///LeafAreaIndexObstacles and the vegetation survey per tray - serialized through its own
    ///camelCase, rounded context and embedded whole, so it is the same document the run store
    ///holds. Budgeted: above the wire budget the per-obstacle series are dropped and the document
    ///says so. Null when nothing was requested: no filter and no imported tray system.
    ///</summary>
    public System.Text.Json.JsonElement? ObstacleLight { get; set; }

    ///<summary>
    ///Why the run stopped before its horizon - its storage volume reached the reserve, a frame could
    ///not be written, nobody was watching - as a clause that reads after "stopped: ". Null when it
    ///ran to completion. The run itself is resumable from its final checkpoint either way.
    ///</summary>
    public string? StopReason { get; set; }
}
