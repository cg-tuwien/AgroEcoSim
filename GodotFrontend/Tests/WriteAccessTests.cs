using System.Net;
using System.Text;
using System.Text.Json;
using Agro;
using GodotFrontend.Core.History;
using GodotFrontend.Core.Net;
using GodotFrontend.Core.Net.Dto;
using GodotFrontend.Core.Persist;
using GodotFrontend.Core.Run;
using GodotFrontend.Core.Scene;
using GodotFrontend.Core.Threading;
using Xunit;

namespace GodotFrontend.Tests;

public class WriteAccessTests
{
    private const string Password = "a-test-password-that-must-never-be-saved";
    private static readonly ServerEndpoint Endpoint = new("http://127.0.0.1:1");

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }

    private static HttpResponseMessage Response(HttpStatusCode status = HttpStatusCode.OK, string body = "")
        => new(status) { Content = new StringContent(body) };

    [Fact]
    public async Task LockedClientsDoNotSendMutationsButCanRead()
    {
        var calls = 0;
        using var rest = new RestClient(Endpoint, handler: new Handler(request =>
        {
            ++calls;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.False(request.Headers.Contains("X-Agro-Write-Password"));
            return Task.FromResult(Response(body: "[]"));
        }));
        Assert.Empty(await rest.GetRunsAsync());
        await Assert.ThrowsAsync<WriteAccessException>(() => rest.UploadAsync(new SimulationRequest()));
        await Assert.ThrowsAsync<WriteAccessException>(() => rest.PostTerrainAsync("model"));
        await Assert.ThrowsAsync<WriteAccessException>(() => rest.DeleteRunAsync("run"));
        await Assert.ThrowsAsync<WriteAccessException>(() => rest.SetProtectionAsync("run", true));
        Assert.Equal(1, calls);

        await using var hub = new HubClient(Endpoint, rest.Access);
        await Assert.ThrowsAsync<WriteAccessException>(() => hub.StartRunAsync("prepared"));
        await Assert.ThrowsAsync<WriteAccessException>(() => hub.PreviewAsync());
        await Assert.ThrowsAsync<WriteAccessException>(() => hub.SubscribeAsync("run"));
        await Assert.ThrowsAsync<WriteAccessException>(() => hub.AbortAsync());
        await Assert.ThrowsAsync<WriteAccessException>(() => hub.TerrainAsync("terrain", 4, "soil", true, 0.5f));
    }

    [Fact]
    public async Task VerifiedCredentialTravelsOnlyWithWritesAndProtectionIsExplicit()
    {
        var calls = new List<(HttpMethod Method, string Path, string? Body)>();
        using var rest = new RestClient(Endpoint, handler: new Handler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync();
            calls.Add((request.Method, path, body));
            if (request.Method == HttpMethod.Get)
            {
                Assert.False(request.Headers.Contains("X-Agro-Write-Password"));
                return Response(body: "[]");
            }
            Assert.Equal(Password, Assert.Single(request.Headers.GetValues("X-Agro-Write-Password")));
            Assert.DoesNotContain(Password, request.RequestUri.ToString());
            Assert.DoesNotContain(Password, body ?? "");
            return path.EndsWith("write-access") ? Response(HttpStatusCode.NoContent)
                : path.EndsWith("protection") ? Response(body: "{\"id\":\"run\",\"protected\":true}")
                : request.Method == HttpMethod.Delete ? Response(body: "{\"deleted\":true,\"reason\":\"\"}")
                : Response(body: "prepared");
        }));
        await rest.UnlockWritesAsync(Password);
        Assert.True(rest.Access.IsUnlocked);
        await rest.UploadAsync(new SimulationRequest());
        await rest.PostTerrainAsync("model");
        var run = await rest.SetProtectionAsync("run", true);
        Assert.True(run.Protected);
        Assert.True((await rest.DeleteRunAsync("run")).Deleted);
        await rest.GetRunsAsync();
        Assert.Equal("{\"protected\":true}", calls.Single(c => c.Method == HttpMethod.Put).Body);
        Assert.DoesNotContain(Password, JsonSerializer.Serialize(rest.Access));
        rest.Access.Lock();
        Assert.False(rest.Access.IsUnlocked);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task FailedValidationNeverUnlocks(HttpStatusCode status)
    {
        using var rest = new RestClient(Endpoint, handler: new Handler(_ => Task.FromResult(Response(status, "write access unavailable"))));
        await Assert.ThrowsAsync<ServerException>(() => rest.UnlockWritesAsync(Password));
        Assert.False(rest.Access.IsUnlocked);
    }

    [Fact]
    public async Task AuthenticationRejectionRelocksWithoutTreatingErrorAsRunData()
    {
        using var rest = new RestClient(Endpoint, handler: new Handler(request => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("write-access") ? Response(HttpStatusCode.NoContent)
                : Response(HttpStatusCode.Unauthorized, "WRITE_ACCESS_REQUIRED: Unlock writes with the shared password."))));
        await rest.UnlockWritesAsync(Password);
        await Assert.ThrowsAsync<ServerException>(() => rest.SetProtectionAsync("run", false));
        Assert.False(rest.Access.IsUnlocked);
        await Assert.ThrowsAsync<WriteAccessException>(() => rest.DeleteRunAsync("run"));
    }

    [Fact]
    public async Task LockCancelsAnInFlightUnlock()
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var rest = new RestClient(Endpoint, handler: new Handler(_ => response.Task));
        var unlocking = rest.UnlockWritesAsync(Password);
        rest.Access.Lock();
        response.SetResult(Response(HttpStatusCode.NoContent));
        await Assert.ThrowsAsync<OperationCanceledException>(() => unlocking);
        Assert.False(rest.Access.IsUnlocked);
    }

    private sealed class NullSink : IFrameSink
    {
        public event Action<int>? FramePresented;
        public void ShowFrame(int index, uint step, ResolvedFrame frame) => FramePresented?.Invoke(index);
        public void ShowNothing() { }
        public void ShowTerrain(TerrainData terrain) { }
        public void ShowFieldBox(float x, float d, float z) { }
        public void ClearTerrain() { }
    }

    [Fact]
    public async Task LockedStartPreservesTheLoadedRunAndItsHistory()
    {
        using var rest = new RestClient(Endpoint, handler: new Handler(_ => throw new InvalidOperationException("No request should be sent.")));
        await using var hub = new HubClient(Endpoint);
        var controller = new RunController(new ManualPump(), rest, hub, new NullSink(), null);
        var frame = new byte[] { 7, 1, 2 };
        controller.History.Upsert(123, frame);
        controller.Session.RunId = controller.Session.ViewingRunId = "previous";
        controller.Session.Renderer = "previous-renderer";
        await controller.StartAsync();
        Assert.Equal("previous", controller.Session.RunId);
        Assert.Equal("previous", controller.Session.ViewingRunId);
        Assert.Equal("previous-renderer", controller.Session.Renderer);
        Assert.Same(frame, controller.History[0].Bytes);
        Assert.False(controller.Session.Computing);
        Assert.False(controller.Pump.Armed);
    }

    [Fact]
    public async Task AnonymousRunningRunOpensAsStoredPlaybackWithoutHubAttachment()
    {
        var requests = new List<string>();
        using var rest = new RestClient(Endpoint, handler: new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            requests.Add(path);
            Assert.Equal(HttpMethod.Get, request.Method);
            if (path.EndsWith("/run/run")) return Task.FromResult(Response(body: "{\"id\":\"run\",\"status\":\"running\",\"storedFrames\":1,\"hoursPerTick\":4,\"initialDay\":\"2026-04-01\",\"protected\":true}"));
            if (path.EndsWith("/index")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[8]) });
            return Task.FromResult(Response(HttpStatusCode.NotFound));
        }));
        await using var hub = new HubClient(Endpoint);
        var controller = new RunController(new ManualPump(), rest, hub, new NullSink(), null);
        Assert.True(await controller.OpenStoredAsync("run"));
        Assert.Single(controller.History.Frames);
        Assert.False(controller.Session.Computing);
        Assert.False(controller.Pump.Armed);
        Assert.False(hub.IsConnected);
        Assert.All(requests, path => Assert.StartsWith("/Simulation/", path));
    }

    [Fact]
    public async Task SessionAndSettingsNeverContainTheCredential()
    {
        using var rest = new RestClient(Endpoint, handler: new Handler(_ => Task.FromResult(Response(HttpStatusCode.NoContent))));
        await using var hub = new HubClient(Endpoint);
        var pump = new ManualPump();
        var controller = new RunController(pump, rest, hub, new NullSink(), null);
        await controller.LoadSessionAsync(Fixtures.Path("fixture-session.json"));
        await rest.UnlockWritesAsync(Password);
        var directory = Directory.CreateTempSubdirectory("agro-access");
        try
        {
            var saved = Path.Combine(directory.FullName, "saved.agro");
            await controller.SaveSessionAsync(saved);
            using var archive = RunArchiveReader.Open(saved);
            Assert.DoesNotContain(Password, archive.Metadata.ToJsonString());
            var settings = Settings.Load(Path.Combine(directory.FullName, "settings.json"));
            settings.Save();
            Assert.DoesNotContain(Password, File.ReadAllText(settings.Path));
        }
        finally { directory.Delete(true); }
    }
}
