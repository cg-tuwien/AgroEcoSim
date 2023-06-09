using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Agro;
using AgroServer.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;

namespace AgroServer.Hubs;

public interface IEditorHub
{
    //Task Run(SimulationRequest request);
    Task Rejected();
    Task Progress(uint step, uint length);
    Task Result(SimulationResponse response);
}

public class SimulationHub : Hub<IEditorHub>
{
    public SimulationHub(IConfiguration configuration)
    {
        var ip = configuration["RendererIP"];
        var port = configuration["RendererPort"];
        IrradianceClient.SetAddress($"http://{ip}:{port}");
    }

    HashSet<string> ClientSimulations = new ();

    public async Task Run(SimulationRequest request)
    {
        lock (ClientSimulations)
        {
            var me = Context.UserIdentifier;
            if (ClientSimulations.Contains(me))
            {
                Clients.Caller.Rejected();
                return;
            }
            else
                ClientSimulations.Add(me);
        }
        var world = Initialize.World(request);
        var start = DateTime.UtcNow.Ticks;
        var simulationLength = (uint)world.TimestepsTotal();
        for(uint i = 0; i < simulationLength; ++i)
        {
            world.Run(1U);
            _ = Clients.Caller.Progress(i, simulationLength);
        }
        var stop = DateTime.UtcNow.Ticks;
        Debug.WriteLine($"Simulation time: {(stop - start) / TimeSpan.TicksPerMillisecond} ms");

        var result = new SimulationResponse() { Plants = new(world.Count) };
        world.ForEach(formation =>
        {
            if (formation is PlantFormation2 plant)
                result.Plants.Add(new(){ Volume = plant.AG.GetVolume()});
        });

        Debug.WriteLine($"RENDER TIME: {IrradianceClient.ElapsedMilliseconds} ms");

        if(request?.RequestGeometry ?? false)
            result.Scene = world.ExportToStream();

        result.Renderer = world.RendererName;
        result.Debug = $"{IrradianceClient.Address}";

        await Clients.Caller.Result(result);
        lock (ClientSimulations) ClientSimulations.Remove(Context.UserIdentifier);
    }
}