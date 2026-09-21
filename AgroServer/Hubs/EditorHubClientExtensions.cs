using AgroServer.Models;
using Microsoft.AspNetCore.SignalR;

namespace AgroServer.Hubs;

/// <summary>
/// Typed client calls without the runtime code generation required by Hub&lt;IEditorHub&gt;.
/// Keep the signatures aligned with IEditorHub and preserve its names on the wire.
/// </summary>
public static class EditorHubClientExtensions
{
    public static Task Rejected(this IClientProxy client)
        => client.SendAsync(nameof(IEditorHub.Rejected));

    public static Task RunStarted(this IClientProxy client, string runId)
        => client.SendAsync(nameof(IEditorHub.RunStarted), runId);

    public static Task RunSite(this IClientProxy client, RunSiteResponse site)
        => client.SendAsync(nameof(IEditorHub.RunSite), site);

    public static Task Progress(this IClientProxy client, uint step, uint length)
        => client.SendAsync(nameof(IEditorHub.Progress), step, length);

    public static Task Result(this IClientProxy client, SimulationResponse response)
        => client.SendAsync(nameof(IEditorHub.Result), response);

    public static Task Preview(this IClientProxy client, PreviewResponse response)
        => client.SendAsync(nameof(IEditorHub.Preview), response);

    public static Task Terrain(this IClientProxy client, byte[] response)
        => client.SendAsync(nameof(IEditorHub.Terrain), response);
}
