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
    ///The leaf area index document - the measurement on the obstacles selected by
    ///LeafAreaIndexObstacles and the vegetation survey per tray - serialized through its own
    ///camelCase, rounded context and embedded whole, so it is the same document the run store
    ///holds. Budgeted: above the wire budget the per-obstacle series are dropped and the document
    ///says so. Null when nothing was requested: no filter and no imported tray system.
    ///</summary>
    public System.Text.Json.JsonElement? ObstacleLight { get; set; }
}