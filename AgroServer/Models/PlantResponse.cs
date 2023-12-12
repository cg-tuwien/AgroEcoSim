using System.Text.Json.Serialization;

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