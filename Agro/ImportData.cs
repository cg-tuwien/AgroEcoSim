using System.Numerics;
using System.Text.Json.Serialization;

namespace Agro;

public class PlantImportData
{
    [JsonConverter(typeof(Utils.Json.Vector3JsonConverter))]
    public Vector3? P { get; set; }
}

public class FieldImportData
{
    public uint? TicksPerHour { get; set; }
    public uint? TotalHours { get; set; }
    public Utils.Json.Vector3XDZ? FieldSize { get; set; }
    public float? FieldResolution { get; set; }

    public PlantImportData[]? Plants { get; set; }
}