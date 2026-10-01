using AimOdometer.Core.Input;

namespace AimOdometer.Core.Tests;

public class FlickWindowTests
{
    private const long TicksPerSecond = 1_000_000; // 1 tick = 1 microsecond

    [Fact]
    public void SteadyMovement_ReportsItsSpeed()
    {
        var window = new FlickWindow(TimeSpan.FromMilliseconds(50), TicksPerSecond);
        double speed = 0;

        // 8 ms samples (Windows 11 background rate), 80 counts each = 10 000 counts/s.
        for (var t = 0; t <= 200_000; t += 8_000)
        {
            speed = window.Add(t, 80);
        }

        Assert.Equal(10_000, speed, tolerance: 1);
    }

    [Fact]
    public void LateBatch_IsNotASpike()
    {
        var window = new FlickWindow(TimeSpan.FromMilliseconds(50), TicksPerSecond);
        for (var t = 0; t <= 96_000; t += 8_000)
        {
            window.Add(t, 80);
        }

        // The thread was busy: the next batch arrives 48 ms later and holds 48 ms of the same 10 000 counts/s movement.
        var speed = window.Add(144_000, 480);
        Assert.Equal(10_000, speed, tolerance: 200);
    }

    [Fact]
    public void FirstSampleAfterPause_DoesNotExceedTrueSpeed()
    {
        var window = new FlickWindow(TimeSpan.FromMilliseconds(50), TicksPerSecond);
        window.Add(0, 80);
        var speed = window.Add(5_000_000, 80); // 5 s later
        Assert.True(speed <= 10_000);
    }

    [Fact]
    public void Reset_ForgetsHistory()
    {
        var window = new FlickWindow(TimeSpan.FromMilliseconds(50), TicksPerSecond);
        window.Add(0, 1_000_000);
        window.Reset();
        Assert.Equal(80 * TicksPerSecond / 50_000.0, window.Add(10, 80), precision: 6);
    }
}
