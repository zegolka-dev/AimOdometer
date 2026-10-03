using System.Text.Json;
using System.Text.Json.Serialization;

namespace AimOdometer.Core.Fun;

/// <summary>One achievement from data/achievements.json. Names are localized (Ach.&lt;id&gt;.Name / .Desc).</summary>
public sealed record AchievementDefinition(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("target")] double Target,
    [property: JsonPropertyName("icon")] string Icon,
    [property: JsonPropertyName("game")] string? Game)
{
    /// <summary>The icon as a Segoe Fluent Icons character.</summary>
    public string Glyph => char.ConvertFromUtf32(Convert.ToInt32(Icon, 16));
}

public sealed record AchievementList(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("achievements")] AchievementDefinition[] Achievements);

[JsonSourceGenerationOptions(ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(AchievementList))]
internal sealed partial class AchievementsJsonContext : JsonSerializerContext;

/// <summary>Everything the achievement conditions look at, computed from the database in one go.</summary>
public sealed record AchievementSnapshot(
    double TotalMeters,
    double BestDayMeters,
    int LongestStreakDays,
    double PeakSpeedMetersPerSecond,
    IReadOnlyDictionary<string, double> GameMeters,
    double NightMetersTotal,
    double BestNightMeters,
    long TotalClicks,
    double TotalWheelNotches,
    double MaxDeviceMeters,
    int ActiveDays)
{
    /// <summary>A game counts toward "different games" after this much movement in it.</summary>
    public const double DistinctGameMinimumMeters = 100;

    public static readonly AchievementSnapshot Empty = new(0, 0, 0, 0, new Dictionary<string, double>(), 0, 0, 0, 0, 0, 0);
}

/// <summary>Where an achievement stands now.</summary>
public sealed record AchievementProgress(AchievementDefinition Definition, double Current, DateTime? UnlockedAtUtc)
{
    public bool IsUnlocked => UnlockedAtUtc is not null || Current >= Definition.Target;

    public double Fraction => Definition.Target <= 0 ? 1 : Math.Clamp(Current / Definition.Target, 0, 1);
}

/// <summary>Evaluates achievement conditions against a snapshot. Unknown condition types are never unlocked.</summary>
public static class AchievementEngine
{
    private static readonly Lazy<IReadOnlyList<AchievementDefinition>> Embedded = new(() =>
    {
        using var stream = typeof(AchievementEngine).Assembly.GetManifestResourceStream("AimOdometer.Data.achievements.json")
            ?? throw new InvalidOperationException("Embedded achievements.json is missing.");
        var list = JsonSerializer.Deserialize(stream, AchievementsJsonContext.Default.AchievementList)
            ?? throw new InvalidDataException("achievements.json is empty.");
        return list.Achievements;
    });

    public static IReadOnlyList<AchievementDefinition> All => Embedded.Value;

    public static double CurrentValue(AchievementDefinition definition, AchievementSnapshot s)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(s);
        return definition.Type switch
        {
            "totalDistance" => s.TotalMeters,
            "dayDistance" => s.BestDayMeters,
            "streak" => s.LongestStreakDays,
            "peakSpeed" => s.PeakSpeedMetersPerSecond,
            "gameDistance" when definition.Game is { } game => s.GameMeters.GetValueOrDefault(game),
            "gameDistance" => s.GameMeters.Count == 0 ? 0 : s.GameMeters.Values.Max(),
            "distinctGames" => s.GameMeters.Values.Count(m => m >= AchievementSnapshot.DistinctGameMinimumMeters),
            "nightDistance" => s.NightMetersTotal,
            "nightOneNight" => s.BestNightMeters,
            "totalClicks" => s.TotalClicks,
            "totalWheel" => s.TotalWheelNotches,
            "deviceDistance" => s.MaxDeviceMeters,
            "activeDays" => s.ActiveDays,
            _ => 0,
        };
    }

    /// <summary>Progress of every achievement; <paramref name="unlocked"/> keeps earlier unlocks (achievements never relock).</summary>
    public static IReadOnlyList<AchievementProgress> Evaluate(
        IEnumerable<AchievementDefinition> definitions, AchievementSnapshot snapshot, IReadOnlyDictionary<string, DateTime> unlocked)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(unlocked);
        return [.. definitions.Select(d => new AchievementProgress(d, CurrentValue(d, snapshot), unlocked.TryGetValue(d.Id, out var at) ? at : null))];
    }
}
