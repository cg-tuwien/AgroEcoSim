// Scripted end-to-end checks of the core against a running AgroServer, without Godot.
//
//   dotnet run --project GodotFrontend/Harness -- --server http://127.0.0.1:7215 [--hours 240] [--render-mode 3]
//
// Streams a single-plant run through the real hub and REST clients, then opens the stored run again over REST.
using System.Diagnostics;
using GodotFrontend.Core.History;
using GodotFrontend.Core.Net;
using GodotFrontend.Core.Playback;
using GodotFrontend.Core.Run;
using GodotFrontend.Core.Scene;
using GodotFrontend.Core.Threading;

if (args.Length >= 2 && args[0] == "--bench")
{
    // decode + pack timing of one stored frame, repeated so the steady state (tier-1 JIT, warm buffers) shows
    var packed = File.ReadAllBytes(args[1]);
    var libB = new GodotFrontend.Core.Geometry.GeometryLibrary();
    var dec = new SceneDecoder(libB);
    var packer = new GodotFrontend.Core.Packing.FramePacker(libB);
    GodotFrontend.Core.Packing.PackedFrame? frame = null;
    for (var k = 0; k < 6; ++k)
    {
        var sw0 = Stopwatch.StartNew();
        var raw = Compressor.Inflate(packed);
        var tInflate = sw0.Elapsed.TotalMilliseconds; sw0.Restart();
        var scene = dec.Decode(raw);
        var tDecode = sw0.Elapsed.TotalMilliseconds; sw0.Restart();
        frame = packer.Pack(scene, frame);
        var tPack = sw0.Elapsed.TotalMilliseconds;
        Console.WriteLine($"run {k}: {scene.PlantCount} plants, {scene.Count} primitives: inflate {tInflate:F1} ms, decode {tDecode:F1} ms, pack {tPack:F1} ms");
    }
    return 0;
}

if (args.Length >= 2 && args[0] == "--terrain")
{
    // decode a stored terrain (terrain.bin.gz or raw) and report what it holds
    var bytesT = RestClient.Gunzip(File.ReadAllBytes(args[1]));
    var swT = Stopwatch.StartNew();
    var t = TerrainDecoder.Decode(bytesT);
    Console.WriteLine($"terrain v{t.Version}: {t.Items.Count} items ({t.Items.Count(i => i is BoxTerrainItem)} boxes), " +
                      $"obstacle mesh {t.ObstacleMesh?.VertexCount ?? 0} vertices, {t.Groups.Count} groups, {swT.Elapsed.TotalMilliseconds:F0} ms");
    return 0;
}

var server = "http://127.0.0.1:7215";
var hours = 240;
byte renderMode = 3;
for (var i = 0; i < args.Length; ++i)
{
    if (args[i] == "--server" && i + 1 < args.Length) server = args[++i];
    else if (args[i] == "--hours" && i + 1 < args.Length) hours = int.Parse(args[++i]);
    else if (args[i] == "--render-mode" && i + 1 < args.Length) renderMode = byte.Parse(args[++i]);
}

Console.Error.WriteLine("harness starting");
var pump = new ManualPump();
pump.InstallSynchronizationContext();
var endpoint = new ServerEndpoint(server);
using var rest = new RestClient(endpoint);
await using var hub = new HubClient(endpoint);
var sink = new RecordingSink();
var controller = new RunController(pump, rest, hub, sink, null);
var log = new List<string>();
controller.Log += m => { log.Add(m); Console.WriteLine($"  log: {m}"); };
var failures = new List<string>();
void Check(bool ok, string what) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {what}"); if (!ok) failures.Add(what); }
bool Wait(Func<bool> pred, int seconds) => pump.DrainUntil(pred, TimeSpan.FromSeconds(seconds));
// Top-level awaits would deadlock: their continuations go to the pump, which only this thread drains.
T Run<T>(Task<T> task) { Wait(() => task.IsCompleted, 600); return task.GetAwaiter().GetResult(); }

Console.Error.WriteLine("requesting version");
Console.WriteLine($"server version: {Run(rest.GetVersionAsync())}");
Console.Error.WriteLine("version received");
var writePassword = Environment.GetEnvironmentVariable("AGRO_WRITE_PASSWORD");
if (string.IsNullOrEmpty(writePassword))
{
    Console.Error.WriteLine("Set AGRO_WRITE_PASSWORD to run the write-capable harness against an isolated test server.");
    SynchronizationContext.SetSynchronizationContext(null);
    return 2;
}
Run(rest.UnlockWritesAsync(writePassword).ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }, TaskScheduler.Default));
writePassword = null;
controller.LoadSpeciesAtStartup();
Check(Wait(() => controller.SpeciesReady.IsCompleted, 30), "species loaded");
Check(controller.Request.Species.Count > 0, $"{controller.Request.Species.Count} species");
Check(controller.Request.Seeds.Count == 1, "default seed present");

