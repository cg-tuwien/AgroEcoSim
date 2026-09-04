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
    ///Simulation steps for which the obstacle light series carry a sample. Only present when the
    ///request selected obstacles with LeafAreaIndexObstacles and the internal light model ran.
    ///</summary>
    public uint[] ObstacleLightTimesteps { get; set; }
    ///<summary>
    ///Per-obstacle leaf area index and solar load series, aligned with
    ///<see cref="ObstacleLightTimesteps"/>.
    ///</summary>
    public List<ObstacleLightResponse> ObstacleLight { get; set; }
}