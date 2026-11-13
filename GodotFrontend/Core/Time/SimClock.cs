using System.Globalization;

namespace GodotFrontend.Core.Time;

/// <summary>
/// Simulated time: the server's HoursPerTick snapping, the UTC instant of a step (AgroWorld.GetTime: the initial
/// date is a LOCAL midnight at the site converted to UTC, then whole ticks), and the clock and elapsed labels of
/// helpers/TimeUnits.ts.
/// </summary>
public static class SimClock
{
    public const string DefaultTimeZone = "Europe/Vienna";
    public const int HoursPerYear = 8760;
    public const int HoursPerDay = 24;

    /// <summary>Agro/AgroWorld.cs SnapHoursPerTick; pinned against the real method by a test.</summary>
    public static int Snap(int requested)
    {
        var h = requested;
        if (h >= 24) return 24 * (h / 24);
        if (h <= 0) return 1;
        switch (h)
        {
            case 5: return 4;
            case 7: return 6;
            case 9: case 10: return 8;
            case 11: return 12;
            default:
                if (h > 12 && h <= 18) return 12;
                if (h > 18) return 24;
                return h;
        }
    }

    /// <summary>The IANA zone, or UTC when the platform does not know it (a shifted sun beats a crash inside a render).</summary>
    public static TimeZoneInfo Zone(string? iana)
    {
        if (string.IsNullOrEmpty(iana)) iana = DefaultTimeZone;
        try { return TimeZoneInfo.FindSystemTimeZoneById(iana); }
        catch { return TimeZoneInfo.Utc; }
    }

    public static DateTime LocalMidnightToUtc(DateOnly date, string? timeZone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, Zone(timeZone));
    }

    public static DateTime TimeOfTimestep(DateOnly initialDate, long timestep, int hoursPerTick, string? timeZone)
        => LocalMidnightToUtc(initialDate, timeZone).AddHours((double)timestep * Snap(hoursPerTick));

    public static DateOnly ParseDate(string? s)
    {
        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)) return DateOnly.FromDateTime(dt);
        return new DateOnly(2026, 2, 1);
    }

    /// <summary>"2027-03-14 08:00" in the given zone, 24-hour clock.</summary>
    public static string FormatClock(DateTime utc, string? timeZone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone(timeZone));
        return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Exact compact breakdown "1 y 47 d 8 h" with nominal 365-day years; zero units dropped.</summary>
    public static string FormatElapsed(double hours)
    {
        var total = System.Math.Max(0, (long)System.Math.Round(hours));
        if (total == 0) return "0 h";
        var years = total / HoursPerYear;
        var days = (total % HoursPerYear) / HoursPerDay;
        var rest = total % HoursPerDay;
        var parts = new List<string>(3);
        if (years > 0) parts.Add($"{years} y");
        if (days > 0) parts.Add($"{days} d");
        if (rest > 0) parts.Add($"{rest} h");
        return string.Join(" ", parts);
    }

    /// <summary>Rounded single-unit label for a setting ("about 3 months").</summary>
    public static string EncodeTime(long t)
    {
        static string Plural(long n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")}";
        if (t == 1) return "1 hour";
        if (t < 24) return $"{t} hours";
        if (t == 24) return "1 day";
        if (t < 168) return Plural((long)System.Math.Round(t / 24.0), "day");
        if (t == 168) return "1 week";
        if (t < 720) return Plural((long)System.Math.Round(t / 168.0), "week");
        if (t == 720) return "1 month";
        if (t < 8760) return Plural((long)System.Math.Round(t / 720.0), "month");
        if (t == 8760) return "1 year";
        return Plural((long)System.Math.Round(t / 8760.0), "year");
    }

    /// <summary>First simulation tick at or after seeding, for either sign (effectiveStartOffsetHours).</summary>
    public static double EffectiveStartOffsetHours(double hours, int requestedHoursPerTick)
    {
        var tick = Snap(requestedHoursPerTick);
        var v = System.Math.Ceiling(hours / tick) * tick;
        return v == 0 ? 0 : v;
    }
}
