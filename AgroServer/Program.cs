using System.Text.Json.Serialization;
using Agro;
using AgroServer.Controllers;
using AgroServer.Hubs;
using AgroServer.Services;

Console.WriteLine("HELLO THERE!");

var builder = WebApplication.CreateSlimBuilder(args);

const string Origins = "_AgroEcoSim";

var origins = builder.Configuration["AGRO_HOSTNAME"] ?? "http://localhost:8080";
builder.Services.AddCors(o => o.AddPolicy(name: Origins, p =>
    p.WithOrigins(origins)
    //p.AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
));
//.SetIsOriginAllowedToAllowWildcardSubdomains()
//.WithMethods("GET", "POST", "OPTIONS").AllowAnyHeader().AllowCredentials().Build()

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.Converters.Add(new Vector2JsonConverter());
    options.SerializerOptions.Converters.Add(new Vector3JsonConverter());
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(7215); // HTTP only
});

builder.WebHost.UseUrls("http://localhost:7215");

// Add services to the container.
var simulationUploeadService = new SimulationUploadService();
var terainBufferService = new TerrainBuffer();
var seedsBufferService = new SeedsBuffer();
builder.Services.AddSingleton<ISimulationUploadService>(simulationUploeadService);
builder.Services.AddSingleton<ITerrainBuffer>(terainBufferService);

builder.Services.AddSignalR(options => {
    options.MaximumParallelInvocationsPerClient = 3;
});

builder.Configuration.AddEnvironmentVariables(prefix: "AGRO_");

var app = builder.Build();
app.UseCors(Origins);
app.MapHub<SimulationHub>("/SimSocket").RequireCors(Origins);
SimulationController.Map(app.MapGroup("/Simulation"), app.Configuration, simulationUploeadService, terainBufferService, seedsBufferService);
app.Run();

[JsonSourceGenerationOptions(Converters = new[] { typeof(Vector3JsonConverter) })]
[JsonSerializable(typeof(System.Numerics.Vector3))]
[JsonSerializable(typeof(System.Numerics.Vector3[]))]
[JsonSerializable(typeof(System.Numerics.Vector2))]
[JsonSerializable(typeof(System.Numerics.Vector2[]))]
[JsonSerializable(typeof(Utils.Json.Vector3Data))]
[JsonSerializable(typeof(Utils.Json.Vector3XYZ))]
[JsonSerializable(typeof(Utils.Json.Vector3XDZ))]
[JsonSerializable(typeof(SimulationRequest))]
[JsonSerializable(typeof(PlantRequest))]
[JsonSerializable(typeof(ObstacleRequest))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{

}