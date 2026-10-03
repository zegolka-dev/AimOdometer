using AimOdometer.Core.Fun;
using AimOdometer.Core.Games;
using AimOdometer.Core.Input;
using AimOdometer.Core.Storage;

namespace AimOdometer.Core.Tests;

public class ComparisonsTests
{
    [Fact]
    public void EmbeddedListIsSortedAndComplete()
    {
        var all = Comparisons.All;
        Assert.True(all.Count >= 10);
        Assert.Equal(all.OrderBy(c => c.Meters).Select(c => c.Id), all.Select(c => c.Id));
        Assert.Contains(all, c => c.Id == Comparisons.MoonId);
    }

    [Theory]
    [InlineData(105_600, "burj-khalifa", 1.275)]       // 1056 m: the largest object fully covered
    [InlineData(66_000, "eiffel-tower", 2.0)]          // 660 m
    [InlineData(5_000, "football-field", 0.476)]      // 50 m: a fraction of the smallest object
    [InlineData(4_219_500, "marathon", 1.0)]
    [InlineData(500_000_000, "channel-tunnel", 99.1)] // 5000 km: the Moon is never the "fitting" object
    public void Best_PicksTheLargestObjectCovered(double centimeters, string id, double ratio)
    {
        var best = Comparisons.Best(centimeters)!;
        Assert.Equal(id, best.Target.Id);
        Assert.Equal(ratio, best.Ratio, tolerance: 0.01);
    }

    [Fact]
    public void Best_IsNullForNothing() => Assert.Null(Comparisons.Best(0));

    [Fact]
    public void Best_IsNullWhileTheFractionRoundsToZero() => Assert.Null(Comparisons.Best(81));

    [Fact]
    public void MoonProgress() => Assert.Equal(0.5, Comparisons.MoonProgress(384_400_000.0 * 100 / 2), precision: 9);
}

public class AchievementEngineTests
{
    private static AchievementSnapshot Snapshot(
        double total = 0, double bestDay = 0, int streak = 0, double peak = 0, Dictionary<string, double>? games = null,
        double night = 0, double bestNight = 0, long clicks = 0, double wheel = 0, double device = 0, int activeDays = 0) =>
        new(total, bestDay, streak, peak, games ?? [], night, bestNight, clicks, wheel, device, activeDays);

    [Fact]
    public void EmbeddedDefinitionsAreValid()
    {
        var all = AchievementEngine.All;
        Assert.True(all.Count >= 30);
        Assert.Equal(all.Count, all.Select(a => a.Id).Distinct().Count());
        Assert.All(all, a =>
        {
            Assert.True(a.Target > 0);
            Assert.NotEqual(0, AchievementEngine.CurrentValue(a, Snapshot(1e9, 1e9, 1000, 1000, new() { ["steam:730"] = 1e9, ["steam:1172470"] = 1e9, ["valorant"] = 1e9, ["steam:570"] = 1e9, ["a"] = 1e9, ["b"] = 1e9, ["c"] = 1e9, ["d"] = 1e9, ["e"] = 1e9, ["f"] = 1e9 }, 1e9, 1e9, 10_000_000, 1e9, 1e9, 1000)));
            Assert.Equal(1, a.Glyph.Length);
        });
    }

    [Fact]
    public void Conditions_ReadTheRightNumbers()
    {
        var s = Snapshot(total: 1500, games: new() { ["steam:730"] = 900, ["valorant"] = 50, ["x"] = 200 }, clicks: 12_000);
        AchievementDefinition Def(string type, double target, string? game = null) => new("t", type, target, "E7C1", game);

        Assert.Equal(1500, AchievementEngine.CurrentValue(Def("totalDistance", 1000), s));
        Assert.Equal(900, AchievementEngine.CurrentValue(Def("gameDistance", 1000, "steam:730"), s));
        Assert.Equal(900, AchievementEngine.CurrentValue(Def("gameDistance", 1000), s));
        Assert.Equal(0, AchievementEngine.CurrentValue(Def("gameDistance", 1000, "steam:570"), s));
        Assert.Equal(2, AchievementEngine.CurrentValue(Def("distinctGames", 3), s)); // valorant is under 100 m
        Assert.Equal(12_000, AchievementEngine.CurrentValue(Def("totalClicks", 10_000), s));
        Assert.Equal(0, AchievementEngine.CurrentValue(Def("unknownType", 1), s));
    }

    [Fact]
    public void Evaluate_KeepsEarlierUnlocks()
    {
        var def = new AchievementDefinition("d", "totalDistance", 1000, "E7C1", null);
        var at = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var progress = AchievementEngine.Evaluate([def], Snapshot(total: 10), new Dictionary<string, DateTime> { ["d"] = at }).Single();
        Assert.True(progress.IsUnlocked);
        Assert.Equal(0.01, progress.Fraction, precision: 9);
    }
}

