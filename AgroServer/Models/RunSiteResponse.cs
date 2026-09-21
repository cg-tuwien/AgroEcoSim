namespace AgroServer.Models;

/// <summary>
/// Where a run is, and what weather it is running under. Sent once, at the start of the run.
/// </summary>
/// <remarks>
/// Both halves are run-level facts that the client needs per TIMESTEP, and sending them once is
/// what keeps them out of the frame format. The weather in particular is planned in full before
/// step 0 (see <see cref="Agro.AgroWorld.SkyCoverageSeries"/>), so there is nothing per-step
/// about it except how it is read.
///
/// <see cref="TimeZone"/> is the one the server actually derived from the coordinates. The
/// browser has no way to map a latitude and longitude to an IANA zone, so without this a client
/// that moved the site would keep the default zone and shift every sun position, every shadow
/// and the whole sky by the offset - while still looking entirely plausible.
/// </remarks>
public class RunSiteResponse
{
    public float Latitude { get; set; }
    public float Longitude { get; set; }
    public float Altitude { get; set; }
    public float Azimuth { get; set; }
    /// <summary>IANA id of the zone the simulated clock runs on.</summary>
    public string TimeZone { get; set; } = "";
    /// <summary>
    /// Cloud cover per timestep as raw little-endian float32, one per step.
    ///
    /// Bytes rather than a JSON number array, the way <c>PreviewResponse.Scene</c> travels. A
    /// 1,440-step run is 5.8 KB raw and about 7.7 KB once base64'd - both the SignalR JSON
    /// protocol and this endpoint's JSON response encode a byte[] that way - against roughly
    /// 30 KB of decimal text.
    /// </summary>
    public byte[] SkyCoverage { get; set; } = [];
}
