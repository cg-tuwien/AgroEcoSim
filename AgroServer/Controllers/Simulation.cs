using AgroServer.Models;
using Agro;
using System.Diagnostics;
using AgroServer.Services;
using System.Diagnostics.CodeAnalysis;
using FacadeJsonImport;

namespace AgroServer.Controllers;

public class SimulationController// : ControllerBase
{
    /// <summary>
    /// Memoised answers for <c>GET /run/{id}/site</c>. A null value is a run that cannot be
    /// described, remembered so a bad id is not rebuilt on every poll.
    /// </summary>
    /// <remarks>
    /// The answer is a pure function of a run's stored request, and a run's request never
    /// changes, so this can never go stale. Bounded by clearing wholesale rather than by an LRU:
    /// entries are a few kilobytes, the bound only exists so a long-lived server cannot grow
    /// without limit, and rebuilding a handful of them costs less than the bookkeeping would.
    /// </remarks>
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, RunSiteResponse?> SiteCache = new();
    const int SiteCacheLimit = 256;

    static void RememberSite(string runId, RunSiteResponse? site)
    {
        if (SiteCache.Count >= SiteCacheLimit) SiteCache.Clear();
        SiteCache[runId] = site;
    }

    [RequiresUnreferencedCode("SimulationController")]
    public static void Map(RouteGroupBuilder api, IConfiguration configuration, ISimulationUploadService uploadService, ITerrainBuffer terrainBuffer, ISeedsBuffer seedsBuffer, IRunStore runStore)
    {
        api.MapGet("/", () => Results.Ok());

        //--- stored runs ----------------------------------------------------------------------
        //
        //REST rather than hub methods, deliberately. The SignalR JSON protocol base64-encodes
        //every byte[], which is a third more bytes for nothing; these return the frame as it is
        //stored. Cached frames must revalidate because checkpoint resume can replace a tail.

        api.MapGet("/run/{runId}", (string runId) =>
        {
            var run = runStore.GetRun(runId);
            return run == null ? Results.NotFound() : Results.Ok(Describe(runStore, run));
        });

        //Eight bytes per stored frame: the simulated timestep, then the stored size. That is the
        //whole of what the client needs eagerly - the clock, the frame maps and the export panel
        //all read `t` and nothing else - so a 1,440-step run costs it about 11 KB instead of
        //gigabytes.
        api.MapGet("/run/{runId}/index", (HttpContext http, string runId) =>
        {
            if (!runStore.TryReadIndexSnapshot(runId, out var bytes, out var revision, out var restart)) return Results.NotFound();
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers["X-Agro-Frame-Revision"] = revision.ToString();
            http.Response.Headers["X-Agro-Restart-Step"] = restart.ToString();
            http.Response.Headers.AccessControlExposeHeaders = "X-Agro-Frame-Revision, X-Agro-Restart-Step";
            return Results.Bytes(bytes, "application/octet-stream");
        });

        api.MapGet("/run/{runId}/frame/{step:int}", (HttpContext http, string runId, int step) =>
        {
            if (step < 0 || !runStore.TryReadFrameDeflated(runId, (uint)step, out var deflated))
                return Results.NotFound();

            //Stored deflated and served deflated: the browser inflates it natively, so neither
            //side spends anything on compression and the transfer is ~1.7x smaller.
            http.Response.Headers.ContentEncoding = "deflate";
            // Resume may replace a tail newer than its checkpoint.
            http.Response.Headers.CacheControl = "no-cache";
            return Results.Bytes(deflated, "application/octet-stream");
        });

        // Archive download needs the stored zlib bytes themselves, without HTTP transparently
        // inflating them. At most one frame is buffered by either endpoint.
        api.MapGet("/run/{runId}/frame/{step:int}/packed", (HttpContext http, string runId, int step) =>
        {
            if (step < 0 || !runStore.TryReadFrameDeflated(runId, (uint)step, out var bytes))
                return Results.NotFound();
            http.Response.Headers.CacheControl = "no-store";
            return Results.Bytes(bytes, "application/octet-stream");
        });

        //The complete leaf-area-index measurement, with every per-obstacle daily series present.
        //
        //The copy that travelled over the socket has them dropped above DailyRecordBudget, because
        //a browser cannot hold a hundred megabytes of JSON delivered as one message. Here it is a
        //file to save: served gzipped, never parsed by the page, and as large as the study needs.
        api.MapGet("/run/{runId}/obstaclelight", (HttpContext http, string runId) =>
        {
            if (!runStore.TryReadObstacleLight(runId, out var gzipped))
                return Results.NotFound();
            http.Response.Headers.ContentEncoding = "gzip";
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers.ContentDisposition = $"attachment; filename=\"leafareaindex-{runId}.json\"";
            return Results.Bytes(gzipped, "application/json");
        });

        api.MapGet("/run/{runId}/obstaclelight/packed", (HttpContext http, string runId) =>
        {
            if (!runStore.TryReadObstacleLight(runId, out var bytes)) return Results.NotFound();
            http.Response.Headers.CacheControl = "no-store";
            return Results.Bytes(bytes, "application/gzip");
        });

        //The complete record per run, not a summary. What the panel has to show - length, plants,
        //trays, obstacles, size, lineage, and whether the run can be re-run or drawn - is a few
        //hundred bytes; fetching each run separately to find it out would be a request per row.
        api.MapGet("/runs", (int? limit) => Results.Ok(runStore.ListRuns(Math.Clamp(limit ?? 50, 1, 500))
            .Select(r => Describe(runStore, r)).ToArray()));

        //The request a run was started from, so the client can fork or continue it.
        //
        //Served as stored rather than re-serialised: it is what the run actually ran with, down to
        //the explicit nulls, and re-encoding it through a second serializer is one more place for
        //the two shapes to drift apart.
        api.MapGet("/run/{runId}/request", (string runId) =>
            runStore.TryReadRequest(runId, out var bytes)
                ? Results.Bytes(bytes, "application/json")
                : Results.NotFound());

        //Where a stored run is and what weather it ran under, for reopening or forking one.
        //
        //RECOMPUTED from the stored request rather than read from a file written at run time.
        //Both halves are pure functions of the request - the zone is derived from the
        //coordinates and the weather table is planned from the seed - so a file would be a
        //second source of truth that could disagree with the run's own scenario, plus a format
        //to version.
        //
        //Memoised per run because building the world is NOT free, whatever the scene size
        //suggests: Init() walks every timestep of the run to build the daylight bitmap, and
        //every AgroWorld also constructs an IrradianceClient, which allocates an HttpClient that
        //nothing disposes. One per distinct run is a bounded cost; one per request, for a panel
        //that refreshes, is not. The answer never changes for a run id, so caching it changes
        //nothing a caller can observe.
        api.MapGet("/run/{runId}/site", (string runId) =>
        {
            if (SiteCache.TryGetValue(runId, out var cached))
                return cached == null ? Results.NotFound() : Results.Json(cached);

            if (!runStore.TryReadRequest(runId, out var bytes)) return Results.NotFound();
            SimulationRequest? request;
            try { request = System.Text.Json.JsonSerializer.Deserialize(bytes, AppJsonSerializerContext.Default.SimulationRequest); }
            catch (System.Text.Json.JsonException) { return Results.NotFound(); }
            if (request == null) return Results.NotFound();

            AgroWorld world;
            //A stored request can carry coordinates this build rejects - a run that cannot be
            //described, not a server error. Cached as a negative so a bad run is not rebuilt on
            //every poll either.
            try { world = new AgroWorld(request); }
            catch (ArgumentOutOfRangeException) { RememberSite(runId, null); return Results.NotFound(); }

            var coverage = world.SkyCoverageSeries();
            var raw = new byte[coverage.Length * sizeof(float)];
            Buffer.BlockCopy(coverage, 0, raw, 0, raw.Length);
            var site = new RunSiteResponse
            {
                Latitude = world.Latitude,
                Longitude = world.Longitude,
                Altitude = world.Altitude,
                AzimuthBase = world.AzimuthBase,
                AzimuthOffset = world.AzimuthOffset,
                TimeZone = world.TimeZone.Id,
                SkyCoverage = raw,
            };
            RememberSite(runId, site);
            return Results.Json(site);
        });

        //The soil as the socket delivers it at upload time, so a stored run can be DRAWN and not
        //just replayed. Without it a past run opens as plants floating in nothing: trays and
        //facade never travel in a frame.
        //
        //Rebuilt from the stored model when the cached copy is absent, which is every run stored
        //before terrains were kept. The model outlives the cache - it is content-addressed and
        //shared - so a run from before this existed is still drawable as long as its OBJ was
        //uploaded once since. The rebuild is seconds and a few hundred megabytes, so the result is
        //written back: the cost is paid by the first person to open that run and by nobody after.
        api.MapGet("/run/{runId}/terrain", (HttpContext http, string runId) =>
        {
            if (!runStore.TryReadTerrain(runId, out var gzipped)
                && !TryRebuildTerrain(runStore, terrainBuffer, runId, out gzipped))
                return Results.NotFound();
            http.Response.Headers.ContentEncoding = "gzip";
            //NOT immutable: a rebuilt terrain is served before it is cached, and a run whose model
            //arrives later would otherwise be pinned as "no trays" in the browser for a year.
            http.Response.Headers.CacheControl = "public, max-age=300";
            return Results.Bytes(gzipped, "application/octet-stream");
        });

        //Deleting is the only way a user can get bytes back on their own terms - eviction is
        //oldest-first and cares nothing for which run mattered. A refusal answers 409 WITH its
        //reason: "this run is still computing" is something the user can act on, and a bare status
        //code reads as a bug.
        api.MapDelete("/run/{runId}", (string runId) =>
        {
            var outcome = runStore.DeleteRun(runId, out var reason);
            return outcome switch
            {
                DeleteOutcome.Deleted => Results.Ok(new RunDeleteResponse(true, "")),
                DeleteOutcome.NoSuchRun => Results.Json(new RunDeleteResponse(false, reason),
                                                        statusCode: StatusCodes.Status404NotFound),
                _ => Results.Json(new RunDeleteResponse(false, reason),
                                  statusCode: StatusCodes.Status409Conflict),
            };
        });

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
            response.ObstacleLight = ObstacleLightResponse.From(world.ObstacleLight, out var omitted);
            //Reported whenever a measurement was asked for, even when the filter matched nothing:
            //without it the client cannot tell a mistyped filter from a run that never measured.
            if (world.ObstacleLightSelection.Filter != null)
                response.ObstacleLightMeta = ObstacleLightMetaResponse.From(world, world.ObstacleLight, world.ObstacleList);
            if (response.ObstacleLightMeta != null)
                response.ObstacleLightMeta.DailySeriesOmitted = omitted;
            if (world.ObstacleLight != null)
                response.ObstacleLightSelection = ObstacleLightSelectionResponse.From(world.ObstacleLight);

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
            //Filed here, at the upload, because this is the last moment the OBJ source exists. The
            //parse is not reversible and the buffer entry is single-use and five minutes old at
            //most, so a run that wants to be continued or forked later has to capture its model
            //now or never. Content-addressed, so uploading the same facade for the tenth run costs
            //nothing, and collected again when no run refers to it.
            var modelHash = runStore.StoreFieldModel(data);
            return Results.Text(terrainBuffer.Add(data, CoordinateSystem.RH_rightPX_upPZ, modelHash));
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

        //A long blade is simulated as a chain of leaf organs. Drawing each of them with the whole
        //leaf outline pinches the blade to a point at every joint, so each segment gets the slice
        //of the outline that belongs to its own stretch. An unsegmented species returns one slice,
        //identical to what /mesh/leaf serves.
        api.MapGet("/mesh/leaf/{species}/slices", (string species) => LaminaSlices(species));
    }

