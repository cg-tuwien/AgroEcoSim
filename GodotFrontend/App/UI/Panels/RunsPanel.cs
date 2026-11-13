using Godot;
using GodotFrontend.App.UI.Widgets;
using GodotFrontend.Core.Net.Dto;
using GodotFrontend.Core.Run;

namespace GodotFrontend.App.UI.Panels;

/// <summary>Runs (pass 1): the stored runs with View and Delete, and the store status - a disabled store is a red banner, never an empty list.</summary>
public partial class RunsPanel : SidePanelTab
{
    private readonly AppRoot app;
    private readonly RunController c;
    private readonly Label header = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Label banner = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly VBoxContainer list = new();
    private List<RunDto> runs = new();
    private StoreStatusDto? store;
    private bool loading;
    private string shownKey = "";

    public RunsPanel(AppRoot app) : base("Runs")
    {
        this.app = app;
        c = app.Controller;
        var refresh = Add(new Button { Text = "Refresh" });
        refresh.Pressed += () => _ = Reload();
        Add(banner);
        Add(header);
        Add(list);
        VisibilityChanged += () => { if (IsVisibleInTree()) _ = Reload(); };
    }

    public async Task Reload()
    {
        if (loading) return;
        loading = true;
        header.Text = "loading...";
        try
        {
            var runsTask = c.Rest.GetRunsAsync(500);
            var storeTask = c.Rest.GetStoreAsync(0);
            runs = await runsTask;
            try { store = await storeTask; } catch { store = null; }
        }
        catch (Exception ex)
        {
            header.Text = $"could not list runs: {ex.Message}";
            runs = new();
        }
        finally { loading = false; }
        shownKey = "";
        app.Invalidate();
    }

    public override void Refresh()
    {
        var key = string.Join(",", runs.Select(r => r.Id + r.Status + r.StoredFrames)) + c.Session.ViewingRunId;
        if (key == shownKey) return;
        shownKey = key;
        if (store is { Enabled: false })
        {
            banner.Text = $"The run store is disabled: {store.DisabledReason}";
            banner.Modulate = AppTheme.Error;
        }
        else if (store is { Warnings.Length: > 0 })
        {
            banner.Text = string.Join("\n", store.Warnings);
            banner.Modulate = AppTheme.Warning;
        }
        else
        {
            banner.Text = store is null ? "" : $"volume {store.MountPoint}: {store.FreeBytes / 1073741824.0:0.0} GB free";
            banner.Modulate = AppTheme.Muted;
        }
        var total = runs.Sum(r => r.TotalBytes);
        header.Text = $"{runs.Count}{(runs.Count >= 500 ? " (newest 500)" : "")} runs, {total / 1073741824.0:0.00} GB";
        foreach (var child in list.GetChildren()) child.QueueFree();
        foreach (var r in runs)
        {
            var card = new VBoxContainer();
            var span = Core.Time.SimClock.EncodeTime((long)r.LastStep * r.HoursPerTick);
            var title = new Label
            {
                Text = $"{r.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  [{r.Status}]{(r.Deterministic ? " det" : "")}{(r.Id == c.Session.ViewingRunId ? "  (shown)" : "")}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            if (r.Id == c.Session.ViewingRunId) title.AddThemeColorOverride("font_color", AppTheme.Accent);
            card.AddChild(title);
            card.AddChild(new Label
            {
                Text = $"{span} of {Core.Time.SimClock.EncodeTime(r.TotalHours)}, {r.Plants} plants, {r.StoredFrames} frames, {r.TotalBytes / 1048576.0:0.0} MB" +
                       (string.IsNullOrEmpty(r.StopReason) ? "" : $"\nstopped: {r.StopReason}"),
                Modulate = AppTheme.Muted,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
            var row = new HBoxContainer();
            var view = new Button { Text = "View", Disabled = r.StoredFrames == 0 || c.Session.Computing };
            var id = r.Id;
            view.Pressed += () => _ = c.OpenStoredAsync(id);
            row.AddChild(view);
            var del = new TwoClickButton("Delete", $"Delete {r.TotalBytes / 1048576.0:0.0} MB?", () => _ = Delete(id)) { Disabled = r.IsRunning };
            row.AddChild(del);
            card.AddChild(row);
            card.AddChild(new HSeparator());
            list.AddChild(card);
        }
    }

    private async Task Delete(string id)
    {
        try
        {
            var res = await c.Rest.DeleteRunAsync(id);
            if (!res.Deleted) app.Status($"could not delete {id}: {res.Reason}");
            else if (c.Session.ViewingRunId == id) c.ForgetRun();
        }
        catch (Exception ex) { app.Status($"delete failed: {ex.Message}"); }
        await Reload();
    }
}
