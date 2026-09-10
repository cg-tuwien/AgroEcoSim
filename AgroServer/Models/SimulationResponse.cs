namespace AgroServer.Models;

public class SimulationResponse
{
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

    public List<uint> StepTimes { get; set; }
    public uint TicksPerMillisecond { get; set; } = (uint)TimeSpan.TicksPerMillisecond;

    ///<summary>
    ///Per-obstacle leaf area index and solar load, one record per day and per horizontal band, for
    ///the obstacles selected by LeafAreaIndexObstacles. Null when none were requested or when no
    ///internal light model ran. Every record carries its own date, so no separate index is needed.
    ///</summary>
    public List<ObstacleLightResponse> ObstacleLight { get; set; }
    ///<summary>What the obstacle light measurement was, so an export of it stands on its own.</summary>
    public ObstacleLightMetaResponse ObstacleLightMeta { get; set; }
    ///<summary>The whole selection as one patch. Always present alongside ObstacleLight, and the
    ///series to plot when the per-obstacle ones were dropped to keep the payload sane.</summary>
    public ObstacleLightSelectionResponse ObstacleLightSelection { get; set; }
}