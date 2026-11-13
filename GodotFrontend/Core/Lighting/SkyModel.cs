namespace GodotFrontend.Core.Lighting;

/// <summary>render/core/SkyDome.ts SkyDome: the sun and the two normalised CIE sky shapes for one instant.</summary>
public readonly record struct SkyDomeSample(
    double SunX, double SunY, double SunZ,
    double DirectNormal, double DiffuseHorizontal, double GlobalHorizontal, double GroundRadiance,
    double ClearScale, double OvercastScale, double SunAirMass)
{
    public bool IsLit => GlobalHorizontal > 0.5;
}

/// <summary>
/// A port of render/core/SkyDome.ts (itself a port of Agro/Irradiance/SkyDome.cs): Kasten-Young air mass with the
/// station-pressure correction, Meinel beam, Liu-Jordan diffuse, Kasten-Czeplak cloud attenuation, the Erbs split
/// and the CIE clear/overcast shapes normalised over 128 Hammersley directions; plus the tick sampling the viewport
/// uses (the brightest of clamp(hoursPerTick, 1, 6) sub-step midpoints, scaled to the tick mean).
/// </summary>
public static class SkyModel
{
    public const double GroundAlbedo = 0.2;
    public const double PressureScaleHeightM = 8435;
    private const double ClearB = -0.32, ClearC = 10, ClearD = -3, ClearE = 0.45, OvercastA = 4, OvercastB = -0.7;
    private const double Deg = System.Math.PI / 180;
    public static readonly double ClearIndicatrixOffset = System.Math.Exp(ClearD * System.Math.PI * 0.5);
    private static readonly double[] NormDirs = BuildDirs(128);

    private static readonly SkyDomeSample Dark = new(0, 1, 0, 0, 0, 0, 0, 0, 0, 0);

    public static double RadicalInverse2(uint bits)
    {
        bits = (bits << 16) | (bits >> 16);
        bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
        bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
        bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
        bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
        return bits * 2.3283064365386963e-10;
    }

    private static double[] BuildDirs(int count)
    {
        var r = new double[count * 3];
        for (var i = 0; i < count; ++i)
        {
            var u1 = (i + 0.5) / count;
            var u2 = RadicalInverse2((uint)i);
            var rr = System.Math.Sqrt(u1);
            var phi = 2 * System.Math.PI * u2;
            r[i * 3] = rr * System.Math.Cos(phi);
            r[i * 3 + 1] = System.Math.Sqrt(System.Math.Max(0, 1 - u1));
            r[i * 3 + 2] = rr * System.Math.Sin(phi);
        }
        return r;
    }

    public static double FastAcos(double x)
    {
        var negate = x < 0;
        var a = System.Math.Abs(x);
        var ret = -0.0187293 * a + 0.0742610;
        ret = ret * a - 0.2121144;
        ret = ret * a + 1.5707288;
        ret *= System.Math.Sqrt(System.Math.Max(0, 1 - a));
        return negate ? System.Math.PI - ret : ret;
    }

    public static double AirMass(double elevationDegrees, double altitudeM = 0)
    {
        var sinElevation = System.Math.Sin(elevationDegrees * Deg);
        var relative = 1 / (sinElevation + 0.50572 * System.Math.Pow(elevationDegrees + 6.07995, -1.6364));
        return altitudeM == 0 ? relative : relative * System.Math.Exp(-altitudeM / PressureScaleHeightM);
    }

    private static double ClearShape(double cosZenith, double cosGamma)
    {
        var gradation = 1 - System.Math.Exp(ClearB / System.Math.Max(cosZenith, 1e-4));
        var gamma = FastAcos(cosGamma);
        var indicatrix = 1 + ClearC * (System.Math.Exp(ClearD * gamma) - ClearIndicatrixOffset) + ClearE * cosGamma * cosGamma;
        return gradation * indicatrix;
    }

    private static double OvercastShape(double cosZenith) => 1 + OvercastA * System.Math.Exp(OvercastB / System.Math.Max(cosZenith, 1e-4));

