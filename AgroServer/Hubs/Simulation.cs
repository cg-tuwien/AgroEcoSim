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
    public SimulationHub(IConfiguration configuration, ISimulationUploadService uploadService)
    {
        Config = configuration;
        UploadService = uploadService;
    }

    static readonly Dictionary<string, SimRequests> ClientSimulations = [];

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
        var exportVersion = (byte)(5 + (request.DownloadRoots ?? false ? 1 : 0));

        var world = Initialize.World(request);
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
                        _ = Clients.Caller.Preview(new() { Step = i, Renderer = world.RendererName, Scene = world.ExportToStream(exportVersion) });
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
            result.Scene = world.ExportToStream(exportVersion);

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