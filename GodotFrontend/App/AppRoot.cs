using Godot;
using GodotFrontend.App.Render;
using GodotFrontend.App.UI;
using GodotFrontend.App.UI.Panels;
using GodotFrontend.App.UI.Widgets;
using GodotFrontend.Core.Cli;
using GodotFrontend.Core.Color;
using GodotFrontend.Core.Geometry;
using GodotFrontend.Core.History;
using GodotFrontend.Core.Lighting;
using GodotFrontend.Core.Math;
using GodotFrontend.Core.Net;
using GodotFrontend.Core.Packing;
using GodotFrontend.Core.Persist;
using GodotFrontend.Core.Picking;
using GodotFrontend.Core.Run;
using GodotFrontend.Core.Scene;
using GodotFrontend.Core.Stereo;
using GodotFrontend.Core.Time;
using Ray = GodotFrontend.Core.Math.Ray;

namespace GodotFrontend.App;

/// <summary>
/// Root node of Main.tscn. Builds the control tree in code (the 3D viewport and a side panel at most a quarter of
/// the window wide, as the web app), composes the engine-free core (run controller, history, decoder, picker) with
/// the Godot adapters (MultiMesh batches, terrain, lighting, camera), and owns their lifetime.
/// </summary>
public partial class AppRoot : Control
{
    public DebugArgs Args { get; private set; } = new();
    public Settings Settings { get; private set; } = null!;
    public GodotMainThread Main { get; private set; } = null!;
    public InputRouter ViewportPanel { get; private set; } = null!;
    public SubViewport World { get; private set; } = null!;
    public CameraRig Rig { get; private set; } = null!;
    public PanelContainer SidePanel { get; private set; } = null!;
    public Dialogs Dialogs { get; private set; } = null!;

    public GeometryLibrary Library { get; } = new();
    public GeometryUploader Uploader { get; private set; } = null!;
    public MaterialSet Materials { get; private set; } = null!;
    public PlantBatches Batches { get; private set; } = null!;
    public TerrainBuilder Terrain { get; private set; } = null!;
    public SceneDecodeService Decoder { get; private set; } = null!;
    public RendererBridge Bridge { get; private set; } = null!;
    public RunController Controller { get; private set; } = null!;
    public LightingRig Lighting { get; private set; } = null!;
    public StereoRig Stereo { get; private set; } = null!;
    public double LastUploadMs { get; set; }

    private SeedMarkers seedMarkers = null!;
    private SceneRaycaster raycaster = null!;
    private readonly PickStore pickStore = new();
    private readonly List<SidePanelTab> tabs = new();
    private Label statusLabel = null!;
    private WriteAccessPanel accessPanel = null!;
    private TabContainer tabContainer = null!;
    private ScreenshotRunner? screenshot;
    private int screenshotIndex = -2;
    private bool dirty = true;
    /// <summary>Set when --mapping, --lighting or a stereo flag forced the view: nothing about the view is persisted then.</summary>
    private bool viewFromCommandLine;
    private const double KeepDrawingSeconds = 1.0;
    /// <summary>The project's low-processor setting, restored once a burst of continuous drawing ends.</summary>
    private bool lowProcessorMode = true;
    private ulong keepDrawingUntilUs;
    private string lightingKey = "";
    private int framesSinceReady;
    private double timeoutLeft;
    private readonly HashSet<string> fetchedSpecies = new();
    private readonly Queue<(Vector2 pos, InputRouter.PointerPhase phase)> pointerEvents = new();
    private Vector2? pendingMove;
    private TerrainData? raycastTerrain;
    private Queue<System.Text.Json.JsonElement>? inputReplay;
    private readonly bool debugPicking = OS.GetCmdlineUserArgs().Contains("--debug-picking");
    private int replayDelay = 10;
    private bool replayDraining;
    private MouseButton replayHeld = MouseButton.None;
    private bool replayShift, replayCtrl, replayMeta;

