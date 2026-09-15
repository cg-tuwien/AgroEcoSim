using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Agro;
using AgroServer.Models;
using AgroServer.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;

namespace AgroServer.Hubs;

public interface IEditorHub
{
    Task Rejected();
    /// <summary>
    /// The id under which this run's frames are being stored, sent once at the start.
    ///
    /// The client needs it to fetch frames back rather than keeping them all in memory, and to
    /// reattach to the run after a reload.
    /// </summary>
    Task RunStarted(string runId);
    Task Progress(uint step, uint length);
    Task Result(SimulationResponse response);
    Task Preview(PreviewResponse response);
    Task Terrain(byte[] response);
}

/// <summary>
/// A built terrain and everything needed to build the same one again.
///
/// The built <see cref="SoilFormationsList"/> cannot be turned back into the model it came from,
/// and the parameters it was built with are not all in the request - the resolution used is the
/// one that was on screen when the OBJ was uploaded, which the user may well have changed before
/// starting the run. So they are captured here, at the one moment they are known to belong
/// together, and travel with the run into its stored record.
/// </summary>
public sealed record ClientTerrain(
    ISoilFormation Terrain,
    string? ModelHash,
    string Pattern,
    bool PatternForMaterial,
    float Resolution);

public class SimulationHub : Hub
{
    readonly ISimulationUploadService UploadService;
    readonly ITerrainBuffer TerrainBuffer;
    readonly ISimulationRunner Runner;
    readonly IRunStore RunStore;

    public SimulationHub(ISimulationUploadService uploadService, ITerrainBuffer terrainBuffer, ISimulationRunner runner, IRunStore runStore)
    {
        UploadService = uploadService;
        TerrainBuffer = terrainBuffer;
        Runner = runner;
        RunStore = runStore;
    }

    static readonly Dictionary<string, ClientTerrain> ClientTerrains = [];

    /// <summary>
    /// Releases what this connection was holding.
    ///
    /// There was no override at all before, and two things leaked because of it. A built
    /// SoilFormationsList for a 93.7 MB facade is 150-250 MB and one was kept per connection id
    /// forever, while the frontend reconnects with infinite retry - so every reconnect leaked
    /// another. And a client that dropped mid-run left its entry behind, marking that connection
    /// permanently busy. The run itself is deliberately NOT stopped: it belongs to the runner
    /// now, and a reconnecting client can attach to it again.
    /// </summary>
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        lock (ClientTerrains) ClientTerrains.Remove(Context.ConnectionId);
        Runner.DetachConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Rejoins a run after a reload or a dropped socket.</summary>
    public async Task<bool> Subscribe(string runId)
    {
        if (!Runner.Attach(runId, Context.ConnectionId)) return false;
        await Groups.AddToGroupAsync(Context.ConnectionId, runId);
        return true;
    }

    public Task Abort()
    {
        Runner.Abort(Context.ConnectionId);
        return Task.CompletedTask;
    }

    public Task<bool> Preview() => Task.FromResult(Runner.RequestPreview(Context.ConnectionId));

    public async Task Terrain(string terrainId, ushort hoursPerTick, string pattern, bool patternForMaterial, float resolution)
    {
        if (TerrainBuffer.TryGetV2(terrainId, out var objData, out var modelHash))
        {
            var terrain = new SoilFormationsList(null, objData, 1f, pattern, patternForMaterial, resolution, false);
            lock (ClientTerrains)
            {
                ClientTerrains[Context.ConnectionId] = new(terrain, modelHash, pattern, patternForMaterial, resolution);
                Clients.Caller.Terrain(terrain.Serialize());
            }
        }
    }

    /// <summary>
    /// Rebuilds the terrain of a stored run, so it can be continued or forked without the user
    /// uploading its model again.
    ///
    /// Not cheap - a facade is seconds of parsing and a few hundred megabytes - but it happens
    /// once, on an explicit click, and the alternative is that a run archived last month simply
    /// cannot be re-run. Returns null whenever anything is missing, which is the ordinary case for
    /// a run that used the default box field and needs no model at all.
    /// </summary>
    ClientTerrain? RebuildStoredTerrain(string? sourceRunId)
    {
        if (string.IsNullOrEmpty(sourceRunId)) return null;
        var source = RunStore.GetRun(sourceRunId);
        if (source?.FieldModelHash == null) return null;
        if (!RunStore.TryReadFieldModel(source.FieldModelHash, out var objSource)) return null;

        try
        {
            //Parsed through the same buffer and the same coordinate system the upload endpoint
            //uses. Anything else here would silently mirror or rotate a rebuilt facade against
            //the run it claims to continue.
            var key = TerrainBuffer.Add(objSource, CoordinateSystem.RH_rightPX_upPZ, source.FieldModelHash);
            if (!TerrainBuffer.TryGetV2(key, out var objData)) return null;

            var terrain = new SoilFormationsList(null, objData, 1f, source.FieldItemRegex,
                source.FieldItemRegexMaterial, source.FieldResolution, false);
            Console.WriteLine($"Rebuilt the terrain of run {sourceRunId} from stored field model {source.FieldModelHash[..12]}.");
            return new(terrain, source.FieldModelHash, source.FieldItemRegex ?? "", source.FieldItemRegexMaterial, source.FieldResolution);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Could not rebuild the terrain of run {sourceRunId}: {e.Message}");
            return null;
        }
    }

    public async Task Run(SimulationRequest request)
    {
        if (Runner.HasRun(Context.ConnectionId))
        {
            await Clients.Caller.Rejected();
            return;
        }

        ClientTerrain? terrain;
        lock (ClientTerrains) ClientTerrains.TryGetValue(Context.ConnectionId, out terrain);
        //An upload on this connection always wins: it is the more recent statement of intent.
        //Only when there is none does a continued or forked run fall back to its parent's model.
        if (terrain == null && !string.IsNullOrEmpty(request.FieldModelRunId))
        {
            terrain = RebuildStoredTerrain(request.FieldModelRunId);
            //Refused rather than run on a plain field. The request NAMED a model, and the client
            //only names one the server has just said it holds - so failing here means the model
            //went away underneath it. Quietly substituting a flat field would produce a run that
            //completes, looks plausible, is filed next to its parent as a continuation, and
            //simulates a different scenario. A HubException carries its message to the caller
            //whether or not detailed errors are enabled.
            if (terrain == null)
                throw new HubException(
                    $"The field model of run {request.FieldModelRunId} is no longer available, so this run " +
                    $"cannot be continued or forked from it. Upload the model again to run this scenario.");
        }

        //Create, join, then begin. Joining after the loop had started would race it: anything
        //pushed before this connection is in the group goes nowhere.
        var runId = Runner.Create(request, terrain, Context.ConnectionId);
        try
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, runId);
            await Clients.Caller.RunStarted(runId);
        }
        catch
        {
            //A client that drops during the join would otherwise leave a run created and never
            //begun: no loop, no cleanup, and a store record stuck at "running" that pins its
            //bytes against the retention budget for the life of the process.
            Runner.Cancel(runId);
            throw;
        }
        Runner.Begin(runId);
    }

    public async Task Start(string id)
    {
        if (UploadService.TryFetch(id, out var request))
            await Run(request);
    }
}
