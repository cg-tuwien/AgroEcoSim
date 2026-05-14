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
            var world = Initialize.World(request);
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

        api.MapPost("/terrain", (ImportedObjData data) => terrainBuffer.Add(data));

        api.MapPost("/seeds", (CellModel[][] data) => seedsBuffer.Add(data));

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