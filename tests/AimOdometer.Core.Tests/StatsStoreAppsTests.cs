using AimOdometer.Core.Games;
using AimOdometer.Core.Input;
using AimOdometer.Core.Storage;

namespace AimOdometer.Core.Tests;

public sealed class StatsStoreAppsTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
    private static readonly HourKey Day1 = new(new DateOnly(2026, 10, 1), 20, 180);
    private static readonly HourKey Day2 = new(new DateOnly(2026, 10, 2), 9, 180);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"aimodometer-apps-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }
    }

    private static DevicePath Mouse => DevicePath.Parse(
        @"\\?\HID#VID_046D&PID_C547&MI_02&Col01#8&2a4b1c3&0&0000#{378de44c-56ef-11d1-bc8c-00a0c91405dd}");

    private static UsageDelta Delta(long deviceId, long appId, double counts, long clicks = 0) =>
        new(0, deviceId, appId, 800, new UsageBucket { PathCounts = counts, ClicksLeft = clicks, Dirty = true });

    [Fact]
    public void GetOrCreateApp_IsStableAndCaseInsensitive()
    {
        using var store = StatsStore.Open(_path);
        var a = store.GetOrCreateApp(@"C:\Games\CS2\cs2.exe", Now);
        var b = store.GetOrCreateApp(@"c:\games\cs2\CS2.EXE", Now);
        Assert.Equal(a, b);
        Assert.Equal("cs2.exe", Assert.Single(store.GetApps()).ExeName);
    }

    [Fact]
    public void AppUsage_CombinesMovementAndForegroundTime()
    {
        using var store = StatsStore.Open(_path);
        var mouse = store.UpsertDevice(Mouse.StableKey, Mouse, DeviceKind.Mouse, null, Now);
        var game = store.GetOrCreateApp(@"C:\Games\cs2.exe", Now);
        var video = store.GetOrCreateApp(@"C:\Apps\player.exe", Now);

        store.WriteHour(Day1, [Delta(mouse.Id, game, 800, clicks: 3)]);
        store.WriteAppTime(Day1, [new(game, 1800.5), new(video, 600)]);
        store.WriteAppTime(Day1, [new(game, 0.5)]);

        var usage = store.GetAppUsage().ToDictionary(u => u.AppId);
        Assert.Equal(2.54, usage[game].Centimeters, precision: 9);
        Assert.Equal(3, usage[game].Clicks);
        Assert.Equal(1801, usage[game].ForegroundSeconds, precision: 9);
        Assert.Equal(0, usage[video].Centimeters); // time without movement still shows up
        Assert.Equal(600, usage[video].ForegroundSeconds);
    }

    [Fact]
    public void AppUsage_FiltersByDateRange()
    {
        using var store = StatsStore.Open(_path);
        var mouse = store.UpsertDevice(Mouse.StableKey, Mouse, DeviceKind.Mouse, null, Now);
        var game = store.GetOrCreateApp(@"C:\Games\cs2.exe", Now);
        store.WriteHour(Day1, [Delta(mouse.Id, game, 800)]);
        store.WriteHour(Day2, [Delta(mouse.Id, game, 1600)]);

        Assert.Equal(5.08, store.GetAppUsage(from: Day2.LocalDate).Single().Centimeters, precision: 9);
        Assert.Equal(2.54, store.GetAppUsage(to: Day1.LocalDate).Single().Centimeters, precision: 9);
        Assert.Equal(7.62, store.GetAppUsage().Single().Centimeters, precision: 9);
    }

    [Fact]
    public void AppUsage_IgnoresExcludedDevices()
    {
        using var store = StatsStore.Open(_path);
        var software = store.UpsertDevice(DevicePath.SoftwareKey, DevicePath.Parse(""), DeviceKind.Software, null, Now);
        var app = store.GetOrCreateApp(@"C:\x.exe", Now);
        store.WriteHour(Day1, [Delta(software.Id, app, 8000)]);
        Assert.Empty(store.GetAppUsage());
    }

    [Fact]
    public void Rules_RoundTripAndDelete()
    {
        using var store = StatsStore.Open(_path);
        var app = store.GetOrCreateApp(@"C:\x.exe", Now);
        store.SetAppRule(app, new AppRule(AppCategory.Game, "steam:730"));
        store.SetAppRule(app, new AppRule(AppCategory.Excluded, null));
        Assert.Equal(new AppRule(AppCategory.Excluded, null), store.GetAppRules()[app]);

        store.SetAppRule(app, null);
        Assert.Empty(store.GetAppRules());
    }

    [Fact]
    public void GameNames_RoundTripAndReset()
    {
        using var store = StatsStore.Open(_path);
        store.SetGameName("steam:730", "  CS  ");
        Assert.Equal("CS", store.GetGameNames()["steam:730"]);
        store.SetGameName("steam:730", " ");
        Assert.Empty(store.GetGameNames());
    }

    [Fact]
    public void ExistingVersion1Database_IsUpgradedWithDataKept()
    {
        // Build a v1 database the way phase 2 trackers wrote it.
        using (var db = SqliteDatabase.Open(_path))
        {
            db.Execute("""
                CREATE TABLE devices (id INTEGER PRIMARY KEY, device_key TEXT NOT NULL UNIQUE, device_path TEXT NOT NULL,
                    vid INTEGER, pid INTEGER, product_name TEXT, display_name TEXT, kind INTEGER NOT NULL DEFAULT 0,
                    dpi REAL NOT NULL DEFAULT 800, excluded INTEGER NOT NULL DEFAULT 0, first_seen TEXT NOT NULL, last_seen TEXT NOT NULL);
                CREATE TABLE apps (id INTEGER PRIMARY KEY, exe_path TEXT NOT NULL UNIQUE COLLATE NOCASE, exe_name TEXT NOT NULL, first_seen TEXT NOT NULL);
                CREATE TABLE hourly (local_date TEXT NOT NULL, local_hour INTEGER NOT NULL, utc_offset_min INTEGER NOT NULL,
                    device_id INTEGER NOT NULL REFERENCES devices(id), app_id INTEGER NOT NULL, dpi REAL NOT NULL,
                    path_counts REAL NOT NULL DEFAULT 0, x_counts REAL NOT NULL DEFAULT 0, y_counts REAL NOT NULL DEFAULT 0,
                    abs_events INTEGER NOT NULL DEFAULT 0, clicks_left INTEGER NOT NULL DEFAULT 0, clicks_right INTEGER NOT NULL DEFAULT 0,
                    clicks_middle INTEGER NOT NULL DEFAULT 0, clicks_x1 INTEGER NOT NULL DEFAULT 0, clicks_x2 INTEGER NOT NULL DEFAULT 0,
                    wheel_notches REAL NOT NULL DEFAULT 0, move_seconds INTEGER NOT NULL DEFAULT 0, fg_seconds INTEGER NOT NULL DEFAULT 0,
                    peak_speed REAL NOT NULL DEFAULT 0,
                    PRIMARY KEY (local_date, local_hour, utc_offset_min, device_id, app_id, dpi)) WITHOUT ROWID;
                CREATE INDEX hourly_by_device ON hourly (device_id, local_date);
                CREATE INDEX hourly_by_app ON hourly (app_id, local_date);
                CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL) WITHOUT ROWID;
                INSERT INTO devices (id, device_key, device_path, first_seen, last_seen) VALUES (1, 'K', 'P', 'x', 'x');
                INSERT INTO hourly (local_date, local_hour, utc_offset_min, device_id, app_id, dpi, path_counts)
                    VALUES ('2026-10-01', 20, 180, 1, 0, 800, 8000);
                PRAGMA user_version = 1;
                """);
        }

        using var store = StatsStore.Open(_path);
        Assert.Equal(25.4, store.CentimetersOn(new DateOnly(2026, 10, 1)), precision: 9);
        store.WriteHour(Day1, [Delta(1, 0, 800)]); // the v2 upsert works against the upgraded table
        Assert.Equal(27.94, store.CentimetersOn(Day1.LocalDate), precision: 9);
    }
}
