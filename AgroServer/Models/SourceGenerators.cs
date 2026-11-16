using System.Text.Json;
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
[JsonSerializable(typeof(RunSiteResponse))]
[JsonSerializable(typeof(PlantResponse))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonElement?))]
[JsonSerializable(typeof(List<SpeciesSettings>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(RunResponse))]
[JsonSerializable(typeof(RunResponse[]))]
[JsonSerializable(typeof(RunDeleteResponse))]
[JsonSerializable(typeof(RunProtectionRequest))]
[JsonSerializable(typeof(StoredTerrainMeta))]
[JsonSerializable(typeof(RunStoreStatusResponse))]
[JsonSerializable(typeof(RunStoreEvent))]
[JsonSerializable(typeof(RunStoreEvent[]))]
// Hub arguments and completion results are resolved by their runtime type.
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(ushort))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(byte[]))]
internal partial class AppJsonSerializerContext : JsonSerializerContext { }

/// <summary>
/// camelCase and rounded, for the leaf-area-index document - the file in the run store and the
/// element embedded in the socket result alike.
///
/// A context of its own rather than a naming policy or a float converter on the one above, which
/// would rename and round every wire payload the frontend already reads. ASP.NET and SignalR
/// camelCase their own output through their serializer options; this document is written directly
/// to a file, so nothing does it for us - and it once came out PascalCase, which parsed to an empty
/// object in the browser without erroring anywhere. The socket copy is serialized through this
/// context first and embedded as a JsonElement, so the two cannot differ.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
                             Converters = new[] { typeof(SignificantFloatConverter) })]
[JsonSerializable(typeof(ObstacleLightDocument))]
internal partial class ObstacleLightJsonContext : JsonSerializerContext { }
