using System.Text.Json.Serialization;
using Agro;
using AgroServer.Services;
using System.Numerics;

namespace AgroServer.Models;

[JsonSourceGenerationOptions(Converters = new[] { typeof(Vector2JsonConverter), typeof(Vector3JsonConverter) })]
[JsonSerializable(typeof(Vector3))]
[JsonSerializable(typeof(Vector3[]))]
[JsonSerializable(typeof(Vector2))]
[JsonSerializable(typeof(Vector2[]))]
[JsonSerializable(typeof(Utils.Json.Vector3Data))]
[JsonSerializable(typeof(Utils.Json.Vector3XYZ))]
[JsonSerializable(typeof(Utils.Json.Vector3XDZ))]
[JsonSerializable(typeof(SimulationRequest))]
[JsonSerializable(typeof(PlantRequest))]
[JsonSerializable(typeof(ObstacleRequest))]
[JsonSerializable(typeof(SimulationResponse))]
[JsonSerializable(typeof(PreviewResponse))]
[JsonSerializable(typeof(PlantResponse))]
[JsonSerializable(typeof(ObstacleLightResponse))]
[JsonSerializable(typeof(List<SpeciesSettings>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(RunResponse))]
[JsonSerializable(typeof(RunSummaryResponse[]))]
// Hub arguments and completion results are resolved by their runtime type.
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(ushort))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(byte[]))]
internal partial class AppJsonSerializerContext : JsonSerializerContext { }
