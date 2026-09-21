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
[JsonSerializable(typeof(ObstacleLightResponse))]
[JsonSerializable(typeof(ObstacleLightMetaResponse))]
[JsonSerializable(typeof(ObstacleLightSelectionResponse))]
[JsonSerializable(typeof(ObstacleLightBandResponse))]
[JsonSerializable(typeof(ObstacleLightSurfaceResponse))]
[JsonSerializable(typeof(ObstacleLightDayResponse))]
[JsonSerializable(typeof(ObstacleLightSummaryResponse))]
[JsonSerializable(typeof(ObstacleLightDocumentResponse))]
[JsonSerializable(typeof(List<ObstacleLightResponse>))]
[JsonSerializable(typeof(List<SpeciesSettings>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(RunResponse))]
[JsonSerializable(typeof(RunResponse[]))]
[JsonSerializable(typeof(RunDeleteResponse))]
[JsonSerializable(typeof(StoredTerrainMeta))]
// Hub arguments and completion results are resolved by their runtime type.
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(ushort))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(byte[]))]
internal partial class AppJsonSerializerContext : JsonSerializerContext { }

/// <summary>
/// camelCase, for the leaf-area-index document written to the run store.
///
/// A context of its own rather than a naming policy on the one above, which would rename every
/// wire payload the frontend already reads. ASP.NET and SignalR camelCase their own output through
/// their serializer options; this document is written directly to a file, so nothing does it for
/// us - and it came out PascalCase, which parsed to an empty object in the browser without
/// erroring anywhere. The two must agree because the same reader opens both files.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
                             Converters = new[] { typeof(Vector2JsonConverter), typeof(Vector3JsonConverter) })]
[JsonSerializable(typeof(ObstacleLightDocumentResponse))]
internal partial class ObstacleLightJsonContext : JsonSerializerContext { }
