using AimOdometer.Core.Input;
using AimOdometer.Core.Storage;

namespace AimOdometer.Core.Tests;

public sealed class RecoveryTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 10, 3);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"aimodometer-recovery-{Guid.NewGuid():N}");

    public RecoveryTests() => Directory.CreateDirectory(_dir);

    private string DbPath => Path.Combine(_dir, "aimodometer.db");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void BackupWithData()
    {
        using var store = StatsStore.Open(DbPath);
        var mouse = DevicePath.Parse(@"\?\HID#VID_046D&PID_C54D&MI_00#a&1&0&0000#{378de44c-56ef-11d1-bc8c-00a0c91405dd}");
        var device = store.UpsertDevice(mouse.StableKey, mouse, DeviceKind.Mouse, null, DateTime.UtcNow);
        store.WriteHour(new HourKey(Day, 12, 180), [new UsageDelta(0, device.Id, 0, 800, new UsageBucket { PathCounts = 800, Dirty = true })]);
        Backups.CreateDaily(store, Backups.FolderFor(_dir), Day);
    }

    private void Damage()
    {
        var bytes = File.ReadAllBytes(DbPath);
        File.WriteAllBytes(DbPath, bytes[..(bytes.Length / 3)]);
    }

    [Fact]
    public void HealthyDatabase_OpensUntouched()
    {
        BackupWithData();
        using var store = Backups.OpenOrRecover(DbPath);
        Assert.Null(store.GetSetting(SettingKeys.RecoveredFrom));
        Assert.Empty(Directory.EnumerateFiles(_dir, "*.broken-*"));
    }

    [Fact]
    public void DamagedDatabase_RestoresTheNewestBackupAndKeepsTheDamagedFile()
    {
        BackupWithData();
        Damage();

        using var store = Backups.OpenOrRecover(DbPath);

        Assert.Equal("2026-10-03", store.GetSetting(SettingKeys.RecoveredFrom));
        Assert.Equal(2.54, store.CentimetersOn(Day), precision: 9);
        Assert.Single(Directory.EnumerateFiles(_dir, "aimodometer.db.broken-*"), f => !f.EndsWith("-wal", StringComparison.Ordinal) && !f.EndsWith("-shm", StringComparison.Ordinal));
    }

    [Fact]
    public void GarbageWithoutBackups_StartsEmpty()
    {
        File.WriteAllBytes(DbPath, [.. Enumerable.Range(0, 4096).Select(i => (byte)(i * 7))]);

        using var store = Backups.OpenOrRecover(DbPath);

        Assert.Equal("none", store.GetSetting(SettingKeys.RecoveredFrom));
        Assert.Equal(0, store.CentimetersOn(Day));
    }

    [Fact]
    public void DamagedBackup_IsSkipped()
    {
        BackupWithData();
        var folder = Backups.FolderFor(_dir);
        File.WriteAllBytes(Path.Combine(folder, "aimodometer-2026-10-04.db"), [.. Enumerable.Range(0, 4096).Select(i => (byte)i)]);
        Damage();

        using var store = Backups.OpenOrRecover(DbPath);

        Assert.Equal("2026-10-03", store.GetSetting(SettingKeys.RecoveredFrom));
    }
}
