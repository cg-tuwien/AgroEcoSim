using GodotFrontend.Core.History;
using GodotFrontend.Core.Model;
using GodotFrontend.Core.Net;
using GodotFrontend.Core.Net.Dto;
using GodotFrontend.Core.Persist;
using GodotFrontend.Core.Playback;
using GodotFrontend.Core.Scene;
using GodotFrontend.Core.Threading;

namespace GodotFrontend.Core.Run;

/// <summary>
/// The run lifecycle of appstate.ts, engine-free: start (upload + Start + arm), the hub messages, the preview pump,
/// the history budget, playback stepping, timeline sync with the server index, stored-run opening and reattaching
/// after a restart. Every hub or HTTP continuation is posted to the main thread; everything public is main-thread.
/// </summary>
public sealed class RunController
{
    private const int TimelineSyncIntervalMs = 2000;
    private const int TimelineSyncLagFrames = 4;

    public readonly IMainThread Main;
    public readonly RestClient Rest;
    public readonly HubClient Hub;
    public readonly History.History History = new();
    public readonly PlaybackMachine Playback = new();
    public readonly RunSession Session = new();
    public readonly RequestState Request = new();
    public readonly PreviewPump Pump;
    public readonly FrameSource Frames;
    public readonly Compressor Compressor;
    public readonly DiskFrameCache? DiskCache;
    public string[] Behaviors = Array.Empty<string>();
    public readonly ViewState View = new();
    /// <summary>The trays and imported obstacles on screen, or null for the plain field box.</summary>
    public TerrainData? Terrain { get; private set; }

    private RunArchiveReader? archive;
    private System.Text.Json.Nodes.JsonObject? loadedMeta;

    /// <summary>Species the server knows, loaded at startup; a stored run opened before this landed must not be overwritten.</summary>
    public Task SpeciesReady { get; private set; } = Task.CompletedTask;

    private readonly IFrameSink sink;
    private readonly Random rng = new();
    private int showToken;
    private bool previewAfterShow;
    private bool timelineSyncing;
    private DateTime lastTimelineSync = DateTime.MinValue;
    private readonly Dictionary<string, string> frameRevisions = new();
    private int pendingShows;

    /// <summary>Raised on the main thread whenever something the UI shows changed.</summary>
    public event Action? Changed;
    public event Action<string>? Log;
    /// <summary>Raised when the species list was replaced (the renderer fetches leaf/petal meshes).</summary>
    public event Action<IReadOnlyList<Agro.SpeciesSettings>>? SpeciesChanged;
    /// <summary>Raised when a session load replaced the view state (mapping, toggles, lighting) or the request.</summary>
    public event Action? ViewChanged;
    /// <summary>Called when the client wants the last run id remembered (settings) or forgotten.</summary>
    public Action<string>? RememberRun;

    public RunController(IMainThread main, RestClient rest, HubClient hub, IFrameSink sink, DiskFrameCache? diskCache)
    {
        Main = main;
        Rest = rest;
        Hub = hub;
        this.sink = sink;
        DiskCache = diskCache;
        Pump = new PreviewPump(hub, main);
        Pump.Log += m => Log?.Invoke(m);
        Frames = new FrameSource(diskCache) { FetchPacked = (id, step, ct) => rest.GetFramePackedAsync(id, step, ct) };
        Compressor = new Compressor(History, main);
        Session.HistoryBudget = MemoryBudget.Compute(false);

        hub.Rejected += () => main.Post(OnRejected);
        hub.Preparation += (s, t) => main.Post(() => { Session.Preparation = t > 0 && s < t ? (s, t) : null; Changed?.Invoke(); });
        hub.Progress += (s, l) => main.Post(() => OnProgress(s, l));
        hub.Result += r => main.Post(() => OnResult(r));
        hub.RunStarted += id => main.Post(() => OnRunStarted(id));
        hub.RunSite += s => main.Post(() => OnRunSite(s));
        hub.Preview += p => main.Post(() => OnPreview(p));
        hub.Terrain += b => main.Post(() => OnTerrain(b));
        hub.Reconnected += id => main.Post(() => _ = OnReconnectedAsync());
        hub.Reconnecting += e => main.Post(() => Log?.Invoke($"hub reconnecting: {e?.Message}"));
        hub.Closed += e => main.Post(() => Log?.Invoke($"hub closed: {e?.Message ?? "by request"}"));

        sink.FramePresented += OnFramePresented;
        Playback.Show += ShowIndex;
    }

