using System.Globalization;
using AimOdometer.Core.Games;
using AimOdometer.Core.Input;

namespace AimOdometer.Core.Storage;

/// <summary>A pointing device as stored in the database.</summary>
public sealed record DeviceRecord(
    long Id,
    string DeviceKey,
    string DevicePath,
    int? VendorId,
    int? ProductId,
    string? ProductName,
    string? DisplayName,
    DeviceKind Kind,
    double Dpi,
    bool Excluded)
{
    /// <summary>Name to show: user's name, else product name, else VID:PID.</summary>
    public string Name => DisplayName
        ?? ProductName
        ?? Kind switch
        {
            DeviceKind.Software => "Software input",
            _ when VendorId is { } vid && ProductId is { } pid => string.Create(CultureInfo.InvariantCulture, $"Mouse {vid:X4}:{pid:X4}"),
            _ => "Mouse",
        };
}

/// <summary>An executable that was in the foreground at some point.</summary>
public sealed record AppRecord(long Id, string ExePath, string ExeName);

/// <summary>Totals of one app over a period (only devices that count toward totals).</summary>
public sealed record AppUsage(
    long AppId,
    double Centimeters,
    double XCentimeters,
    double YCentimeters,
    long Clicks,
    double WheelNotches,
    long MoveSeconds,
    double ForegroundSeconds,
    double PeakSpeedCmPerSecond);

/// <summary>Totals of one local date.</summary>
public sealed record DayTotal(
    DateOnly Date,
    double Centimeters,
    double XCentimeters,
    double YCentimeters,
    long Clicks,
    double WheelNotches,
    long MoveSeconds,
    double PeakSpeedCmPerSecond);

/// <summary>Distance moved in one hour-of-day on one weekday, summed over a period.</summary>
public sealed record HourOfWeekTotal(DayOfWeek Day, int Hour, double Centimeters);

/// <summary>What a piece of gear is.</summary>
public enum GearKind
{
    MousePad = 0,
    Mouse = 1,
    Glides = 2,
    Sleeve = 3,
}

/// <summary>A mouse pad, mouse, set of glides or arm sleeve with the distance it is expected to last.</summary>
public sealed record GearItem(long Id, GearKind Kind, string Name, long? DeviceId, DateOnly StartedOn, double LifetimeKm, DateOnly? RetiredOn);

/// <summary>Typed access to the local statistics database.</summary>
public sealed class StatsStore : IDisposable
{
    public const double DefaultDpi = 800;

    private const string UpsertHourlySql = """
        INSERT INTO hourly (local_date, local_hour, utc_offset_min, device_id, app_id, dpi,
                            path_counts, x_counts, y_counts, abs_events,
                            clicks_left, clicks_right, clicks_middle, clicks_x1, clicks_x2,
                            wheel_notches, move_seconds, peak_speed)
        VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15, ?16, ?17, ?18)
        ON CONFLICT (local_date, local_hour, utc_offset_min, device_id, app_id, dpi) DO UPDATE SET
            path_counts   = path_counts   + excluded.path_counts,
            x_counts      = x_counts      + excluded.x_counts,
            y_counts      = y_counts      + excluded.y_counts,
            abs_events    = abs_events    + excluded.abs_events,
            clicks_left   = clicks_left   + excluded.clicks_left,
            clicks_right  = clicks_right  + excluded.clicks_right,
            clicks_middle = clicks_middle + excluded.clicks_middle,
            clicks_x1     = clicks_x1     + excluded.clicks_x1,
            clicks_x2     = clicks_x2     + excluded.clicks_x2,
            wheel_notches = wheel_notches + excluded.wheel_notches,
            move_seconds  = move_seconds  + excluded.move_seconds,
            peak_speed    = max(peak_speed, excluded.peak_speed);
        """;

    private const string DeviceColumns =
        "id, device_key, device_path, vid, pid, product_name, display_name, kind, dpi, excluded";

    private readonly SqliteDatabase _db;
    private SqliteStatement? _upsertHourly;

    private StatsStore(SqliteDatabase db) => _db = db;

