namespace AimOdometer.Core.Fun;

/// <summary>A look of the streak flame: from <see cref="MinDays"/> days in a row on.</summary>
public sealed record StreakTier(int Index, int MinDays, string Id);

/// <summary>
/// The streak flame grows with the streak: a new look at 20, 50, 100, 200, 300, 400, 500 and 1000 days in a row.
/// The same tiers drive the big flame on the overview and the small one next to names on leaderboards.
/// </summary>
public static class StreakTiers
{
    public static IReadOnlyList<StreakTier> All { get; } =
    [
        new(0, 1, "spark"),
        new(1, 20, "flame"),
        new(2, 50, "blaze"),
        new(3, 100, "neon"),
        new(4, 200, "blue"),
        new(5, 300, "plasma"),
        new(6, 400, "gold"),
        new(7, 500, "aurora"),
        new(8, 1000, "legend"),
    ];

    /// <summary>The tier of a streak, or null when there is no streak.</summary>
    public static StreakTier? For(int days) => days < 1 ? null : All.Last(t => days >= t.MinDays);

    /// <summary>The next tier to reach, or null at the top.</summary>
    public static StreakTier? Next(int days) => All.FirstOrDefault(t => t.MinDays > days);
}
