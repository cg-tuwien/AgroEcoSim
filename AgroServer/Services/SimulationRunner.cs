using System.Collections.Concurrent;
using System.Diagnostics;
using Agro;
using AgroServer.Hubs;
using AgroServer.Models;
using Microsoft.AspNetCore.SignalR;

namespace AgroServer.Services;

/// <summary>
/// Owns running simulations, independently of the connection that asked for one.
///
/// The loop used to live inside the <c>Run</c> hub invocation. Three things followed from that,
/// all of them bad: it held one of the three parallel invocation slots for the entire run while
/// <c>Preview</c> and <c>Abort</c> competed for the other two; a client that reloaded or dropped
/// its socket orphaned the run, which kept ticking with nowhere to send frames and never removed
/// its entry, so that connection id was permanently "already running"; and there was no way for
/// anything to outlive the connection, which is what crash-resume needs.
///
/// Frames now go to a SignalR group named by the run id. A reconnecting client rejoins the group
/// and picks up where it left off, and the run id is the durable handle - the same one the store
/// files frames under.
/// </summary>
public interface ISimulationRunner
{
    /// <summary>
    /// Creates a run and returns its id WITHOUT starting the loop.
    ///
    /// Split from <see cref="Begin"/> so the caller can join the run's SignalR group first.
    /// Starting the loop here would race it: frames pushed before the connection is in the group
    /// go nowhere, and there is no way to notice that other than a missing first progress tick.
    /// </summary>
    string Create(SimulationRequest request, Hubs.ClientTerrain? terrain, string connectionId);

    /// <summary>
    /// Restarts a stored run from its checkpoint, appending to the SAME run.
    ///
    /// Returns the run id on success. The request supplies the new total; everything else comes
    /// from the run itself, because a resumed run is the same simulation and changing its scenario
    /// half way through would make its frames describe two different experiments.
    /// </summary>
    string? Resume(string runId, int totalHours, Hubs.ClientTerrain? terrain, string connectionId, out string reason);

    /// <summary>Starts the loop for a run created by <see cref="Create"/>.</summary>
    void Begin(string runId);

    /// <summary>
    /// Where a created-but-not-yet-begun run is, and the weather it will see. Null if unknown.
    ///
    /// Captured at Create/Resume because that is the only moment the built world is in hand, and
    /// read by the hub after it has joined the caller's group - the same create-join-begin order
    /// everything else on this path follows.
    /// </summary>
    Models.RunSiteResponse? SiteOf(string runId);

    /// <summary>
    /// Discards a run created by <see cref="Create"/> that will never be begun.
    ///
    /// Create and Begin are separate so the caller can join the run's group in between, which
    /// means the caller can also fail in between - a client that drops during the join leaves a
    /// run that exists everywhere and runs nowhere. Its record stays "running" in the store,
    /// where nothing can evict it and its bytes count against the retention budget until the
    /// process restarts.
    /// </summary>
    void Cancel(string runId);

    /// <summary>Arms a preview for the run this connection owns. False when it has none.</summary>
    bool RequestPreview(string connectionId);

    bool Abort(string connectionId);

    /// <summary>The run a connection owns, or null.</summary>
    string? RunIdForConnection(string connectionId);

    /// <summary>Re-points a run at a reconnecting client. False when the run is gone.</summary>
    bool Attach(string runId, string connectionId);

    void DetachConnection(string connectionId);

    bool HasRun(string connectionId);
}

class RunState
{
    public required string RunId { get; init; }
    /// <summary>The step a resumed run picks up at. Zero for a run that starts from nothing.</summary>
    public uint StartStep { get; init; }
    public volatile bool Preview;
    public volatile bool Abort;
    public List<uint> StepTimes { get; } = new(1000);
    /// <summary>The connection currently watching. Empty while nobody is attached.</summary>
    public volatile string ConnectionId = "";
    /// <summary>When the last client detached, or null while one is attached.</summary>
    public DateTime? UnwatchedSince;
    /// <summary>The site and weather, captured while the built world was in hand.</summary>
    public Models.RunSiteResponse? Site;
}

public class SimulationRunner : ISimulationRunner
{
    readonly IHubContext<SimulationHub> Hub;
    readonly IRunStore Store;
    readonly IConfiguration Config;

