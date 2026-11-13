namespace GodotFrontend.Core.Lighting;

/// <summary>A sun position: altitude above the horizon and azimuth clockwise from north, in degrees.</summary>
public readonly record struct SolarPosition(double Altitude, double Azimuth);

/// <summary>helpers/Sun.ts: the NOAA solar position algorithm and the scene's compass convention.</summary>
public static class Solar
{
    private const double Deg = System.Math.PI / 180;

    private static double Mod(double v, double m) => ((v % m) + m) % m;

    public static SolarPosition Position(DateTime utc, double latitude, double longitude)
    {
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var julianDay = (utc - DateTime.UnixEpoch).TotalMilliseconds / 86400000.0 + 2440587.5;
        var t = (julianDay - 2451545) / 36525;
        var meanLongitude = Mod(280.46646 + t * (36000.76983 + t * 0.0003032), 360);
        var meanAnomaly = 357.52911 + t * (35999.05029 - 0.0001537 * t);
        var eccentricity = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);
        var centre = System.Math.Sin(meanAnomaly * Deg) * (1.914602 - t * (0.004817 + 0.000014 * t))
            + System.Math.Sin(2 * meanAnomaly * Deg) * (0.019993 - 0.000101 * t)
            + System.Math.Sin(3 * meanAnomaly * Deg) * 0.000289;
        var trueLongitude = meanLongitude + centre;
        var apparentLongitude = trueLongitude - 0.00569 - 0.00478 * System.Math.Sin((125.04 - 1934.136 * t) * Deg);
        var obliquity = 23 + (26 + (21.448 - t * (46.815 + t * (0.00059 - t * 0.001813))) / 60) / 60;
        var correctedObliquity = obliquity + 0.00256 * System.Math.Cos((125.04 - 1934.136 * t) * Deg);
        var declination = System.Math.Asin(System.Math.Sin(correctedObliquity * Deg) * System.Math.Sin(apparentLongitude * Deg)) / Deg;
        var varY = System.Math.Pow(System.Math.Tan(correctedObliquity / 2 * Deg), 2);
        var equationOfTime = 4 * (varY * System.Math.Sin(2 * meanLongitude * Deg)
            - 2 * eccentricity * System.Math.Sin(meanAnomaly * Deg)
            + 4 * eccentricity * varY * System.Math.Sin(meanAnomaly * Deg) * System.Math.Cos(2 * meanLongitude * Deg)
            - 0.5 * varY * varY * System.Math.Sin(4 * meanLongitude * Deg)
            - 1.25 * eccentricity * eccentricity * System.Math.Sin(2 * meanAnomaly * Deg)) / Deg;
        var utcMinutes = utc.Hour * 60 + utc.Minute + utc.Second / 60.0;
        var trueSolarTime = Mod(utcMinutes + equationOfTime + 4 * longitude, 1440);
        var hourAngle = trueSolarTime / 4 < 0 ? trueSolarTime / 4 + 180 : trueSolarTime / 4 - 180;
        double latRad = latitude * Deg, decRad = declination * Deg, haRad = hourAngle * Deg;
        var cosZenith = System.Math.Sin(latRad) * System.Math.Sin(decRad) + System.Math.Cos(latRad) * System.Math.Cos(decRad) * System.Math.Cos(haRad);
        var zenith = System.Math.Acos(System.Math.Clamp(cosZenith, -1, 1)) / Deg;
        var altitude = 90 - zenith;
        double azimuth;
        var denom = System.Math.Cos(latRad) * System.Math.Sin(zenith * Deg);
        if (System.Math.Abs(denom) > 1e-9)
        {
            var cosAz = (System.Math.Sin(latRad) * System.Math.Cos(zenith * Deg) - System.Math.Sin(decRad)) / denom;
            var b = System.Math.Acos(System.Math.Clamp(cosAz, -1, 1)) / Deg;
            azimuth = hourAngle > 0 ? Mod(b + 180, 360) : Mod(540 - b, 360);
        }
        else azimuth = latitude > 0 ? 180 : 0;
        return new SolarPosition(altitude, azimuth);
    }

    /// <summary>Unit vector toward the sun: +Y up, +X east, north is -Z.</summary>
    public static (double x, double y, double z) DirectionFrom(double altitudeDeg, double azimuthDeg)
    {
        var alt = altitudeDeg * Deg;
        var az = azimuthDeg * Deg;
        var horizontal = System.Math.Cos(alt);
        return (horizontal * System.Math.Sin(az), System.Math.Sin(alt), -horizontal * System.Math.Cos(az));
    }
}
