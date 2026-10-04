using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AimOdometer.Core.Games;
using AimOdometer.Core.Storage;

namespace AimOdometer.Cloud;

/// <summary>One uploaded row: a local date x game x mouse of this PC.</summary>
public sealed record CloudRow(DateOnly Day, string GameKey, string MouseKey, double Centimeters, long Clicks, long MoveSeconds, double PeakSpeed);

/// <summary>Result of a sync run.</summary>
public sealed record SyncOutcome(int Uploaded, int Rejected, DateTimeOffset At);

/// <summary>
/// Uploads this PC's daily totals. Only the window syncs (on open, every 15 minutes while open, on close); the tracker
/// never touches the network. The first sync for an account uploads the whole history; later syncs re-upload the last
/// days (today keeps growing). Changing game rules, devices or DPI resets that to a full upload.
/// </summary>
public static class CloudSync
{
    /// <summary>Days re-uploaded behind the last synced day (late data of yesterday, time zone edges).</summary>
    public const int OverlapDays = 2;

    /// <summary>Rows per request; the server takes at most 5000. A day is never split across requests.</summary>
    public const int MaxRowsPerRequest = 4000;

    /// <summary>
    /// Uploads what changed since the last sync for this account. Awaits keep the caller's context on purpose: the
    /// database connection belongs to the window's thread.
    /// </summary>
    public static async Task<SyncOutcome> RunAsync(CloudClient client, StatsStore store, GameCatalog catalog, TimeProvider time, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(time);
        var session = client.Session ?? throw new CloudException(CloudError.SessionExpired, "Not signed in.");
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var from = FirstDayToUpload(store, session.UserId);
        var rows = BuildRows(store, catalog, from);
        var pcId = PcId(store);

        var uploaded = 0;
        var rejected = 0;
        foreach (var batch in Batches(rows, MaxRowsPerRequest))
        {
            var response = await client.SyncAsync(Payload(pcId, Environment.MachineName, batch), cancellation);
            uploaded += response.Accepted;
            rejected += response.Rejected;
        }

        if (rows.Count == 0)
        {
            // Registers the PC even before it has statistics, so it shows up in the account's PC list.
            await client.SyncAsync(Payload(pcId, Environment.MachineName, []), cancellation);
        }

        var now = time.GetUtcNow();
        store.SetSetting(SettingKeys.CloudSyncedUser, session.UserId);
        store.SetSetting(SettingKeys.CloudSyncedThrough, today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        store.SetSetting(SettingKeys.CloudLastSync, now.ToString("O", CultureInfo.InvariantCulture));
        return new SyncOutcome(uploaded, rejected, now);
    }

    /// <summary>Forget what was uploaded: the next sync sends the whole history again (after rule, device or DPI changes).</summary>
    public static void RequestFullUpload(StatsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        store.SetSetting(SettingKeys.CloudSyncedThrough, string.Empty);
    }

    /// <summary>When the last sync finished, if ever.</summary>
    public static DateTimeOffset? LastSync(StatsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return DateTimeOffset.TryParse(store.GetSetting(SettingKeys.CloudLastSync), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? at
            : null;
    }

    /// <summary>Null = everything (first sync for this account, or a full upload was requested).</summary>
    internal static DateOnly? FirstDayToUpload(StatsStore store, string userId)
    {
        if (store.GetSetting(SettingKeys.CloudSyncedUser) != userId)
        {
            return null;
        }

        return DateOnly.TryParseExact(store.GetSetting(SettingKeys.CloudSyncedThrough), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var through)
            ? through.AddDays(-OverlapDays)
            : null;
    }

    /// <summary>This PC's random id in the account (no hardware ids leave the PC).</summary>
    internal static Guid PcId(StatsStore store)
    {
        if (Guid.TryParse(store.GetSetting(SettingKeys.CloudPcId), out var id))
        {
            return id;
        }

        id = Guid.NewGuid();
        store.SetSetting(SettingKeys.CloudPcId, id.ToString());
        return id;
    }

    /// <summary>Local hours → rows per day x game x mouse. Not-a-game apps are summed into game key "".</summary>
    internal static List<CloudRow> BuildRows(StatsStore store, GameCatalog catalog, DateOnly? from)
    {
        var apps = store.GetApps().ToDictionary(a => a.Id, a => a.ExePath);
        var mice = store.GetDevices().ToDictionary(d => d.Id, d => MouseKey(d.DeviceKey));
        var gameKeys = new Dictionary<long, string>();
        string GameKeyOf(long appId)
        {
            if (!gameKeys.TryGetValue(appId, out var key))
            {
                var classification = apps.TryGetValue(appId, out var exe) ? catalog.Classify(appId, exe) : AppClassification.Other;
                key = classification.Category == AppCategory.Game && classification.Game is { } game ? game.Key : string.Empty;
                gameKeys[appId] = key;
            }

            return key;
        }

        return Aggregate(store.GetDailyBreakdown(from), GameKeyOf, deviceId => mice.TryGetValue(deviceId, out var key) ? key : string.Empty);
    }

    internal static List<CloudRow> Aggregate(IEnumerable<DailyBreakdown> breakdown, Func<long, string> gameKeyOf, Func<long, string> mouseKeyOf) =>
        [.. breakdown
            .GroupBy(b => (b.Date, Game: Limit(gameKeyOf(b.AppId)), Mouse: mouseKeyOf(b.DeviceId)))
            .Select(g => new CloudRow(
                g.Key.Date, g.Key.Game, g.Key.Mouse,
                Math.Round(g.Sum(b => b.Centimeters), 2),
                g.Sum(b => b.Clicks),
                g.Sum(b => b.MoveSeconds),
                Math.Round(g.Max(b => b.PeakSpeed), 2)))
            .Where(r => r.Centimeters > 0 || r.Clicks > 0)
            .OrderBy(r => r.Day)];

    /// <summary>A short, irreversible id for a mouse: the first 16 hex digits of SHA-256 of its local device key.</summary>
    internal static string MouseKey(string deviceKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(deviceKey)))[..16];

    /// <summary>Groups rows into requests without splitting a day (the server replaces whole days).</summary>
    internal static IEnumerable<List<CloudRow>> Batches(IReadOnlyList<CloudRow> rows, int maxRows)
    {
        var batch = new List<CloudRow>();
        foreach (var day in rows.GroupBy(r => r.Day))
        {
            var dayRows = day.ToList();
            if (batch.Count > 0 && batch.Count + dayRows.Count > maxRows)
            {
                yield return batch;
                batch = [];
            }

            batch.AddRange(dayRows);
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }

    internal static string Payload(Guid pcId, string pcName, IReadOnlyList<CloudRow> rows)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("pcId", pcId.ToString());
            w.WriteString("pcName", pcName.Length <= 64 ? pcName : pcName[..64]);
            w.WriteStartArray("rows");
            foreach (var r in rows)
            {
                w.WriteStartObject();
                w.WriteString("day", r.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                w.WriteString("gameKey", r.GameKey);
                w.WriteString("mouseKey", r.MouseKey);
                w.WriteNumber("centimeters", r.Centimeters);
                w.WriteNumber("clicks", r.Clicks);
                w.WriteNumber("moveSeconds", r.MoveSeconds);
                w.WriteNumber("peakSpeed", r.PeakSpeed);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    // The server accepts letters, digits and ":._ -" up to 64 characters (exe names can contain anything).
    public static string Limit(string gameKey)
    {
        var safe = string.Concat(gameKey.Select(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '.' or '_' or ' ' or '-' ? c : '_'));
        return safe.Length <= 64 ? safe : safe[..64];
    }
}
