using System.Collections.ObjectModel;
using System.Windows.Media;
using AimOdometer.App.Controls;
using AimOdometer.App.Localization;
using AimOdometer.App.Services;
using AimOdometer.Core.Games;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AimOdometer.App.ViewModels;

public enum GamesRange
{
    Today,
    Week,
    Month,
    All,
}

/// <summary>A game (or the "Desktop and apps" group) in the games table.</summary>
public sealed partial class GameRowViewModel(GamesViewModel owner, GameTotal total, double share, ImageSource? icon, Brush brush)
    : ObservableObject
{
    public GameTotal Total { get; } = total;

    public string Key => Total.Key;

    public bool IsGame => Total.Category == AppCategory.Game;

    public ImageSource? Icon { get; } = icon;

    public Brush Brush { get; } = brush;

    public double Share { get; } = share;

    public string ShareText => Format.Percent(Share);

    public string Distance => Format.Distance(Total.Centimeters);

    public string Time => Format.Duration(Total.ForegroundSeconds);

    public string PerHour => Format.PerHour(Total.KilometersPerHour);

    public string Clicks => Format.Number(Total.Clicks);

    public string Executables => string.Join(", ", Total.Executables);

    [ObservableProperty]
    public partial string Name { get; set; } = total.Name;

    [ObservableProperty]
    public partial bool IsRenaming { get; set; }

    /// <summary>Other games this one can be merged into (for the context menu).</summary>
    public IReadOnlyList<GameRowViewModel> MergeTargets => owner.Games.Where(g => g != this && g.IsGame).ToList();

    [RelayCommand]
    private void StartRename() => IsRenaming = true;

    [RelayCommand]
    private void CommitRename()
    {
        IsRenaming = false;
        owner.Rename(this, Name);
    }

    [RelayCommand]
    private void NotAGame() => owner.ApplyRule(this, new AppRule(AppCategory.Other, null));

    [RelayCommand]
    private void Hide() => owner.ApplyRule(this, new AppRule(AppCategory.Excluded, null));

    [RelayCommand]
    private void ResetDetection() => owner.ApplyRule(this, null);

    [RelayCommand]
    private void MergeInto(GameRowViewModel target) => owner.ApplyRule(this, new AppRule(AppCategory.Game, target.Key));
}

/// <summary>A non-game app (in "Desktop and apps") or a hidden app.</summary>
public sealed partial class AppRowViewModel(GamesViewModel owner, long appId, string exePath, string distance, string time)
{
    public long AppId { get; } = appId;

    public string ExeName { get; } = System.IO.Path.GetFileName(exePath);

    public string ExePath { get; } = exePath;

    public string Distance { get; } = distance;

    public string Time { get; } = time;

    public ImageSource? Icon => IconCache.Get(ExePath);

    [RelayCommand]
    private void MarkAsGame() => owner.SetAppRule(AppId, new AppRule(AppCategory.Game, null));

    [RelayCommand]
    private void Hide() => owner.SetAppRule(AppId, new AppRule(AppCategory.Excluded, null));

    [RelayCommand]
    private void Restore() => owner.SetAppRule(AppId, null);
}

/// <summary>Distance, time and "km/h" per game, with the user's rules (rename, not a game, hide, merge).</summary>
public sealed partial class GamesViewModel(AppData data) : PageViewModel(data)
{
    private const int MaxOtherApps = 30;

    public override string TitleKey => "Nav.Games";

    public override string Icon => "";

    [ObservableProperty]
    public partial GamesRange Range { get; set; } = GamesRange.Week;

    [ObservableProperty]
    public partial IReadOnlyList<DonutSlice> Slices { get; set; } = [];

    [ObservableProperty]
    public partial string GamesShareText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    public ObservableCollection<GameRowViewModel> Games { get; } = [];

    public ObservableCollection<AppRowViewModel> OtherApps { get; } = [];

    public ObservableCollection<AppRowViewModel> HiddenApps { get; } = [];

    partial void OnRangeChanged(GamesRange value) => Refresh();

    public override void Refresh()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        DateOnly? from = Range switch
        {
            GamesRange.Today => today,
            GamesRange.Week => Core.Stats.StatsSummary.StartOfWeek(today, System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek),
            GamesRange.Month => new DateOnly(today.Year, today.Month, 1),
            _ => null,
        };

        var apps = Data.Store.GetApps();
        var appsById = apps.ToDictionary(a => a.Id);
        var usage = Data.Store.GetAppUsage(from, today);
        // Games first (by distance), "Desktop and apps" last.
        var totals = GameStats.Summarize(Data.Catalog, apps, usage, Loc.Instance["Games.Other"])
            .OrderBy(t => t.Category == AppCategory.Game ? 0 : 1)
            .ThenByDescending(t => t.Centimeters)
            .ToList();
        var sum = totals.Sum(t => t.Centimeters);

        Games.Clear();
        var gameIndex = 0;
        foreach (var total in totals)
        {
            var isGame = total.Category == AppCategory.Game;
            var icon = isGame
                ? (total.AppIds ?? []).Select(id => IconCache.Get(appsById.GetValueOrDefault(id)?.ExePath)).FirstOrDefault(i => i is not null)
                : null;
            Games.Add(new GameRowViewModel(this, total, sum > 0 ? total.Centimeters / sum : 0, icon,
                isGame ? SeriesBrushes.For(gameIndex++) : SeriesBrushes.Other));
        }

        Slices = [.. Games.Select(g => new DonutSlice(g.Total.Centimeters, g.Brush))];
        var gamesShare = sum > 0 ? Games.Where(g => g.IsGame).Sum(g => g.Total.Centimeters) / sum : 0;
        GamesShareText = Format.Percent(gamesShare);
        IsEmpty = sum <= 0 && Games.All(g => g.Total.ForegroundSeconds <= 0);

        OtherApps.Clear();
        var other = totals.FirstOrDefault(t => t.Key == GameTotal.OtherKey)?.AppIds ?? [];
        foreach (var row in usage.Where(u => other.Contains(u.AppId) && appsById.ContainsKey(u.AppId))
                     .OrderByDescending(u => u.ForegroundSeconds + u.Centimeters).Take(MaxOtherApps))
        {
            OtherApps.Add(new AppRowViewModel(this, row.AppId, appsById[row.AppId].ExePath, Format.Distance(row.Centimeters), Format.Duration(row.ForegroundSeconds)));
        }

        HiddenApps.Clear();
        foreach (var (appId, rule) in Data.Store.GetAppRules().Where(r => r.Value.Category == AppCategory.Excluded))
        {
            if (appsById.TryGetValue(appId, out var app))
            {
                HiddenApps.Add(new AppRowViewModel(this, appId, app.ExePath, string.Empty, string.Empty));
            }
        }
    }

    internal void Rename(GameRowViewModel row, string name)
    {
        Data.Store.SetGameName(row.Key, name); // empty restores the detected name
        Data.NotifyChanged(reloadTracker: false);
        Refresh();
    }

    internal void ApplyRule(GameRowViewModel row, AppRule? rule)
    {
        foreach (var appId in row.Total.AppIds ?? [])
        {
            if (appId != 0)
            {
                Data.Store.SetAppRule(appId, rule);
            }
        }

        Data.NotifyChanged(reloadTracker: false);
        Refresh();
    }

    internal void SetAppRule(long appId, AppRule? rule)
    {
        Data.Store.SetAppRule(appId, rule);
        Data.NotifyChanged(reloadTracker: false);
        Refresh();
    }
}
