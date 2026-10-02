using AimOdometer.Core.Stats;
using AimOdometer.Core.Storage;

namespace AimOdometer.Core.Tests;

public class StatsSummaryTests
{
    private static readonly DateOnly Today = new(2026, 10, 14); // a Wednesday

    private static DayTotal Day(DateOnly date, double cm, long clicks = 0, double peak = 0) =>
        new(date, cm, cm * 0.6, cm * 0.4, clicks, 0, 60, peak);

    private static DayTotal Day(int daysAgo, double cm) => Day(Today.AddDays(-daysAgo), cm);

    [Theory]
    [InlineData(DayOfWeek.Monday, 2026, 10, 12)]
    [InlineData(DayOfWeek.Sunday, 2026, 10, 11)]
    [InlineData(DayOfWeek.Wednesday, 2026, 10, 14)]
    public void StartOfWeek_RespectsFirstDay(DayOfWeek first, int y, int m, int d)
    {
        Assert.Equal(new DateOnly(y, m, d), StatsSummary.StartOfWeek(Today, first));
    }

    [Fact]
    public void Periods_SplitTodayWeekMonthAllTime()
    {
        DayTotal[] days =
        [
            Day(0, 100),                       // today (Wed)
            Day(2, 200),                       // Monday this week
            Day(3, 400),                       // Sunday: last week when weeks start on Monday
            Day(new DateOnly(2026, 9, 30), 800), // last month
        ];

        var periods = StatsSummary.Periods(days, Today, DayOfWeek.Monday);

        Assert.Equal(100, periods.Today.Centimeters);
        Assert.Equal(300, periods.Week.Centimeters);
        Assert.Equal(700, periods.Month.Centimeters);
        Assert.Equal(1500, periods.AllTime.Centimeters);
        Assert.Equal(700, StatsSummary.Periods(days, Today, DayOfWeek.Sunday).Week.Centimeters);
    }

    [Fact]
    public void Sum_KeepsMaximumPeakSpeed()
    {
        DayTotal[] days = [Day(Today, 10, clicks: 2, peak: 50), Day(Today.AddDays(-1), 10, clicks: 3, peak: 80)];
        var total = StatsSummary.Sum(days, DateOnly.MinValue, DateOnly.MaxValue);
        Assert.Equal(80, total.PeakSpeedCmPerSecond);
        Assert.Equal(5, total.Clicks);
    }

    [Fact]
    public void Averages_DistinguishActiveAndCalendarDays()
    {
        // Started 9 days ago; active on 3 of the 10 calendar days.
        DayTotal[] days = [Day(9, 300), Day(5, 600), Day(0, 100)];
        var averages = StatsSummary.Averages(days, DateOnly.MinValue, Today);

        Assert.Equal(3, averages.ActiveDays);
        Assert.Equal(10, averages.CalendarDays);
        Assert.Equal(1000.0 / 3, averages.PerActiveDayCm, precision: 9);
        Assert.Equal(100, averages.PerCalendarDayCm, precision: 9);
        Assert.Equal(700, averages.PerWeekCm, precision: 9);
    }

    [Fact]
    public void Averages_EmptyIsZero()
    {
        Assert.Equal(0, StatsSummary.Averages([], DateOnly.MinValue, Today).PerCalendarDayCm);
    }

    [Fact]
    public void Records_FindBestDayWeekAndStreaks()
    {
        DayTotal[] days =
        [
            Day(20, 150), Day(19, 150), Day(18, 150), Day(17, 150), // 4-day streak
            Day(10, 5000),                                           // best day
            Day(1, 150), Day(0, 150),                                // current streak, 2 days
            Day(5, 50),                                              // under 1 m: not a streak day
        ];

        var records = StatsSummary.Records(days, Today, DayOfWeek.Monday);

        Assert.Equal(new DayRecord(Today.AddDays(-10), 5000), records.BestDay);
        Assert.Equal(StatsSummary.StartOfWeek(Today.AddDays(-10), DayOfWeek.Monday), records.BestWeek!.WeekStart);
        Assert.Equal(4, records.LongestStreak!.Days);
        Assert.Equal(2, records.CurrentStreak!.Days);
    }

    [Fact]
    public void CurrentStreak_SurvivesUntilTheDayIsOver()
    {
        DayTotal[] days = [Day(2, 150), Day(1, 150)]; // nothing yet today
        Assert.Equal(2, StatsSummary.Records(days, Today, DayOfWeek.Monday).CurrentStreak!.Days);
    }

    [Fact]
    public void CurrentStreak_IsNullAfterAMissedDay()
    {
        DayTotal[] days = [Day(3, 150), Day(2, 150)];
        Assert.Null(StatsSummary.Records(days, Today, DayOfWeek.Monday).CurrentStreak);
    }

    [Fact]
    public void Records_EmptyHasNoRecords()
    {
        var records = StatsSummary.Records([], Today, DayOfWeek.Monday);
        Assert.Null(records.BestDay);
        Assert.Null(records.BestWeek);
        Assert.Null(records.LongestStreak);
    }

    [Fact]
    public void Series_FillsMissingDaysWithZero()
    {
        var series = StatsSummary.Series([Day(1, 42)], Today.AddDays(-2), Today);
        Assert.Equal([0, 42, 0], series.Select(s => s.Centimeters));
        Assert.Equal(Today, series[^1].Date);
    }
}
