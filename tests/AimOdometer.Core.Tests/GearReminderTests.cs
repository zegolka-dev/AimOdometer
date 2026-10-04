using AimOdometer.Core.Fun;
using AimOdometer.Core.Input;
using AimOdometer.Core.Storage;

namespace AimOdometer.Core.Tests;

public sealed class GearReminderTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 10, 3);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"aimodometer-gear-{Guid.NewGuid():N}");
    private readonly StatsStore _store;

    public GearReminderTests()
    {
        Directory.CreateDirectory(_dir);
        _store = StatsStore.Open(Path.Combine(_dir, "aimodometer.db"));
        var path = DevicePath.Parse(@"\\?\HID#VID_046D&PID_C54D&MI_00#a&1&0&0000#{378de44c-56ef-11d1-bc8c-00a0c91405dd}");
        var mouse = _store.UpsertDevice(path.StableKey, path, DeviceKind.Mouse, null, DateTime.UtcNow).Id;
        // 1 km at 800 DPI.
        _store.WriteHour(new HourKey(Day, 12, 180), [new UsageDelta(0, mouse, 0, 800, new UsageBucket { PathCounts = 100_000 / 2.54 * 800, Dirty = true })]);
    }

    public void Dispose()
    {
        _store.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private long Add(string name, double lifetimeKm, DateOnly? retired = null) =>
        _store.SaveGear(new GearItem(0, GearKind.MousePad, name, null, Day, lifetimeKm, retired));

    [Fact]
    public void WornGearIsAnnouncedOnce()
    {
        var worn = Add("Worn pad", 1.05);   // 95 % used
        Add("Fresh pad", 10);               // 10 % used
        Add("Retired pad", 1, Day);         // retired: never announced

        Assert.Equal([worn], GearWear.DueForReplacement(_store).Select(s => s.Item.Id));

        GearWear.MarkAnnounced(_store, [worn]);
        Assert.Empty(GearWear.DueForReplacement(_store));
    }

    [Fact]
    public void AnnouncementsAccumulate()
    {
        var first = Add("First", 1);
        GearWear.MarkAnnounced(_store, [first]);
        var second = Add("Second", 1);

        Assert.Equal([second], GearWear.DueForReplacement(_store).Select(s => s.Item.Id));
        GearWear.MarkAnnounced(_store, [second]);
        Assert.Equal($"{first},{second}", _store.GetSetting(SettingKeys.GearAnnounced));
    }
}
