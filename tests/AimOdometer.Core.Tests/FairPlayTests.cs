using AimOdometer.Core.Fun;
using AimOdometer.Core.Games;
using AimOdometer.Core.Input;
using AimOdometer.Core.Storage;

namespace AimOdometer.Core.Tests;

public sealed class FairPlayTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 10, 3);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"aimodometer-fair-{Guid.NewGuid():N}");
    private readonly StatsStore _store;
    private readonly long _mouse;

    public FairPlayTests()
    {
        Directory.CreateDirectory(_dir);
        _store = StatsStore.Open(Path.Combine(_dir, "aimodometer.db"));
        var path = DevicePath.Parse(@"\\?\HID#VID_046D&PID_C54D&MI_00#a&1&0&0000#{378de44c-56ef-11d1-bc8c-00a0c91405dd}");
        _mouse = _store.UpsertDevice(path.StableKey, path, DeviceKind.Mouse, null, DateTime.UtcNow).Id;
    }

    public void Dispose()
    {
        _store.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Writes <paramref name="meters"/> of movement in one hour at <paramref name="dpi"/>.</summary>
    private void Move(DateOnly day, int hour, double meters, double dpi, double peakMetersPerSecond = 1) =>
        _store.WriteHour(new HourKey(day, hour, 180), [new UsageDelta(0, _mouse, 0, dpi, new UsageBucket
        {
            PathCounts = meters * 100 / 2.54 * dpi,
            PeakSpeed = peakMetersPerSecond * 100 / 2.54 * dpi,
            MoveSeconds = 60,
            Dirty = true,
        })]);

    private IReadOnlyList<string> Unlocked() =>
        [.. AchievementService.UnlockNew(_store, GameCatalog.Create(_store, Path.Combine(_dir, "games.json")), Day, DateTime.UtcNow)
            .Where(a => a.Shame).Select(a => a.Id)];

    [Fact]
    public void HonestPlay_EarnsNoShame()
    {
        Move(Day, 10, 3000, 800, peakMetersPerSecond: 6);
        Move(Day, 11, 3000, 1600, peakMetersPerSecond: 8);
        Assert.Empty(Unlocked());
    }

    [Fact]
    public void TenKilometresAtLowDpi_IsAClown()
    {
        Move(Day, 10, 6000, 150);
        Move(Day, 11, 5000, 150);
        Assert.Equal(["shame-clown"], Unlocked());
    }

    [Fact]
    public void TheClownCountsTheDayAsAWhole()
    {
        Move(Day, 10, 6000, 150);
        Move(Day, 11, 5000, 3200); // weighted DPI of the day is well above 200
        Assert.DoesNotContain("shame-clown", Unlocked());
    }

    [Fact]
    public void VeryLowDpi_IsABlockhead()
    {
        Move(Day, 10, 150, 60);
        Assert.Equal(["shame-blockhead"], Unlocked());
    }

    [Fact]
    public void ImpossibleFlicks_AreFoolAndBooster()
    {
        Move(Day, 10, 10, 800, peakMetersPerSecond: 35);
        Assert.Equal(["shame-fool"], Unlocked());
        Move(Day, 11, 10, 800, peakMetersPerSecond: 60);
        Assert.Equal(["shame-booster"], Unlocked());
    }

    [Fact]
    public void ShameTargetsMatchTheLimits()
    {
        double Target(string id) => AchievementEngine.All.Single(a => a.Id == id).Target;
        Assert.Equal(FairPlay.ClownDayMeters, Target("shame-clown"));
        Assert.Equal(FairPlay.BlockheadMeters, Target("shame-blockhead"));
        Assert.Equal(FairPlay.FoolFlickMetersPerSecond, Target("shame-fool"));
        Assert.Equal(4, AchievementEngine.All.Count(a => a.Shame));
    }

    [Fact]
    public void DailyBreakdown_CarriesTheWeightedDpiAndTheDpiOfTheFastestFlick()
    {
        Move(Day, 10, 100, 800, peakMetersPerSecond: 3);
        Move(Day, 11, 300, 1600, peakMetersPerSecond: 7);

        var row = Assert.Single(_store.GetDailyBreakdown());
        Assert.Equal(1400, row.Dpi, precision: 6); // distance-weighted: (100*800 + 300*1600) / 400
        Assert.Equal(1600, row.PeakDpi);
        Assert.Equal(700, row.PeakSpeed, precision: 6);
    }

    [Fact]
    public void WeightedDpi_IgnoresUnknownParts() =>
        Assert.Equal(1000, FairPlay.WeightedDpi([(10, 800), (10, 1200), (5, 0), (0, 3200)]));
}
