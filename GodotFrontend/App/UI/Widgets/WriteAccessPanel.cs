using Godot;

namespace GodotFrontend.App.UI.Widgets;

/// <summary>A session-only password prompt, outside the tabs so write access is always visible.</summary>
public partial class WriteAccessPanel : VBoxContainer
{
    private readonly AppRoot app;
    private readonly Label state = new();
    private readonly LineEdit password = new() { Secret = true, PlaceholderText = "Shared write password", SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Button unlock = new() { Text = "Unlock writes" };
    private readonly Button lockButton = new() { Text = "Lock" };
    private readonly Label message = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = AppTheme.Error };
    private bool unlocking;

    public WriteAccessPanel(AppRoot app)
    {
        this.app = app;
        AddChild(state);
        var row = new HBoxContainer();
        row.AddChild(password);
        row.AddChild(unlock);
        row.AddChild(lockButton);
        AddChild(row);
        AddChild(message);
        AddChild(new HSeparator());
        unlock.Pressed += () => _ = Unlock();
        password.TextSubmitted += text => { if (!unlocking) _ = Unlock(); };
        lockButton.Pressed += () => { password.Text = ""; app.Controller.Access.Lock(); message.Text = ""; app.Invalidate(); };
        Refresh();
    }

    public void Refresh()
    {
        var unlocked = app.Controller.Access.IsUnlocked;
        state.Text = unlocked ? "Writes unlocked for this session" : "Read-only access";
        state.Modulate = unlocked ? AppTheme.Accent : AppTheme.Muted;
        password.Visible = unlock.Visible = !unlocked;
        password.Editable = !unlocking;
        unlock.Disabled = unlocking;
        unlock.Text = unlocking ? "Checking…" : "Unlock writes";
        lockButton.Visible = unlocked;
    }

    private async Task Unlock()
    {
        if (unlocking) return;
        unlocking = true;
        message.Text = "";
        var entered = password.Text;
        password.Text = "";
        app.Invalidate();
        try { await app.Controller.Rest.UnlockWritesAsync(entered); }
        catch (Exception ex) { message.Text = ex.Message; }
        finally { unlocking = false; app.Invalidate(); }
    }
}
