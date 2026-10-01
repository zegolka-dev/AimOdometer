namespace AimOdometer.Core.Storage;

/// <summary>
/// Versioned local database schema. Each entry upgrades from version i to i + 1.
/// Never edit a released migration; append a new one instead.
/// </summary>
public static class Schema
{
    private static readonly string[] Migrations =
    [
        // v1: devices, apps, hourly aggregates, settings.
        """
        CREATE TABLE devices (
            id            INTEGER PRIMARY KEY,
            device_key    TEXT    NOT NULL UNIQUE,
            device_path   TEXT    NOT NULL,
            vid           INTEGER,
            pid           INTEGER,
            product_name  TEXT,
            display_name  TEXT,
            kind          INTEGER NOT NULL DEFAULT 0,   -- 0 mouse, 1 touchpad, 2 software/injected input
            dpi           REAL    NOT NULL DEFAULT 800,
            excluded      INTEGER NOT NULL DEFAULT 0,
            first_seen    TEXT    NOT NULL,
            last_seen     TEXT    NOT NULL
        );

        CREATE TABLE apps (
            id          INTEGER PRIMARY KEY,
            exe_path    TEXT NOT NULL UNIQUE COLLATE NOCASE,
            exe_name    TEXT NOT NULL,
            first_seen  TEXT NOT NULL
        );

        -- One row per local hour x device x foreground app x DPI.
        -- local_date/local_hour/utc_offset_min are captured when the movement happened,
        -- so midnight, DST and time-zone changes are unambiguous.
        -- Distances are stored in sensor counts; cm = path_counts / dpi * 2.54.
        CREATE TABLE hourly (
            local_date      TEXT    NOT NULL,           -- yyyy-MM-dd
            local_hour      INTEGER NOT NULL,           -- 0..23
            utc_offset_min  INTEGER NOT NULL,
            device_id       INTEGER NOT NULL REFERENCES devices(id),
            app_id          INTEGER NOT NULL,           -- 0 = unknown / not tracked yet
            dpi             REAL    NOT NULL,
            path_counts     REAL    NOT NULL DEFAULT 0,
            x_counts        REAL    NOT NULL DEFAULT 0,
            y_counts        REAL    NOT NULL DEFAULT 0,
            abs_events      INTEGER NOT NULL DEFAULT 0,
            clicks_left     INTEGER NOT NULL DEFAULT 0,
            clicks_right    INTEGER NOT NULL DEFAULT 0,
            clicks_middle   INTEGER NOT NULL DEFAULT 0,
            clicks_x1       INTEGER NOT NULL DEFAULT 0,
            clicks_x2       INTEGER NOT NULL DEFAULT 0,
            wheel_notches   REAL    NOT NULL DEFAULT 0,
            move_seconds    INTEGER NOT NULL DEFAULT 0,
            fg_seconds      INTEGER NOT NULL DEFAULT 0,
            peak_speed      REAL    NOT NULL DEFAULT 0, -- counts per second
            PRIMARY KEY (local_date, local_hour, utc_offset_min, device_id, app_id, dpi)
        ) WITHOUT ROWID;

        CREATE INDEX hourly_by_device ON hourly (device_id, local_date);
        CREATE INDEX hourly_by_app ON hourly (app_id, local_date);

        CREATE TABLE settings (
            key    TEXT PRIMARY KEY,
            value  TEXT NOT NULL
        ) WITHOUT ROWID;
        """,
    ];

    public static int LatestVersion => Migrations.Length;

    /// <summary>Upgrades the database to <see cref="LatestVersion"/>. Returns the version before the upgrade.</summary>
    public static int Migrate(SqliteDatabase db)
    {
        ArgumentNullException.ThrowIfNull(db);
        var current = (int)db.ScalarInt64("PRAGMA user_version;");
        if (current > LatestVersion)
        {
            throw new InvalidOperationException(
                $"Database schema v{current} is newer than this app (v{LatestVersion}). Please update AimOdometer.");
        }

        for (var version = current; version < LatestVersion; version++)
        {
            db.BeginTransaction();
            try
            {
                db.Execute(Migrations[version]);
                db.Execute($"PRAGMA user_version={version + 1};");
                db.Commit();
            }
            catch
            {
                db.Rollback();
                throw;
            }
        }

        return current;
    }
}
