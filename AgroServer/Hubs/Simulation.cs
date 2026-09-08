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

public class SimulationHub : Hub
{
    readonly ISimulationUploadService UploadService;
    readonly ITerrainBuffer TerrainBuffer;
    readonly ISimulationRunner Runner;
    public SimulationHub(ISimulationUploadService uploadService, ITerrainBuffer terrainBuffer, ISimulationRunner runner)
    {
        UploadService = uploadService;
        TerrainBuffer = terrainBuffer;
        Runner = runner;
    }

    static readonly Dictionary<string, ISoilFormation> ClientTerrains = [];

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
        if (TerrainBuffer.TryGetV2(terrainId, out var objData))
        {
            var terrain = new SoilFormationsList(null, objData, 1f, pattern, patternForMaterial, resolution, false);
            lock (ClientTerrains)
            {
                ClientTerrains[Context.ConnectionId] = terrain;
                Clients.Caller.Terrain(terrain.Serialize());
            }
        }
    }

    public async Task Run(SimulationRequest request)
    {
        if (Runner.HasRun(Context.ConnectionId))
        {
            await Clients.Caller.Rejected();
            return;
        }

        ClientTerrains.TryGetValue(Context.ConnectionId, out var terrain);

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