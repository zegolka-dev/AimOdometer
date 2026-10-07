using System.Diagnostics;
using AimOdometer.Core.Input;
using AimOdometer.Core.Storage;
using AimOdometer.Win32;

namespace AimOdometer.Tracker.Tests;

public sealed class ForegroundRecheckTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aimodometer-fg-" + Guid.NewGuid().ToString("N"));

    public ForegroundRecheckTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void RecheckFindsTheForegroundWindowEvenWithoutAHookEvent()
    {
        // The hook is never installed (no Start), as if Windows had not reported the change at all.
        if (WinEvents.GetForegroundWindow() == 0)
        {
            return; // no interactive desktop (service session): nothing to find
        }

        using var store = StatsStore.Open(Path.Combine(_dir, "aimodometer.db"));
        var accumulator = new InputAccumulator(_ => -1, Stopwatch.Frequency);
        using var tracker = new ForegroundTracker(store, accumulator, () => { });
        Assert.Equal(0, tracker.CurrentAppId);

        tracker.Recheck();

        Assert.NotEqual(0, tracker.CurrentAppId);
        Assert.Contains(store.GetApps(), a => a.Id == tracker.CurrentAppId);
    }
}

public sealed class ElevationTests
{
    [Fact]
    public void ThisProcessIsNotSeenAsElevatedUnlessItIs()
    {
        var self = (uint)Environment.ProcessId;
        Assert.Equal(AimOdometer.Core.ElevatedTask.IsElevated, ForegroundTracker.IsElevatedProcess(self));
    }

    [Fact]
    public void AnElevatedTrackerIsSeenAsElevatedFromANormalProcess()
    {
        // Runs where an AimOdometer tracker started with administrator rights is running (the author's PC).
        if (AimOdometer.Core.ElevatedTask.IsElevated)
        {
            return;
        }

        foreach (var tracker in Process.GetProcessesByName("AimOdometer.Tracker"))
        {
            using (tracker)
            {
                bool elevatedByPath;
                try
                {
                    _ = tracker.MainModule; // a normal process may not open an elevated one this way
                    elevatedByPath = false;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    elevatedByPath = true;
                }

                if (elevatedByPath)
                {
                    Assert.True(ForegroundTracker.IsElevatedProcess((uint)tracker.Id));
                }
            }
        }
    }

    [Fact]
    public void ANormalForegroundWindowIsTakenAsItIs()
    {
        var hwnd = WinEvents.GetForegroundWindow();
        if (hwnd == 0)
        {
            return;
        }

        var dir = Path.Combine(Path.GetTempPath(), "aimodometer-os-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using (var store = StatsStore.Open(Path.Combine(dir, "aimodometer.db")))
            using (var tracker = new ForegroundTracker(store, new InputAccumulator(_ => -1, Stopwatch.Frequency), () => { }))
            {
                var onScreen = tracker.OnScreen(hwnd);
                Assert.NotEqual(0, onScreen);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
