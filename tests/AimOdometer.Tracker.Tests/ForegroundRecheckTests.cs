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