    public override void _Ready()
    {
        try { Args = DebugArgs.Parse(OS.GetCmdlineUserArgs()); }
        catch (Exception ex)
        {
            GD.PrintErr($"command line: {ex.Message}");
            GetTree().Quit(2);
            return;
        }
        foreach (var u in Args.Unknown) GD.PrintErr($"command line: unknown argument '{u}'");
        timeoutLeft = Args.TimeoutSeconds;
        lowProcessorMode = OS.LowProcessorUsageMode;
        Settings = Settings.Load(Args.SettingsPath is { } settingsPath ? ProjectSettings.GlobalizePath(settingsPath)
            : System.IO.Path.Combine(OS.GetUserDataDir(), "settings.json"));
        Theme = AppTheme.Build();
        RestoreWindow();

        Main = new GodotMainThread { Name = "MainThread" };
        AddChild(Main);
        Dialogs = new Dialogs { Name = "Dialogs" };
        AddChild(Dialogs);
        Uploader = new GeometryUploader(Library);
        Materials = new MaterialSet();
        Decoder = new SceneDecodeService(Main, Library);
        raycaster = new SceneRaycaster(Library);

        var endpoint = ServerEndpoint.Resolve(Args.Server, System.Environment.GetEnvironmentVariable("AGRO_SERVER"), Settings.ServerUrl);
        Settings.ServerUrl ??= endpoint.ToString();
        var rest = new RestClient(endpoint);
        var hub = new HubClient(endpoint);
        var cache = new DiskFrameCache(System.IO.Path.Combine(OS.GetUserDataDir(), "framecache"), (Settings.FrameCacheMb ?? 2048) << 20);

        BuildViewport();
        Stereo = new StereoRig(this, World, ViewportPanel, Rig);
        Bridge = new RendererBridge(this);
        Controller = new RunController(Main, rest, hub, Bridge, cache);
        ApplySettingsToView();
        Controller.Changed += Invalidate;
        Controller.Log += Status;
        Controller.SpeciesChanged += FetchSpeciesMeshes;
        Controller.ViewChanged += ViewChanged;
        Controller.RememberRun = id => { Settings.LastRunId = id.Length == 0 ? null : id; Settings.Save(); };
        Bridge.SceneChanged += OnSceneChanged;
        Bridge.FramePresented += OnFramePresented;
        BuildSidePanel();
        Terrain.ShowFieldBox(Controller.Request.FieldSizeX, Controller.Request.FieldSizeD, Controller.Request.FieldSizeZ);

        if (Args.Camera is { } cam)
            Rig.SetPositionAndTarget(new Vec3(cam[0], cam[1], cam[2]), new Vec3(cam[3], cam[4], cam[5]));
        if (Args.DumpPose)
        {
            Rig.Controls.Changed += () => GD.Print("POSE " + EventScript.PoseJson(Rig.Controls.Pose));
            GD.Print("POSE " + EventScript.PoseJson(Rig.Controls.Pose));
        }
        if (Args.Replay is { } replay)
        {
            EventScript.Replay(File.ReadAllText(ProjectSettings.GlobalizePath(replay)), Rig.Controls);
            GD.Print("REPLAYED " + EventScript.PoseJson(Rig.Controls.Pose));
        }
        if (Args.ReplayInput is { } replayInput)
            inputReplay = new Queue<System.Text.Json.JsonElement>(System.Text.Json.JsonDocument.Parse(File.ReadAllText(ProjectSettings.GlobalizePath(replayInput)))
                .RootElement.GetProperty("events").EnumerateArray().Select(e => e.Clone()));
        if (Args.Screenshot is { } shot)
        {
            screenshot = new ScreenshotRunner(this, shot, Args.Width ?? 1280, Args.Height ?? 800, Args.Ssaa, Args.Settle) { Name = "Screenshot" };
            AddChild(screenshot);
        }

        if (Args.TransitionProbe is { } probeDir) AddChild(new TransitionProbe(this, ProjectSettings.GlobalizePath(probeDir)));

        ApplyCliView();
        Controller.LoadSpeciesAtStartup();
        if (Args.Simulate is int hours) _ = SimulateFromCli(hours);
        else if (Args.OpenRun is { } runId) _ = OpenRunFromCli(runId);
        else if (Args.Load is { } load) LoadSession(ProjectSettings.GlobalizePath(load));
        else if (Args.FrameFile is { } frameFile) LoadFrameFile(ProjectSettings.GlobalizePath(frameFile));
        else
        {
            screenshot?.Begin();
            if (Args.Screenshot == null) _ = Controller.RecoverRunAsync(Settings.LastRunId);
        }
        Status($"server {endpoint}");
        GD.Print($"GodotFrontend ready ({Core.CoreInfo.Name}), server {endpoint}");
    }

    // ------------------------------------------------------------------ tree

    private void BuildViewport()
    {
        var row = new HBoxContainer { Name = "Layout" };
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        row.AddThemeConstantOverride("separation", 0);
        AddChild(row);

        Rig = new CameraRig { Name = "CameraRig" };
        World = new SubViewport
        {
            Name = "World",
            OwnWorld3D = true,
            HandleInputLocally = false,
            // InputRouter draws this texture itself; nothing else would ask for it to be rendered.
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Viewport.Msaa.Msaa4X,
        };
        World.AddChild(Rig);
        var plantsRoot = new Node3D { Name = "Plants" };
        var terrainRoot = new Node3D { Name = "Terrain" };
        World.AddChild(plantsRoot);
        World.AddChild(terrainRoot);
        Lighting = new LightingRig(World, Materials);
        Batches = new PlantBatches(plantsRoot, Uploader, Materials);
        Terrain = new TerrainBuilder(terrainRoot, Uploader, Materials);
        seedMarkers = new SeedMarkers(terrainRoot, Uploader, Materials.Seed);

        ViewportPanel = new InputRouter
        {
            Name = "ViewportPanel",
            Rig = Rig,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            WheelAccumulate = Args.WheelAccumulate,
            View = World,
        };
        ViewportPanel.AddChild(World);
        // Low-processor mode draws once per change; a resize needs a few frames (the held image, the render at the
        // new size, the effects' history) and a window manager sends several sizes for one fullscreen toggle.
        ViewportPanel.SizeChanging += () => KeepDrawing(KeepDrawingSeconds);
        GetTree().Root.SizeChanged += () => KeepDrawing(KeepDrawingSeconds);
        ViewportPanel.Pointer += (pos, phase) =>
        {
            if (phase == InputRouter.PointerPhase.Move) pendingMove = pos;
            else pointerEvents.Enqueue((pos, phase));
        };
        row.AddChild(ViewportPanel);

        SidePanel = new PanelContainer { Name = "SidePanel", Visible = !Args.NoUi };
        row.AddChild(SidePanel);
        Resized += LayoutSidePanel;
    }

