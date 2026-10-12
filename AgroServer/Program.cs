using Agro;
using AgroServer.Controllers;
using AgroServer.Hubs;
using AgroServer.Models;
using AgroServer.Services;

Console.WriteLine("HELLO THERE!");

//A tick is a chain of short fork/join phases over all cores. The pool injects threads beyond
//the processor count only at about one per half second, so without this the first phases of
//every run spin up on a handful of threads while the rest of the machine waits for the pool to
//notice - which reads as "threads run only briefly".
ThreadPool.SetMinThreads(Environment.ProcessorCount, Environment.ProcessorCount);

var builder = WebApplication.CreateSlimBuilder(args);

//AGRO_-prefixed environment variables, registered FIRST so that everything below can read them.
//This used to sit further down and AGRO_RunStoreRoot was invisible to the store, which was
//constructed above it; the same trap catches every setting added later, so it goes at the top.
builder.Configuration.AddEnvironmentVariables(prefix: "AGRO_");

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

//Where the server listens.
//
//Localhost by DEFAULT, deliberately: this API has no authentication of any kind, so a development
//machine must not put it on every interface because a container needed it there. A container sets
//AGRO_ListenAnyIP=true and gets 0.0.0.0, which is the only address that can be published.
//
//That distinction was not available before, and the consequence was quiet: an explicit Listen call
//in ConfigureKestrel overrides UseUrls AND ASPNETCORE_URLS, so the deployed image bound
//127.0.0.1:7215 inside its own network namespace and nothing outside the container could ever
//reach it - while docker-compose published a port and the frontend was configured to call it.
var listenPort = int.TryParse(builder.Configuration["Port"], out var configuredPort) && configuredPort > 0
    ? configuredPort : 7215;
var listenAnyIP = string.Equals(builder.Configuration["ListenAnyIP"], "true", StringComparison.OrdinalIgnoreCase);

builder.WebHost.ConfigureKestrel(options =>
{
    if (listenAnyIP) options.ListenAnyIP(listenPort); else options.ListenLocalhost(listenPort);
    options.Limits.MaxRequestBodySize = 256L * 1024 * 1024;
});
Console.WriteLine($"Listening on {(listenAnyIP ? "0.0.0.0" : "localhost")}:{listenPort}.");

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
//How often the store enforces that budget, and how long an unreferenced field model is kept. Both
//matter on a host that uploads and browses more than it simulates: before there was a timer at all,
//eviction happened only when a run STARTED, so such a host swept nothing at all.
//Zero is allowed and means off, which is why this is `>= 0` - a kill switch that needs a code
//change is not a kill switch.
if (int.TryParse(builder.Configuration["RunStoreSweepMinutes"], out var sweepMinutes) && sweepMinutes >= 0)
    runStoreService.SweepInterval = TimeSpan.FromMinutes(sweepMinutes);
if (double.TryParse(builder.Configuration["RunStoreModelGraceHours"], System.Globalization.CultureInfo.InvariantCulture, out var graceHours) && graceHours >= 0)
    runStoreService.ModelGrace = TimeSpan.FromHours(graceHours);
//Field models get their own cap rather than a share of the run budget: evicting a run does not
//release its model, so one budget would have the eviction loop deleting every completed run in the
//store chasing a total that only model collection can bring down.
if (long.TryParse(builder.Configuration["RunStoreModelBytes"], out var modelBytes) && modelBytes > 0)
{
    runStoreService.ModelRetentionBytes = modelBytes;
    Console.WriteLine($"RunStore field model budget set to {modelBytes / (1024 * 1024)} MB.");
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
//Registered as an INSTANCE, which the container does not own and therefore never disposes. Without
//this, shutting the server down abandons the frame writers mid-queue and leaves LiteDB unflushed -
//the frames a run had just computed, and the metadata describing them, silently do not arrive.
//
//Two events, not one. ApplicationStopping fires while requests are still being served, so it only
//stops NEW maintenance work; closing the database there would fail the requests still reading it.
//ApplicationStopped fires once the web server has drained, which is when the database can go.
app.Lifetime.ApplicationStopping.Register(() => runStoreService.BeginShutdown());
app.Lifetime.ApplicationStopped.Register(() => runStoreService.Dispose());
//Started here rather than in the constructor: the first sweep enforces the budgets, and those are
//configured above, after the store exists.
runStoreService.StartMaintenance();
app.UseCors(Origins);
app.MapHub<SimulationHub>("/SimSocket").RequireCors(Origins);
SimulationController.Map(app.MapGroup("/Simulation"), app.Configuration, simulationUploeadService, terainBufferService, seedsBufferService, runStoreService);
app.Run();
