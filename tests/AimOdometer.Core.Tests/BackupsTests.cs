using AimOdometer.Core.Input;
using AimOdometer.Core.Ipc;
using AimOdometer.Core.Storage;

namespace AimOdometer.Core.Tests;

public sealed class BackupsTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 10, 3);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"aimodometer-backup-{Guid.NewGuid():N}");

    public BackupsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private StatsStore StoreWithData()
    {
        var store = StatsStore.Open(Path.Combine(_dir, "aimodometer.db"));
        var mouse = DevicePath.Parse(@"\\?\HID#VID_046D&PID_C54D&MI_00#a&1&0&0000#{378de44c-56ef-11d1-bc8c-00a0c91405dd}");
        var device = store.UpsertDevice(mouse.StableKey, mouse, DeviceKind.Mouse, null, DateTime.UtcNow);
        store.WriteHour(new HourKey(Day, 12, 180), [new UsageDelta(0, device.Id, 0, 800, new UsageBucket { PathCounts = 800, Dirty = true })]);
        return store;
    }

    [Fact]
    public void CreatesAReadableCopyOncePerDay()
    {
        using var store = StoreWithData();
        var folder = Backups.FolderFor(_dir);

        var first = Backups.CreateDaily(store, folder, Day);
        var second = Backups.CreateDaily(store, folder, Day);

        Assert.NotNull(first);
        Assert.Null(second);
        using var copy = StatsStore.Open(first);
        Assert.Equal(2.54, copy.CentimetersOn(Day), precision: 9);
    }

    [Fact]
    public void KeepsOnlyTheNewest()
    {
        using var store = StoreWithData();
        var folder = Backups.FolderFor(_dir);
        for (var i = 0; i < 10; i++)
        {
            Backups.CreateDaily(store, folder, Day.AddDays(i), keep: 3);
        }

        var names = Backups.List(folder).Select(Path.GetFileName).ToList();
        Assert.Equal(["aimodometer-2026-10-12.db", "aimodometer-2026-10-11.db", "aimodometer-2026-10-10.db"], names);
    }

    [Fact]
    public void IgnoresUnrelatedFiles()
    {
        var folder = Backups.FolderFor(_dir);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "keep me");
        File.WriteAllText(Path.Combine(folder, "aimodometer-latest.db"), "keep me");
        using var store = StoreWithData();
        Backups.CreateDaily(store, folder, Day, keep: 1);
        Assert.True(File.Exists(Path.Combine(folder, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(folder, "aimodometer-latest.db")));
    }

    [Theory]
    [InlineData(@"C:\Users\x\AppData\Local\AimOdometer", @"c:\users\x\appdata\local\aimodometer\", true)]
    [InlineData(@"C:\Users\x\AppData\Local\AimOdometer", @"C:\Claude general\AimOdometer\artifacts\uismoke", false)]
    public void SameFolder_IgnoresCaseAndTrailingSeparator(string a, string b, bool expected) =>
        Assert.Equal(expected, AppIdentity.SameFolder(a, b));

    [Fact]
    public void Ping_CarriesTheDataFolder()
    {
        var buffer = new byte[TrackerProtocol.MaxResponseSize];
        var length = TrackerProtocol.WritePing(buffer, 1234, @"C:\Users\Тест\AppData\Local\AimOdometer");
        var (version, pid, folder) = TrackerProtocol.ReadPing(buffer.AsSpan(0, length));
        Assert.Equal(TrackerProtocol.Version, version);
        Assert.Equal(1234, pid);
        Assert.Equal(@"C:\Users\Тест\AppData\Local\AimOdometer", folder);
    }
}
