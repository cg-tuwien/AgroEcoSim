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
}