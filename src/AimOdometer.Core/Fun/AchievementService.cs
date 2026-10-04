using AimOdometer.Core.Games;
using AimOdometer.Core.Input;
using AimOdometer.Core.Stats;
using AimOdometer.Core.Storage;

namespace AimOdometer.Core.Fun;

/// <summary>Builds the achievement snapshot from the database and records new unlocks.</summary>
public static class AchievementService
{
    /// <summary>Local hours that count as "night" (03:00 to 04:59).</summary>
    public const int NightFromHour = 3;
    public const int NightToHour = 4;

    public static AchievementSnapshot BuildSnapshot(StatsStore store, GameCatalog catalog, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(catalog);
        var days = store.GetDailyTotals();
        if (days.Count == 0)
        {
            return AchievementSnapshot.Empty;
        }

        var all = StatsSummary.Sum(days, DateOnly.MinValue, DateOnly.MaxValue);
        var records = StatsSummary.Records(days, today, DayOfWeek.Monday);
        var games = GameStats.Summarize(catalog, store.GetApps(), store.GetAppUsage(), string.Empty)
            .Where(g => g.Category == AppCategory.Game)
            .ToDictionary(g => g.Key, g => g.Centimeters / 100, StringComparer.Ordinal);
        var nights = store.GetHourRangeTotals(NightFromHour, NightToHour);
        var devices = store.GetDeviceTotals().Where(d => d.Device.Kind != DeviceKind.Software).ToList();
        var lowDpi = store.GetLowDpiTotals(FairPlay.ClownMaxDpi, FairPlay.BlockheadMaxDpi);

        return new AchievementSnapshot(
            TotalMeters: all.Centimeters / 100,
            BestDayMeters: (records.BestDay?.Centimeters ?? 0) / 100,
            LongestStreakDays: records.LongestStreak?.Days ?? 0,
            PeakSpeedMetersPerSecond: all.PeakSpeedCmPerSecond / 100,
            GameMeters: games,
            NightMetersTotal: nights.Sum(n => n.Centimeters) / 100,
            BestNightMeters: nights.Count == 0 ? 0 : nights.Max(n => n.Centimeters) / 100,
            TotalClicks: all.Clicks,
            TotalWheelNotches: all.WheelNotches,
            MaxDeviceMeters: devices.Count == 0 ? 0 : devices.Max(d => d.Centimeters) / 100,
            ActiveDays: days.Count(d => d.Centimeters > 0),
            LowDpiBestDayMeters: lowDpi.BestDayCentimeters / 100,
            VeryLowDpiMeters: lowDpi.VeryLowCentimeters / 100);
    }

    /// <summary>
    /// Records achievements that are reached but not yet stored. Returns the newly unlocked ones (not yet notified).
    /// </summary>
    public static IReadOnlyList<AchievementDefinition> UnlockNew(
        StatsStore store, GameCatalog catalog, DateOnly today, DateTime utcNow, IEnumerable<AchievementDefinition>? definitions = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        var stored = store.GetAchievements();
        var snapshot = BuildSnapshot(store, catalog, today);
        var newly = (definitions ?? AchievementEngine.All)
            .Where(d => !stored.ContainsKey(d.Id) && AchievementEngine.CurrentValue(d, snapshot) >= d.Target)
            .ToList();
        if (newly.Count > 0)
        {
            store.UnlockAchievements(newly.Select(d => d.Id), utcNow, notified: false);
        }

        return newly;
    }
}
