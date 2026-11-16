using GodotFrontend.Core.Net;
using GodotFrontend.Core.Threading;

namespace GodotFrontend.Core.Run;

/// <summary>
/// The lazy wire: the server sends a frame only after Preview() is called. Arm() records the wish; TryFlush sends at
/// most one call at a time, keeps the wish when the server answers false (no run attached to this connection yet)
/// or when the connection is down, and drops it when a connected server fails the call (the web's "spin" guard).
/// </summary>
public sealed class PreviewPump
{
    private readonly HubClient hub;
    private readonly IMainThread main;
    private bool armed;
    private bool inFlight;
    private int retriesLeft;

    public event Action<string>? Log;

    public PreviewPump(HubClient hub, IMainThread main)
    {
        this.hub = hub;
        this.main = main;
    }

    public bool Armed => armed;

    public void Arm()
    {
        if (!hub.Access.IsUnlocked) return;
        armed = true;
        retriesLeft = 20;
        TryFlush();
    }

    public void Disarm() => armed = false;

    public void TryFlush()
    {
        if (!armed || inFlight || !hub.IsConnected || !hub.Access.IsUnlocked) return;
        inFlight = true;
        armed = false;
        _ = FlushAsync();
    }

    private async Task FlushAsync()
    {
        bool ok;
        Exception? error = null;
        try { ok = await hub.PreviewAsync().ConfigureAwait(false); }
        catch (Exception ex) { ok = false; error = ex; }
        main.Post(() =>
        {
            inFlight = false;
            if (!hub.Access.IsUnlocked) { armed = false; return; }
            if (ok) return;
            if (error != null)
            {
                Log?.Invoke($"preview request failed: {error.Message}");
                if (!hub.IsConnected) armed = true; // picked up again when the connection returns
                return;
            }
            // false: no run attached yet (Start still being processed) - retry shortly
            if (retriesLeft-- > 0)
            {
                armed = true;
                main.PostDelayed(250, TryFlush);
            }
        });
    }
}
