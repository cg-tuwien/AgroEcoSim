namespace Agro;

/// <summary>Daily air temperatures and their time-weighted integration over simulation ticks.</summary>
/// <remarks>The fallback is a smoothly interpolated CLIMATE NORMAL, not measured weather.
/// GeoSphere Austria, Klimanormalwerte 1991-2020, station Wien_Hohe_Warte (48.2486 N,
/// 16.3564 E, 198 m), sheet Temperatur, cells FJ22:FJ33, verified 2026-10-23:
/// https://klimaportal.geosphere.at/klimamittelwerte_1991_2020.html
/// https://klimaportal.geosphere.at/klimamittel/klimanormalperiode_19912020.xlsx
/// These official means use homogenised daily (Tmax + Tmin)/2; they differ from Datahub
/// daily means that also use observation times. The twelve monthly normals are anchored at
/// each month's midpoint and interpolated at local noon to produce a daily mean. Interpolation
/// supplies a smooth seasonal forcing; it does not reconstruct actual daily temperatures.
/// </remarks>
internal sealed class AirTemperatureProfile
{
    static readonly float[] ViennaMonthlyMeansC =
        [1.1f, 2.8f, 6.9f, 11.9f, 16.3f, 20f, 21.9f, 21.6f, 16.6f, 11.2f, 6.2f, 1.8f];

    readonly Dictionary<DateOnly, float> Overrides = new();
    float[] DailyMeans = [];
    float[] DayHours = [];
    Step[] Steps = [];

    readonly record struct Step(int FirstDay, int LastDay, float FirstHours, float LastHours,
        float MeanC);

    internal AirTemperatureProfile(DailyAirTemperature[]? overrides)
    {
        if (overrides == null) return;
        foreach (var entry in overrides)
        {
            if (entry == null || entry.Date == default || !float.IsFinite(entry.MeanC))
                throw new ArgumentException("Daily air temperatures require a date and a finite MeanC.",
                    nameof(SimulationRequest.DailyAirTemperatures));
            if (!Overrides.TryAdd(DateOnly.FromDateTime(entry.Date), entry.MeanC))
                throw new ArgumentException($"Duplicate daily air temperature date: {entry.Date:yyyy-MM-dd}.",
                    nameof(SimulationRequest.DailyAirTemperatures));
        }
    }

    static float ClimateMean(DateTime date)
    {
        var sample = date.Date.AddHours(12);
        var month = new DateTime(date.Year, date.Month, 1);
        var midpoint = month.AddDays(DateTime.DaysInMonth(date.Year, date.Month) / 2.0);
        var adjacent = month.AddMonths(sample < midpoint ? -1 : 1);
        var adjacentMidpoint = adjacent.AddDays(DateTime.DaysInMonth(adjacent.Year, adjacent.Month) / 2.0);
        var weight = (sample - midpoint).TotalDays / (adjacentMidpoint - midpoint).TotalDays;
        var current = ViennaMonthlyMeansC[date.Month - 1];
        return current + (float)weight * (ViennaMonthlyMeansC[adjacent.Month - 1] - current);
    }

    // Midnight transitions exist in some time zones. Use the first valid instant of that
    // local day rather than failing on a date whose clock starts at 01:00.
    static DateTime DayStartUtc(DateTime date, TimeZoneInfo zone)
    {
        while (zone.IsInvalidTime(date)) date = date.AddMinutes(1);
        return TimeZoneInfo.ConvertTimeToUtc(date, zone);
    }

    internal void Plan(DateTime initialLocal, TimeZoneInfo zone, ushort hoursPerTick, int count)
        => PlanUtc(TimeZoneInfo.ConvertTimeToUtc(initialLocal, zone), zone, hoursPerTick, count);

    internal void PlanUtc(DateTime startUtc, TimeZoneInfo zone, ushort hoursPerTick, int count)
    {
        Steps = new Step[count];
        if (count == 0) return;
        var endUtc = startUtc.AddHours((double)count * hoursPerTick);
        var firstDate = TimeZoneInfo.ConvertTimeFromUtc(startUtc, zone).Date;
        var lastDate = TimeZoneInfo.ConvertTimeFromUtc(endUtc.AddTicks(-1), zone).Date;
        var days = (lastDate - firstDate).Days + 1;
        DailyMeans = new float[days];
        DayHours = new float[days];
        var dayStarts = new DateTime[days + 1];
        for (int day = 0; day <= days; ++day)
        {
            var date = firstDate.AddDays(day);
            dayStarts[day] = DayStartUtc(date, zone);
            if (day == days) break;
            DailyMeans[day] = Overrides.TryGetValue(DateOnly.FromDateTime(date), out var value)
                ? value : ClimateMean(date);
        }
        for (int day = 0; day < days; ++day)
            DayHours[day] = (float)(dayStarts[day + 1] - dayStarts[day]).TotalHours;

        int first = 0, last = 0;
        for (int tick = 0; tick < count; ++tick)
        {
            var start = startUtc.AddHours((double)tick * hoursPerTick);
            var end = start.AddHours(hoursPerTick);
            while (first + 1 < days && dayStarts[first + 1] <= start) ++first;
            last = Math.Max(first, last);
            while (last + 1 < days && dayStarts[last + 1] < end) ++last;
            if (first == last)
            {
                Steps[tick] = new(first, last, hoursPerTick, 0, DailyMeans[first]);
                continue;
            }
            var firstHours = (float)(dayStarts[first + 1] - start).TotalHours;
            var lastHours = (float)(end - dayStarts[last]).TotalHours;
            double sum = DailyMeans[first] * firstHours + DailyMeans[last] * lastHours;
            for (int day = first + 1; day < last; ++day)
                sum += DailyMeans[day] * DayHours[day];
            Steps[tick] = new(first, last, firstHours, lastHours, (float)(sum / hoursPerTick));
        }
    }

    internal float MeanC(uint timestep) => Steps[timestep].MeanC;

    /// <summary>Integrates the clamped daily thermal response, rather than clamping the
    /// tick's mean temperature. These differ for ticks spanning cold and warm days.</summary>
    internal float VegetativeFactor(uint timestep, float baseC, float fullC, ushort hoursPerTick)
    {
        float Factor(float meanC) => Math.Clamp((meanC - baseC) / (fullC - baseC), 0f, 1f);
        var step = Steps[timestep];
        if (step.FirstDay == step.LastDay) return Factor(step.MeanC);
        double sum = Factor(DailyMeans[step.FirstDay]) * step.FirstHours
            + Factor(DailyMeans[step.LastDay]) * step.LastHours;
        for (int day = step.FirstDay + 1; day < step.LastDay; ++day)
            sum += Factor(DailyMeans[day]) * DayHours[day];
        return (float)(sum / hoursPerTick);
    }
}
