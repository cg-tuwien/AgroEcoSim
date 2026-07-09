using AgroServer.Models;
using Agro;
using System.Diagnostics;
using AgroServer.Services;
using System.Diagnostics.CodeAnalysis;
using FacadeJsonImport;

namespace AgroServer.Controllers;

public class SimulationController// : ControllerBase
{
    [RequiresUnreferencedCode("SimulationController")]
    public static void Map(RouteGroupBuilder api, IConfiguration configuration, ISimulationUploadService uploadService, ITerrainBuffer terrainBuffer, ISeedsBuffer seedsBuffer)
    {
        api.MapGet("/", () => Results.Ok());

        api.MapPost("/", (SimulationRequest request) =>
        {
            var world = !string.IsNullOrWhiteSpace(request.FieldModelKey) && terrainBuffer.TryGetV2(request.FieldModelKey, out var terrain)
                ? Initialize.World(request, terrain)
                : Initialize.World(request);

            world.Irradiance.SetAddress(configuration["RendererIPMitsuba"], configuration["RendererPortMitsuba"], configuration["RendererIPTamashii"], configuration["RendererPortTamashii"], request?.RenderMode ?? 0);

            var start = DateTime.UtcNow.Ticks;
            world.Run((uint)world.TimestepsTotal());
            var stop = DateTime.UtcNow.Ticks;
            Debug.WriteLine($"Simulation time: {(stop - start) / TimeSpan.TicksPerMillisecond} ms");

            var response = new SimulationResponse() { Plants = new(world.Count) };
            world.ForEach(formation =>
            {
                if (formation is PlantFormation2 plant)
                    //plantData.Add(@$"{{""P"":{JsonSerializer.Serialize(new Vector3Data(plant.Position))},""V"":{plant.AG.GetVolume()}}}");
                    response.Plants.Add(new() { Volume = plant.AG.GetVolume() });
            });

            Debug.WriteLine($"RENDER TIME: {world.Irradiance.ElapsedMilliseconds} ms");

            if (request?.RequestGeometry ?? false)
            {
                var exportFlags = (request.DownloadRoots ?? false ? ExportFlags.Roots : ExportFlags.None) | ExportFlags.Analytics;
                response.Scene = world.ExportToStream((byte)exportFlags);
            }

            response.Renderer = world.RendererName;

            return response;
        });

        api.MapPost("/upload", (SimulationRequest request) =>
        {
            //TODO Validate the regex
            return uploadService.Add(request);
        });

        //api.MapPost("/terrain", (ImportedObjData data) => terrainBuffer.Add(data));
        //api.MapPost("/terrain", (string data) => terrainBuffer.Add(data));
        api.MapPost("/terrain", async (HttpContext context) =>
        {
            var maxBodySizeFeature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (!maxBodySizeFeature.IsReadOnly) maxBodySizeFeature.MaxRequestBodySize = 256L * 1024L * 1024L;

            using var reader = new StreamReader(context.Request.Body);
            var data = await reader.ReadToEndAsync();
            return Results.Text(terrainBuffer.Add(data));
        });

        api.MapPost("/seeds", (TrayModel[] data) => seedsBuffer.Add(data));

        //Returns a listing of all predefined species
        api.MapGet("/species", () => SpeciesSettings.Predefined);

        //Returns a listing of all predefined behaviors
        api.MapGet("/behaviors", () => Enum.GetNames<Behavior>());

        api.MapGet("/mesh/leaf/{species}", (string species) => {
            var data = SpeciesSettings.Predefined.FirstOrDefault(x => x.Name == species);
            if (data == null) return Results.NotFound();

            var lod = RenderLeafLod.CoarseOutline;
            if (!data.LeafMorphology.TryGetDefaultLeafMesh(lod, out var mesh))
            {
                var genericLeaf = new LeafInstance()
                {
                    Morphology = data.LeafMorphology,
                    Physiology = new()
                };
                genericLeaf.InitializeLeafletsIfNeeded();

                mesh = LeafMeshBuilder.Build(genericLeaf, lod);
            }

            using var binaryStream = new MemoryStream();
            using var writer = new BinaryWriter(binaryStream);
            mesh.WriteMesh(writer);
            return Results.Bytes(binaryStream.ToArray());
        });
    }
}