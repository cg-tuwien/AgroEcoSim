using System.Text.Json;
using GodotFrontend.Core.Color;
using GodotFrontend.Core.Lighting;
using Xunit;

namespace GodotFrontend.Tests;

/// <summary>The sun, sky and lighting ports against values dumped from the web modules (Tests/fixtures/sky-expected.json).</summary>
public class LightingTests
{
    private static JsonElement Expected => JsonDocument.Parse(File.ReadAllText(Fixtures.Path("sky-expected.json"))).RootElement;

    private static DateTime Utc(string s) => DateTime.Parse(s, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);

    [Fact]
    public void SolarPositionMatchesTheWeb()
    {
        foreach (var e in Expected.GetProperty("solar").EnumerateArray())
        {
            var p = Solar.Position(Utc(e.GetProperty("utc").GetString()!), 48.208333, 16.3725);
            Assert.Equal(e.GetProperty("altitude").GetDouble(), p.Altitude, 9);
            Assert.Equal(e.GetProperty("azimuth").GetDouble(), p.Azimuth, 9);
        }
    }

    [Fact]
    public void SkyDomeMatchesTheWeb()
    {
        foreach (var e in Expected.GetProperty("dome").EnumerateArray())
        {
            var d = SkyModel.Sample(Utc(e.GetProperty("utc").GetString()!), 48.208333, 16.3725, 0.3, 20, 188);
            Assert.Equal(e.GetProperty("globalHorizontal").GetDouble(), d.GlobalHorizontal, 6);
            Assert.Equal(e.GetProperty("diffuseHorizontal").GetDouble(), d.DiffuseHorizontal, 6);
            Assert.Equal(e.GetProperty("clearScale").GetDouble(), d.ClearScale, 6);
            Assert.Equal(e.GetProperty("overcastScale").GetDouble(), d.OvercastScale, 6);
            Assert.Equal(e.GetProperty("sunAirMass").GetDouble(), d.SunAirMass, 6);
            var dir = e.GetProperty("sunDirection").EnumerateArray().Select(x => x.GetDouble()).ToArray();
            Assert.Equal(dir[0], d.SunX, 9);
            Assert.Equal(dir[1], d.SunY, 9);
            Assert.Equal(dir[2], d.SunZ, 9);
        }
    }

    [Fact]
    public void TickSamplingMatchesTheWeb()
    {
        foreach (var e in Expected.GetProperty("tick").EnumerateArray())
        {
            var start = Utc(e.GetProperty("utc").GetString()!);
            var d = SkyModel.SampleTick(start, 4, 48.208333, 16.3725, 0.6, 0, 188);
            Assert.Equal(e.GetProperty("globalHorizontal").GetDouble(), d.GlobalHorizontal, 6);
            Assert.Equal(e.GetProperty("overcastScale").GetDouble(), d.OvercastScale, 6);
            Assert.Equal(Utc(e.GetProperty("instant").GetString()!), SkyModel.TickSunInstant(start, 4, 48.208333, 16.3725));
        }
    }

    [Fact]
    public void ResolverMatchesTheWeb()
    {
        var site = new SolarSite(48.208333, 16.3725, 188, 0, "Europe/Vienna");
        var o = new LightingOptions(Daylight: true, Shadows: true, LeafTranslucency: true);
        foreach (var e in Expected.GetProperty("lighting").EnumerateArray())
        {
            var r = LightingResolver.Resolve(o, VisualMapping.Natural, "2026-03-01", e.GetProperty("step").GetInt32(), 4, site);
            Assert.Equal(e.GetProperty("shadows").GetBoolean(), r.Shadows);
            var sun = e.GetProperty("sun").EnumerateArray().Select(x => x.GetDouble()).ToArray();
            if (r.Shadows)
            {
                Assert.Equal(sun[0], r.SunX, 9);
                Assert.Equal(sun[1], r.SunY, 9);
                Assert.Equal(sun[2], r.SunZ, 9);
                var col = e.GetProperty("sunColor").EnumerateArray().Select(x => x.GetDouble()).ToArray();
                Assert.Equal(col[0], r.SunColor.R, 5);
            }
            var sky = e.GetProperty("hemiSky").EnumerateArray().Select(x => x.GetDouble()).ToArray();
            Assert.Equal(sky[2], r.HemiSky.B, 5);
            Assert.Equal(e.GetProperty("translucency").GetDouble(), r.LeafTranslucency, 6);
        }
        // a data mapping forces the historical look
        var forced = LightingResolver.Resolve(o, VisualMapping.Water, "2026-03-01", 3, 4, site);
        Assert.False(forced.Shadows);
        Assert.Equal(0f, forced.LeafTranslucency);
        Assert.Equal(3.175f, forced.HemiSky.R, 4);
    }
}