    /// <summary>
    /// One stored run on the wire.
    ///
    /// The three "Has" flags are what let the panel offer only the actions that will work: a run
    /// whose field model has been collected cannot be continued without the user uploading the OBJ
    /// again, and one stored before terrains were kept cannot be drawn with its trays. Offering
    /// those buttons anyway and failing afterwards is worse than not offering them.
    /// </summary>
    static RunResponse Describe(IRunStore store, RunRecord run) => new(
        run.Id, run.Status, run.CreatedUtc, run.UpdatedUtc, run.HoursPerTick, run.TotalSteps,
        run.StoredFrames, run.LastStep, run.TotalHours, run.InitialDay, run.Seed, run.RenderMode,
        run.Plants, run.Trays, run.Obstacles, run.Species, run.Analytics, run.Deterministic,
        run.BytesOnDisk, run.AuxBytesOnDisk, run.FieldModelName,
        //"Can be drawn with its trays", which is what the panel decides with - a cached terrain, or
        //a stored model the endpoint can rebuild one from.
        store.HasFieldModel(run.FieldModelHash),
        store.HasTerrain(run.Id) || store.HasFieldModel(run.FieldModelHash),
        store.HasObstacleLight(run.Id), store.HasCheckpoint(run.Id), run.ParentRunId, run.BranchStep);

