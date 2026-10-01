using System.Globalization;
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

/// <summary>Typed access to the local statistics database.</summary>
public sealed class StatsStore : IDisposable
{
    public const double DefaultDpi = 800;

    private const string UpsertHourlySql = """
        INSERT INTO hourly (local_date, local_hour, utc_offset_min, device_id, app_id, dpi,
                            path_counts, x_counts, y_counts, abs_events,
                            clicks_left, clicks_right, clicks_middle, clicks_x1, clicks_x2,
                            wheel_notches, move_seconds, fg_seconds, peak_speed)
        VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15, ?16, ?17, ?18, ?19)
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
            fg_seconds    = fg_seconds    + excluded.fg_seconds,
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
                    .Bind(16, b.WheelNotches).Bind(17, b.MoveSeconds).Bind(18, b.ForegroundSeconds)
                    .Bind(19, b.PeakSpeed)
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
}
