using AimOdometer.Core.Input;

namespace AimOdometer.Core.Tests;

public class HourKeyTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
    private static readonly TimeZoneInfo India = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void LocalMidnight_StartsNewDate()
    {
        // 23:00 UTC in winter is 00:00 in Berlin (UTC+1).
        var key = HourKey.FromUtc(Utc(2026, 1, 14, 23), Berlin);
        Assert.Equal(new HourKey(new DateOnly(2026, 1, 15), 0, 60), key);
    }

    [Fact]
    public void DstEnd_RepeatedHourHasDifferentOffsets()
    {
        // 25 Oct 2026: clocks go back from 03:00 CEST to 02:00 CET; 02:xx happens twice.
        var first = HourKey.FromUtc(Utc(2026, 10, 25, 0, 30), Berlin);
        var second = HourKey.FromUtc(Utc(2026, 10, 25, 1, 30), Berlin);
        Assert.Equal(2, first.LocalHour);
        Assert.Equal(2, second.LocalHour);
        Assert.Equal(120, first.UtcOffsetMinutes);
        Assert.Equal(60, second.UtcOffsetMinutes);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void DstStart_SkipsMissingHour()
    {
        // 29 Mar 2026: 02:00 CET jumps to 03:00 CEST.
        var before = HourKey.FromUtc(Utc(2026, 3, 29, 0, 59), Berlin);
        var after = HourKey.FromUtc(Utc(2026, 3, 29, 1, 0), Berlin);
        Assert.Equal(1, before.LocalHour);
        Assert.Equal(3, after.LocalHour);
    }

    [Fact]
    public void HalfHourOffset_HourChangesAtQuarterBoundary()
    {
        // India is UTC+5:30: local 10:00 starts at 04:30 UTC.
        Assert.Equal(9, HourKey.FromUtc(Utc(2026, 5, 1, 4, 29), India).LocalHour);
        Assert.Equal(10, HourKey.FromUtc(Utc(2026, 5, 1, 4, 30), India).LocalHour);
        Assert.Equal(Utc(2026, 5, 1, 4, 30), HourKey.NextCheckUtc(Utc(2026, 5, 1, 4, 29)));
    }

    [Fact]
    public void NextCheck_IsStrictlyLater()
    {
        var boundary = Utc(2026, 5, 1, 4, 30);
        Assert.Equal(Utc(2026, 5, 1, 4, 45), HourKey.NextCheckUtc(boundary));
    }

    [Fact]
    public void RejectsLocalTime()
    {
        Assert.Throws<ArgumentException>(() => HourKey.FromUtc(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local), Berlin));
    }
}
