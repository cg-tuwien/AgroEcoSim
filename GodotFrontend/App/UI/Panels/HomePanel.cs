using Godot;
using GodotFrontend.App.UI.Widgets;
using GodotFrontend.Core.Run;

namespace GodotFrontend.App.UI.Panels;

/// <summary>Home: Simulate/Stop, the transport bar, the counters, the progress line, and session load/save.</summary>
public partial class HomePanel : SidePanelTab
{
    private readonly AppRoot app;
    private readonly RunController c;
    private readonly Button simulate = new() { Text = "Simulate" };
    private readonly TransportBar transport;
    private readonly Label counters = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label progress = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label timing = new() { Modulate = AppTheme.Muted, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Button save = new() { Text = "Save session (.agro)" };
    private readonly Button prim = new() { Text = "Download frame (.prim)" };

    public HomePanel(AppRoot app) : base("Home")
    {
        this.app = app;
        c = app.Controller;
        transport = new TransportBar(c);
        Add(simulate);
        simulate.Pressed += () => c.SimulateOrStop();
        Add(transport);
        Add(progress);
        Add(counters);
        Add(timing);
        Heading("Session");
        var load = Add(new Button { Text = "Load session (.agro / .json)" });
        load.Pressed += () => app.Dialogs.Open("Load session", new[] { "*.agro, *.json ; Agro sessions" }, path => app.LoadSession(path));
        Add(save);
        save.Pressed += () => app.Dialogs.Save("Save session", $"AgroEco-{DateTime.Now:yyyy-MM-ddTHH-mm-ss}.agro", new[] { "*.agro ; Agro session" }, path => app.SaveSession(path));
        Add(prim);
        prim.Pressed += () => app.Dialogs.Save("Download frame", $"AgroEco-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{c.CurrentStep}.prim", new[] { "*.prim ; Raw scene frame" }, path => app.ExportPrim(path));
    }

    public override void Refresh()
    {
        var s = c.Session;
        simulate.Text = c.Starting ? "Starting…" : s.Computing ? "Stop" : "Simulate";
        simulate.Disabled = !c.Access.IsUnlocked || c.Starting || (!s.Computing && c.Request.Seeds.Count == 0);
        simulate.TooltipText = c.Access.IsUnlocked ? "" : "Unlock writes to start or stop a simulation.";
        transport.Refresh();
        save.Disabled = c.History.Count == 0 || s.Computing;
        prim.Disabled = c.History.Count == 0;

        var used = c.History.ResidentBytes;
        var budget = s.HistoryBudget;
        var share = budget > 0 ? (double)used / budget : 0;
        var text = $"{used / 1048576.0:0.###} Mb";
        if (share > 0.75) text += $" / {budget / 1048576.0:0}";
        if (s.HistoryLimitHit) text += " - full, run stopped";
        if (c.History.EvictedCount > 0) text += $"  ({c.History.EvictedCount} streamed)";
        if (s.RunId.Length > 0 && s.RunStored == false) text += "  not stored";
        if (s.StopReason.Length > 0) text += $"  stopped: {s.StopReason}";
        if (s.Preparation is { } prep) text += $"\nPreparing plants: {prep.step} / {prep.total} ({100.0 * prep.step / prep.total:0.00}%)";
        else if (s.SimLength > 0) text += $"\n{s.SimStep} / {s.SimLength} ({100.0 * s.SimStep / s.SimLength:0.00}%)";
        progress.Text = text;
        progress.Modulate = s.HistoryLimitHit || share >= 1 ? AppTheme.Error : share > 0.75 ? AppTheme.Warning : AppTheme.Text;

        counters.Text = $"Seeds: {c.Request.Seeds.Count}\nObstacles: {c.Request.Obstacles.Count + (c.Terrain?.ObstacleMesh != null ? 1 : 0)}\n" +
                        $"Objects: {app.Batches.InstanceCount}\nTriangles: {app.Batches.TriangleCount}\nBackend Renderer: {s.Renderer}";
        if (app.Bridge.LastReady is { } r)
            timing.Text = $"frame step {app.Bridge.LiveStep}: {r.Scene.PlantCount} plants; inflate {r.InflateMs:F1} ms, decode {r.DecodeMs:F1} ms, pack {r.PackMs:F1} ms, upload {app.LastUploadMs:F1} ms";
    }
}
