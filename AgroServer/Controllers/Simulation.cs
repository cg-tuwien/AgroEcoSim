using Microsoft.AspNetCore.Mvc;
using AgroServer.Models;
using Agro;
using System.Diagnostics;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace AgroServer.Controllers;

[ApiController]
[Route("[controller]")]
public class SimulationController : ControllerBase
{
    private readonly IConfiguration Configuration;

    public SimulationController(IConfiguration configuration) => Configuration = configuration;

    [HttpGet]
    public ActionResult Get() => Ok();

    [HttpPost]
    public async Task<ActionResult<SimulationResponse>> Post([FromBody]SimulationRequest request)
    {
        var world = Initialize.World(request);
        world.Irradiance.SetAddress(Configuration["RendererIPMitsuba"], Configuration["RendererPortMitsuba"], Configuration["RendererIPTamashii"], Configuration["RendererPortTamashii"], request?.RenderMode ?? 0);

        var start = DateTime.UtcNow.Ticks;
        world.Run((uint)world.TimestepsTotal());
        var stop = DateTime.UtcNow.Ticks;
        Debug.WriteLine($"Simulation time: {(stop - start) / TimeSpan.TicksPerMillisecond} ms");

        var response = new SimulationResponse() { Plants = new(world.Count) };
        world.ForEach(formation =>
        {
            if (formation is PlantFormation2 plant)
                //plantData.Add(@$"{{""P"":{JsonSerializer.Serialize(new Vector3Data(plant.Position))},""V"":{plant.AG.GetVolume()}}}");
                response.Plants.Add(new(){ Volume = plant.AG.GetVolume()});
        });

        Debug.WriteLine($"RENDER TIME: {world.Irradiance.ElapsedMilliseconds} ms");

        if(request?.RequestGeometry ?? false)
            response.Scene = world.ExportToStream(3);

        response.Renderer = world.RendererName;

        return response;
    }
}