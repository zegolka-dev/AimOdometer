using System.Collections.ObjectModel;
using AimOdometer.App.Localization;
using AimOdometer.App.Services;
using AimOdometer.Core.Fun;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AimOdometer.App.ViewModels;

/// <summary>One achievement as shown in the list.</summary>
public sealed record AchievementItem(
    string Id,
    string Glyph,
    string Name,
    string Description,
    bool IsUnlocked,
    double Fraction,
    string ProgressText,
    string? UnlockedText,
    bool IsShame = false);

/// <summary>All achievements with progress; unlocked first (newest on top), then the closest to unlocking.</summary>
public sealed partial class AchievementsViewModel(AppData data) : PageViewModel(data)
{
    public override string TitleKey => "Nav.Achievements";

    public override string Icon => "";

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double UnlockedFraction { get; set; }

    public ObservableCollection<AchievementItem> Items { get; } = [];

    /// <summary>Latest unlocked achievement, for the share card.</summary>
    public AchievementItem? Latest { get; private set; }

    public override void Refresh()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        // Unlock what is already reached (the tracker does this every 5 minutes; the window should never lag behind).
        var newly = AchievementService.UnlockNew(Data.Store, Data.Catalog, today, DateTime.UtcNow);
        Data.Store.MarkAchievementsNotified(newly.Select(a => a.Id)); // the user sees them right here
        var stored = Data.Store.GetAchievements();
        var snapshot = AchievementService.BuildSnapshot(Data.Store, Data.Catalog, today);
        var progress = AchievementEngine.Evaluate(
            AchievementEngine.All, snapshot, stored.ToDictionary(s => s.Key, s => s.Value.UnlockedAtUtc));

        Items.Clear();
        // Shame achievements stay hidden until earned (they are not goals), then lead the list; they do not count toward the total.
        foreach (var p in progress
                     .Where(p => !p.Definition.Shame || p.IsUnlocked)
                     .OrderByDescending(p => p.Definition.Shame) // earned shame on top: hard to miss
                     .ThenByDescending(p => p.IsUnlocked)
                     .ThenByDescending(p => p.UnlockedAtUtc)
                     .ThenByDescending(p => p.Fraction))
        {
            Items.Add(ToItem(p));
        }

        var goals = Items.Where(i => !i.IsShame).ToList();
        var unlocked = goals.Count(i => i.IsUnlocked);
        Summary = Loc.Instance.Format("Ach.Summary", unlocked, goals.Count);
        UnlockedFraction = goals.Count == 0 ? 0 : (double)unlocked / goals.Count;
        Latest = goals.FirstOrDefault(i => i.IsUnlocked);
    }

    public static AchievementItem ToItem(AchievementProgress p)
    {
        var L = Loc.Instance;
        var d = p.Definition;
        var target = FormatValue(d.Type, d.Target);
        var current = FormatValue(d.Type, Math.Min(p.Current, d.Target));
        return new AchievementItem(
            d.Id,
            d.Glyph,
            L[$"Ach.{d.Id}.Name"],
            L[$"Ach.{d.Id}.Desc"],
            p.IsUnlocked,
            p.IsUnlocked ? 1 : p.Fraction,
            p.IsUnlocked ? target : $"{current} / {target}",
            p.UnlockedAtUtc is { } at ? L.Format("Ach.UnlockedOn", Format.Date(DateOnly.FromDateTime(at.ToLocalTime()))) : null,
            d.Shame);
    }

    /// <summary>Target and progress in the achievement's own unit.</summary>
    public static string FormatValue(string type, double value) => type switch
    {
        "totalDistance" or "dayDistance" or "gameDistance" or "nightDistance" or "nightOneNight" or "deviceDistance"
            or "lowDpiDay" or "lowDpiDistance" =>
            Format.Distance(value * 100),
        "peakSpeed" => Format.Speed(value * 100),
        "streak" or "activeDays" => Loc.Instance.Format("Stats.DaysCount", Format.Number(value)),
        _ => Format.Number(value),
    };
}
