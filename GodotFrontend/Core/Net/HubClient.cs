using Microsoft.AspNetCore.SignalR.Client;
using GodotFrontend.Core.Net.Dto;

namespace GodotFrontend.Core.Net;

/// <summary>
/// The SignalR half: the hub at /SimSocket with the JSON protocol. Handlers are registered under the server's
/// PascalCase names and raise plain events on the connection's thread; the run controller posts them to the main
/// thread. Reconnects use the web client's delays (200, 200, 500, 500, 1000 x4, 2000 x4, then 5000 forever).
/// Preview and Abort are keyed by connection id server-side, so after a reconnect Subscribe(runId) must come first.
/// </summary>
public sealed class HubClient : IAsyncDisposable
{
    private sealed class WebRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext ctx) => ctx.PreviousRetryCount switch
        {
            0 or 1 => TimeSpan.FromMilliseconds(200),
            2 or 3 => TimeSpan.FromMilliseconds(500),
            >= 4 and <= 7 => TimeSpan.FromMilliseconds(1000),
            >= 8 and <= 11 => TimeSpan.FromMilliseconds(2000),
            _ => TimeSpan.FromMilliseconds(5000),
        };
    }

    private readonly HubConnection connection;
    private readonly SemaphoreSlim startGate = new(1, 1);

    public event Action? Rejected;
    public event Action<uint, uint>? Preparation;
    public event Action<uint, uint>? Progress;
    public event Action<ResultDto>? Result;
    public event Action<string>? RunStarted;
    public event Action<RunSiteDto>? RunSite;
    public event Action<PreviewDto>? Preview;
    public event Action<byte[]>? Terrain;
    public event Action<Exception?>? Reconnecting;
    public event Action<string?>? Reconnected;
    public event Action<Exception?>? Closed;

    public HubClient(ServerEndpoint endpoint)
    {
        connection = new HubConnectionBuilder()
            .WithUrl(endpoint.Hub)
            .WithAutomaticReconnect(new WebRetryPolicy())
            .Build();
        connection.On("Rejected", () => Rejected?.Invoke());
        connection.On<uint, uint>("Preparation", (s, t) => Preparation?.Invoke(s, t));
        connection.On<uint, uint>("Progress", (s, l) => Progress?.Invoke(s, l));
        connection.On<ResultDto>("Result", r => Result?.Invoke(r));
        connection.On<string>("RunStarted", id => RunStarted?.Invoke(id));
        connection.On<RunSiteDto>("RunSite", s => RunSite?.Invoke(s));
        connection.On<PreviewDto>("Preview", p => Preview?.Invoke(p));
        connection.On<byte[]>("Terrain", b => Terrain?.Invoke(b));
        connection.Reconnecting += e => { Reconnecting?.Invoke(e); return Task.CompletedTask; };
        connection.Reconnected += id => { Reconnected?.Invoke(id); return Task.CompletedTask; };
        connection.Closed += e => { Closed?.Invoke(e); return Task.CompletedTask; };
    }

    public HubConnectionState State => connection.State;
    public bool IsConnected => connection.State == HubConnectionState.Connected;
    public string? ConnectionId => connection.ConnectionId;

    /// <summary>Connects if not connected; concurrent callers share one attempt.</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;
        await startGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (connection.State == HubConnectionState.Disconnected)
                await connection.StartAsync(ct).ConfigureAwait(false);
        }
        finally { startGate.Release(); }
    }

    public Task StartRunAsync(string uploadId, CancellationToken ct = default) => connection.InvokeAsync("Start", uploadId, ct);
    public Task<bool> PreviewAsync(CancellationToken ct = default) => connection.InvokeAsync<bool>("Preview", ct);
    public Task<bool> SubscribeAsync(string runId, CancellationToken ct = default) => connection.InvokeAsync<bool>("Subscribe", runId, ct);
    public Task AbortAsync(CancellationToken ct = default) => connection.InvokeAsync("Abort", ct);
    public Task TerrainAsync(string terrainId, ushort hoursPerTick, string pattern, bool patternForMaterial, float resolution, CancellationToken ct = default)
        => connection.InvokeAsync("Terrain", terrainId, hoursPerTick, pattern, patternForMaterial, resolution, ct);

    public async ValueTask DisposeAsync()
    {
        try { await connection.DisposeAsync().ConfigureAwait(false); } catch { }
    }
}