    readonly ConcurrentDictionary<string, RunState> Runs = new();
    readonly ConcurrentDictionary<string, string> ConnectionToRun = new();
    /// <summary>Runs created but not yet begun, so the caller can join their group first.</summary>
    readonly ConcurrentDictionary<string, Func<Task>> Pending = new();

    public SimulationRunner(IHubContext<SimulationHub> hub, IRunStore store, IConfiguration config)
    {
        Hub = hub;
        Store = store;
        Config = config;
    }

    public bool HasRun(string connectionId) => ConnectionToRun.ContainsKey(connectionId);

    public string? RunIdForConnection(string connectionId) =>
        ConnectionToRun.TryGetValue(connectionId, out var id) ? id : null;

    /// <summary>
    /// How long a run may keep computing with nobody watching before it is stopped.
    ///
    /// Long enough for a page reload or a flaky network to reattach, short enough that a closed
    /// tab does not leave a simulation running to completion. Without this a reload orphaned the
    /// run permanently: the loop only exits on Abort or completion, and Abort is reachable only
    /// through ConnectionToRun, which the disconnect had just cleared - so nothing on the server
    /// or the wire could ever stop it, and every reload added another.
    /// </summary>
    public TimeSpan UnwatchedGrace { get; set; } = TimeSpan.FromMinutes(5);

    public bool Attach(string runId, string connectionId)
    {
        if (!Runs.TryGetValue(runId, out var run)) return false;
        run.ConnectionId = connectionId;
        run.UnwatchedSince = null;
        ConnectionToRun[connectionId] = runId;
        return true;
    }

    public void DetachConnection(string connectionId)
    {
        //The run keeps going. Only the association is dropped, so the connection id is free again
        //and a reconnect can Attach to the same run rather than being told one is already running.
        if (ConnectionToRun.TryRemove(connectionId, out var runId)
            && Runs.TryGetValue(runId, out var run)
            && run.ConnectionId == connectionId)
        {
            run.ConnectionId = "";
            run.UnwatchedSince = DateTime.UtcNow;
        }
    }

    public bool RequestPreview(string connectionId)
    {
        if (RunIdForConnection(connectionId) is string id && Runs.TryGetValue(id, out var run))
        {
            run.Preview = true;
            return true;
        }
        return false;
    }

    public bool Abort(string connectionId)
    {
        if (RunIdForConnection(connectionId) is string id && Runs.TryGetValue(id, out var run))
        {
            run.Abort = true;
            return true;
        }
        return false;
    }

