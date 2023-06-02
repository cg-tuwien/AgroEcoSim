#if !GODOT
using System.Text.Json.Serialization;
using Utils.Json;

namespace AgroServer.Models;
///<summary>
///Per plant response data
///</summary>
public class PlantResponse
{
    ///<summary>
    ///Volume of the plant in m³
    ///</summary>
    [JsonPropertyName("V")]
    public float Volume { get; set; }
}

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
}
#endif