using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using AimOdometer.App.Controls;
using AimOdometer.App.Localization;
using AimOdometer.App.Services;
using AimOdometer.Core.Games;
using AimOdometer.Core.Stats;
using AimOdometer.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AimOdometer.App.ViewModels;

/// <summary>A game in a short "top games" list.</summary>
public sealed record TopGameItem(string Name, string Distance, double Share, ImageSource? Icon, Brush Brush);

/// <summary>Today at a glance, live, plus this week, this month and all time.</summary>
public sealed partial class OverviewViewModel(AppData data) : PageViewModel(data)
{
    private const int ChartDays = 14;
    private PeriodTotals? _periods;

    public override string TitleKey => "Nav.Overview";

    public override string Icon => "";

    [ObservableProperty]
    public partial string TodayNumber { get; set; } = "0";

    [ObservableProperty]
    public partial string TodayUnit { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Week { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Month { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AllTime { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TodayClicks { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TodayActive { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TodayPeak { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StreakText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TodayComparison { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AllTimeComparison { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MoonText { get; set; } = string.Empty;

    /// <summary>Data for the share card.</summary>
    public PeriodTotals? Periods => _periods;

    [ObservableProperty]
    public partial IReadOnlyList<ChartBar> Bars { get; set; } = [];

    [ObservableProperty]
    public partial bool HasGamesToday { get; set; }

    public ObservableCollection<TopGameItem> TopGames { get; } = [];

    public override void Refresh()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var firstDay = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var days = Data.Store.GetDailyTotals();
        _periods = StatsSummary.Periods(days, today, firstDay);
        ShowTotals(0);

        var todayTotal = _periods.Today;
        TodayClicks = Format.Number(todayTotal.Clicks);
        TodayActive = Format.Duration(todayTotal.MoveSeconds);
        TodayPeak = Format.Speed(todayTotal.PeakSpeedCmPerSecond);

        var records = StatsSummary.Records(days, today, firstDay);
        StreakText = records.CurrentStreak is { } streak
            ? Loc.Instance.Format("Overview.StreakDays", streak.Days)
            : Loc.Instance["Overview.NoStreak"];

        Bars = [.. StatsSummary.Series(days, today.AddDays(-(ChartDays - 1)), today).Select(d => new ChartBar(
            d.Centimeters,
            d.Date == today ? Loc.Instance["Common.Today"] : Format.ShortDate(d.Date),
            $"{Format.ShortDate(d.Date)}: {Format.Distance(d.Centimeters)}",
            d.Date == today))];

        LoadTopGames(today);
    }

    public override void OnLiveUpdate(TrackerConnection tracker)
    {
        if (_periods is null || tracker.Status is not { } status)
        {
            return;
        }

        // The tracker's "today" includes movement not yet written to the database (up to a minute).
        ShowTotals(Math.Max(0, status.TodayCentimeters - _periods.Today.Centimeters));
    }

    private void ShowTotals(double unsavedCm)
    {
        if (_periods is null)
        {
            return;
        }

        (TodayNumber, TodayUnit) = Format.DistanceParts(_periods.Today.Centimeters + unsavedCm);
        Week = Format.Distance(_periods.Week.Centimeters + unsavedCm);
        Month = Format.Distance(_periods.Month.Centimeters + unsavedCm);
        AllTime = Format.Distance(_periods.AllTime.Centimeters + unsavedCm);
        TodayComparison = ComparisonText(_periods.Today.Centimeters + unsavedCm);
        AllTimeComparison = ComparisonText(_periods.AllTime.Centimeters + unsavedCm);
        MoonText = Loc.Instance.Format("Overview.Moon",
            Format.PercentPrecise(Core.Fun.Comparisons.MoonProgress(_periods.AllTime.Centimeters + unsavedCm)));
    }

    /// <summary>"That's 1.3 Burj Khalifas" style text, empty for no distance.</summary>
    public static string ComparisonText(double centimeters) => Core.Fun.Comparisons.Best(centimeters) is { } c
        ? Loc.Instance.Format($"Cmp.{c.Target.Id}", c.Ratio.ToString("0.0", Loc.Instance.Culture))
        : string.Empty;

    private void LoadTopGames(DateOnly today)
    {
        TopGames.Clear();
        var totals = GameStats.Summarize(Data.Catalog, Data.Store.GetApps(), Data.Store.GetAppUsage(today, today), Loc.Instance["Games.Other"]);
        var sum = totals.Sum(t => t.Centimeters);
        // Several paths can share an exe name (e.g. two installs); any of them gives the icon.
        var apps = Data.Store.GetApps()
            .GroupBy(a => a.ExeName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().ExePath, StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var total in totals.Where(t => t.Category == AppCategory.Game).Take(3))
        {
            var icon = total.Executables.Select(e => IconCache.Get(apps.GetValueOrDefault(e))).FirstOrDefault(i => i is not null);
            TopGames.Add(new TopGameItem(total.Name, Format.Distance(total.Centimeters), sum > 0 ? total.Centimeters / sum : 0, icon, SeriesBrushes.For(index++)));
        }

        HasGamesToday = TopGames.Count > 0;
    }
}