    // ------------------------------------------------------------------ startup

    public void LoadSpeciesAtStartup()
    {
        var tcs = new TaskCompletionSource();
        SpeciesReady = tcs.Task;
        _ = Task.Run(async () =>
        {
            try
            {
                var list = await Rest.GetSpeciesAsync().ConfigureAwait(false);
                string[] behaviors;
                try { behaviors = await Rest.GetBehaviorsAsync().ConfigureAwait(false); } catch { behaviors = Array.Empty<string>(); }
                Main.Post(() =>
                {
                    Behaviors = behaviors;
                    if (Request.Species.Count == 0 || Session.ViewingRunId.Length == 0)
                    {
                        Request.Species = list;
                        if (Request.Seeds.Count == 0 && list.Count > 0) Request.AddDefaultSeed(list[0].Name);
                        SpeciesChanged?.Invoke(list);
                    }
                    Log?.Invoke($"server {Rest.Endpoint}: {list.Count} species");
                    Changed?.Invoke();
                    tcs.TrySetResult();
                });
            }
            catch (Exception ex)
            {
                Main.Post(() =>
                {
                    Session.LastError = $"species: {ex.Message}";
                    Log?.Invoke($"could not load species from {Rest.Endpoint}: {ex.Message}");
                    Changed?.Invoke();
                    tcs.TrySetResult();
                });
            }
        });
    }

    // ------------------------------------------------------------------ run

    public bool CanStart => !Session.Computing && Request.Seeds.Count > 0;

    /// <summary>The Simulate button: aborts when computing, starts otherwise.</summary>
    public void SimulateOrStop()
    {
        if (Session.Computing) Abort();
        else _ = StartAsync();
    }

    public void Abort()
    {
        _ = Task.Run(async () =>
        {
            try { await Hub.AbortAsync().ConfigureAwait(false); }
            catch (Exception ex) { Main.Post(() => Log?.Invoke($"abort failed: {ex.Message}")); }
        });
    }

