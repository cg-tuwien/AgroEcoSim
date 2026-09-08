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
    string Create(SimulationRequest request, ISoilFormation? terrain, string connectionId);

    /// <summary>Starts the loop for a run created by <see cref="Create"/>.</summary>
    void Begin(string runId);

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
    public volatile bool Preview;
    public volatile bool Abort;
    public List<uint> StepTimes { get; } = new(1000);
    /// <summary>The connection currently watching. Empty while nobody is attached.</summary>
    public volatile string ConnectionId = "";
    /// <summary>When the last client detached, or null while one is attached.</summary>
    public DateTime? UnwatchedSince;
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

    public string Create(SimulationRequest request, ISoilFormation? terrain, string connectionId)
    {
        var lazyPreviews = !(request.ExactPreview ?? false);
        var exportFlags = (request.DownloadRoots ?? false ? ExportFlags.Roots : ExportFlags.None) | ExportFlags.Analytics;

        var world = Initialize.World(request, terrain);
        world.Deterministic = request.Deterministic ?? false;

        var runId = Store.BeginRun(
            System.Text.Json.JsonSerializer.Serialize(request, AppJsonSerializerContext.Default.SimulationRequest),
            (ushort)(request.HoursPerTick ?? 1), (uint)world.TimestepsTotal(),
            (exportFlags & ExportFlags.Analytics) != 0, world.Deterministic);

        var run = new RunState { RunId = runId, ConnectionId = connectionId };
        Pending[runId] = () => Loop(run, world, lazyPreviews, exportFlags, request);
        Runs[runId] = run;
        ConnectionToRun[connectionId] = runId;
        return runId;
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

            for (uint i = 0; i < simulationLength && !run.Abort;)
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
            result.ObstacleLight = ObstacleLightResponse.From(world.ObstacleLight);
            if (result.ObstacleLight != null)
                result.ObstacleLightTimesteps = [.. world.ObstacleLight.Timesteps];

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
