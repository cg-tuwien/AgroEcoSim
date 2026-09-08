using Agro;
using AgroServer.Controllers;
using AgroServer.Hubs;
using AgroServer.Models;
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
    options.Limits.MaxRequestBodySize = 256L * 1024 * 1024;
});

builder.WebHost.UseUrls("http://localhost:7215");

//Registered before the first Configuration read below - AGRO_RunStoreRoot was previously
//invisible to it, because this line used to sit after the RunStore was already constructed.
builder.Configuration.AddEnvironmentVariables(prefix: "AGRO_");

// Add services to the container.
var simulationUploeadService = new SimulationUploadService();
var terainBufferService = new TerrainBuffer();
var seedsBufferService = new SeedsBuffer();
//Where stored runs live. Overridable so a container can point it at a mounted volume - the
//simulation service declares none today, and without one the store dies with the container,
//which would defeat the point of persisting at all.
var runStoreRoot = builder.Configuration["RunStoreRoot"]
    ?? Path.Combine(AppContext.BaseDirectory, "runstore");
var runStoreService = new RunStore(runStoreRoot);
//The retention budget has to be reachable without a rebuild: the default is 64 GiB and the host
//the container lands on is the only thing that knows whether that is generous or fatal. Set
//before any run begins, which is the only time it is read.
if (long.TryParse(builder.Configuration["RunStoreRetentionBytes"], out var retentionBytes) && retentionBytes > 0)
{
    runStoreService.RetentionBytes = retentionBytes;
    Console.WriteLine($"RunStore retention set to {retentionBytes / (1024 * 1024)} MB.");
}
builder.Services.AddSingleton<ISimulationUploadService>(simulationUploeadService);
builder.Services.AddSingleton<ITerrainBuffer>(terainBufferService);
builder.Services.AddSingleton<IRunStore>(runStoreService);
//Resolved through DI rather than constructed here: it needs IHubContext, which only exists
//after the app is built.
builder.Services.AddSingleton<ISimulationRunner, SimulationRunner>();

builder.Services.AddSignalR(options => {
    options.MaximumParallelInvocationsPerClient = 3;
}).AddJsonProtocol(options =>
{
    // SignalR has its own JSON options; ConfigureHttpJsonOptions only covers HTTP endpoints.
    options.PayloadSerializerOptions.Converters.Add(new Vector2JsonConverter());
    options.PayloadSerializerOptions.Converters.Add(new Vector3JsonConverter());
    options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

var app = builder.Build();
app.UseCors(Origins);
app.MapHub<SimulationHub>("/SimSocket").RequireCors(Origins);
SimulationController.Map(app.MapGroup("/Simulation"), app.Configuration, simulationUploeadService, terainBufferService, seedsBufferService, runStoreService);
app.Run();