    /// <summary>Default database location: %LOCALAPPDATA%\AimOdometer\aimodometer.db.</summary>
    public static string DefaultPath => Path.Combine(AppIdentity.DataDirectory, "aimodometer.db");

    public static StatsStore Open(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var db = SqliteDatabase.Open(path);
        try
        {
            Schema.Migrate(db);
            return new StatsStore(db);
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Inserts a newly seen device or refreshes an existing one; returns the stored record.
    /// <paramref name="key"/> is normally <see cref="DevicePath.StableKey"/>; see <see cref="DevicePath.UniqueKey"/>
    /// for two identical mice connected at once.
    /// </summary>
    public DeviceRecord UpsertDevice(string key, DevicePath path, DeviceKind kind, string? productName, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(path);
        var now = utcNow.ToString("O", CultureInfo.InvariantCulture);
        var defaultDpi = GetSettingDouble(SettingKeys.DefaultDpi) ?? DefaultDpi;

        using (var insert = _db.Prepare("""
            INSERT INTO devices (device_key, device_path, vid, pid, product_name, kind, dpi, excluded, first_seen, last_seen)
            VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?9)
            ON CONFLICT (device_key) DO UPDATE SET
                device_path = excluded.device_path,
                product_name = coalesce(excluded.product_name, devices.product_name),
                last_seen = excluded.last_seen;
            """))
        {
            insert.Bind(1, key).Bind(2, path.FullPath);
            BindNullable(insert, 3, path.VendorId);
            BindNullable(insert, 4, path.ProductId);
            insert.Bind(5, productName)
                .Bind(6, (long)kind)
                .Bind(7, defaultDpi)
                .Bind(8, kind == DeviceKind.Software ? 1L : 0L) // injected input is excluded from totals by default
                .Bind(9, now)
                .Run();
        }

        using var select = _db.Prepare($"SELECT {DeviceColumns} FROM devices WHERE device_key = ?1;");
        select.Bind(1, key);
        return select.Step() ? ReadDevice(select) : throw new InvalidOperationException("Device row vanished after upsert.");
    }

    public IReadOnlyList<DeviceRecord> GetDevices()
    {
        using var select = _db.Prepare($"SELECT {DeviceColumns} FROM devices ORDER BY id;");
        var result = new List<DeviceRecord>();
        while (select.Step())
        {
            result.Add(ReadDevice(select));
        }

        return result;
    }

    public void SetDeviceDpi(long deviceId, double dpi)
    {
        if (!double.IsFinite(dpi) || dpi <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), dpi, "DPI must be positive.");
        }

        using var update = _db.Prepare("UPDATE devices SET dpi = ?1 WHERE id = ?2;");
        update.Bind(1, dpi).Bind(2, deviceId).Run();
    }

    /// <summary>Adds the deltas of one hour to the hourly table in a single transaction.</summary>
    public void WriteHour(HourKey hour, IReadOnlyList<UsageDelta> deltas)
    {
        ArgumentNullException.ThrowIfNull(deltas);
        if (deltas.Count == 0)
        {
            return;
        }

        _upsertHourly ??= _db.Prepare(UpsertHourlySql);
        var date = hour.LocalDateText;
        _db.BeginTransaction();
        try
        {
            foreach (var delta in deltas)
            {
                var b = delta.Bucket;
                _upsertHourly.Reset();
                _upsertHourly
                    .Bind(1, date).Bind(2, hour.LocalHour).Bind(3, hour.UtcOffsetMinutes)
                    .Bind(4, delta.DeviceId).Bind(5, delta.AppId).Bind(6, delta.Dpi)
                    .Bind(7, b.PathCounts).Bind(8, b.XCounts).Bind(9, b.YCounts).Bind(10, b.AbsoluteEvents)
                    .Bind(11, b.ClicksLeft).Bind(12, b.ClicksRight).Bind(13, b.ClicksMiddle)
                    .Bind(14, b.ClicksX1).Bind(15, b.ClicksX2)
                    .Bind(16, b.WheelNotches).Bind(17, b.MoveSeconds)
                    .Bind(18, b.PeakSpeed)
                    .Run();
            }

            _db.Commit();
        }
        catch
        {
            _db.Rollback();
            throw;
        }
    }

