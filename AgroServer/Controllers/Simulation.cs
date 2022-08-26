#if !GODOT
using Microsoft.AspNetCore.Mvc;
using AgroServer.Models;
using Agro;
using System.Diagnostics;

namespace AgroServer.Controllers;

[ApiController]
[Route("[controller]")]
public class SimulationController : ControllerBase
{
    //private readonly ILogger<SimulationController> _logger;
    //private readonly IConfiguration Configuration;

    public SimulationController(IConfiguration configuration)
    {
        //_logger = logger;
        //Configuration = configuration;
        var ip = configuration["RendererIP"];
        var port = configuration["RendererPort"];
        IrradianceClient.SetAddress($"http://{ip}:{port}");
    }

    [HttpPost]
    public async Task<ActionResult<SimulationResponse>> Post([FromBody]SimulationRequest request)
    {
        var world = Initialize.World(request);
        var start = DateTime.UtcNow.Ticks;
        world.Run(AgroWorld.TimestepsTotal);
        var stop = DateTime.UtcNow.Ticks;
        Debug.WriteLine($"Simulation time: {(stop - start) / TimeSpan.TicksPerMillisecond} ms");
#if HISTORY_LOG || HISTORY_TICK
            //var exported = world.HistoryToJSON();
            //File.WriteAllText("export.json", exported.Replace("},", "},\n").Replace("],", "],\n"));
#endif
        var response = new SimulationResponse(){ Plants = new(world.Count) };
        world.ForEach(formation =>
        {
            if (formation is PlantFormation plant)
                //plantData.Add(@$"{{""P"":{JsonSerializer.Serialize(new Vector3Data(plant.Position))},""V"":{plant.AG.GetVolume()}}}");
                response.Plants.Add(new(){ Volume = plant.AG.GetVolume()});
        });

        Debug.WriteLine($"RENDER TIME: {IrradianceClient.Singleton.SW.ElapsedMilliseconds} ms");
        return response;
    }
}
#endif