    public string Create(SimulationRequest request, Hubs.ClientTerrain? terrain, string connectionId)
    {
        var lazyPreviews = !(request.ExactPreview ?? false);
        var exportFlags = (request.DownloadRoots ?? false ? ExportFlags.Roots : ExportFlags.None) | ExportFlags.Analytics;

        var world = Initialize.World(request, terrain?.Terrain);
        world.Deterministic = request.Deterministic ?? false;
        // Persist the resolved numerical tolerance so a later replay does not inherit
        // a different default from the server version installed at that time.
        request = request with { CollisionGeometryToleranceM = world.CollisionGeometryToleranceM };

        //Counted off the BUILT WORLD, not off the request, because for an imported facade the two
        //are not the same measurement. The request carries a single 'mesh' obstacle standing for
        //the whole model - SoilFormationsList merges them before they reach the browser - while
        //the world holds one per named OBJ group, which is the number that means anything. Trays
        //are not in the request at all.
        var plants = 0;
        world.ForEach(formation => { if (formation is PlantFormation2) ++plants; });

        var runId = Store.BeginRun(new RunSpec
        {
            RequestJson = System.Text.Json.JsonSerializer.Serialize(request, AppJsonSerializerContext.Default.SimulationRequest),
            HoursPerTick = (ushort)(request.HoursPerTick ?? 1),
            TotalSteps = (uint)world.TimestepsTotal(),
            Analytics = (exportFlags & ExportFlags.Analytics) != 0,
            Deterministic = world.Deterministic,
            TotalHours = request.TotalHours ?? 0,
            InitialDay = (request.InitialDate ?? default).ToString("yyyy-MM-dd"),
            Seed = request.Seed ?? 0,
            RenderMode = request.RenderMode ?? 0,
            Plants = (uint)plants,
            Trays = (uint)world.SoilFieldsCount,
            Obstacles = (uint)world.ObstacleList.Count,
            Species = (uint)(request.Species?.Length ?? 0),
            //Recorded only when a model was ACTUALLY used. The request carries FieldModelPath
            //whenever the editor has a file name on screen, which outlives the model itself: a run
            //forked from one whose OBJ is gone still names it, and so does any run started after an
            //upload failed. Taking the name from the request alone produced rows claiming a model
            //while running on the default box field - see the `terrain != null` guard on
            //WriteTerrain below, which already knows there was no imported field.
            FieldModelName = terrain != null && !string.IsNullOrEmpty(request.FieldModelPath) ? request.FieldModelPath : null,
            FieldModelHash = terrain?.ModelHash,
            FieldItemRegex = terrain?.Pattern ?? request.FieldItemRegex,
            FieldItemRegexMaterial = terrain?.PatternForMaterial ?? (request.FieldItemRegexMaterial ?? false),
            //The resolution the TRAYS were built with, which is the one that was on screen when
            //the model was uploaded - not necessarily the one in the request by the time the run
            //started. Rebuilding with the wrong one gives a different set of trays.
            FieldResolution = terrain?.Resolution ?? (request.FieldResolution ?? 0),
            ParentRunId = string.IsNullOrEmpty(request.ParentRunId) ? null : request.ParentRunId,
        });

        //The soil exactly as the socket sends it to a viewport, so this run can be opened and
        //drawn months from now. Written once, here, rather than with every frame.
        //
        //Only for an IMPORTED field. A plain box field is already drawn by the viewport from the
        //configured field size when it has no terrain of its own - that is what a live run shows -
        //so storing one would hand the same box back through a second path whose position and
        //extent are derived differently, and a reopened run would sit in a subtly different box
        //from the one it ran in. Nothing is lost by not storing it: it is not data, it is a shape
        //the client can already draw from numbers it has.
        //
        //Wrapped, because SerializeTerrain is evaluated HERE and not inside WriteTerrain's own
        //try: it materialises the whole merged obstacle mesh and then doubles it in ToArray(), so
        //on a large facade in a memory-capped container it is a plausible place to throw. A throw
        //escaping this point is the worst shape of failure available - BeginRun has already
        //inserted a "running" row and started the frame writer, while Runs/Pending/ConnectionToRun
        //are still empty, so nothing could abort it, delete it or evict it for the life of the
        //process. Losing the ability to redraw this run later is a far smaller loss.
        if (terrain != null)
        {
            try { Store.WriteTerrain(runId, world.SerializeTerrain()); }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Run {runId}: could not store the terrain ({e.Message}). " +
                                        $"The run proceeds; it will replay without its trays.");
            }
        }

        var run = new RunState { RunId = runId, ConnectionId = connectionId, Site = SiteOf(world) };
        Pending[runId] = () => Loop(run, world, lazyPreviews, exportFlags, request);
        Runs[runId] = run;
        ConnectionToRun[connectionId] = runId;
        return runId;
    }

    /// <summary>
    /// Serialises resumes against each other.
    ///
    /// Building the world from a checkpoint takes seconds, and the liveness check has to still be
    /// true when the run is finally registered. Without this, two tabs resuming the same run both
    /// pass the check, both build a world, and the second RunState replaces the first - after which
    /// nothing can abort the first loop and two writers share one index file.
    /// </summary>
    readonly object ResumeLock = new();

    public string? Resume(string runId, int totalHours, Hubs.ClientTerrain? terrain, string connectionId, out string reason)
    {
        lock (ResumeLock) return ResumeUnderLock(runId, totalHours, terrain, connectionId, out reason);
    }

    string? ResumeUnderLock(string runId, int totalHours, Hubs.ClientTerrain? terrain, string connectionId, out string reason)
    {
        reason = "";
        if (Runs.ContainsKey(runId)) { reason = "That run is already computing."; return null; }

        var stored = Store.GetRun(runId);
        if (stored == null) { reason = "No such run."; return null; }
        if (!Store.TryReadCheckpoint(runId, out var step, out var state))
        {
            reason = "That run has no checkpoint, so it cannot be resumed. Fork it to recompute it instead.";
            return null;
        }

        if (!Store.TryReadRequest(runId, out var requestJson)) { reason = "That run's request is missing."; return null; }
        var request = System.Text.Json.JsonSerializer.Deserialize(requestJson, AppJsonSerializerContext.Default.SimulationRequest);
        if (request == null) { reason = "That run's request could not be read."; return null; }

        //The ONLY thing the caller may change. The rest of the scenario is the run's own, because
        //a world restored from a checkpoint was grown under those settings and cannot be
        //retrospectively given different ones.
        var hoursPerTick = request.HoursPerTick ?? 1;
        var extended = request with { TotalHours = Math.Max(totalHours, (int)(step * hoursPerTick)) };

        var lazyPreviews = !(extended.ExactPreview ?? false);
        var exportFlags = (extended.DownloadRoots ?? false ? ExportFlags.Roots : ExportFlags.None) | ExportFlags.Analytics;

        var world = Initialize.World(extended, terrain?.Terrain);
        world.Deterministic = extended.Deterministic ?? false;
        try { world.RestoreState(state); }
        catch (Exception e)
        {
            //A checkpoint that does not fit the world the request builds is refused rather than
            //patched: the alternative is a run that continues as a different simulation.
            reason = $"That checkpoint does not fit this scenario: {e.Message}";
            return null;
        }

        if (world.Timestep != step)
        {
            reason = $"The checkpoint says step {step} but the restored world is at {world.Timestep}.";
            return null;
        }

        var totalSteps = (uint)world.TimestepsTotal();
        if (totalSteps <= step)
        {
            reason = $"That run already reached step {step} of {totalSteps}; give it more hours to carry it further.";
            return null;
        }

        //The store has the last word on liveness: it knows about a writer that is still draining,
        //which this class does not.
        if (!Store.ResumeRun(runId, step, totalSteps, extended.TotalHours ?? 0,
                System.Text.Json.JsonSerializer.Serialize(extended, AppJsonSerializerContext.Default.SimulationRequest)))
        {
            reason = "That run is still being written. Wait for it to settle and try again.";
            return null;
        }

        var run = new RunState { RunId = runId, ConnectionId = connectionId, StartStep = step, Site = SiteOf(world) };
        Pending[runId] = () => Loop(run, world, lazyPreviews, exportFlags, extended);
        Runs[runId] = run;
        ConnectionToRun[connectionId] = runId;
        Console.WriteLine($"Run {runId} resumed at step {step} of a new total {totalSteps}.");
        return runId;
    }

    public Models.RunSiteResponse? SiteOf(string runId) =>
        Runs.TryGetValue(runId, out var run) ? run.Site : null;

    /// <summary>Reads the site and the whole weather series off a freshly built world.</summary>
    static Models.RunSiteResponse SiteOf(AgroWorld world)
    {
        var coverage = world.SkyCoverageSeries();
        //Raw little-endian float32, the way PreviewResponse.Scene travels. Every platform this
        //runs on and every browser that reads it back into a Float32Array is little-endian; the
        //frame format already relies on the same thing.
        var bytes = new byte[coverage.Length * sizeof(float)];
        Buffer.BlockCopy(coverage, 0, bytes, 0, bytes.Length);
        return new Models.RunSiteResponse
        {
            Latitude = world.Latitude,
            Longitude = world.Longitude,
            Altitude = world.Altitude,
            AzimuthBase = world.AzimuthBase,
            AzimuthOffset = world.AzimuthOffset,
            TimeZone = world.TimeZone.Id,
            SkyCoverage = bytes,
        };
    }

    public void Begin(string runId)
    {
        if (!Pending.TryRemove(runId, out var loop)) return;
        //Deliberately not awaited: the hub invocation returns immediately and frees its slot,
        //which is the whole reason the loop moved out of it.
        _ = Task.Run(loop);
    }

    public void Cancel(string runId)
    {
        //Only ever a run that never started. Removing it from Pending first makes that certain:
        //if Begin got there first the loop owns the cleanup and this must not touch it.
        if (!Pending.TryRemove(runId, out _)) return;
        Runs.TryRemove(runId, out _);
        foreach (var pair in ConnectionToRun.Where(p => p.Value == runId).ToList())
            ConnectionToRun.TryRemove(pair.Key, out _);
        Store.CompleteRun(runId, aborted: true);
        Console.WriteLine($"Run {runId} was created but never begun; discarded.");
    }

    /// <summary>
    /// Writes the complete leaf-area-index measurement to the run store.
    ///
    /// Best-effort and deliberately last: it runs after the result has been assembled, so a failure
    /// here costs the study its detailed file and nothing else. Serialising sixteen hundred trays
    /// with a daily series each is not free, which is the other reason it only happens when the
    /// wire copy was actually truncated.
    /// </summary>
    void StoreFullObstacleLight(string runId, AgroWorld world, ObstacleLightMetaResponse meta,
                                ObstacleLightSelectionResponse selection)
    {
        try
        {
            var document = new ObstacleLightDocumentResponse
            {
                RunId = runId,
                Meta = meta,
                Selection = selection,
                Obstacles = ObstacleLightResponse.From(world.ObstacleLight, out _, unbudgeted: true),
            };
            Store.WriteObstacleLight(runId, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                document, ObstacleLightJsonContext.Default.ObstacleLightDocumentResponse));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Run {runId}: could not store the full leaf area index measurement: {e.Message}");
        }
    }

    async Task Loop(RunState run, AgroWorld world, bool lazyPreviews, ExportFlags exportFlags, SimulationRequest request)
    {
        var to = Hub.Clients.Group(run.RunId);
        try
        {
            world.Irradiance.SetAddress(Config["RendererIPMitsuba"], Config["RendererPortMitsuba"],
                Config["RendererIPTamashii"], Config["RendererPortTamashii"], request.RenderMode ?? 0);

            var start = DateTime.UtcNow.Ticks;
            var simulationLength = (uint)world.TimestepsTotal();

            //How often a frame is stored. One - every step - is the honest default: the stored run
            //is then complete and every timestep can be scrubbed to. It is also the setting that
            //costs the most, because each stored frame is an export the simulation thread has to
            //wait for. On a large stand where total wall time matters more than per-step
            //granularity, AGRO_RunStoreFrameStride=4 stores a frame every 16 simulated hours and
            //returns almost all of that time.
            var frameStride = uint.TryParse(Config["RunStoreFrameStride"], out var stride) && stride > 0 ? stride : 1u;
            if (frameStride > 1)
                Console.WriteLine($"Run {run.RunId}: storing every {frameStride} steps ({frameStride * (request.HoursPerTick ?? 1)} simulated hours per stored frame).");

            //How often the live world is written down so the run can be RESUMED rather than
            //recomputed. Every step would double the I/O of a large run for a file only the last
            //copy of which is ever read; every 32 steps bounds what a stop throws away to about a
            //day of simulated time at four hours a tick, for one checkpoint's worth of disk.
            var checkpointStride = uint.TryParse(Config["RunStoreCheckpointStride"], out var cs) && cs > 0 ? cs : 32u;

            //A resumed run starts where its checkpoint left off, not at zero. The world has already
            //lived those steps and its Timestep says so; the loop counter has to agree, or the
            //frames would be filed under step numbers the run already used.
            for (uint i = run.StartStep; i < simulationLength && !run.Abort;)
            {
                //Checked on EVERY iteration, not only on the ones that compute a step.
                //
                //This check used to live inside the computing branch below. With lazy previews
                //that is every iteration, so it looked correct - but in ExactPreview mode the
                //loop parks in the else branch waiting for a Preview call, and a client that has
                //gone will never make one. The abandoned run the grace period exists to stop is
                //precisely the one that never reached the check.
                if (run.UnwatchedSince is DateTime since && DateTime.UtcNow - since > UnwatchedGrace)
                {
                    Console.WriteLine($"Run {run.RunId} abandoned for over {UnwatchedGrace.TotalMinutes:0} min; stopping at step {i}.");
                    run.Abort = true;
                    break;
                }

                if (lazyPreviews || run.Preview)
                {
                    var prevTime = DateTime.UtcNow.Ticks;
                    world.Run(1U);
                    run.StepTimes.Add((uint)(DateTime.UtcNow.Ticks - prevTime));
                    _ = to.Progress(i, simulationLength);

                    //The export is the one part of storing a frame that cannot leave this thread:
                    //it reads live world state, so it cannot overlap the next step. Measured at
                    //1,600 plants it costs about two thirds of a simulation step, which is why it
                    //is worth NOT doing when nothing wants the result.
                    //
                    //The last step is always stored, so a run's final state is on disk whatever
                    //the stride is.
                    var wanted = i % frameStride == 0 || i + 1 == simulationLength;
                    if (wanted || run.Preview)
                    {
                        var scene = world.ExportToStream((byte)exportFlags);
                        //Queued, not written: the deflate, the file write, the index append and
                        //the metadata update all happen on the store's own writer.
                        if (wanted) Store.WriteFrame(run.RunId, i, scene);

                        if (run.Preview)
                        {
                            run.Preview = false;
                            _ = to.Preview(new() { Step = i, Renderer = world.RendererName, Scene = scene });
                        }
                    }

                    ++i;

                    //Taken BETWEEN steps, which is the only moment the world is consistent: mid-tick
                    //the agent arrays are half-swapped and the postboxes hold undelivered messages,
                    //neither of which a checkpoint captures.
                    if (i % checkpointStride == 0)
                        Store.WriteCheckpoint(run.RunId, i, world.SerializeState());
                }
                else
                {
                    //Yields the thread rather than spinning on it. The lazy-preview branch has no
                    //await at all, so a run in that mode pins a thread-pool thread throughout.
                    await Task.Delay(10);
                }
            }

            Debug.WriteLine($"Simulation time: {(DateTime.UtcNow.Ticks - start) / TimeSpan.TicksPerMillisecond} ms");

            var result = new SimulationResponse() { Plants = new(world.Count) };
            world.ForEach(formation =>
            {
                if (formation is PlantFormation2 plant)
                    result.Plants.Add(new() { Volume = plant.AG.GetVolume() });
            });

            if (request.RequestGeometry ?? false)
                result.Scene = world.ExportToStream((byte)exportFlags);

            result.Renderer = world.RendererName;
            result.StepTimes = run.StepTimes;
            result.ObstacleLight = ObstacleLightResponse.From(world.ObstacleLight, out var omitted);
            //Reported whenever a measurement was asked for, even when the filter matched nothing:
            //without it the client cannot tell a mistyped filter from a run that never measured.
            if (world.ObstacleLightSelection.Filter != null)
                result.ObstacleLightMeta = ObstacleLightMetaResponse.From(world, world.ObstacleLight, world.ObstacleList);
            if (result.ObstacleLightMeta != null)
                result.ObstacleLightMeta.DailySeriesOmitted = omitted;
            if (world.ObstacleLight != null)
                result.ObstacleLightSelection = ObstacleLightSelectionResponse.From(world.ObstacleLight);

            //When the wire copy had to drop the per-obstacle series, keep the whole measurement on
            //disk instead. That budget exists because a browser cannot hold a hundred megabytes of
            //JSON arriving as one socket message - it is not a statement about how much the study
            //is allowed to know. Fetched over REST it is a file the browser saves without parsing.
            if (omitted && world.ObstacleLight != null)
                StoreFullObstacleLight(run.RunId, world, result.ObstacleLightMeta, result.ObstacleLightSelection);

            //Always, however the run ended. On an abort it is what makes the run resumable at all;
            //on a completed one it is what lets the horizon be extended later without recomputing
            //the months already paid for.
            try { Store.WriteCheckpoint(run.RunId, (uint)world.Timestep, world.SerializeState()); }
            catch (Exception e) { Console.Error.WriteLine($"Run {run.RunId}: could not write the final checkpoint: {e.Message}"); }

            Store.CompleteRun(run.RunId, run.Abort);
            await to.Result(result);
        }
        catch (Exception e)
        {
            //A run that dies must still release its slot, or its connection is wedged forever -
            //which is exactly what the old code did when Result threw at a departed client.
            Console.Error.WriteLine($"Run {run.RunId} failed: {e}");
            Store.CompleteRun(run.RunId, true);
        }
        finally
        {
            Runs.TryRemove(run.RunId, out _);
            foreach (var pair in ConnectionToRun.Where(p => p.Value == run.RunId).ToList())
                ConnectionToRun.TryRemove(pair.Key, out _);
        }
    }
}