    private void BuildSidePanel()
    {
        var column = new VBoxContainer();
        SidePanel.AddChild(column);
        accessPanel = new WriteAccessPanel(this);
        column.AddChild(accessPanel);
        tabContainer = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(tabContainer);
        tabs.Add(new HomePanel(this));
        tabs.Add(new SimulationPanel(this));
        tabs.Add(new AnalysisPanel(this));
        tabs.Add(new RenderingPanel(this));
        tabs.Add(new RunsPanel(this));
        foreach (var t in tabs) tabContainer.AddChild(t);
        tabContainer.CurrentTab = System.Math.Clamp(Settings.ActiveTab ?? 0, 0, tabs.Count - 1);
        tabContainer.TabChanged += i => { Settings.ActiveTab = (int)i; dirty = true; };
        column.AddChild(new HSeparator());
        statusLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = AppTheme.Muted, CustomMinimumSize = new Vector2(0, 40) };
        column.AddChild(statusLabel);
        LayoutSidePanel();
    }

    private void LayoutSidePanel()
    {
        if (SidePanel == null) return;
        // At most a quarter of the window, never narrower than 320 px (style/index.css). Whole pixels, so the 3D view
        // next to it maps one to one onto the window: the stereo checkerboard does not survive any resampling.
        var width = MathF.Round(System.Math.Max(320f, Size.X * 0.25f));
        SidePanel.CustomMinimumSize = new Vector2(width, 0);
    }

    private void RestoreWindow()
    {
        if (Args.Width is int w && Args.Height is int h)
        {
            DisplayServer.WindowSetSize(new Vector2I(w, h));
            return;
        }
        if (Settings.WindowW is int sw && Settings.WindowH is int sh && sw > 200 && sh > 200)
            DisplayServer.WindowSetSize(new Vector2I(sw, sh));
        if (Settings.WindowX is int x && Settings.WindowY is int y)
            DisplayServer.WindowSetPosition(new Vector2I(x, y));
        if (Settings.WindowMaximized == true) DisplayServer.WindowSetMode(DisplayServer.WindowMode.Maximized);
    }

    // ------------------------------------------------------------------ frame loop

    public void Invalidate() => dirty = true;

    public override void _Process(double delta)
    {
        ++framesSinceReady;
        if (Args.Quit && framesSinceReady >= Args.Settle) GetTree().Quit(0);
        if (Args.UiScreenshot is { } ui && framesSinceReady == System.Math.Max(Args.Settle, 30))
        {
            var img = GetViewport().GetTexture().GetImage();
            img.SavePng(ui);
            GD.Print($"UI_SCREENSHOT {ui} {img.GetWidth()}x{img.GetHeight()}");
            GetTree().Quit(0);
        }
        if (screenshot != null)
        {
            timeoutLeft -= delta;
            if (timeoutLeft < 0)
            {
                GD.PrintErr("screenshot: timed out waiting for the frame");
                GetTree().Quit(4);
            }
        }
        ReplayInputStep();
        ProcessPointer();
        Stereo.Sync();
        if (keepDrawingUntilUs != 0 && Time.GetTicksUsec() > keepDrawingUntilUs)
        {
            keepDrawingUntilUs = 0;
            OS.LowProcessorUsageMode = lowProcessorMode;
        }
        if (!dirty) return;
        dirty = false;
        UpdateLighting();
        UpdateSeeds();
        accessPanel.Refresh();
        var current = tabContainer.CurrentTab;
        if (current >= 0 && current < tabs.Count) tabs[current].Refresh();
    }

    private void UpdateLighting()
    {
        var c = Controller;
        var step = c.CurrentStep;
        var s = c.Session;
        var site = new SolarSite(s.Site.Latitude, s.Site.Longitude, s.Site.Altitude,
            s.Site.AzimuthDegrees, s.Site.TimeZone);
        var coverage = s.Site.Coverage.Length == 0 ? 0f : s.Site.Coverage[(int)System.Math.Min(step, (uint)s.Site.Coverage.Length - 1)];
        var key = $"{step}|{c.View.Lighting}|{c.View.Mapping}|{site}|{s.SimHoursPerTick}|{s.InitialDate}|{coverage}";
        if (key == lightingKey) return;
        lightingKey = key;
        var frame = LightingResolver.Resolve(c.View.Lighting, c.View.Mapping, s.InitialDate, step, s.SimHoursPerTick, site);
        var dome = new SkyDomeSample(0, 1, 0, 0, 0, 0, 0, 0, 0, 0);
        if (frame.Sky)
        {
            var start = SimClock.TimeOfTimestep(SimClock.ParseDate(s.InitialDate), step, s.SimHoursPerTick, site.TimeZone);
            dome = SkyModel.SampleTick(start, SimClock.Snap(s.SimHoursPerTick), site.Latitude, site.Longitude, coverage, site.AzimuthDeg, site.AltitudeM);
        }
        Lighting.Apply(frame, dome, coverage);
    }

    private void UpdateSeeds()
    {
        var c = Controller;
        var list = new List<(Vec3, SelectionState)>(c.Request.Seeds.Count);
        for (var i = 0; i < c.Request.Seeds.Count; ++i)
        {
            var sd = c.Request.Seeds[i];
            var st = i < pickStore.SeedObjects.Count ? pickStore.SeedObjects[i].State : SelectionState.None;
            list.Add((SeedWorld(sd), st));
        }
        seedMarkers.Update(list);
        raycaster.Seeds.Clear();
        foreach (var (p, _) in list) raycaster.Seeds.Add((p, 0.03));
        while (pickStore.SeedObjects.Count < c.Request.Seeds.Count) pickStore.SeedObjects.Add(new SelectableObject(0, 0, 0));
        while (pickStore.SeedObjects.Count > c.Request.Seeds.Count) pickStore.SeedObjects.RemoveAt(pickStore.SeedObjects.Count - 1);
        if (!ReferenceEquals(raycastTerrain, c.Terrain)) RebuildStaticPicking();
    }

    private Vec3 SeedWorld(GodotFrontend.Core.Model.SeedModel s)
    {
        var p = new Vec3(s.Px, s.Py, s.Pz);
        var t = Controller.Terrain;
        if (t != null && s.Field >= 0 && s.Field < t.Items.Count)
        {
            var item = t.Items[s.Field];
            p += new Vec3(item.Px, item.Py, item.Pz);
        }
        return p;
    }

    private void RebuildStaticPicking()
    {
        raycastTerrain = Controller.Terrain;
        raycaster.Trays.Clear();
        raycaster.Obstacles = null;
        pickStore.TerrainObjects.Clear();
        pickStore.ObstacleObjects.Clear();
        var t = raycastTerrain;
        if (t == null) return;
        var box = Library.Get(Geom.BoxCorner);
        foreach (var item in t.Items)
        {
            switch (item)
            {
                case BoxTerrainItem b:
                {
                    var pos = (float[])box.Positions.Clone();
                    for (var i = 0; i < pos.Length; i += 3) { pos[i] *= b.Sx; pos[i + 1] *= b.Sy; pos[i + 2] *= b.Sz; }
                    raycaster.Trays.Add(new MeshBvh(pos, box.Indices, new Vec3(b.Px, b.Py - b.Sy, b.Pz)));
                    break;
                }
                case MeshTerrainItem m:
                    raycaster.Trays.Add(new MeshBvh(m.Points, m.Triangles, new Vec3(m.Px, m.Py, m.Pz)));
                    break;
            }
            pickStore.TerrainObjects.Add(new SelectableObject(item.Px, item.Py, item.Pz, item.Index) { Movable = false });
        }
        if (t.ObstacleMesh is { } mesh)
        {
            var obstacles = mesh;
            Task.Run(() => new MeshBvh(obstacles.Positions, obstacles.Indices)).ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully) Main.Post(() => { if (ReferenceEquals(raycastTerrain, t)) raycaster.Obstacles = task.Result; });
            });
            pickStore.ObstacleObjects.Add(new SelectableObject(0, 0, 0) { Movable = false });
        }
    }

    /// <summary>--replay-input: one scripted pointer event per frame, injected as if the user produced it.</summary>
    private void ReplayInputStep()
    {
        if (inputReplay == null) return;
        if ((Args.Load != null || Args.FrameFile != null) && Bridge.LiveScene == null) return; // wait for the scene
        if (replayDelay-- > 0) return;
        if (inputReplay.Count == 0 && !replayDraining)
        {
            replayDraining = true; // buffered input is flushed at the start of the next frame; let it land first
            replayDelay = 3;
            return;
        }
        if (inputReplay.Count == 0)
        {
            GD.Print("REPLAYED_INPUT " + EventScript.PoseJson(Rig.Controls.Pose));
            var picked = PickedDetailsText();
            if (picked.Length > 0) GD.Print("PICKED " + picked.Replace("\n", " | "));
            inputReplay = null;
            GetTree().Quit(0);
            return;
        }
        var e = inputReplay.Dequeue();
        var t = e.GetProperty("t").GetString();
        float X(string k) => e.TryGetProperty(k, out var v) ? v.GetSingle() : 0;
        var origin = ViewportPanel.GlobalPosition;
        var pos = origin + new Vector2(X("x"), X("y"));
        MouseButton Button() => (e.TryGetProperty("b", out var b) ? b.GetInt32() : 0) switch { 1 => MouseButton.Middle, 2 => MouseButton.Right, _ => MouseButton.Left };
        MouseButtonMask Mask(MouseButton mb) => mb switch { MouseButton.Left => MouseButtonMask.Left, MouseButton.Middle => MouseButtonMask.Middle, MouseButton.Right => MouseButtonMask.Right, _ => 0 };
        InputEvent ev;
        switch (t)
        {
            case "down":
                replayHeld = Button();
                replayShift = replayCtrl = replayMeta = false;
                if (e.TryGetProperty("mods", out var mods))
                    foreach (var m in mods.EnumerateArray())
                        switch (m.GetString()) { case "shift": replayShift = true; break; case "ctrl": replayCtrl = true; break; case "meta": replayMeta = true; break; }
                ev = new InputEventMouseButton { ButtonIndex = replayHeld, Pressed = true, Position = pos, GlobalPosition = pos, ButtonMask = Mask(replayHeld), ShiftPressed = replayShift, CtrlPressed = replayCtrl, MetaPressed = replayMeta };
                break;
            case "move":
                ev = new InputEventMouseMotion { Position = pos, GlobalPosition = pos, ButtonMask = Mask(replayHeld), ShiftPressed = replayShift, CtrlPressed = replayCtrl, MetaPressed = replayMeta };
                break;
            case "up":
                ev = new InputEventMouseButton { ButtonIndex = Button(), Pressed = false, Position = pos, GlobalPosition = pos, ShiftPressed = replayShift, CtrlPressed = replayCtrl, MetaPressed = replayMeta };
                replayHeld = MouseButton.None;
                break;
            default:
            {
                var down = X("dy") > 0;
                var center = origin + ViewportPanel.Size * new Vector2(0.6f, 0.5f);
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = down ? MouseButton.WheelDown : MouseButton.WheelUp, Pressed = true, Position = center, GlobalPosition = center, Factor = 1 });
                ev = new InputEventMouseButton { ButtonIndex = down ? MouseButton.WheelDown : MouseButton.WheelUp, Pressed = false, Position = center, GlobalPosition = center, Factor = 1 };
                break;
            }
        }
        Input.ParseInputEvent(ev);
    }

    // ------------------------------------------------------------------ picking

    private void ProcessPointer()
    {
        var picking = Controller.View.PerformPicking;
        while (pointerEvents.Count > 0)
        {
            var (pos, phase) = pointerEvents.Dequeue();
            if (phase == InputRouter.PointerPhase.Leave)
            {
                PickingLogic.ApplyPick(null, Clicks.None, default, pickStore);
                dirty = true;
                continue;
            }
            if (!picking) continue;
            var clicks = phase switch { InputRouter.PointerPhase.Down => Clicks.Down, InputRouter.PointerPhase.Up => Clicks.Up, _ => Clicks.Double };
            Pick(pos, clicks);
        }
        if (pendingMove is { } move)
        {
            pendingMove = null;
            if (picking) Pick(move, Clicks.None);
        }
    }

    private void Pick(Vector2 pos, Clicks clicks)
    {
        var size = ViewportPanel.Size;
        if (size.X <= 0 || size.Y <= 0) return;
        var ctl = Rig.Controls;
        var ndc = Ray.ToNdc(pos.X, pos.Y, 0, 0, size.X, size.Y);
        var ray = Ray.FromNdc(ctl.Position, ctl.Basis, ctl.FovY, size.X / size.Y, ctl.Far, ndc.X, ndc.Y);
        var hit = raycaster.Pick(ray);
        if (debugPicking) GD.Print($"PICK at {pos} ray {ray.Origin} -> {ray.Direction}: {hit.Kind} #{hit.Index} plant {hit.Plant} d={hit.Distance:F3} scene={(raycaster.Scene?.Count ?? -1)}");
        PickRef? r = hit.Kind switch
        {
            PickKind.Seed when hit.Index < pickStore.SeedObjects.Count => new SeedRef(pickStore.SeedObjects[hit.Index]),
            PickKind.Plant => new PlantRef(new PlantIndex(hit.Plant, hit.PrimitiveInPlant)),
            PickKind.Terrain when hit.Index < pickStore.TerrainObjects.Count => new TerrainRef(pickStore.TerrainObjects[hit.Index], hit.Index),
            PickKind.Obstacle when pickStore.ObstacleObjects.Count > 0 => new ObstacleRef(pickStore.ObstacleObjects[0]),
            _ => null,
        };
        var before = (pickStore.PlantPick, pickStore.SeedPick, pickStore.TerrainPick);
        PickingLogic.ApplyPick(r, clicks, ray, pickStore);
        if (before != (pickStore.PlantPick, pickStore.SeedPick, pickStore.TerrainPick) || clicks != Clicks.None) dirty = true;
    }

    /// <summary>components/viewport/PickedDetails.tsx as text.</summary>
    public string PickedDetailsText()
    {
        var c = Controller;
        if (pickStore.SeedPick >= 0 && pickStore.SeedPick < c.Request.Seeds.Count)
        {
            var sd = c.Request.Seeds[pickStore.SeedPick];
            var v = SeedWorld(sd);
            return $"Index: {pickStore.SeedPick}\nSpecies: {sd.Species}\nPosition: {v.X:F4}, {v.Y:F4}, {v.Z:F4}";
        }
        if (pickStore.PlantPick.Length > 0 && PlantIndex.Decode(pickStore.PlantPick) is { } idx && Bridge.LiveScene is { } s
            && idx.Entity < s.PlantCount && idx.Primitive < s.PlantDrawCount[idx.Entity])
        {
            var i = s.PlantFirst[idx.Entity] + idx.Primitive;
            var st = s.StatsOf(i);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Plant part: {pickStore.PlantPick}");
            sb.AppendLine($"Species: {s.Species[s.PlantSpecies[idx.Entity]]}");
            sb.AppendLine($"Organ: {s.Organ[i]}");
            if (!st.IsEmpty)
            {
                sb.AppendLine($"Water ratio: {st[Stat.Water]}");
                sb.AppendLine($"Energy ratio: {st[Stat.Energy]}");
                sb.AppendLine($"Auxins: {st[Stat.Auxins]}");
                sb.AppendLine($"Cytokinins: {st[Stat.Cytokinins]}");
                if (s.Kind[i] is PrimitiveType.Cylinder or PrimitiveType.Box) sb.AppendLine($"Wood ratio: {st[Stat.IrradianceOrWood]}");
                if (s.Kind[i] == PrimitiveType.Rectangle && s.Organ[i] != OrganType.FruitWing) sb.AppendLine($"Irradiance: {st[Stat.IrradianceOrWood]}");
                sb.AppendLine($"Resources availability: {st[Stat.DailyResource]}");
                sb.AppendLine($"Production efficiency: {st[Stat.DailyProduction]}");
                sb.AppendLine($"Relative Resources: {st[Stat.RelResource]}");
                sb.AppendLine($"Relative Production: {st[Stat.RelProduction]}");
            }
            var m = s.TransformOf(i);
            sb.Append($"Position: {m[3]:F4}, {m[7]:F4}, {m[11]:F4}");
            return sb.ToString();
        }
        if (pickStore.TerrainPick >= 0 && c.Terrain is { } t && pickStore.TerrainPick < t.Items.Count)
            return $"Terrain: {t.Items[pickStore.TerrainPick].Id}";
        return "";
    }

    private sealed class PickStore : IPickStore
    {
        public readonly List<SelectableObject> SeedObjects = new();
        public readonly List<SelectableObject> ObstacleObjects = new();
        public readonly List<SelectableObject> TerrainObjects = new();
        public IReadOnlyList<ISelectable> Seeds => SeedObjects;
        public IReadOnlyList<ISelectable> Obstacles => ObstacleObjects;
        public IReadOnlyList<ISelectable> Terrain => TerrainObjects;
        public string PlantPick { get; set; } = "";
        public int SeedPick { get; set; } = -1;
        public int TerrainPick { get; set; } = -1;
    }

    // ------------------------------------------------------------------ state changes from the UI

    public void ViewChanged()
    {
        var v = Controller.View;
        Materials.SetMapping(v.Mapping);
        Batches.SetVisible(Category.Leaves, v.ShowLeaves);
        Batches.SetVisible(Category.Roots, v.ShowRoots);
        Terrain.SetVisible(v.ShowTerrain, v.ShowObstacles);
        seedMarkers.Visible = v.ShowSeeds;
        raycaster.ShowLeaves = v.ShowLeaves;
        raycaster.ShowRoots = v.ShowRoots;
        raycaster.ShowTerrain = v.ShowTerrain;
        raycaster.ShowSeeds = v.ShowSeeds;
        raycaster.ShowObstacles = v.ShowObstacles;
        Stereo.Apply(v.Stereo);
        ViewportPanel.KeepPixelParity = v.Stereo.Active;
        dirty = true;
        // A view forced from the command line is for that one run; persisting it would hand the next interactive
        // start a scripted mapping, lighting or stereo setup (it did: a capture with --eye-separation 0 stuck).
        if (viewFromCommandLine) return;
        Settings.VisualMapping = PlantColors.MappingName(v.Mapping);
        Settings.ShowLeaves = v.ShowLeaves; Settings.ShowRoots = v.ShowRoots; Settings.ShowTerrain = v.ShowTerrain;
        Settings.ShowSeeds = v.ShowSeeds; Settings.ShowObstacles = v.ShowObstacles; Settings.PerformPicking = v.PerformPicking;
        var l = v.Lighting;
        Settings.Lighting = new LightingSettings
        {
            Daylight = l.Daylight, Shadows = l.Shadows, Occlusion = l.Occlusion, Indirect = l.Indirect, SecondBounce = l.SecondBounce,
            LeafTranslucency = l.LeafTranslucency, Sky = l.Sky, ShadowBiasTexels = (float)l.ShadowBiasTexels,
        };
        var st = v.Stereo;
        Settings.Stereo = new StereoSettings
        {
            Mode = StereoOptions.Name(st.Mode), EyeSeparation = st.EyeSeparation, ConvergeOnTarget = st.ConvergeOnTarget,
            Convergence = st.Convergence, SwapEyes = st.SwapEyes,
        };
    }

    private void ApplySettingsToView()
    {
        var v = Controller.View;
        v.Mapping = PlantColors.ParseMapping(Settings.VisualMapping);
        v.ShowLeaves = Settings.ShowLeaves; v.ShowRoots = Settings.ShowRoots; v.ShowTerrain = Settings.ShowTerrain;
        v.ShowSeeds = Settings.ShowSeeds; v.ShowObstacles = Settings.ShowObstacles; v.PerformPicking = Settings.PerformPicking;
        var l = Settings.Lighting;
        v.Lighting = new LightingOptions(l.Daylight, l.Shadows, l.Occlusion, l.Indirect, l.SecondBounce, l.LeafTranslucency, l.Sky, l.ShadowBiasTexels);
        var st = Settings.Stereo;
        // OpenXR is listed but not built yet; a stored choice of it opens in mono
        var mode = StereoOptions.Parse(st.Mode);
        v.Stereo = new StereoOptions(mode == StereoMode.OpenXr ? StereoMode.Mono : mode, System.Math.Max(0, st.EyeSeparation), st.ConvergeOnTarget,
            System.Math.Max(0.05, st.Convergence), st.SwapEyes, false);
        ViewChanged();
    }

    public void FieldChanged()
    {
        if (Controller.Terrain == null)
            Terrain.ShowFieldBox(Controller.Request.FieldSizeX, Controller.Request.FieldSizeD, Controller.Request.FieldSizeZ);
        dirty = true;
    }

    public void SeedsChanged() => dirty = true;

    private void OnSceneChanged()
    {
        raycaster.Scene = Bridge.LiveScene;
        raycaster.GeometryOf = Bridge.LivePacked?.GeometryOf;
        dirty = true;
    }

    private void OnFramePresented(int index)
    {
        if (screenshot != null && (screenshotIndex == index || screenshotIndex == int.MaxValue))
        {
            screenshotIndex = -2;
            dirty = true;
            _Process(0); // lighting for the shown step before the capture
            screenshot.Begin();
        }
    }

    public void Status(string message)
    {
        GD.Print(message);
        if (statusLabel != null)
        {
            var connected = Controller?.Hub.IsConnected == true ? "connected" : "not connected";
            statusLabel.Text = $"{Controller?.Rest.Endpoint} ({connected})\n{message}";
        }
    }

    // ------------------------------------------------------------------ species meshes

    private void FetchSpeciesMeshes(IReadOnlyList<Agro.SpeciesSettings> list)
    {
        foreach (var sp in list)
        {
            var name = sp.Name;
            if (string.IsNullOrEmpty(name) || !fetchedSpecies.Add(name)) continue;
            var rest = Controller.Rest;
            _ = Task.Run(async () =>
            {
                var any = false;
                try
                {
                    var slices = await rest.GetLeafSlicesAsync(name).ConfigureAwait(false);
                    if (slices is { Length: > 1 }) { Library.RegisterSpeciesLeaves(name, LaminaMeshReader.ReadSlices(slices, $"leaf:{name}")); any = true; }
                    var petal = await rest.GetPetalAsync(name).ConfigureAwait(false);
                    if (petal is { Length: > 8 }) { Library.RegisterSpeciesPetal(name, LaminaMeshReader.ReadOne(petal, $"petal:{name}")); any = true; }
                }
                catch (Exception ex) { Main.Post(() => GD.PrintErr($"meshes of {name}: {ex.Message}")); }
                // a V7 frame on screen was packed with the plane fallback: draw it again with the species meshes
                if (any) Main.Post(() => { if (Bridge.LiveScene is { Version: 7 } && Controller.History.Count > 0) Controller.Seek(Controller.Playback.Pointer); });
            });
        }
    }

    // ------------------------------------------------------------------ sessions and exports

    /// <summary>--lighting / --mapping: forced view settings for scripted stills (not written to the saved settings).</summary>
    private void ApplyCliView()
    {
        var v = Controller.View;
        if (Args.Mapping is { } m) v.Mapping = PlantColors.ParseMapping(m);
        if (Args.Lighting is { } l)
        {
            var on = l.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
            v.Lighting = new LightingOptions(on.Contains("daylight"), on.Contains("shadows"), on.Contains("occlusion"), on.Contains("indirect"),
                on.Contains("bounce"), on.Contains("translucency"), on.Contains("sky"));
        }
        var stereoArgs = Args.Stereo != null || Args.StereoSwap || Args.StereoPattern || Args.EyeSeparation != null || Args.Convergence != null;
        if (stereoArgs)
        {
            var st = v.Stereo;
            if (Args.Stereo is { } mode) st = st with { Mode = StereoOptions.Parse(mode) == StereoMode.Checkerboard ? StereoMode.Checkerboard : StereoMode.Mono };
            if (Args.StereoSwap) st = st with { SwapEyes = true };
            if (Args.StereoPattern) st = st with { TestPattern = true };
            if (Args.EyeSeparation is { } sep) st = st with { EyeSeparation = System.Math.Max(0, sep) };
            if (Args.Convergence is { } conv) st = st with { ConvergeOnTarget = false, Convergence = System.Math.Max(0.05, conv) };
            v.Stereo = st;
        }
        if (Args.Mapping != null || Args.Lighting != null || stereoArgs)
        {
            viewFromCommandLine = true;
            ViewChanged();
        }
    }

    /// <summary>F11 toggles fullscreen and F10 the side panel: what putting the view on the whole LED wall takes.</summary>
    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.F11)
        {
            var full = DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;
            DisplayServer.WindowSetMode(full ? DisplayServer.WindowMode.Windowed : DisplayServer.WindowMode.Fullscreen);
            KeepDrawing(KeepDrawingSeconds);
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.F10)
        {
            SidePanel.Visible = !SidePanel.Visible;
            KeepDrawing(KeepDrawingSeconds);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Draws every frame for the next <paramref name="seconds"/> instead of only when something changed.</summary>
    public void KeepDrawing(double seconds)
    {
        keepDrawingUntilUs = System.Math.Max(keepDrawingUntilUs, Time.GetTicksUsec() + (ulong)(seconds * 1e6));
        OS.LowProcessorUsageMode = false;
    }

    /// <summary>--simulate HOURS: one run from the default request; with --screenshot the last frame is captured.</summary>
    private async Task SimulateFromCli(int hours)
    {
        if (!Controller.Access.IsUnlocked)
        {
            var password = System.Environment.GetEnvironmentVariable("AGRO_WRITE_PASSWORD");
            try
            {
                if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("--simulate requires AGRO_WRITE_PASSWORD.");
                await Controller.Rest.UnlockWritesAsync(password);
            }
            catch (Exception ex) { Status($"could not unlock writes: {ex.Message}"); GetTree().Quit(2); return; }
        }
        await Controller.SpeciesReady;
        Controller.Request.TotalHours = hours;
        if (Args.Mapping == null && Args.Lighting == null) ApplyCliView();
        var wasComputing = false;
        Controller.Changed += () =>
        {
            if (Controller.Session.Computing) wasComputing = true;
            else if (wasComputing && screenshot != null && screenshotIndex == -2 && Controller.History.Count > 0)
            {
                wasComputing = false;
                screenshotIndex = Controller.History.Count - 1;
                Controller.Seek(screenshotIndex);
            }
        };
        await Controller.StartAsync();
    }

    /// <summary>--open-run ID: view a stored run; with --screenshot the frame shown after opening (or --frame N) is captured.</summary>
    private async Task OpenRunFromCli(string runId)
    {
        await Controller.SpeciesReady;
        if (screenshot != null && Args.Frame is null) screenshotIndex = int.MaxValue;
        var ok = await Controller.OpenStoredAsync(runId);
        if (!ok) { if (screenshot != null) GetTree().Quit(5); return; }
        if (Args.Frame is int f && Controller.History.Count > 0)
        {
            var target = f < 0 ? Controller.History.Count - 1 : System.Math.Min(f, Controller.History.Count - 1);
            screenshotIndex = target;
            Controller.Seek(target);
        }
    }

    public async void LoadSession(string path)
    {
        try
        {
            await Controller.LoadSessionAsync(path);
            ApplyCliView(); // the session's own view settings must not override a scripted --lighting / --mapping
            if (Args.Frame is int f && Controller.History.Count > 0)
            {
                var target = f < 0 ? Controller.History.Count - 1 : System.Math.Min(f, Controller.History.Count - 1);
                screenshotIndex = target;
                Controller.Seek(target);
            }
            else if (screenshot != null)
            {
                if (Controller.History.Count > 0) screenshotIndex = 0; else screenshot.Begin();
            }
        }
        catch (Exception ex)
        {
            Status($"could not load {System.IO.Path.GetFileName(path)}: {ex.Message}");
            if (screenshot != null) GetTree().Quit(5);
        }
    }

    public async void SaveSession(string path)
    {
        try
        {
            Status($"saving {System.IO.Path.GetFileName(path)}...");
            await Controller.SaveSessionAsync(path, new Progress<int>(n => Status($"saving frame {n} of {Controller.History.Count}")));
            Status($"saved {path}");
        }
        catch (Exception ex) { Status($"could not save: {ex.Message}"); }
    }

    public async void ExportPrim(string path)
    {
        try
        {
            var bytes = await Controller.CurrentFrameBytesAsync();
            if (bytes == null) { Status("no frame to export"); return; }
            await File.WriteAllBytesAsync(path, bytes);
            Status($"wrote {path} ({bytes.Length} bytes)");
        }
        catch (Exception ex) { Status($"could not export: {ex.Message}"); }
    }

    /// <summary>One stored frame (.g zlib or raw) shown without a session or a server, for parity stills.</summary>
    private void LoadFrameFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        screenshotIndex = int.MaxValue;
        Bridge.ShowFrame(0, 0, new ResolvedFrame(bytes, SceneDecodeService.LooksDeflated(bytes)));
    }

    public override void _ExitTree()
    {
        if (Settings == null) return;
        if (Args.Screenshot == null && Args.Width == null)
        {
            var mode = DisplayServer.WindowGetMode();
            Settings.WindowMaximized = mode == DisplayServer.WindowMode.Maximized;
            if (!Settings.WindowMaximized.Value)
            {
                var size = DisplayServer.WindowGetSize();
                var pos = DisplayServer.WindowGetPosition();
                Settings.WindowW = size.X; Settings.WindowH = size.Y; Settings.WindowX = pos.X; Settings.WindowY = pos.Y;
            }
        }
        Settings.Save();
        Decoder?.Dispose();
        _ = Controller?.Hub.DisposeAsync();
    }
}