    /// <summary>
    /// Builds a stored run's soil from the field model filed with it, and keeps the result.
    ///
    /// The one path by which a run stored before terrains were cached becomes viewable again. It
    /// needs the same coordinate system and the same build parameters the run used - the store
    /// records them beside the run precisely because the request does not carry them.
    /// </summary>
    static bool TryRebuildTerrain(IRunStore store, ITerrainBuffer buffer, string runId, out byte[] gzipped)
    {
        gzipped = [];
        var run = store.GetRun(runId);
        if (run?.FieldModelHash == null) return false;
        if (!store.TryReadFieldModel(run.FieldModelHash, out var objSource)) return false;
        try
        {
            var key = buffer.Add(objSource, CoordinateSystem.RH_rightPX_upPZ, run.FieldModelHash);
            if (!buffer.TryGetV2(key, out var objData)) return false;

            var soil = new SoilFormationsList(null, objData, 1f, run.FieldItemRegex,
                                              run.FieldItemRegexMaterial, run.FieldResolution, false);
            store.WriteTerrain(runId, soil.Serialize());
            Console.WriteLine($"Rebuilt and cached the terrain of run {runId} from stored field model {run.FieldModelHash[..12]}.");
            return store.TryReadTerrain(runId, out gzipped);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Could not rebuild the terrain of run {runId}: {e.Message}");
            return false;
        }
    }

