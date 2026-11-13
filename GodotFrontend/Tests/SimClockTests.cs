using GodotFrontend.Core.Time;
using Xunit;

namespace GodotFrontend.Tests;

/// <summary>Port of ThreeFrontend/tests/timeUnits.test.ts plus snap parity with the server.</summary>
public class SimClockTests
{
    [Fact]
    public void SnapMatchesAgroWorld()
    {
        for (var h = 1; h <= 200; ++h) Assert.Equal(Agro.AgroWorld.SnapHoursPerTick(h), SimClock.Snap(h));
        Assert.Equal(1, SimClock.Snap(0));
        Assert.Equal(1, SimClock.Snap(-3));
    }

    [Theory]
    [InlineData(-25, 4, -24)]
    [InlineData(25, 4, 28)]
    [InlineData(-0.5, 4, 0)]
    [InlineData(0, 4, 0)]
    [InlineData(-25, 5, -24)]
    [InlineData(25, 25, 48)]
    public void EffectiveStartOffsetQuantisesTowardTheNextTick(double hours, int hpt, double expected)
        => Assert.Equal(expected, SimClock.EffectiveStartOffsetHours(hours, hpt));

    [Fact]
    public void EncodeTimeReadsSensibly()
    {
        Assert.Equal("1 year", SimClock.EncodeTime(8760));
        Assert.Equal("2 years", SimClock.EncodeTime(2 * 8760));
        Assert.Equal("3 years", SimClock.EncodeTime(3 * 8760 + 100));
        Assert.Equal("1 hour", SimClock.EncodeTime(1));
        Assert.Equal("6 hours", SimClock.EncodeTime(6));
        Assert.Equal("2 days", SimClock.EncodeTime(48));
        Assert.Equal("1 week", SimClock.EncodeTime(168));
        Assert.Equal("1 month", SimClock.EncodeTime(720));
    }

    [Fact]
    public void FormatElapsedIsExact()
    {
        Assert.Equal("0 h", SimClock.FormatElapsed(0));
        Assert.Equal("4 h", SimClock.FormatElapsed(4));
        Assert.Equal("1 d", SimClock.FormatElapsed(24));
        Assert.Equal("2 d 4 h", SimClock.FormatElapsed(52));
        Assert.Equal("1 y", SimClock.FormatElapsed(8760));
        Assert.Equal("1 y 47 d 8 h", SimClock.FormatElapsed(8760 + 47 * 24 + 8));
        Assert.Equal("1 y 3 h", SimClock.FormatElapsed(8760 + 3));
        Assert.Equal("2 y 364 d 20 h", SimClock.FormatElapsed(2 * 8760 + 364 * 24 + 20));
        Assert.Equal("0 h", SimClock.FormatElapsed(-5));
    }

    [Fact]
    public void ClockLabelsTheInstantInTheSitesZone()
    {
        var start = SimClock.TimeOfTimestep(new DateOnly(2026, 3, 1), 0, 4, "Europe/Vienna");
        Assert.Equal("2026-03-01 00:00", SimClock.FormatClock(start, "Europe/Vienna"));
        Assert.Equal("2026-02-28 23:00", SimClock.FormatClock(start, "UTC"));
        var when = SimClock.TimeOfTimestep(new DateOnly(2026, 3, 1), 1 + 359 * 6, 4, "Europe/Vienna");
        Assert.Equal("2027-02-23 04:00", SimClock.FormatClock(when, "Europe/Vienna"));
        var summer = SimClock.TimeOfTimestep(new DateOnly(2026, 3, 1), 150 * 6, 4, "Europe/Vienna");
        Assert.Equal("2026-07-29 01:00", SimClock.FormatClock(summer, "Europe/Vienna"));
        Assert.Equal("2027-01-01 00:00", SimClock.FormatClock(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), "UTC"));
        Assert.Equal("2027-06-01 13:05", SimClock.FormatClock(new DateTime(2027, 6, 1, 13, 5, 0, DateTimeKind.Utc), "Not/AZone"));
    }
}
