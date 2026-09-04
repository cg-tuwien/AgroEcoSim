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
            response.ObstacleLight = ObstacleLightResponse.From(world.ObstacleLight);
            if (response.ObstacleLight != null)
                response.ObstacleLightTimesteps = [.. world.ObstacleLight.Timesteps];

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
            return Results.Text(terrainBuffer.Add(data, CoordinateSystem.RH_rightPX_upPZ));
        });

        //probably not needed and not used
        //api.MapPost("/seeds", (TrayModel[] data) => seedsBuffer.Add(data));

        //Returns a listing of all predefined species
        api.MapGet("/species", () => SpeciesSettings.Predefined);

        //Returns a listing of all predefined behaviors
        api.MapGet("/behaviors", () => Enum.GetNames<Behavior>());

        api.MapGet("/mesh/leaf/{species}", (string species) => LaminaMesh(species, petal: false));

        //A petal is a lamina and travels in the same record as a leaf, so without an
        //outline of its own the viewport drew it with the species' LEAF mesh - a maple
        //blade for a three-millimetre Heuchera petal.
        api.MapGet("/mesh/petal/{species}", (string species) => LaminaMesh(species, petal: true));
    }

    static IResult LaminaMesh(string species, bool petal)
    {
        var data = SpeciesSettings.Predefined.FirstOrDefault(x => x.Name == species);
        if (data == null) return Results.NotFound();

        var morphology = petal ? data.FlowerSettings.PetalMorphology : data.LeafMorphology;
        if (morphology == null) return Results.NotFound();

        var lod = RenderLeafLod.CoarseOutline;
        if (!morphology.TryGetDefaultLeafMesh(lod, out var mesh))
        {
            var generic = new LeafInstance()
            {
                Morphology = morphology,
                Physiology = new()
            };
            generic.InitializeLeafletsIfNeeded();

            mesh = LeafMeshBuilder.Build(generic, lod);
        }

        using var binaryStream = new MemoryStream();
        using var writer = new BinaryWriter(binaryStream);
        mesh.WriteMesh(writer);
        return Results.Bytes(binaryStream.ToArray());
    }

}