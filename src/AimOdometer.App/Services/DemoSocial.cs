using System.IO;
using System.Text.Json;
using AimOdometer.Cloud;
using AimOdometer.Core;

namespace AimOdometer.App.Services;

/// <summary>Canned friends and world leaderboards for promo videos and screenshots.</summary>
public sealed record DemoBoards(IReadOnlyList<BoardRow> Friends, IReadOnlyList<BoardRow> World, WorldPlace? Me);

/// <summary>
/// Leaderboards without the cloud, for recording promo videos: active only in a test window (a data folder set with
/// AIMODOMETER_DATA_DIR) and only when AIMODOMETER_DEMO_SOCIAL names a JSON file. Real users never see it.
/// The file: {"friends": [row...], "world": [row...], "me": {"rank", "centimeters", "players"}}, a row being
/// {"name", "avatar" (https or a local file), "centimeters", "isMe", "dpi", "peakSpeed", "peakDpi", "badges", "titles", "streak"}.
/// </summary>
public static class DemoSocial
{
    public const string Variable = "AIMODOMETER_DEMO_SOCIAL";

    /// <summary>Enlarges the whole UI (e.g. 1.33) so a full-screen promo capture stays sharp; test window only.</summary>
    public const string ScaleVariable = "AIMODOMETER_UI_SCALE";

    private static readonly Lazy<DemoBoards?> Loaded = new(Load);

    public static DemoBoards? Boards => Loaded.Value;

    /// <summary>The UI scale from <see cref="ScaleVariable"/> (1 to 3), or null outside a test window.</summary>
    public static double? UiScale =>
        AppIdentity.IsDataDirectoryOverridden
        && double.TryParse(Environment.GetEnvironmentVariable(ScaleVariable), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale)
        && scale is > 1 and <= 3
            ? scale
            : null;

    /// <summary>The same boards for every period, scaled so switching the period visibly changes the numbers.</summary>
    public static double PeriodFactor(string period) => period switch { "week" => 1, "month" => 3.9, _ => 14.2 };

    private static DemoBoards? Load()
    {
        if (!AppIdentity.IsDataDirectoryOverridden || Environment.GetEnvironmentVariable(Variable) is not { Length: > 0 } path || !File.Exists(path))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        WorldPlace? me = root.TryGetProperty("me", out var m) && m.ValueKind == JsonValueKind.Object
            ? new WorldPlace(m.GetProperty("rank").GetInt32(), m.GetProperty("centimeters").GetDouble(), m.GetProperty("players").GetInt32())
            : null;
        return new DemoBoards(Rows(root, "friends"), Rows(root, "world"), me);
    }

    private static List<BoardRow> Rows(JsonElement root, string name) =>
        !root.TryGetProperty(name, out var list) ? [] : [.. list.EnumerateArray().Select((r, i) => new BoardRow(
            r.TryGetProperty("rank", out var rank) ? rank.GetInt32() : i + 1,
            r.GetProperty("name").GetString() ?? string.Empty,
            r.TryGetProperty("avatar", out var avatar) ? avatar.GetString() ?? string.Empty : string.Empty,
            r.GetProperty("centimeters").GetDouble(),
            r.TryGetProperty("isMe", out var isMe) && isMe.GetBoolean(),
            r.TryGetProperty("dpi", out var dpi) ? dpi.GetDouble() : null,
            r.TryGetProperty("peakSpeed", out var peak) ? peak.GetDouble() : 0,
            r.TryGetProperty("peakDpi", out var peakDpi) ? peakDpi.GetDouble() : null,
            Strings(r, "badges"),
            Strings(r, "titles"),
            r.TryGetProperty("streak", out var streak) ? streak.GetInt32() : 0))];

    private static List<string> Strings(JsonElement row, string name) =>
        row.TryGetProperty(name, out var list) ? [.. list.EnumerateArray().Select(x => x.GetString() ?? string.Empty)] : [];
}
