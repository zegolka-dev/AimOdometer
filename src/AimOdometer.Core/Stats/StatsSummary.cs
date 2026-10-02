using AimOdometer.Core.Storage;

namespace AimOdometer.Core.Stats;

/// <summary>Distance and activity in the standard periods shown on the overview.</summary>
public sealed record PeriodTotals(DayTotal Today, DayTotal Week, DayTotal Month, DayTotal AllTime);

/// <summary>
/// Two kinds of averages, because both are useful and both are easy to misread:
/// per active day (only days you used the mouse) and per calendar day (every day since you started).
/// </summary>
public sealed record Averages(
    double PerActiveDayCm,
    double PerCalendarDayCm,
    double PerWeekCm,
    double PerMonthCm,
    int ActiveDays,
    int CalendarDays);

public sealed record DayRecord(DateOnly Date, double Centimeters);

public sealed record WeekRecord(DateOnly WeekStart, double Centimeters);

public sealed record Streak(DateOnly Start, DateOnly End)
{
    public int Days => End.DayNumber - Start.DayNumber + 1;
}

public sealed record Records(DayRecord? BestDay, WeekRecord? BestWeek, Streak? LongestStreak, Streak? CurrentStreak);

/// <summary>Pure calculations over daily totals (no database access), so they are easy to test.</summary>
public static class StatsSummary
{
    /// <summary>A day counts toward a streak when the mouse travelled at least this far (1 m).</summary>
    public const double ActiveDayMinimumCm = 100;

    private const double DaysPerMonth = 365.2425 / 12;

    public static DateOnly StartOfWeek(DateOnly date, DayOfWeek firstDayOfWeek) =>
        date.AddDays(-(((int)date.DayOfWeek - (int)firstDayOfWeek + 7) % 7));

    /// <summary>Sums the days in [from, to] into one total (peak speed is the maximum).</summary>
    public static DayTotal Sum(IEnumerable<DayTotal> days, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(days);
        var cm = 0.0;
        var x = 0.0;
        var y = 0.0;
        var wheel = 0.0;
        var peak = 0.0;
        long clicks = 0;
        long move = 0;
        foreach (var d in days)
        {
            if (d.Date < from || d.Date > to)
            {
                continue;
            }

            cm += d.Centimeters;
            x += d.XCentimeters;
            y += d.YCentimeters;
            clicks += d.Clicks;
            wheel += d.WheelNotches;
            move += d.MoveSeconds;
            peak = Math.Max(peak, d.PeakSpeedCmPerSecond);
        }

        return new DayTotal(from, cm, x, y, clicks, wheel, move, peak);
    }

    public static PeriodTotals Periods(IReadOnlyList<DayTotal> days, DateOnly today, DayOfWeek firstDayOfWeek) => new(
        Today: Sum(days, today, today),
        Week: Sum(days, StartOfWeek(today, firstDayOfWeek), today),
        Month: Sum(days, new DateOnly(today.Year, today.Month, 1), today),
        AllTime: Sum(days, DateOnly.MinValue, DateOnly.MaxValue));

    /// <summary>
    /// Averages over [from, to]. Calendar days start at the first day with data (or <paramref name="from"/> if later),
    /// so a new user is not penalised for the years before they installed the app.
    /// </summary>
    public static Averages Averages(IReadOnlyList<DayTotal> days, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(days);
        var inRange = days.Where(d => d.Date >= from && d.Date <= to).ToList();
        if (inRange.Count == 0)
        {
            return new Averages(0, 0, 0, 0, 0, 0);
        }

        var total = inRange.Sum(d => d.Centimeters);
        var active = inRange.Count(d => d.Centimeters > 0);
        var start = inRange.Min(d => d.Date);
        var calendarDays = to.DayNumber - (start > from ? start : from).DayNumber + 1;
        var perCalendarDay = total / calendarDays;
        return new Averages(
            PerActiveDayCm: active == 0 ? 0 : total / active,
            PerCalendarDayCm: perCalendarDay,
            PerWeekCm: perCalendarDay * 7,
            PerMonthCm: perCalendarDay * DaysPerMonth,
            ActiveDays: active,
            CalendarDays: calendarDays);
    }

    public static Records Records(IReadOnlyList<DayTotal> days, DateOnly today, DayOfWeek firstDayOfWeek)
    {
        ArgumentNullException.ThrowIfNull(days);
        var bestDay = days.Where(d => d.Centimeters > 0).MaxBy(d => d.Centimeters) is { } best
            ? new DayRecord(best.Date, best.Centimeters)
            : null;

        var bestWeek = days
            .GroupBy(d => StartOfWeek(d.Date, firstDayOfWeek))
            .Select(g => new WeekRecord(g.Key, g.Sum(d => d.Centimeters)))
            .Where(w => w.Centimeters > 0)
            .MaxBy(w => w.Centimeters);

        var activeDates = days.Where(d => d.Centimeters >= ActiveDayMinimumCm).Select(d => d.Date).Order().ToList();
        Streak? longest = null;
        Streak? current = null;
        for (var i = 0; i < activeDates.Count;)
        {
            var j = i;
            while (j + 1 < activeDates.Count && activeDates[j + 1].DayNumber == activeDates[j].DayNumber + 1)
            {
                j++;
            }

            var streak = new Streak(activeDates[i], activeDates[j]);
            if (longest is null || streak.Days > longest.Days)
            {
                longest = streak;
            }

            // The current streak may end today or yesterday (today is not over yet).
            if (streak.End == today || streak.End == today.AddDays(-1))
            {
                current = streak;
            }

            i = j + 1;
        }

        return new Records(bestDay, bestWeek, longest, current);
    }

    /// <summary>One value per day in [from, to], zero for days without data.</summary>
    public static IReadOnlyList<DayRecord> Series(IReadOnlyList<DayTotal> days, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(days);
        var byDate = days.ToDictionary(d => d.Date, d => d.Centimeters);
        var result = new List<DayRecord>(Math.Max(0, to.DayNumber - from.DayNumber + 1));
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            result.Add(new DayRecord(date, byDate.GetValueOrDefault(date)));
        }

        return result;
    }
}
