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
    public static void Map(RouteGroupBuilder api, IConfiguration configuration, ISimulationUploadService uploadService, ITerrainBuffer terrainBuffer, ISeedsBuffer seedsBuffer, IRunStore runStore)
    {
        api.MapGet("/", () => Results.Ok());

        //--- stored runs ----------------------------------------------------------------------
        //
        //REST rather than hub methods, deliberately. The SignalR JSON protocol base64-encodes
        //every byte[], which is a third more bytes for nothing; these return the frame as it is
        //stored. Frames are immutable once written, so the browser cache can hold the working
        //set and the client need not manage one.

        api.MapGet("/run/{runId}", (string runId) =>
        {
            var run = runStore.GetRun(runId);
            return run == null ? Results.NotFound() : Results.Ok(new RunResponse(
                run.Id, run.Status, run.CreatedUtc, run.HoursPerTick, run.TotalSteps,
                run.StoredFrames, run.Analytics, run.Deterministic,
                run.BytesOnDisk, run.ParentRunId, run.BranchStep));
        });

        //Eight bytes per stored frame: the simulated timestep, then the stored size. That is the
        //whole of what the client needs eagerly - the clock, the frame maps and the export panel
        //all read `t` and nothing else - so a 1,440-step run costs it about 11 KB instead of
        //gigabytes.
        api.MapGet("/run/{runId}/index", (string runId) =>
            runStore.TryReadIndex(runId, out var bytes)
                ? Results.Bytes(bytes, "application/octet-stream")
                : Results.NotFound());

        api.MapGet("/run/{runId}/frame/{step:int}", (HttpContext http, string runId, int step) =>
        {
            if (step < 0 || !runStore.TryReadFrameDeflated(runId, (uint)step, out var deflated))
                return Results.NotFound();

            //Stored deflated and served deflated: the browser inflates it natively, so neither
            //side spends anything on compression and the transfer is ~1.7x smaller.
            http.Response.Headers.ContentEncoding = "deflate";
            //Frames never change once written.
            http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.Bytes(deflated, "application/octet-stream");
        });

        api.MapGet("/runs", (int? limit) => Results.Ok(runStore.ListRuns(Math.Clamp(limit ?? 25, 1, 200))
            .Select(r => new RunSummaryResponse(r.Id, r.Status, r.CreatedUtc,
                                               r.StoredFrames, r.BytesOnDisk)).ToArray()));

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
