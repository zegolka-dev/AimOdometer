using AimOdometer.Core.Storage;

namespace AimOdometer.Core.Games;

/// <summary>Totals of one game (or of "Desktop &amp; apps") over a period.</summary>
public sealed record GameTotal(
    string Key,
    string Name,
    AppCategory Category,
    double Centimeters,
    double ForegroundSeconds,
    long Clicks,
    double PeakSpeedCmPerSecond,
    IReadOnlyList<string> Executables)
{
    /// <summary>Key of the "Desktop &amp; apps" group.</summary>
    public const string OtherKey = "other";

    /// <summary>Kilometers per hour of foreground time: how "running-heavy" the game is. Null with too little time.</summary>
    public double? KilometersPerHour => ForegroundSeconds >= 60 ? Centimeters / 100_000 / (ForegroundSeconds / 3600) : null;
}

/// <summary>Groups per-app usage into games.</summary>
public static class GameStats
{
    /// <summary>
    /// Sums app usage per game. Apps that are not games are grouped under <see cref="GameTotal.OtherKey"/>
    /// (named <paramref name="otherName"/>); excluded apps are left out. Sorted by distance, largest first.
    /// </summary>
    public static IReadOnlyList<GameTotal> Summarize(
        GameCatalog catalog, IEnumerable<AppRecord> apps, IEnumerable<AppUsage> usage, string otherName)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(usage);
        var appsById = apps.ToDictionary(a => a.Id);
        var groups = new Dictionary<string, Group>(StringComparer.Ordinal);

        foreach (var row in usage)
        {
            // App id 0 = no known foreground app (desktop, lock screen, data recorded before games were tracked).
            var app = appsById.GetValueOrDefault(row.AppId);
            var classification = app is not null ? catalog.Classify(app.Id, app.ExePath) : AppClassification.Other;
            if (classification.Category == AppCategory.Excluded)
            {
                continue;
            }

            var (key, name) = classification.Game is { } game ? (game.Key, game.Name) : (GameTotal.OtherKey, otherName);
            if (!groups.TryGetValue(key, out var group))
            {
                group = new Group(name, classification.Category);
                groups[key] = group;
            }

            group.Add(row, app?.ExeName);
        }

        return [.. groups
            .Select(g => g.Value.ToTotal(g.Key))
            .OrderByDescending(t => t.Centimeters)];
    }

    private sealed class Group(string name, AppCategory category)
    {
        private readonly SortedSet<string> _executables = new(StringComparer.OrdinalIgnoreCase);
        private double _centimeters;
        private double _foregroundSeconds;
        private long _clicks;
        private double _peak;

        public void Add(AppUsage row, string? exeName)
        {
            _centimeters += row.Centimeters;
            _foregroundSeconds += row.ForegroundSeconds;
            _clicks += row.Clicks;
            _peak = Math.Max(_peak, row.PeakSpeedCmPerSecond);
            if (exeName is not null)
            {
                _executables.Add(exeName);
            }
        }

        public GameTotal ToTotal(string key) =>
            new(key, name, category, _centimeters, _foregroundSeconds, _clicks, _peak, [.. _executables]);
    }
}