public sealed class AchievementServiceAndGearTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 3);
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"aimodometer-fun-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(f);
        }
    }

    private static readonly GameCatalog Catalog = new(
        [new SteamApp(730, "Counter-Strike 2", @"C:\Steam\steamapps\common\cs")], BuiltInGameList.LoadEmbedded());

    private static (StatsStore Store, long Mouse) Seed(string path)
    {
        var store = StatsStore.Open(path);
        var p = DevicePath.Parse(@"\\?\HID#VID_046D&PID_C54D&MI_00#a&1&0&0000#{378de44c-56ef-11d1-bc8c-00a0c91405dd}");
        var mouse = store.UpsertDevice(p.StableKey, p, DeviceKind.Mouse, null, DateTime.UtcNow).Id;
        var cs = store.GetOrCreateApp(@"C:\Steam\steamapps\common\cs\cs2.exe", DateTime.UtcNow);

        // 1.6 km over two days (800 DPI: 800 counts = 2.54 cm), 0.6 km of it in CS2 at 3 am.
        void Add(DateOnly date, int hour, long app, double meters) => store.WriteHour(new HourKey(date, hour, 180),
            [new UsageDelta(0, mouse, app, 800, new UsageBucket { PathCounts = meters * 100 / 2.54 * 800, ClicksLeft = 10, Dirty = true })]);
        Add(Today.AddDays(-1), 15, 0, 1000);
        Add(Today, 3, cs, 600);
        return (store, mouse);
    }

    [Fact]
    public void Snapshot_SummarizesTheDatabase()
    {
        var (store, _) = Seed(_path);
        using (store)
        {
            var s = AchievementService.BuildSnapshot(store, Catalog, Today);
            Assert.Equal(1600, s.TotalMeters, precision: 6);
            Assert.Equal(1000, s.BestDayMeters, precision: 6);
            Assert.Equal(2, s.LongestStreakDays);
            Assert.Equal(600, s.GameMeters["steam:730"], precision: 6);
            Assert.Equal(600, s.BestNightMeters, precision: 6);
            Assert.Equal(20, s.TotalClicks);
            Assert.Equal(2, s.ActiveDays);
        }
    }

    [Fact]
    public void UnlockNew_StoresEachAchievementOnce()
    {
        var (store, _) = Seed(_path);
        using (store)
        {
            var first = AchievementService.UnlockNew(store, Catalog, Today, DateTime.UtcNow);
            var second = AchievementService.UnlockNew(store, Catalog, Today, DateTime.UtcNow);

            Assert.Contains(first, a => a.Id == "distance-1km");
            Assert.Contains(first, a => a.Id == "night-500m");
            Assert.DoesNotContain(first, a => a.Id == "distance-5km");
            Assert.Empty(second);
            Assert.All(store.GetAchievements().Values, v => Assert.False(v.Notified));

            store.MarkAchievementsNotified(first.Select(a => a.Id));
            Assert.All(store.GetAchievements().Values, v => Assert.True(v.Notified));
        }
    }

    [Fact]
    public void GearWear_PadCountsAllMiceGlidesOnlyTheirMouse()
    {
        var (store, mouse) = Seed(_path);
        using (store)
        {
            var padId = store.SaveGear(new GearItem(0, GearKind.MousePad, "Pad", null, Today, 1, null));
            var glides = new GearItem(0, GearKind.Glides, "Glides", mouse + 99, Today.AddDays(-5), 250, null); // another mouse

            var pad = GearWear.Status(store, store.GetGear().Single(g => g.Id == padId));
            Assert.Equal(0.6, pad.KilometersUsed, precision: 6); // only today's 600 m (started today)
            Assert.Equal(0.6, pad.Fraction, precision: 6);
            Assert.False(pad.TimeToReplace);
            Assert.Equal(0, GearWear.Status(store, glides).KilometersUsed);

            var sleeve = new GearItem(0, GearKind.Sleeve, "Рукав", mouse + 99, Today, 1500, null); // device is ignored
            Assert.Equal(0.6, GearWear.Status(store, sleeve).KilometersUsed, precision: 6);

            var retired = GearWear.Status(store, new GearItem(1, GearKind.MousePad, "Old", null, Today.AddDays(-1), 1, Today.AddDays(-1)));
            Assert.Equal(1.0, retired.KilometersUsed, precision: 6); // yesterday only
            Assert.False(retired.TimeToReplace); // retired items never nag
        }
    }

    [Fact]
    public void Gear_SaveUpdateDelete()
    {
        using var store = StatsStore.Open(_path);
        var id = store.SaveGear(new GearItem(0, GearKind.MousePad, "Artisan", null, Today, 800, null));
        store.SaveGear(new GearItem(id, GearKind.MousePad, "Artisan Zero Мягкий", null, Today, 900, Today));
        var item = Assert.Single(store.GetGear());
        Assert.Equal("Artisan Zero Мягкий", item.Name);
        Assert.Equal(Today, item.RetiredOn);
        store.DeleteGear(id);
        Assert.Empty(store.GetGear());
    }
}
