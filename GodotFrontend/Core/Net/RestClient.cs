using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Agro;
using GodotFrontend.Core.Net.Dto;

namespace GodotFrontend.Core.Net;

/// <summary>Thrown for a non-success HTTP status, carrying the server's text (a 400 from /upload says what was wrong).</summary>
public sealed class ServerException : Exception
{
    public HttpStatusCode Status { get; }
    public ServerException(HttpStatusCode status, string message) : base(message) => Status = status;
}

/// <summary>
/// The REST half of AgroServer (/Simulation/...). Frames come from the /packed endpoint as the stored zlib bytes;
/// gzip bodies (terrain, obstacle light) are decompressed by the handler. Every call runs on the thread pool and
/// never touches application state; callers post results to the main thread.
/// </summary>
public sealed class RestClient : IDisposable
{
    private readonly HttpClient http;
    public ServerEndpoint Endpoint { get; }

    public RestClient(ServerEndpoint endpoint)
    {
        Endpoint = endpoint;
        http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private Uri U(string path) => Endpoint.Rest(path);

    private static async Task Ensure(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new ServerException(res.StatusCode, string.IsNullOrWhiteSpace(body) ? $"{(int)res.StatusCode} {res.ReasonPhrase}" : body.Trim());
    }

    public async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        using var res = await http.GetAsync(U("version"), ct).ConfigureAwait(false);
        await Ensure(res, ct).ConfigureAwait(false);
        return await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<SpeciesSettings>> GetSpeciesAsync(CancellationToken ct = default)
    {
        using var res = await http.GetAsync(U("species"), ct).ConfigureAwait(false);
        await Ensure(res, ct).ConfigureAwait(false);
        await using var s = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<List<SpeciesSettings>>(s, Json.WireIn, ct).ConfigureAwait(false) ?? new();
    }

    public async Task<string[]> GetBehaviorsAsync(CancellationToken ct = default)
    {
        using var res = await http.GetAsync(U("behaviors"), ct).ConfigureAwait(false);
        await Ensure(res, ct).ConfigureAwait(false);
        return await res.Content.ReadFromJsonAsync<string[]>(Json.WireIn, ct).ConfigureAwait(false) ?? Array.Empty<string>();
    }

    /// <summary>POST upload: the request becomes a single-use prepared id the hub's Start takes.</summary>
    public async Task<string> UploadAsync(SimulationRequest request, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(request, Json.RequestOut);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync(U("upload"), content, ct).ConfigureAwait(false);
        await Ensure(res, ct).ConfigureAwait(false);
        return (await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim().Trim('"');
    }

    /// <summary>POST terrain: the raw OBJ text; returns the terrain id the hub's Terrain call takes.</summary>
    public async Task<string> PostTerrainAsync(string objText, CancellationToken ct = default)
    {
        using var content = new StringContent(objText, Encoding.UTF8, "text/plain");
        using var res = await http.PostAsync(U("terrain"), content, ct).ConfigureAwait(false);
        await Ensure(res, ct).ConfigureAwait(false);
        return (await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim().Trim('"');
    }

    /// <summary>GET run/{id}: the run's metadata, or null for a 404.</summary>
    public async Task<RunDto?> GetRunAsync(string runId, CancellationToken ct = default)
    {
        using var res = await http.GetAsync(U($"run/{runId}"), ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        await Ensure(res, ct).ConfigureAwait(false);
        return await res.Content.ReadFromJsonAsync<RunDto>(Json.WireIn, ct).ConfigureAwait(false);
    }

    public async Task<RunIndex> GetRunIndexAsync(string runId, CancellationToken ct = default)
    {
        using var res = await http.GetAsync(U($"run/{runId}/index"), ct).ConfigureAwait(false);
        await Ensure(res, ct).ConfigureAwait(false);
        var bytes = await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        res.Headers.TryGetValues("X-Agro-Frame-Revision", out var rev);
        res.Headers.TryGetValues("X-Agro-Restart-Step", out var restart);
        return RunIndex.Decode(bytes, rev?.FirstOrDefault(), restart?.FirstOrDefault());
    }

    /// <summary>GET run/{id}/frame/{step}/packed: the stored zlib bytes, or null for a 404.</summary>
    public async Task<byte[]?> GetFramePackedAsync(string runId, uint step, CancellationToken ct = default)
    {
        using var res = await http.GetAsync(U($"run/{runId}/frame/{step}/packed"), ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        await Ensure(res, ct).ConfigureAwait(false);
        return await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    /// <summary>GET run/{id}/terrain (gzip, decompressed here), or null when the run has none.</summary>
    public async Task<byte[]?> GetTerrainAsync(string runId, CancellationToken ct = default)
    {
        using var res = await http.GetAsync(U($"run/{runId}/terrain"), ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        await Ensure(res, ct).ConfigureAwait(false);
        var bytes = await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        return Gunzip(bytes);
    }

    public async Task<SimulationRequest?> GetRequestAsync(string runId, CancellationToken ct = default)
    {
        using var res = await http.GetAsync(U($"run/{runId}/request"), ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        await Ensure(res, ct).ConfigureAwait(false);
        await using var s = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<SimulationRequest>(s, Json.WireIn, ct).ConfigureAwait(false);
    }

    public async Task<RunSiteDto?> GetSiteAsync(string runId, CancellationToken ct = default)
    {
        using var res = await http.GetAsync(U($"run/{runId}/site"), ct).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        await Ensure(res, ct).ConfigureAwait(false);
        return await res.Content.ReadFromJsonAsync<RunSiteDto>(Json.WireIn, ct).ConfigureAwait(false);
    }

    public async Task<List<RunDto>> GetRunsAsync(int limit = 500, CancellationToken ct = default)
    {
        using var res = await http.GetAsync(U($"runs?limit={limit}"), ct).ConfigureAwait(false);
        await Ensure(res, ct).ConfigureAwait(false);
        return await res.Content.ReadFromJsonAsync<List<RunDto>>(Json.WireIn, ct).ConfigureAwait(false) ?? new();
    }

    public async Task<StoreStatusDto?> GetStoreAsync(int events = 500, CancellationToken ct = default)
    {
        using var res = await http.GetAsync(U($"store?events={events}"), ct).ConfigureAwait(false);
        await Ensure(res, ct).ConfigureAwait(false);
        return await res.Content.ReadFromJsonAsync<StoreStatusDto>(Json.WireIn, ct).ConfigureAwait(false);
    }

    public async Task<DeleteDto> DeleteRunAsync(string runId, CancellationToken ct = default)
    {
        using var res = await http.DeleteAsync(U($"run/{runId}"), ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            var dto = JsonSerializer.Deserialize<DeleteDto>(body, Json.WireIn);
            if (dto != null) return dto;
        }
        catch (JsonException) { }
        return new DeleteDto { Deleted = res.IsSuccessStatusCode, Reason = res.IsSuccessStatusCode ? "" : body };
    }

    public async Task<byte[]?> GetLeafSlicesAsync(string species, CancellationToken ct = default)
    {
        using var res = await http.GetAsync(U($"mesh/leaf/{Uri.EscapeDataString(species)}/slices"), ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    public async Task<byte[]?> GetPetalAsync(string species, CancellationToken ct = default)
    {
        using var res = await http.GetAsync(U($"mesh/petal/{Uri.EscapeDataString(species)}"), ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    public async Task<byte[]?> GetObstacleLightPackedAsync(string runId, CancellationToken ct = default)
    {
        using var res = await http.GetAsync(U($"run/{runId}/obstaclelight/packed"), ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) return null;
        return await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    public static byte[] Gunzip(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
        {
            using var input = new MemoryStream(bytes);
            using var z = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var output = new MemoryStream();
            z.CopyTo(output);
            return output.ToArray();
        }
        return bytes;
    }

    public void Dispose() => http.Dispose();
}