    public async Task StartAsync()
    {
        if (Session.Computing) return;
        Session.SimHoursPerTick = Time.SimClock.Snap(Request.HoursPerTick);
        Session.InitialDate = Request.InitialDate;
        ResetForNewRun();
        Session.Computing = true;
        Playback.Computing = true;
        Playback.Request(PlayState.ForwardWaiting, 0);
        Changed?.Invoke();
        try
        {
            await Hub.ConnectAsync().ConfigureAwait(true);
            var request = Request.ToRequest(rng);
            var prepared = await Rest.UploadAsync(request).ConfigureAwait(true);
            if (string.IsNullOrEmpty(prepared)) throw new InvalidOperationException("the server returned no prepared id");
            _ = Hub.StartRunAsync(prepared).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    Main.Post(() =>
                    {
                        Session.Computing = false;
                        Playback.Computing = false;
                        Session.Preparation = null;
                        Session.Renderer = "error";
                        Session.LastError = t.Exception?.GetBaseException().Message ?? "start failed";
                        Log?.Invoke($"start failed: {Session.LastError}");
                        Playback.Stop();
                        Changed?.Invoke();
                    });
            }, TaskScheduler.Default);
            Pump.Arm();
        }
        catch (Exception ex)
        {
            Session.Computing = false;
            Playback.Computing = false;
            Session.LastError = ex.Message;
            Log?.Invoke($"could not start: {ex.Message}");
            Playback.Stop();
            Changed?.Invoke();
        }
    }

    private void ResetForNewRun()
    {
        Session.Preparation = null;
        Session.Renderer = "";
        Session.HistoryLimitHit = false;
        Session.RunId = "";
        Session.RunStored = null;
        Session.ViewingRunId = "";
        Session.StopReason = "";
        Session.ObstacleLight = null;
        Session.SimStep = 0;
        Session.SimLength = 0;
        Session.LastError = "";
        Session.HistoryBudget = MemoryBudget.Compute(false);
        RememberRun?.Invoke("");
        History.Reset();
        Frames.Archive = null;
        archive?.Dispose();
        archive = null;
        loadedMeta = null;
        Playback.LastReceivedIndex = -1;
        Playback.SetPointer(0);
        showToken++;
        sink.ShowNothing();
    }

    // ------------------------------------------------------------------ hub messages

    private void OnRejected()
    {
        Session.Computing = false;
        Playback.Computing = false;
        Session.Preparation = null;
        Log?.Invoke("A simulation is already running on this connection. Wait for it to finish, or stop it first.");
        Playback.Stop();
        Changed?.Invoke();
    }

    private void OnProgress(uint step, uint length)
    {
        Session.SimStep = step + 1;
        Session.SimLength = length;
        MaybeSyncTimeline(step + 1);
        Changed?.Invoke();
    }

    private void OnRunStarted(string runId)
    {
        Session.RunId = runId;
        Session.RunStored = null;
        Session.HistoryBudget = MemoryBudget.Compute(false);
        RememberRun?.Invoke(runId);
        Pump.TryFlush();
        _ = ConfirmStoredAsync(runId);
        Changed?.Invoke();
    }

    private async Task ConfirmStoredAsync(string runId, int attempts = 3, int retryMs = 2000)
    {
        for (var attempt = 0; attempt < attempts; ++attempt)
        {
            if (Session.RunId != runId) return;
            bool? answer = null;
            try
            {
                var meta = await Rest.GetRunAsync(runId).ConfigureAwait(true);
                answer = meta != null;
            }
            catch { }
            if (Session.RunId != runId) return;
            if (answer is bool a)
            {
                Session.RunStored = a;
                Session.HistoryBudget = MemoryBudget.Compute(a);
                if (!a) Log?.Invoke($"Run {runId} is not being stored by the server; its frames are kept in memory.");
                Changed?.Invoke();
                return;
            }
            if (attempt + 1 < attempts) await Task.Delay(retryMs).ConfigureAwait(true);
        }
    }

    private void OnRunSite(RunSiteDto s)
    {
        Session.Site = new SiteInfo
        {
            Latitude = s.Latitude, Longitude = s.Longitude, Altitude = s.Altitude,
            AzimuthBase = s.AzimuthBase, AzimuthOffset = s.AzimuthOffset,
            TimeZone = string.IsNullOrEmpty(s.TimeZone) ? null : s.TimeZone,
            Coverage = s.Coverage(),
        };
        Changed?.Invoke();
    }

    private void OnPreview(PreviewDto p)
    {
        var index = History.Upsert(p.Step, p.Scene);
        Playback.LastReceivedIndex = index;
        Session.Renderer = p.Renderer;
        Compressor.Kick();
        EnforceBudget();
        if (Playback.AtLiveEdge)
        {
            // The web's preview handler draws the arrived frame itself; the state machine only moves the pointer.
            // Relying on the machine's "pointer moved" test would never draw the first frame (index 0 = pointer 0)
            // and, with nothing presented, the pump would never re-arm.
            previewAfterShow = true;
            Playback.SetPointer(index);
            ShowIndex(index);
        }
        else
        {
            Pump.Arm();
        }
        Changed?.Invoke();
    }

    private void OnResult(ResultDto r)
    {
        Session.Preparation = null;
        if (!string.IsNullOrEmpty(r.Debug)) Log?.Invoke(r.Debug!);
        var completed = r.CompletedSteps ?? (uint)(r.StepTimes?.Count ?? 0);
        var step = (long)completed - 1;
        if (step >= 0 && r.Scene is { Length: > 0 } && (History.Count == 0 || History[History.Count - 1].Step < step))
        {
            Playback.LastReceivedIndex = History.Upsert((uint)step, r.Scene);
            // The final frame is what the run ends on; a run faster than the preview round trip never followed it.
            Playback.SetPointer(Playback.LastReceivedIndex);
            ShowIndex(Playback.LastReceivedIndex);
        }
        else if (completed == 0)
        {
            sink.ShowNothing();
        }
        Session.Computing = false;
        Session.Renderer = r.Renderer;
        Session.SimLength = 0;
        Session.ObstacleLight = r.ObstacleLight;
        Session.StopReason = r.StopReason ?? "";
        if (!string.IsNullOrEmpty(r.StopReason)) Log?.Invoke($"The run stopped early: {r.StopReason}.");
        Playback.RunEnded(History.Count);
        if (Session.RunId.Length > 0) _ = SyncTimelineAsync(Session.RunId, false);
        Changed?.Invoke();
    }

    private void OnTerrain(byte[] bytes)
    {
        try
        {
            var terrain = TerrainDecoder.Decode(bytes);
            SetTerrain(terrain);
        }
        catch (Exception ex) { Log?.Invoke($"terrain: {ex.Message}"); }
        Changed?.Invoke();
    }

    private async Task OnReconnectedAsync()
    {
        Log?.Invoke("hub reconnected");
        var runId = Session.RunId;
        if (runId.Length == 0) { Pump.TryFlush(); return; }
        try
        {
            if (History.Count == 0) await SyncTimelineAsync(runId, true).ConfigureAwait(true);
            var attached = await Hub.SubscribeAsync(runId).ConfigureAwait(true);
            if (!attached)
            {
                Log?.Invoke($"run {runId} is no longer computing");
                Session.Computing = false;
                Playback.Computing = false;
                Playback.RunEnded(History.Count);
            }
            else Pump.TryFlush();
        }
        catch (Exception ex) { Log?.Invoke($"resubscribe failed: {ex.Message}"); }
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ memory

    private void EnforceBudget()
    {
        if (History.ResidentBytes <= Session.HistoryBudget) return;
        if (Session.FramesRecoverable)
        {
            History.EvictToBudget(Playback.Pointer, Session.HistoryBudget, true, out var stillOver);
            if (stillOver) StopForMemory();
        }
        else StopForMemory();
    }

    private void StopForMemory()
    {
        if (Session.HistoryLimitHit) return;
        Session.HistoryLimitHit = true;
        Log?.Invoke("history is full - stopping the run; the frames computed so far stay in memory");
        if (Session.Computing) Abort();
    }

    // ------------------------------------------------------------------ playback

    /// <summary>Transport controls.</summary>
    public void Play(PlayState state)
    {
        Playback.Request(state, History.Count);
        Playback.Step(History.Count);
        Changed?.Invoke();
    }

    public void Seek(int index)
    {
        Playback.SeekTo(index, History.Count);
        Playback.Step(History.Count);
        Changed?.Invoke();
    }

    private void ShowIndex(int index)
    {
        if (History.Count == 0) return;
        index = History.ClampIndex(index);
        var e = History[index];
        var token = ++showToken;
        pendingShows++;
        var runId = Session.RunId;
        _ = Task.Run(async () =>
        {
            ResolvedFrame? resolved = null;
            string? error = null;
            try { resolved = await Frames.ResolveAsync(e, runId, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { error = ex.Message; }
            Main.Post(() =>
            {
                pendingShows--;
                if (token != showToken) return; // a newer show superseded this one
                if (resolved is { } f) sink.ShowFrame(index, e.Step, f);
                else
                {
                    if (error != null) Log?.Invoke($"frame {e.Step}: {error}");
                    OnFramePresented(index); // nothing to draw; keep the machine moving
                }
            });
        });
    }

    private void OnFramePresented(int index)
    {
        if (previewAfterShow)
        {
            previewAfterShow = false;
            Pump.Arm();
        }
        var delay = Playback.AtLiveEdge ? 0 : 20;
        if (Playback.IsPlaying) Main.PostDelayed(delay, () => Playback.Step(History.Count));
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ timeline

    private void MaybeSyncTimeline(uint serverSteps)
    {
        var runId = Session.RunId;
        if (runId.Length == 0 || timelineSyncing) return;
        if (serverSteps - History.Count < TimelineSyncLagFrames) return;
        var now = DateTime.UtcNow;
        if ((now - lastTimelineSync).TotalMilliseconds < TimelineSyncIntervalMs) return;
        lastTimelineSync = now;
        _ = SyncTimelineAsync(runId, false);
    }

    /// <summary>Merges the server's stored index into the timeline by step; returns the merged count (0 on failure).</summary>
    public async Task<int> SyncTimelineAsync(string runId, bool seekToEnd)
    {
        if (timelineSyncing) return History.Count;
        timelineSyncing = true;
        try
        {
            RunIndex index;
            try { index = await Rest.GetRunIndexAsync(runId).ConfigureAwait(true); }
            catch (Exception ex) { Log?.Invoke($"could not read the frame index of run {runId}: {ex.Message}"); return 0; }
            if (Session.RunId != runId) return 0;
            var replaced = frameRevisions.TryGetValue(runId, out var prev) ? prev != index.Revision : index.Revision != "0";
            frameRevisions[runId] = index.Revision;
            if (index.Steps.Length == 0 && !replaced) return 0;
            var atPointerStep = History.Count > 0 ? History.StepAt(Playback.Pointer) : (uint?)null;
            var receivedStep = Playback.LastReceivedIndex >= 0 && Playback.LastReceivedIndex < History.Count ? History[Playback.LastReceivedIndex].Step : (uint?)null;
            var outcome = History.MergeIndex(index.Steps, replaced, index.RestartStep);
            if (!outcome.Changed && !replaced) return History.Count;
            Playback.LastReceivedIndex = receivedStep is uint rs ? System.Math.Max(-1, History.IndexOfStep(rs)) : -1;
            var restored = seekToEnd || atPointerStep is null
                ? System.Math.Max(0, History.Count - 1)
                : System.Math.Max(0, History.IndexOfStep(atPointerStep.Value));
            var moved = restored != Playback.Pointer;
            Playback.SetPointer(restored);
            if (moved || replaced) ShowIndex(restored);
            Log?.Invoke($"timeline synced to {History.Count} stored frames of run {runId}");
            Changed?.Invoke();
            return History.Count;
        }
        finally { timelineSyncing = false; }
    }

    // ------------------------------------------------------------------ stored runs

    /// <summary>View a stored run: terrain, request, site, then the timeline from the index (frames fetched on demand).</summary>
    public async Task<bool> OpenStoredAsync(string runId, bool allowEmpty = false)
    {
        if (Session.Computing) { Log?.Invoke("stop the running simulation first"); return false; }
        RunDto? meta;
        try { meta = await Rest.GetRunAsync(runId).ConfigureAwait(true); }
        catch (Exception ex) { Log?.Invoke($"run {runId}: {ex.Message}"); return false; }
        if (meta == null) { Log?.Invoke($"run {runId} is not on the server"); return false; }
        if (meta.StoredFrames == 0 && !allowEmpty) { Log?.Invoke($"run {runId} stored no frames"); return false; }

        ResetForNewRun();
        Session.RunId = runId;
        Session.RunStored = true;
        Session.ViewingRunId = runId;
        Session.SimHoursPerTick = meta.HoursPerTick;
        Session.InitialDate = meta.InitialDay;
        Session.HistoryBudget = MemoryBudget.Compute(true);
        Session.StopReason = meta.StopReason ?? "";
        RememberRun?.Invoke(runId);
        Changed?.Invoke();

        await RestoreStoredTerrainAsync(runId, meta.HasTerrain).ConfigureAwait(true);
        await RestoreStoredScenarioAsync(runId).ConfigureAwait(true);
        var count = await SyncTimelineAsync(runId, true).ConfigureAwait(true);
        if (count == 0 && !allowEmpty) { Log?.Invoke($"run {runId} has no readable frames"); }
        if (meta.IsRunning)
        {
            await ReattachAsync(runId, meta).ConfigureAwait(true);
        }
        Changed?.Invoke();
        return true;
    }

    private async Task RestoreStoredTerrainAsync(string runId, bool hasTerrain)
    {
        if (!hasTerrain) { SetTerrain(null); return; }
        try
        {
            var bytes = await Rest.GetTerrainAsync(runId).ConfigureAwait(true);
            if (bytes != null) SetTerrain(TerrainDecoder.Decode(bytes));
        }
        catch (Exception ex) { Log?.Invoke($"terrain of run {runId}: {ex.Message}"); }
    }

    private async Task RestoreStoredScenarioAsync(string runId)
    {
        try
        {
            await SpeciesReady.ConfigureAwait(true);
            var request = await Rest.GetRequestAsync(runId).ConfigureAwait(true);
            if (request != null)
            {
                Request.FromRequest(request);
                SpeciesChanged?.Invoke(Request.Species);
                if (Terrain == null) sink.ShowFieldBox(Request.FieldSizeX, Request.FieldSizeD, Request.FieldSizeZ);
            }
            var site = await Rest.GetSiteAsync(runId).ConfigureAwait(true);
            if (site != null) OnRunSite(site);
        }
        catch (Exception ex) { Log?.Invoke($"scenario of run {runId}: {ex.Message}"); }
    }

    private async Task ReattachAsync(string runId, RunDto meta)
    {
        try
        {
            await Hub.ConnectAsync().ConfigureAwait(true);
            var attached = await Hub.SubscribeAsync(runId).ConfigureAwait(true);
            if (!attached) { Log?.Invoke($"run {runId} finished meanwhile"); return; }
            Session.Computing = true;
            Playback.Computing = true;
            Session.SimLength = meta.TotalSteps;
            Session.SimStep = meta.LastStep;
            Playback.Request(PlayState.ForwardWaiting, History.Count);
            Pump.Arm();
        }
        catch (Exception ex) { Log?.Invoke($"could not reattach to run {runId}: {ex.Message}"); }
    }

    /// <summary>Startup: reattach to the run this client last started or viewed, if the server still has it.</summary>
    public async Task RecoverRunAsync(string? lastRunId)
    {
        if (string.IsNullOrEmpty(lastRunId)) return;
        var ok = await OpenStoredAsync(lastRunId!, allowEmpty: true).ConfigureAwait(true);
        if (!ok) RememberRun?.Invoke("");
    }

    public void SetTerrain(TerrainData? terrain)
    {
        Terrain = terrain;
        if (terrain != null) sink.ShowTerrain(terrain);
        else sink.ShowFieldBox(Request.FieldSizeX, Request.FieldSizeD, Request.FieldSizeZ);
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ sessions

    /// <summary>Loads a session saved by this app or the web app (.agro archive or legacy .json).</summary>
    public async Task LoadSessionAsync(string path)
    {
        if (Session.Computing) throw new InvalidOperationException("stop the running simulation first");
        RunArchiveReader? reader = null;
        System.Text.Json.Nodes.JsonNode meta;
        List<(uint Step, byte[] Bytes)>? legacy = null;
        if (RunArchive.IsArchive(path))
        {
            reader = await Task.Run(() => RunArchiveReader.Open(path)).ConfigureAwait(true);
            meta = reader.Metadata;
        }
        else
        {
            var read = await Task.Run(() => LegacySessionJson.Read(path)).ConfigureAwait(true);
            meta = read.Metadata;
            legacy = read.Frames;
        }
        await SpeciesReady.ConfigureAwait(true);
        var contents = SessionMeta.Apply(meta, Request.Species);

        ResetForNewRun();
        archive = reader;
        loadedMeta = contents.Original;
        CopyInto(Request, contents.Request);
        View.Mapping = contents.View.Mapping;
        View.DebugBoxes = contents.View.DebugBoxes;
        View.ShowLeaves = contents.View.ShowLeaves;
        View.ShowTerrain = contents.View.ShowTerrain;
        View.ShowRoots = contents.View.ShowRoots;
        View.ShowObstacles = contents.View.ShowObstacles;
        View.ShowSeeds = contents.View.ShowSeeds;
        View.PerformPicking = contents.View.PerformPicking;
        View.Lighting = contents.View.Lighting;
        View.ObstacleMapping = contents.View.ObstacleMapping;
        if (contents.Behaviors.Length > 0) Behaviors = contents.Behaviors;
        Session.SimHoursPerTick = contents.SimHoursPerTick;
        Session.InitialDate = contents.Request.InitialDate;
        Session.Site = contents.Site;
        Session.ObstacleLight = contents.ObstacleLight;
        Session.HistoryBudget = MemoryBudget.Compute(false);
        SetTerrain(contents.Terrain);
        if (reader != null)
        {
            Frames.Archive = reader;
            foreach (var f in reader.Frames) History.AddArchived(f.Step, f.Offset, f.Length, f.Plain);
        }
        else if (legacy != null)
        {
            foreach (var (step, bytes) in legacy) History.Upsert(step, bytes);
        }
        History.Remeasure();
        Playback.LastReceivedIndex = History.Count - 1;
        SpeciesChanged?.Invoke(Request.Species);
        ViewChanged?.Invoke();
        Playback.SetPointer(0);
        if (History.Count > 0) ShowIndex(0);
        Compressor.Kick();
        Log?.Invoke($"loaded {Path.GetFileName(path)}: {History.Count} frames");
        Changed?.Invoke();
    }

    private static void CopyInto(RequestState dst, RequestState src)
    {
        foreach (var field in typeof(RequestState).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
            field.SetValue(dst, field.GetValue(src));
    }

    /// <summary>Saves the timeline and the scenario as an .agro archive the web app can open.</summary>
    public async Task SaveSessionAsync(string path, IProgress<int>? progress = null)
    {
        var runId = Session.RunId;
        string? revision = null;
        if (runId.Length > 0 && Session.RunStored == true)
        {
            await SyncTimelineAsync(runId, false).ConfigureAwait(true);
            revision = frameRevisions.TryGetValue(runId, out var rv) ? rv : "0";
        }
        var contents = new SessionContents
        {
            Request = Request, View = View, Terrain = Terrain, SimHoursPerTick = Session.SimHoursPerTick,
            Site = Session.Site, ObstacleLight = Session.ObstacleLight, Behaviors = Behaviors, Original = loadedMeta,
        };
        var meta = SessionMeta.Build(contents, History.ResidentBytes);
        var frames = History.Frames.ToArray();
        var source = Frames;
        var measurementBytes = archive?.ReadMeasurement();
        Func<Task<Stream?>>? measurement = null;
        if (measurementBytes != null) measurement = () => Task.FromResult<Stream?>(new MemoryStream(measurementBytes));
        else if (runId.Length > 0 && Session.ObstacleLight is { } doc && doc.TryGetProperty("complete", out var complete) && complete.ValueKind == System.Text.Json.JsonValueKind.False)
            measurement = async () =>
            {
                var b = await Rest.GetObstacleLightPackedAsync(runId).ConfigureAwait(false);
                if (b == null) throw new IOException("the full leaf area index document could not be fetched");
                return new MemoryStream(b);
            };
        await Task.Run(() => RunArchiveWriter.WriteAsync(path, meta, frames.Length, async i =>
        {
            var e = frames[i];
            var r = await source.ResolveAsync(e, runId, CancellationToken.None).ConfigureAwait(false)
                ?? throw new IOException($"frame {e.Step} has no stored source");
            return new ArchiveFrame(e.Step, r.Bytes, !r.Deflated);
        }, measurement, progress)).ConfigureAwait(true);
        if (revision != null)
        {
            var now = await Rest.GetRunIndexAsync(runId).ConfigureAwait(true);
            if (now.Revision != revision) throw new IOException("The run was resumed while saving. Please retry.");
        }
        Log?.Invoke($"saved {Path.GetFileName(path)}: {frames.Length} frames");
    }

    /// <summary>The raw scene bytes of the frame at the playhead (the web's .prim download).</summary>
    public async Task<byte[]?> CurrentFrameBytesAsync()
    {
        if (History.Count == 0) return null;
        var e = History[History.ClampIndex(Playback.Pointer)];
        var r = await Frames.ResolveAsync(e, Session.RunId, CancellationToken.None).ConfigureAwait(true);
        if (r is not { } f) return null;
        return f.Deflated ? Compressor.Inflate(f.Bytes) : f.Bytes;
    }

    public uint CurrentStep => History.Count == 0 ? 0 : History.StepAt(Playback.Pointer);

    public void ForgetRun()
    {
        Session.RunId = "";
        Session.RunStored = null;
        Session.ViewingRunId = "";
        RememberRun?.Invoke("");
        Changed?.Invoke();
    }
}
