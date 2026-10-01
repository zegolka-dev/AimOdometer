using AimOdometer.Core.Input;
using AimOdometer.Core.Storage;

namespace AimOdometer.Core.Tests;

public sealed class StatsStoreTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
    private static readonly HourKey Hour = new(new DateOnly(2026, 10, 2), 14, 180);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"aimodometer-test-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }
    }

    private static DevicePath Mouse => DevicePath.Parse(
        @"\\?\HID#VID_046D&PID_C547&MI_02&Col01#8&2a4b1c3&0&0000#{378de44c-56ef-11d1-bc8c-00a0c91405dd}");

    private static UsageDelta Delta(long deviceId, double counts, double dpi = 800, double peak = 0) =>
        new(0, deviceId, 0, dpi, new UsageBucket { PathCounts = counts, ClicksLeft = 1, PeakSpeed = peak, Dirty = true });

    [Fact]
    public void Migrate_IsIdempotent()
    {
        using (StatsStore.Open(_path))
        {
        }

        using var store = StatsStore.Open(_path);
        Assert.Empty(store.GetDevices());
    }

    [Fact]
    public void UpsertDevice_ReturnsSameIdForSameKey()
    {
        using var store = StatsStore.Open(_path);
        var first = store.UpsertDevice(Mouse.StableKey, Mouse, DeviceKind.Mouse, "G Pro", Now);
        var second = store.UpsertDevice(Mouse.StableKey, Mouse, DeviceKind.Mouse, null, Now.AddHours(1));
        Assert.Equal(first.Id, second.Id);
        Assert.Equal("G Pro", second.Name);
        Assert.Equal(StatsStore.DefaultDpi, second.Dpi);
        Assert.False(second.Excluded);
    }

    [Fact]
    public void SoftwareDevice_IsExcludedByDefault()
    {
        using var store = StatsStore.Open(_path);
        var device = store.UpsertDevice(DevicePath.SoftwareKey, DevicePath.Parse(""), DeviceKind.Software, null, Now);
        Assert.True(device.Excluded);
        Assert.Equal("Software input", device.Name);
    }

    [Fact]
    public void WriteHour_AddsUp()
    {
        using var store = StatsStore.Open(_path);
        var device = store.UpsertDevice(Mouse.StableKey, Mouse, DeviceKind.Mouse, null, Now);
        store.WriteHour(Hour, [Delta(device.Id, 400, peak: 5)]);
        store.WriteHour(Hour, [Delta(device.Id, 400, peak: 3)]);

        Assert.Equal(2.54, store.CentimetersOn(Hour.LocalDate), precision: 9);
    }

    [Fact]
    public void Centimeters_UseDpiStoredWithEachRow()
    {
        using var store = StatsStore.Open(_path);
        var device = store.UpsertDevice(Mouse.StableKey, Mouse, DeviceKind.Mouse, null, Now);
        store.WriteHour(Hour, [Delta(device.Id, 800, dpi: 800), Delta(device.Id, 1600, dpi: 1600)]);
        Assert.Equal(5.08, store.CentimetersOn(Hour.LocalDate), precision: 9);
    }

    [Fact]
    public void Centimeters_IgnoreExcludedDevicesAndOtherDays()
    {
        using var store = StatsStore.Open(_path);
        var mouse = store.UpsertDevice(Mouse.StableKey, Mouse, DeviceKind.Mouse, null, Now);
        var software = store.UpsertDevice(DevicePath.SoftwareKey, DevicePath.Parse(""), DeviceKind.Software, null, Now);
        store.WriteHour(Hour, [Delta(mouse.Id, 800), Delta(software.Id, 8000)]);
        store.WriteHour(Hour with { LocalDate = Hour.LocalDate.AddDays(1) }, [Delta(mouse.Id, 800)]);

        Assert.Equal(2.54, store.CentimetersOn(Hour.LocalDate), precision: 9);
    }

    [Fact]
    public void SetDeviceDpi_RejectsInvalid()
    {
        using var store = StatsStore.Open(_path);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.SetDeviceDpi(1, 0));
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        using var store = StatsStore.Open(_path);
        Assert.Null(store.GetSetting(SettingKeys.Units));
        store.SetSetting(SettingKeys.Units, "metric");
        store.SetSetting(SettingKeys.Units, "imperial");
        Assert.Equal("imperial", store.GetSetting(SettingKeys.Units));
    }
}
