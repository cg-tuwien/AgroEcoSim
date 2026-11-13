using GodotFrontend.Core.Color;
using GodotFrontend.Core.Time;

namespace GodotFrontend.Core.Lighting;

/// <summary>The viewport lighting switches (core/Lighting.ts LightingOptions); independent, not a ladder.</summary>
public sealed record LightingOptions(bool Daylight = false, bool Shadows = false, bool Occlusion = false, bool Indirect = false,
    bool SecondBounce = false, bool LeafTranslucency = false, bool Sky = false, double ShadowBiasTexels = 4)
{
    public static readonly LightingOptions Off = new();
}

/// <summary>Where a run is, for the sun and the sky.</summary>
public readonly record struct SolarSite(double Latitude, double Longitude, double AltitudeM, double AzimuthDeg, string? TimeZone)
{
    public static readonly SolarSite Default = new(48.208333, 16.3725, 188, 0, null);
}

/// <summary>What a frame is lit with (core/Lighting.ts FrameLighting). Colours already carry their intensity.</summary>
public sealed record FrameLighting(RGB HemiSky, RGB HemiGround, bool AmbientOcclusion, bool Indirect, int Bounces,
    double SunX, double SunY, double SunZ, RGB SunColor, bool Shadows, float LeafTranslucency, bool Sky, double ShadowBiasTexels)
{
    /// <summary>Whether this frame uses no screen-space or GI effect, so the exact analytic hemisphere can be used.</summary>
    public bool Analytic => !AmbientOcclusion && !Indirect;
}

/// <summary>
/// core/Lighting.ts resolveLighting. Two rules live here rather than in the UI: a non-natural data mapping forces the
/// historical look (a quantitative readout must not change colour with leaf orientation), and a sun below the
/// horizon contributes nothing.
/// </summary>
public static class LightingResolver
{
    public static readonly RGB HemiSky = ColorMath.FromHex("#ffffff");
    public static readonly RGB HemiGround = ColorMath.FromHex("#707070");
    public const float HemiIntensity = 3.175f;
    public static readonly RGB DaySky = ColorMath.FromHex("#bfd4ff");
    public static readonly RGB DayGround = ColorMath.FromHex("#6b5a44");
    public const float DaySkyIntensity = 0.85f;
    public const float DaySunIntensity = 4.0f;
    public const float SunIntensity = 1.6f;
    public const float LeafTranslucency = 0.4f;
    public static readonly RGB SunColor = ColorMath.FromHex("#fff4e0");

    public static FrameLighting Resolve(LightingOptions o, VisualMapping mapping, string initialDate, long timestep, int hoursPerTick, SolarSite site)
    {
        var dataMapping = mapping != VisualMapping.Natural;
        var anything = o.Daylight || o.Shadows || o.Occlusion || o.Indirect || o.LeafTranslucency;
        var legacySky = HemiSky * HemiIntensity;
        var legacyGround = HemiGround * HemiIntensity;
        var sky = o.Sky && !dataMapping;
        if (!anything || dataMapping)
            return new FrameLighting(legacySky, legacyGround, false, false, 1, 0, 0, 0, RGB.Black, false, 0, sky, o.ShadowBiasTexels);

        var hemiSky = o.Daylight ? DaySky * DaySkyIntensity : legacySky;
        var hemiGround = o.Daylight ? DayGround * DaySkyIntensity : legacyGround;
        var sunPeak = o.Daylight ? DaySunIntensity : SunIntensity;
        var bounces = o.Indirect && o.SecondBounce ? 2 : 1;
        var translucency = o.LeafTranslucency ? LeafTranslucency : 0f;
        var noSun = new FrameLighting(hemiSky, hemiGround, o.Occlusion, o.Indirect, bounces, 0, 1, 0, RGB.Black, false, translucency, sky, o.ShadowBiasTexels);
        if (!o.Shadows) return noSun;

        var start = SimClock.TimeOfTimestep(SimClock.ParseDate(initialDate), timestep, hoursPerTick, site.TimeZone);
        var when = SkyModel.TickSunInstant(start, SimClock.Snap(hoursPerTick), site.Latitude, site.Longitude);
        var sun = Solar.Position(when, site.Latitude, site.Longitude);
        if (sun.Altitude <= 0) return noSun;
        var horizonFade = System.Math.Min(1, sun.Altitude / 3);
        var scale = (float)(sunPeak * horizonFade);
        var (x, y, z) = Solar.DirectionFrom(sun.Altitude, sun.Azimuth - site.AzimuthDeg);
        return noSun with { SunX = x, SunY = y, SunZ = z, SunColor = SunColor * scale, Shadows = true };
    }
}