// ---- a short live run
controller.Request.HoursPerTick = 4;
controller.Request.TotalHours = hours;
controller.Request.RenderMode = renderMode;
var expectedSteps = hours / 4;
var sw = Stopwatch.StartNew();
_ = controller.StartAsync();
Check(Wait(() => controller.Session.RunId.Length > 0, 60), "RunStarted received");
Check(Wait(() => controller.Session.Site.TimeZone != null, 60), $"RunSite received (zone {controller.Session.Site.TimeZone}, {controller.Session.Site.Coverage.Length} coverage values)");
Check(Wait(() => sink.Shown.Count > 0, 120), "first preview frame shown");
Check(Wait(() => !controller.Session.Computing, 600), $"run finished in {sw.Elapsed.TotalSeconds:F1} s");
Check(controller.Session.SimStep >= (uint)expectedSteps, $"progress reached {controller.Session.SimStep} of {expectedSteps}");
Check(Wait(() => controller.History.Count >= expectedSteps, 60), $"timeline synced to {controller.History.Count} frames (expected {expectedSteps})");
Check(controller.Session.RunStored == true, "run stored on the server");
Check(controller.Playback.Playing == PlayState.None, $"playback rests ({controller.Playback.Playing})");
Check(controller.Playback.Pointer == controller.History.Count - 1, $"pointer at the last frame ({controller.Playback.Pointer})");
Check(sink.Shown.Count >= 2, $"{sink.Shown.Count} frames shown during the run");
Check(string.IsNullOrEmpty(controller.Session.StopReason), $"no stop reason ('{controller.Session.StopReason}')");
var liveRunId = controller.Session.RunId;

// ---- the frames of the finished run decode
var lib = new GodotFrontend.Core.Geometry.GeometryLibrary();
var decoder = new SceneDecoder(lib);
var decodedPlants = -1;
foreach (var idx in new[] { 0, controller.History.Count / 2, controller.History.Count - 1 })
{
    var frame = controller.History[idx];
    var resolved = Run(controller.Frames.ResolveAsync(frame, liveRunId, CancellationToken.None));
    Check(resolved != null, $"frame {frame.Step} resolved ({frame.Kind})");
    if (resolved is { } f)
    {
        var raw = f.Deflated ? Compressor.Inflate(f.Bytes) : f.Bytes;
        Check(raw.Length > 1 && raw[0] is 7 or 8 or 9, $"frame {frame.Step}: version {raw[0]}, {raw.Length} bytes");
        var scene = decoder.Decode(raw);
        decodedPlants = scene.PlantCount;
    }
}
Check(decodedPlants >= 0, $"last frame decoded with {decodedPlants} plant(s)");

// ---- open the same run again purely over REST
var opened = Run(controller.OpenStoredAsync(liveRunId));
Check(opened, "stored run opened");
Check(sink.Soil == "box", $"a plain-field run is drawn on its own field box (soil: {sink.Soil})");
Check(controller.Request.Seeds.Count == 1, $"the run's own seed is shown ({controller.Request.Seeds.Count})");
Check(Wait(() => controller.History.Count >= expectedSteps, 60), $"stored timeline has {controller.History.Count} frames");
Check(controller.History.EvictedCount == controller.History.Count, "stored frames are fetched on demand (none resident)");
controller.Seek(controller.History.Count / 2);
Check(Wait(() => sink.Shown.Count > 0 && sink.Shown[^1].step == controller.History.StepAt(controller.History.Count / 2), 60), "seek fetched the frame from the server");

// ---- abort mid-run
controller.Request.TotalHours = 24 * 400;
sw.Restart();
_ = controller.StartAsync();
Check(Wait(() => controller.Session.SimStep >= 10, 120), "second run reached step 10");
controller.Abort();
Check(Wait(() => !controller.Session.Computing, 120), $"abort stopped the run in {sw.Elapsed.TotalSeconds:F1} s (stop reason '{controller.Session.StopReason}')");

// ---- runs listing
var runs = Run(rest.GetRunsAsync());
Check(runs.Any(r => r.Id == liveRunId), $"runs listing contains the live run ({runs.Count} runs)");
var store = Run(rest.GetStoreAsync());
Check(store != null && store.Enabled, "store enabled");

// A run built on an imported model is drawn on its trays, or on nothing when the server holds none - never on the
// field box of whatever scenario was on screen before.
foreach (var modelRun in runs.Where(r => !string.IsNullOrEmpty(r.FieldModelName) && r.StoredFrames > 0).Take(2))
{
    Run(controller.OpenStoredAsync(liveRunId));
    var ok = Run(controller.OpenStoredAsync(modelRun.Id));
    Check(ok && sink.Soil == (modelRun.HasTerrain ? "terrain" : "none"),
        $"model run {modelRun.Id} opened over a box run shows {sink.Soil} (terrain stored: {modelRun.HasTerrain})");
}
// Two opens in flight: the later one wins, whatever order the server answers in.
{
    var first = controller.OpenStoredAsync(liveRunId);
    var second = controller.OpenStoredAsync(liveRunId);
    Check(!Run(first) && Run(second), "an open superseded by a later one reports false and leaves the later one standing");
}

Console.WriteLine(failures.Count == 0 ? "ALL PASSED" : $"{failures.Count} FAILED");
// The hub is disposed after this line; nothing drains the pump any more, so let continuations use the thread pool.
SynchronizationContext.SetSynchronizationContext(null);
return failures.Count == 0 ? 0 : 1;

/// <summary>Stands in for the renderer: records shown frames and presents them immediately.</summary>
sealed class RecordingSink : IFrameSink
{
    public readonly List<(int index, uint step, int bytes, bool deflated)> Shown = new();
    public int Terrains, FieldBoxes;
    public event Action<int>? FramePresented;

    public void ShowFrame(int historyIndex, uint step, ResolvedFrame frame)
    {
        Shown.Add((historyIndex, step, frame.Bytes.Length, frame.Deflated));
        FramePresented?.Invoke(historyIndex);
    }

    public void ShowNothing() { }
    public void ShowTerrain(TerrainData terrain) { Terrains++; Soil = "terrain"; }
    public void ShowFieldBox(float sizeX, float sizeD, float sizeZ) { FieldBoxes++; Soil = "box"; }
    public void ClearTerrain() => Soil = "none";
    /// <summary>What the soil on screen is after the last call: box, terrain or none.</summary>
    public string Soil = "box";
}
