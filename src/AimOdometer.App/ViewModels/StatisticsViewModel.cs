using System.Globalization;
using AimOdometer.App.Controls;
using AimOdometer.App.Localization;
using AimOdometer.App.Services;
using AimOdometer.Core.Games;
using AimOdometer.Core.Stats;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AimOdometer.App.ViewModels;

public enum StatsRange
{
    Days7,
    Days30,
    Days90,
    Days365,
    All,
    Custom,
}

/// <summary>Totals, averages, records and the daily chart for a chosen period.</summary>
public sealed partial class StatisticsViewModel(AppData data) : PageViewModel(data)
{
    private const int MaxDailyBars = 120;

    public override string TitleKey => "Nav.Statistics";

    public override string Icon => "";

    [ObservableProperty]
    public partial StatsRange Range { get; set; } = StatsRange.Days30;

    [ObservableProperty]
    public partial DateTime? CustomFrom { get; set; } = DateTime.Today.AddDays(-29);

    [ObservableProperty]
    public partial DateTime? CustomTo { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial string Total { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Clicks { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ActiveTime { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PerActiveDay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PerCalendarDay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PerWeek { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PerMonth { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ActiveDaysText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BestDay { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BestWeek { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LongestStreak { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FlickRecord { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double HorizontalShare { get; set; }

    [ObservableProperty]
    public partial string AxisSplit { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ClicksPerMinute { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ClicksPerMeter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WheelNotches { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChartTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<ChartBar> Bars { get; set; } = [];

    [ObservableProperty]
    public partial HeatMapData? Activity { get; set; }

    public bool IsCustom => Range == StatsRange.Custom;

    partial void OnRangeChanged(StatsRange value)
    {
        OnPropertyChanged(nameof(IsCustom));
        Refresh();
    }

    partial void OnCustomFromChanged(DateTime? value)
    {
        if (IsCustom)
        {
            Refresh();
        }
    }

    partial void OnCustomToChanged(DateTime? value)
    {
        if (IsCustom)
        {
            Refresh();
        }
    }

    public override void Refresh()
    {
        var L = Loc.Instance;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var firstDay = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var all = Data.Store.GetDailyTotals();
        var (from, to) = Period(today, all.Count > 0 ? all[0].Date : today);
        var days = all.Where(d => d.Date >= from && d.Date <= to).ToList();

        var sum = StatsSummary.Sum(days, from, to);
        Total = Format.Distance(sum.Centimeters);
        Clicks = Format.Number(sum.Clicks);
        ActiveTime = Format.Duration(sum.MoveSeconds);

        var averages = StatsSummary.Averages(days, from, to);
        PerActiveDay = Format.Distance(averages.PerActiveDayCm);
        PerCalendarDay = Format.Distance(averages.PerCalendarDayCm);
        PerWeek = Format.Distance(averages.PerWeekCm);
        PerMonth = Format.Distance(averages.PerMonthCm);
        ActiveDaysText = L.Format("Stats.ActiveDaysOf", averages.ActiveDays, averages.CalendarDays);

        var records = StatsSummary.Records(days, today, firstDay);
        BestDay = records.BestDay is { } d ? $"{Format.Distance(d.Centimeters)} · {Format.Date(d.Date)}" : "-";
        BestWeek = records.BestWeek is { } w ? $"{Format.Distance(w.Centimeters)} · {Format.ShortDate(w.WeekStart)} - {Format.ShortDate(w.WeekStart.AddDays(6))}" : "-";
        LongestStreak = records.LongestStreak is { } s
            ? $"{L.Format("Stats.DaysCount", s.Days)} · {Format.ShortDate(s.Start)} - {Format.ShortDate(s.End)}"
            : "-";
        FlickRecord = FormatFlickRecord(from, to);

        var axisTotal = sum.XCentimeters + sum.YCentimeters;
        HorizontalShare = axisTotal > 0 ? sum.XCentimeters / axisTotal : 0.5;
        AxisSplit = L.Format("Stats.AxisSplit", Format.Percent(HorizontalShare), Format.Percent(1 - HorizontalShare));
        ClicksPerMinute = sum.MoveSeconds > 0 ? Format.Number(sum.Clicks / (sum.MoveSeconds / 60.0), "0.0") : "-";
        ClicksPerMeter = sum.Centimeters >= 100 ? Format.Number(sum.Clicks / (sum.Centimeters / 100), "0.0") : "-";
        WheelNotches = Format.Number(sum.WheelNotches);

        BuildChart(days, from, to, firstDay);

        var cells = new double[7, 24];
        foreach (var cell in Data.Store.GetHourOfWeekTotals(from, to))
        {
            cells[(int)cell.Day, cell.Hour] += cell.Centimeters;
        }

        Activity = new HeatMapData(cells, firstDay, Loc.Instance.Culture, cm => Format.Distance(cm));
    }

    private (DateOnly From, DateOnly To) Period(DateOnly today, DateOnly firstData) => Range switch
    {
        StatsRange.Days7 => (today.AddDays(-6), today),
        StatsRange.Days90 => (today.AddDays(-89), today),
        StatsRange.Days365 => (today.AddDays(-364), today),
        StatsRange.All => (firstData < today ? firstData : today, today),
        StatsRange.Custom when CustomFrom is { } f && CustomTo is { } t =>
            DateOnly.FromDateTime(f) <= DateOnly.FromDateTime(t)
                ? (DateOnly.FromDateTime(f), DateOnly.FromDateTime(t))
                : (DateOnly.FromDateTime(t), DateOnly.FromDateTime(f)),
        _ => (today.AddDays(-29), today),
    };

    private string FormatFlickRecord(DateOnly from, DateOnly to)
    {
        if (Data.Store.GetPeakSpeedRecord() is not { } record || record.Date < from || record.Date > to)
        {
            // The all-time record lies outside the period: show the period's best day value instead.
            var best = Data.Store.GetDailyTotals(from, to).MaxBy(d => d.PeakSpeedCmPerSecond);
            return best is null || best.PeakSpeedCmPerSecond <= 0 ? "-" : $"{Format.Speed(best.PeakSpeedCmPerSecond)} · {Format.Date(best.Date)}";
        }

        var app = Data.Store.GetApps().FirstOrDefault(a => a.Id == record.AppId);
        var where = app is null ? null : Data.Catalog.Classify(app.Id, app.ExePath).Game?.Name;
        return where is null
            ? $"{Format.Speed(record.CmPerSecond)} · {Format.Date(record.Date)}"
            : $"{Format.Speed(record.CmPerSecond)} · {where} · {Format.Date(record.Date)}";
    }

    private void BuildChart(IReadOnlyList<Core.Storage.DayTotal> days, DateOnly from, DateOnly to, DayOfWeek firstDay)
    {
        var L = Loc.Instance;
        var series = StatsSummary.Series(days, from, to);
        if (series.Count <= MaxDailyBars)
        {
            ChartTitle = L["Stats.ChartDaily"];
            Bars = [.. series.Select(d => new ChartBar(d.Centimeters, Format.ShortDate(d.Date), $"{Format.Date(d.Date)}: {Format.Distance(d.Centimeters)}"))];
            return;
        }

        // Long periods: one bar per week keeps bars wide enough to read.
        ChartTitle = L["Stats.ChartWeekly"];
        Bars = [.. series
            .GroupBy(d => StatsSummary.StartOfWeek(d.Date, firstDay))
            .Select(g => new ChartBar(g.Sum(d => d.Centimeters), Format.ShortDate(g.Key),
                $"{Format.ShortDate(g.Key)} - {Format.ShortDate(g.Key.AddDays(6))}: {Format.Distance(g.Sum(d => d.Centimeters))}"))];
    }
}