    /// <summary>Distance in centimeters stored for a local date, counting only devices not excluded.</summary>
    public double CentimetersOn(DateOnly localDate)
    {
        using var select = _db.Prepare("""
            SELECT coalesce(sum(h.path_counts / h.dpi), 0) * 2.54
            FROM hourly h JOIN devices d ON d.id = h.device_id
            WHERE h.local_date = ?1 AND d.excluded = 0;
            """);
        select.Bind(1, localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return select.Step() ? select.GetDouble(0) : 0;
    }

    /// <summary>Returns the id of an executable, adding it on first sight. The path is matched case-insensitively.</summary>
    public long GetOrCreateApp(string exePath, DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrEmpty(exePath);
        using (var insert = _db.Prepare(
            "INSERT INTO apps (exe_path, exe_name, first_seen) VALUES (?1, ?2, ?3) ON CONFLICT (exe_path) DO NOTHING;"))
        {
            insert.Bind(1, exePath).Bind(2, Path.GetFileName(exePath)).Bind(3, utcNow.ToString("O", CultureInfo.InvariantCulture)).Run();
        }

        using var select = _db.Prepare("SELECT id FROM apps WHERE exe_path = ?1;");
        select.Bind(1, exePath);
        return select.Step() ? select.GetInt64(0) : throw new InvalidOperationException("App row vanished after insert.");
    }

    public IReadOnlyList<AppRecord> GetApps()
    {
        using var select = _db.Prepare("SELECT id, exe_path, exe_name FROM apps ORDER BY id;");
        var result = new List<AppRecord>();
        while (select.Step())
        {
            result.Add(new AppRecord(select.GetInt64(0), select.GetString(1)!, select.GetString(2)!));
        }

        return result;
    }

    /// <summary>Adds foreground seconds per app for one hour, in a single transaction.</summary>
    public void WriteAppTime(HourKey hour, IReadOnlyCollection<KeyValuePair<long, double>> secondsByApp)
    {
        ArgumentNullException.ThrowIfNull(secondsByApp);
        if (secondsByApp.Count == 0)
        {
            return;
        }

        using var upsert = _db.Prepare("""
            INSERT INTO app_time (local_date, local_hour, utc_offset_min, app_id, fg_seconds) VALUES (?1, ?2, ?3, ?4, ?5)
            ON CONFLICT (local_date, local_hour, utc_offset_min, app_id) DO UPDATE SET fg_seconds = fg_seconds + excluded.fg_seconds;
            """);
        _db.BeginTransaction();
        try
        {
            foreach (var (appId, seconds) in secondsByApp)
            {
                upsert.Reset();
                upsert.Bind(1, hour.LocalDateText).Bind(2, hour.LocalHour).Bind(3, hour.UtcOffsetMinutes)
                    .Bind(4, appId).Bind(5, seconds).Run();
            }

            _db.Commit();
        }
        catch
        {
            _db.Rollback();
            throw;
        }
    }

    /// <summary>Per-app totals for local dates in [from, to] (both inclusive; null = unbounded).</summary>
    public IReadOnlyList<AppUsage> GetAppUsage(DateOnly? from = null, DateOnly? to = null)
    {
        var fromText = from?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "0000-01-01";
        var toText = to?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "9999-12-31";
        using var select = _db.Prepare("""
            WITH movement AS (
                SELECT h.app_id,
                       sum(h.path_counts / h.dpi) * 2.54 AS cm,
                       sum(h.x_counts / h.dpi) * 2.54 AS x_cm,
                       sum(h.y_counts / h.dpi) * 2.54 AS y_cm,
                       sum(h.clicks_left + h.clicks_right + h.clicks_middle + h.clicks_x1 + h.clicks_x2) AS clicks,
                       sum(h.wheel_notches) AS wheel,
                       sum(h.move_seconds) AS move_seconds,
                       max(h.peak_speed / h.dpi) * 2.54 AS peak
                FROM hourly h JOIN devices d ON d.id = h.device_id
                WHERE d.excluded = 0 AND h.local_date BETWEEN ?1 AND ?2
                GROUP BY h.app_id),
            focus AS (
                SELECT app_id, sum(fg_seconds) AS fg FROM app_time
                WHERE local_date BETWEEN ?1 AND ?2 GROUP BY app_id),
            ids AS (SELECT app_id FROM movement UNION SELECT app_id FROM focus)
            SELECT ids.app_id, coalesce(m.cm, 0), coalesce(m.x_cm, 0), coalesce(m.y_cm, 0), coalesce(m.clicks, 0),
                   coalesce(m.wheel, 0), coalesce(m.move_seconds, 0), coalesce(f.fg, 0), coalesce(m.peak, 0)
            FROM ids LEFT JOIN movement m ON m.app_id = ids.app_id LEFT JOIN focus f ON f.app_id = ids.app_id
            ORDER BY ids.app_id;
            """);
        select.Bind(1, fromText).Bind(2, toText);
        var result = new List<AppUsage>();
        while (select.Step())
        {
            result.Add(new AppUsage(
                select.GetInt64(0), select.GetDouble(1), select.GetDouble(2), select.GetDouble(3), select.GetInt64(4),
                select.GetDouble(5), select.GetInt64(6), select.GetDouble(7), select.GetDouble(8)));
        }

        return result;
    }

    /// <summary>Per local date totals over devices that count toward totals, oldest first. Days without data are absent.</summary>
    public IReadOnlyList<DayTotal> GetDailyTotals(DateOnly? from = null, DateOnly? to = null)
    {
        using var select = _db.Prepare("""
            SELECT h.local_date,
                   sum(h.path_counts / h.dpi) * 2.54, sum(h.x_counts / h.dpi) * 2.54, sum(h.y_counts / h.dpi) * 2.54,
                   sum(h.clicks_left + h.clicks_right + h.clicks_middle + h.clicks_x1 + h.clicks_x2),
                   sum(h.wheel_notches), sum(h.move_seconds), max(h.peak_speed / h.dpi) * 2.54
            FROM hourly h JOIN devices d ON d.id = h.device_id
            WHERE d.excluded = 0 AND h.local_date BETWEEN ?1 AND ?2
            GROUP BY h.local_date ORDER BY h.local_date;
            """);
        select.Bind(1, from?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "0000-01-01")
            .Bind(2, to?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "9999-12-31");
        var result = new List<DayTotal>();
        while (select.Step())
        {
            result.Add(new DayTotal(
                DateOnly.ParseExact(select.GetString(0)!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                select.GetDouble(1), select.GetDouble(2), select.GetDouble(3), select.GetInt64(4),
                select.GetDouble(5), select.GetInt64(6), select.GetDouble(7)));
        }

        return result;
    }

    /// <summary>Movement per local hour of day (0..23) and weekday, for the activity heat map.</summary>
    public IReadOnlyList<HourOfWeekTotal> GetHourOfWeekTotals(DateOnly? from = null, DateOnly? to = null)
    {
        using var select = _db.Prepare("""
            SELECT h.local_date, h.local_hour, sum(h.path_counts / h.dpi) * 2.54
            FROM hourly h JOIN devices d ON d.id = h.device_id
            WHERE d.excluded = 0 AND h.local_date BETWEEN ?1 AND ?2
            GROUP BY h.local_date, h.local_hour;
            """);
        select.Bind(1, from?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "0000-01-01")
            .Bind(2, to?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "9999-12-31");
        var cells = new Dictionary<(DayOfWeek, int), double>();
        while (select.Step())
        {
            var day = DateOnly.ParseExact(select.GetString(0)!, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayOfWeek;
            var key = (day, (int)select.GetInt64(1));
            cells[key] = cells.GetValueOrDefault(key) + select.GetDouble(2);
        }

        return [.. cells.Select(c => new HourOfWeekTotal(c.Key.Item1, c.Key.Item2, c.Value))];
    }

    /// <summary>Distance per device over all time (including excluded devices, flagged).</summary>
    public IReadOnlyList<(DeviceRecord Device, double Centimeters)> GetDeviceTotals()
    {
        var devices = GetDevices().ToDictionary(d => d.Id);
        using var select = _db.Prepare(
            "SELECT device_id, coalesce(sum(path_counts / dpi), 0) * 2.54 FROM hourly GROUP BY device_id;");
        var totals = new Dictionary<long, double>();
        while (select.Step())
        {
            totals[select.GetInt64(0)] = select.GetDouble(1);
        }

        return [.. devices.Values.Select(d => (d, totals.GetValueOrDefault(d.Id))).OrderByDescending(t => t.Item2)];
    }

    /// <summary>The fastest flick ever recorded: speed, local date and foreground app; null without data.</summary>
    public (double CmPerSecond, DateOnly Date, long AppId)? GetPeakSpeedRecord()
    {
        using var select = _db.Prepare("""
            SELECT h.peak_speed / h.dpi * 2.54 AS speed, h.local_date, h.app_id
            FROM hourly h JOIN devices d ON d.id = h.device_id
            WHERE d.excluded = 0 AND h.peak_speed > 0
            ORDER BY speed DESC LIMIT 1;
            """);
        return select.Step()
            ? (select.GetDouble(0), DateOnly.ParseExact(select.GetString(1)!, "yyyy-MM-dd", CultureInfo.InvariantCulture), select.GetInt64(2))
            : null;
    }

    /// <summary>Changes a device's display name (null or empty restores the product name).</summary>
    public void SetDeviceName(long deviceId, string? displayName)
    {
        using var update = _db.Prepare("UPDATE devices SET display_name = ?1 WHERE id = ?2;");
        update.Bind(1, string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim()).Bind(2, deviceId).Run();
    }

    /// <summary>Includes or excludes a device from totals (e.g. a touchpad).</summary>
    public void SetDeviceExcluded(long deviceId, bool excluded)
    {
        using var update = _db.Prepare("UPDATE devices SET excluded = ?1 WHERE id = ?2;");
        update.Bind(1, excluded ? 1L : 0L).Bind(2, deviceId).Run();
    }

    public IReadOnlyDictionary<long, AppRule> GetAppRules()
    {
        using var select = _db.Prepare("SELECT app_id, category, game_key FROM app_rules;");
        var result = new Dictionary<long, AppRule>();
        while (select.Step())
        {
            result[select.GetInt64(0)] = new AppRule((AppCategory)select.GetInt64(1), select.GetString(2));
        }

        return result;
    }

    /// <summary>Sets or (with null) removes the user's rule for an app.</summary>
    public void SetAppRule(long appId, AppRule? rule)
    {
        if (rule is null)
        {
            using var delete = _db.Prepare("DELETE FROM app_rules WHERE app_id = ?1;");
            delete.Bind(1, appId).Run();
            return;
        }

        using var upsert = _db.Prepare("""
            INSERT INTO app_rules (app_id, category, game_key) VALUES (?1, ?2, ?3)
            ON CONFLICT (app_id) DO UPDATE SET category = excluded.category, game_key = excluded.game_key;
            """);
        upsert.Bind(1, appId).Bind(2, (long)rule.Category).Bind(3, rule.GameKey).Run();
    }

    public IReadOnlyDictionary<string, string> GetGameNames()
    {
        using var select = _db.Prepare("SELECT game_key, display_name FROM game_names;");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (select.Step())
        {
            result[select.GetString(0)!] = select.GetString(1)!;
        }

        return result;
    }

    /// <summary>Renames a game for display; null or empty restores the detected name.</summary>
    public void SetGameName(string gameKey, string? displayName)
    {
        ArgumentException.ThrowIfNullOrEmpty(gameKey);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            using var delete = _db.Prepare("DELETE FROM game_names WHERE game_key = ?1;");
            delete.Bind(1, gameKey).Run();
            return;
        }

        using var upsert = _db.Prepare(
            "INSERT INTO game_names (game_key, display_name) VALUES (?1, ?2) ON CONFLICT (game_key) DO UPDATE SET display_name = excluded.display_name;");
        upsert.Bind(1, gameKey).Bind(2, displayName.Trim()).Run();
    }

    /// <summary>Writes a consistent copy of the whole database to <paramref name="path"/> (must not exist).</summary>
    public void BackupTo(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _db.Execute($"VACUUM INTO '{path.Replace("'", "''", StringComparison.Ordinal)}';");
    }

    /// <summary>Unlocked achievements: id -> (UTC time, whether the notification was shown).</summary>
    public IReadOnlyDictionary<string, (DateTime UnlockedAtUtc, bool Notified)> GetAchievements()
    {
        using var select = _db.Prepare("SELECT id, unlocked_at, notified FROM achievements;");
        var result = new Dictionary<string, (DateTime, bool)>(StringComparer.Ordinal);
        while (select.Step())
        {
            result[select.GetString(0)!] = (
                DateTime.Parse(select.GetString(1)!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                select.GetInt64(2) != 0);
        }

        return result;
    }

    public void UnlockAchievements(IEnumerable<string> ids, DateTime utcNow, bool notified)
    {
        ArgumentNullException.ThrowIfNull(ids);
        using var insert = _db.Prepare("INSERT INTO achievements (id, unlocked_at, notified) VALUES (?1, ?2, ?3) ON CONFLICT (id) DO NOTHING;");
        foreach (var id in ids)
        {
            insert.Reset();
            insert.Bind(1, id).Bind(2, utcNow.ToString("O", CultureInfo.InvariantCulture)).Bind(3, notified ? 1L : 0L).Run();
        }
    }

    public void MarkAchievementsNotified(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        using var update = _db.Prepare("UPDATE achievements SET notified = 1 WHERE id = ?1;");
        foreach (var id in ids)
        {
            update.Reset();
            update.Bind(1, id).Run();
        }
    }

    /// <summary>Distance per local date within local hours [fromHour, toHour] (for "night owl" achievements).</summary>
    public IReadOnlyList<(DateOnly Date, double Centimeters)> GetHourRangeTotals(int fromHour, int toHour)
    {
        using var select = _db.Prepare("""
            SELECT h.local_date, sum(h.path_counts / h.dpi) * 2.54
            FROM hourly h JOIN devices d ON d.id = h.device_id
            WHERE d.excluded = 0 AND h.local_hour BETWEEN ?1 AND ?2
            GROUP BY h.local_date;
            """);
        select.Bind(1, fromHour).Bind(2, toHour);
        var result = new List<(DateOnly, double)>();
        while (select.Step())
        {
            result.Add((DateOnly.ParseExact(select.GetString(0)!, "yyyy-MM-dd", CultureInfo.InvariantCulture), select.GetDouble(1)));
        }

        return result;
    }

    /// <summary>Distance since a local date (inclusive), for one device or (null) all counted devices.</summary>
    public double CentimetersSince(DateOnly from, long? deviceId)
    {
        using var select = _db.Prepare("""
            SELECT coalesce(sum(h.path_counts / h.dpi), 0) * 2.54
            FROM hourly h JOIN devices d ON d.id = h.device_id
            WHERE h.local_date >= ?1 AND ((?2 IS NULL AND d.excluded = 0) OR h.device_id = ?2);
            """);
        select.Bind(1, from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (deviceId is { } id)
        {
            select.Bind(2, id);
        }
        else
        {
            select.Bind(2, (string?)null);
        }

        return select.Step() ? select.GetDouble(0) : 0;
    }

    public IReadOnlyList<GearItem> GetGear()
    {
        using var select = _db.Prepare("SELECT id, kind, name, device_id, started_on, lifetime_km, retired_on FROM gear ORDER BY retired_on IS NOT NULL, id;");
        var result = new List<GearItem>();
        while (select.Step())
        {
            result.Add(new GearItem(
                select.GetInt64(0), (GearKind)select.GetInt64(1), select.GetString(2)!,
                select.IsNull(3) ? null : select.GetInt64(3),
                DateOnly.ParseExact(select.GetString(4)!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                select.GetDouble(5),
                select.IsNull(6) ? null : DateOnly.ParseExact(select.GetString(6)!, "yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        return result;
    }

    /// <summary>Adds (Id = 0) or updates a gear item; returns its id.</summary>
    public long SaveGear(GearItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        using var statement = _db.Prepare(item.Id == 0
            ? "INSERT INTO gear (kind, name, device_id, started_on, lifetime_km, retired_on) VALUES (?1, ?2, ?3, ?4, ?5, ?6);"
            : "UPDATE gear SET kind = ?1, name = ?2, device_id = ?3, started_on = ?4, lifetime_km = ?5, retired_on = ?6 WHERE id = ?7;");
        statement.Bind(1, (long)item.Kind).Bind(2, item.Name);
        if (item.DeviceId is { } device)
        {
            statement.Bind(3, device);
        }
        else
        {
            statement.Bind(3, (string?)null);
        }

        statement.Bind(4, item.StartedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Bind(5, item.LifetimeKm)
            .Bind(6, item.RetiredOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (item.Id != 0)
        {
            statement.Bind(7, item.Id);
        }

        statement.Run();
        return item.Id == 0 ? _db.LastInsertRowId : item.Id;
    }

    public void DeleteGear(long id)
    {
        using var delete = _db.Prepare("DELETE FROM gear WHERE id = ?1;");
        delete.Bind(1, id).Run();
    }

    /// <summary>A cached map service response and when it was fetched, or null.</summary>
    public (string Value, DateTimeOffset FetchedAt)? GetGeoCache(string kind, string key)
    {
        using var select = _db.Prepare("SELECT value, fetched_at FROM geo_cache WHERE kind = ?1 AND key = ?2;");
        select.Bind(1, kind).Bind(2, key);
        return select.Step() ? (select.GetString(0)!, DateTimeOffset.FromUnixTimeSeconds(select.GetInt64(1))) : null;
    }

    public void SetGeoCache(string kind, string key, string value, DateTimeOffset fetchedAt)
    {
        using var upsert = _db.Prepare(
            """
            INSERT INTO geo_cache (kind, key, value, fetched_at) VALUES (?1, ?2, ?3, ?4)
            ON CONFLICT (kind, key) DO UPDATE SET value = excluded.value, fetched_at = excluded.fetched_at;
            """);
        upsert.Bind(1, kind).Bind(2, key).Bind(3, value).Bind(4, fetchedAt.ToUnixTimeSeconds()).Run();
    }

    public string? GetSetting(string key)
    {
        using var select = _db.Prepare("SELECT value FROM settings WHERE key = ?1;");
        select.Bind(1, key);
        return select.Step() ? select.GetString(0) : null;
    }

    public double? GetSettingDouble(string key) =>
        double.TryParse(GetSetting(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    public void SetSetting(string key, string value)
    {
        using var upsert = _db.Prepare(
            "INSERT INTO settings (key, value) VALUES (?1, ?2) ON CONFLICT (key) DO UPDATE SET value = excluded.value;");
        upsert.Bind(1, key).Bind(2, value).Run();
    }

    public void Dispose()
    {
        _upsertHourly?.Dispose();
        _db.Dispose();
    }

    private static void BindNullable(SqliteStatement statement, int index, int? value)
    {
        if (value is { } v)
        {
            statement.Bind(index, v);
        }
        else
        {
            statement.Bind(index, (string?)null);
        }
    }

    private static DeviceRecord ReadDevice(SqliteStatement row) => new(
        Id: row.GetInt64(0),
        DeviceKey: row.GetString(1)!,
        DevicePath: row.GetString(2)!,
        VendorId: row.IsNull(3) ? null : (int)row.GetInt64(3),
        ProductId: row.IsNull(4) ? null : (int)row.GetInt64(4),
        ProductName: row.GetString(5),
        DisplayName: row.GetString(6),
        Kind: (DeviceKind)row.GetInt64(7),
        Dpi: row.GetDouble(8),
        Excluded: row.GetInt64(9) != 0);
}

/// <summary>Keys of the settings table.</summary>
public static class SettingKeys
{
    public const string DefaultDpi = "default_dpi";
    public const string Autostart = "autostart";
    public const string Language = "language";
    public const string Units = "units";
    public const string LogLevel = "log_level";
    public const string EcoQos = "ecoqos";
    public const string Notifications = "notifications";
    public const string MapEnabled = "map_enabled";
    public const string MapPlaces = "map_places";
    public const string MapPeriod = "map_period";
}