    public static SkyDomeSample Sample(DateTime utc, double latitude, double longitude, double skyCoverage = 0, double worldAzimuthDeg = 0, double altitudeM = 0)
    {
        var solar = Solar.Position(utc, latitude, longitude);
        var sinElevation = System.Math.Sin(solar.Altitude * Deg);
        if (!(sinElevation > 0.001)) return Dark;
        var (sx, sy, sz) = Solar.DirectionFrom(solar.Altitude, solar.Azimuth - worldAzimuthDeg);
        var dayAngle = 2 * System.Math.PI * (utc.DayOfYear - 1) / 365;
        var extraterrestrial = 1361 * (1 + 0.033 * System.Math.Cos(dayAngle));
        var m = AirMass(solar.Altitude, altitudeM);
        var beamTransmittance = System.Math.Pow(0.7, System.Math.Pow(m, 0.678));
        var diffuseTransmittance = System.Math.Max(0, 0.271 - 0.294 * beamTransmittance);
        var clearSkyGlobal = extraterrestrial * sinElevation * (beamTransmittance + diffuseTransmittance);
        var coverage = System.Math.Clamp(skyCoverage, 0, 1);
        var global = clearSkyGlobal * (1 - 0.75 * System.Math.Pow(coverage, 3.4));
        if (!(global > 0.5)) return Dark with { SunX = sx, SunY = sy, SunZ = sz, SunAirMass = m };
        var clearness = System.Math.Clamp(global / (extraterrestrial * sinElevation), 0, 1);
        double diffuseFraction;
        if (clearness <= 0.22) diffuseFraction = 1 - 0.09 * clearness;
        else if (clearness <= 0.8)
        {
            var k2 = clearness * clearness;
            diffuseFraction = 0.9511 - 0.1604 * clearness + 4.388 * k2 - 16.638 * k2 * clearness + 12.336 * k2 * k2;
        }
        else diffuseFraction = 0.165;
        diffuseFraction = System.Math.Clamp(diffuseFraction, 0.05, 1);
        var diffuseHorizontal = global * diffuseFraction;
        var directNormal = System.Math.Max(0, (global - diffuseHorizontal) / sinElevation);
        double clearSum = 0, overcastSum = 0;
        var count = NormDirs.Length / 3;
        for (var i = 0; i < count; ++i)
        {
            double dx = NormDirs[i * 3], dy = NormDirs[i * 3 + 1], dz = NormDirs[i * 3 + 2];
            var cosGamma = System.Math.Clamp(dx * sx + dy * sy + dz * sz, -1, 1);
            clearSum += ClearShape(dy, cosGamma);
            overcastSum += OvercastShape(dy);
        }
        var clearIntegral = System.Math.PI * (clearSum / count);
        var overcastIntegral = System.Math.PI * (overcastSum / count);
        var clearScale = clearIntegral > 1e-6 ? (1 - coverage) * diffuseHorizontal / clearIntegral : 0;
        var overcastScale = overcastIntegral > 1e-6 ? coverage * diffuseHorizontal / overcastIntegral : 0;
        return new SkyDomeSample(sx, sy, sz, directNormal, diffuseHorizontal, global, GroundAlbedo * global / System.Math.PI, clearScale, overcastScale, m);
    }

    private static int SubStepsOf(int hoursPerTick) => hoursPerTick < 1 ? 1 : hoursPerTick > 6 ? 6 : hoursPerTick;

    private static DateTime SubStepInstant(DateTime start, int s, double spanHours) => start.AddHours((s + 0.5) * spanHours);

    /// <summary>The brightest sub-step midpoint of the tick, scaled to the tick's mean global irradiance.</summary>
    public static SkyDomeSample SampleTick(DateTime start, int hoursPerTick, double latitude, double longitude, double skyCoverage = 0, double worldAzimuthDeg = 0, double altitudeM = 0)
    {
        var subSteps = SubStepsOf(hoursPerTick);
        var span = (double)hoursPerTick / subSteps;
        SkyDomeSample? brightest = null;
        double meanGlobal = 0;
        for (var s = 0; s < subSteps; ++s)
        {
            var dome = Sample(SubStepInstant(start, s, span), latitude, longitude, skyCoverage, worldAzimuthDeg, altitudeM);
            meanGlobal += dome.GlobalHorizontal;
            if (brightest is null || dome.GlobalHorizontal > brightest.Value.GlobalHorizontal) brightest = dome;
        }
        meanGlobal /= subSteps;
        var b = brightest!.Value;
        if (!b.IsLit) return b;
        var f = meanGlobal / b.GlobalHorizontal;
        var g = b.GlobalHorizontal * f;
        return b with
        {
            DirectNormal = b.DirectNormal * f, DiffuseHorizontal = b.DiffuseHorizontal * f, GlobalHorizontal = g,
            GroundRadiance = GroundAlbedo * g / System.Math.PI, ClearScale = b.ClearScale * f, OvercastScale = b.OvercastScale * f,
        };
    }

    /// <summary>The instant the viewport's sun is placed at for a tick (the brightest clear-sky sub-step midpoint).</summary>
    public static DateTime TickSunInstant(DateTime start, int hoursPerTick, double latitude, double longitude)
    {
        var subSteps = SubStepsOf(hoursPerTick);
        var span = (double)hoursPerTick / subSteps;
        var best = SubStepInstant(start, 0, span);
        double bestGlobal = -1;
        for (var s = 0; s < subSteps; ++s)
        {
            var at = SubStepInstant(start, s, span);
            var g = Sample(at, latitude, longitude).GlobalHorizontal;
            if (g > bestGlobal) { bestGlobal = g; best = at; }
        }
        return best;
    }
}