    static IResult LaminaSlices(string species)
    {
        var data = SpeciesSettings.Predefined.FirstOrDefault(x => x.Name == species);
        if (data?.LeafMorphology == null) return Results.NotFound();

        var whole = BuildLamina(data.LeafMorphology);
        var count = Math.Max(1, data.LeafSegmentCount);

        using var binaryStream = new MemoryStream();
        using var writer = new BinaryWriter(binaryStream);
        writer.Write((byte)count);
        for (int i = 0; i < count; ++i)
            LaminaMeshSlicer.Slice(whole, i, count).WriteMesh(writer);
        return Results.Bytes(binaryStream.ToArray());
    }

    static MeshData BuildLamina(LeafMorphology morphology)
        => LaminaTemplates.Build(morphology);

    static IResult LaminaMesh(string species, bool petal)
    {
        var data = SpeciesSettings.Predefined.FirstOrDefault(x => x.Name == species);
        if (data == null) return Results.NotFound();

        var morphology = petal ? data.FlowerSettings.PetalMorphology : data.LeafMorphology;
        if (morphology == null) return Results.NotFound();

        var mesh = BuildLamina(morphology);

        using var binaryStream = new MemoryStream();
        using var writer = new BinaryWriter(binaryStream);
        mesh.WriteMesh(writer);
        return Results.Bytes(binaryStream.ToArray());
    }

}
