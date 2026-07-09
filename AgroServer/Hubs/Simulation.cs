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
    Task Progress(uint step, uint length);
    Task Result(SimulationResponse response);
    Task Preview(PreviewResponse response);
    Task Terrain(byte[] response);
}

public class SimRequests
{
    public bool Preview = false;
    public bool Abort = false;
    public List<uint> StepTimes = new(1000);
}

public class SimulationHub : Hub<IEditorHub>
{
    readonly IConfiguration Config;
    readonly ISimulationUploadService UploadService;
    readonly ITerrainBuffer TerrainBuffer;
    public SimulationHub(IConfiguration configuration, ISimulationUploadService uploadService, ITerrainBuffer terrainBuffer)
    {
        Config = configuration;
        UploadService = uploadService;
        TerrainBuffer = terrainBuffer;
    }

    static readonly Dictionary<string, SimRequests> ClientSimulations = [];
    static readonly Dictionary<string, ISoilFormation> ClientTerrains = [];

    public async Task Abort()
    {
        await Task.Run(() =>
        {
            lock (ClientSimulations)
            {
                if (ClientSimulations.TryGetValue(Context.ConnectionId, out var sim))
                    sim.Abort = true;
            }
        });
    }

    public async Task<bool> Preview()
    {
        return await Task.Run(() =>
        {
            lock (ClientSimulations)
            {
                if (ClientSimulations.TryGetValue(Context.ConnectionId, out var sim))
                {
                    sim.Preview = true;
                    return true;
                }
                else
                    return false;
            }
        });
    }

    public async Task Terrain(string terrainId, ushort hoursPerTick, string pattern, bool patternForMaterial, float resolution)
    {
        if (TerrainBuffer.TryGetV2(terrainId, out var objData))
        {
            var terrain = new SoilFormationsList(null, objData, 1f, pattern, patternForMaterial, resolution, true);
            lock (ClientTerrains)
            {
                ClientTerrains[Context.ConnectionId] = terrain;
                Clients.Caller.Terrain(terrain.Serialize());
            }
        }
    }

    public async Task Run(SimulationRequest request)
    {
        var requests = new SimRequests();
        var me = Context.ConnectionId;
        lock (ClientSimulations)
        {
            if (ClientSimulations.ContainsKey(me))
            {
                Clients.Caller.Rejected();
                return;
            }
            else
                ClientSimulations.Add(me, requests);
        }
        var lazyPreviews = !(request.ExactPreview ?? false);
        var exportFlags = (request.DownloadRoots ?? false ? ExportFlags.Roots : ExportFlags.None) | ExportFlags.Analytics;

        ClientTerrains.TryGetValue(Context.ConnectionId, out var terrain);
        var world = Initialize.World(request, terrain);
        //_ = Clients.Caller.Terrain(world.SerializeTerrain());
        await Task.Run(() =>
        {
            world.Irradiance.SetAddress(Config["RendererIPMitsuba"], Config["RendererPortMitsuba"], Config["RendererIPTamashii"], Config["RendererPortTamashii"], request?.RenderMode ?? 0);
            var start = DateTime.UtcNow.Ticks;
            var simulationLength = (uint)world.TimestepsTotal();
            long prevTime;
            for (uint i = 0; i < simulationLength && !requests.Abort;)
                if (lazyPreviews || requests.Preview)
                {
                    prevTime = DateTime.UtcNow.Ticks;
                    world.Run(1U);
                    var now = DateTime.UtcNow.Ticks;
                    requests.StepTimes.Add((uint)(now - prevTime));
                    _ = Clients.Caller.Progress(i, simulationLength);

                    if (requests.Preview)
                    {
                        requests.Preview = false;
                        _ = Clients.Caller.Preview(new() { Step = i, Renderer = world.RendererName, Scene = world.ExportToStream((byte)exportFlags) });
                    }

                    ++i;
                }
                else
                    Thread.Sleep(10);
            var stop = DateTime.UtcNow.Ticks;
            Debug.WriteLine($"Simulation time: {(stop - start) / TimeSpan.TicksPerMillisecond} ms");
        });

        var result = new SimulationResponse() { Plants = new(world.Count) };
        world.ForEach(formation =>
        {
            if (formation is PlantFormation2 plant)
                result.Plants.Add(new() { Volume = plant.AG.GetVolume() });
        });

        if (request?.RequestGeometry ?? false)
            result.Scene = world.ExportToStream((byte)exportFlags);

        result.Renderer = world.RendererName;
        //result.Debug = $"{IrradianceClient.Address}";
        result.StepTimes = requests.StepTimes;

        await Clients.Caller.Result(result);
        lock (ClientSimulations) ClientSimulations.Remove(me);
    }

    public async Task Start(string id)
    {
        if (UploadService.TryFetch(id, out var request))
            await Run(request);
    }
}