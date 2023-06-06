using Agro;
using Microsoft.AspNetCore.SignalR;

namespace AgroServer.Hubs;

public interface IEditorHub
{
    Task Run(SimulationRequest request);
}

public class SimulationHub : Hub<IEditorHub>
{
    public SimulationHub(IConfiguration configuration)
    {
        //_logger = logger;
        //Configuration = configuration;
        var ip = configuration["RendererIP"];
        var port = configuration["RendererPort"];
        IrradianceClient.SetAddress($"http://{ip}:{port}");
    }

    public async Task Run(SimulationRequest request)
    {

    }
}