using AimOdometer.Core.Input;

namespace AimOdometer.Core.Tests;

public unsafe class InputAccumulatorTests
{
    private const long TicksPerSecond = 1_000_000;
    private static readonly nint MouseA = 0x1000;
    private static readonly nint MouseB = 0x2000;

    private static InputAccumulator CreateAccumulator(out List<nint> resolved)
    {
        var seen = new List<nint>();
        resolved = seen;
        InputAccumulator? accumulator = null;
        accumulator = new InputAccumulator(
            handle =>
            {
                seen.Add(handle);
                var slot = accumulator!.AllocateSlot();
                accumulator.ConfigureDevice(slot, deviceId: 100 + slot, dpi: 800, countsTowardTotals: handle != 0);
                return slot;
            },
            TicksPerSecond);
        return accumulator;
    }

    private static void Process(InputAccumulator accumulator, RawInputBuilder builder, long timestamp)
    {
        var bytes = builder.Build();
        fixed (byte* p = bytes)
        {
            accumulator.ProcessRawInputBuffer(p, builder.Count, timestamp);
        }
    }

    private static List<UsageDelta> Drain(InputAccumulator accumulator)
    {
        var list = new List<UsageDelta>();
        accumulator.Drain(list);
        return list;
    }

    [Fact]
    public void RelativeMoves_SumEuclideanPathPerDevice()
    {
        var acc = CreateAccumulator(out _);
        Process(acc, new RawInputBuilder().Move(MouseA, 3, 4).Move(MouseA, -6, 8).Move(MouseB, 0, 5), 1000);

        var deltas = Drain(acc);
        var a = deltas.Single(d => d.DeviceId == 100).Bucket;
        var b = deltas.Single(d => d.DeviceId == 101).Bucket;
        Assert.Equal(15, a.PathCounts, precision: 9);
        Assert.Equal(9, a.XCounts);
        Assert.Equal(12, a.YCounts);
        Assert.Equal(5, b.PathCounts, precision: 9);
    }

    [Fact]
    public void AbsoluteMoves_AreCountedSeparatelyNotAsPath()
    {
        var acc = CreateAccumulator(out _);
        Process(acc, new RawInputBuilder().Move(MouseA, 30000, 30000, flags: 0x0001), 1000);

        var bucket = Drain(acc).Single().Bucket;
        Assert.Equal(0, bucket.PathCounts);
        Assert.Equal(1, bucket.AbsoluteEvents);
    }

    [Fact]
    public void Buttons_CountPressesAndWheelNotches()
    {
        var acc = CreateAccumulator(out _);
        var builder = new RawInputBuilder()
            .Buttons(MouseA, 0x0001)          // left down
            .Buttons(MouseA, 0x0002)          // left up: not a click
            .Buttons(MouseA, 0x0004 | 0x0010) // right and middle down in one report
            .Buttons(MouseA, 0x0040)          // X1
            .Buttons(MouseA, 0x0100)          // X2
            .Buttons(MouseA, 0x0400, 120)     // wheel up one notch
            .Buttons(MouseA, 0x0400, -240)    // wheel down two notches
            .Buttons(MouseA, 0x0400, 30);     // a quarter notch from a smooth-scrolling wheel
        Process(acc, builder, 1000);

        var b = Drain(acc).Single().Bucket;
        Assert.Equal(1, b.ClicksLeft);
        Assert.Equal(1, b.ClicksRight);
        Assert.Equal(1, b.ClicksMiddle);
        Assert.Equal(1, b.ClicksX1);
        Assert.Equal(1, b.ClicksX2);
        Assert.Equal(3.25, b.WheelNotches, precision: 9);
    }

    [Fact]
    public void KeyboardRecords_AreIgnored()
    {
        var acc = CreateAccumulator(out var resolved);
        Process(acc, new RawInputBuilder().Keyboard().Move(MouseA, 1, 0), 1000);
        Assert.Single(resolved);
        Assert.Equal(1, acc.EventsProcessed);
    }

    [Fact]
    public void DevicesAreResolvedOnlyOnce()
    {
        var acc = CreateAccumulator(out var resolved);
        for (var i = 0; i < 100; i++)
        {
            Process(acc, new RawInputBuilder().Move(MouseA, 1, 1).Move(MouseB, 1, 1), i * 1000);
        }

        Assert.Equal([MouseA, MouseB], resolved);
    }

    [Fact]
    public void IgnoredDevice_IsNotCounted()
    {
        var acc = new InputAccumulator(_ => -1, TicksPerSecond);
        Process(acc, new RawInputBuilder().Move(MouseA, 10, 0), 1000);
        Assert.False(acc.HasPendingData());
    }

    [Fact]
    public void PendingCentimeters_SkipsDevicesNotCountingTowardTotals()
    {
        var acc = CreateAccumulator(out _);
        Process(acc, new RawInputBuilder().Move(MouseA, 800, 0).Move(0, 800, 0), 1000); // handle 0 = injected input
        Assert.Equal(2.54, acc.PendingCentimeters(), precision: 9);
    }

    [Fact]
    public void ForegroundApp_SplitsBuckets()
    {
        var acc = CreateAccumulator(out _);
        Process(acc, new RawInputBuilder().Move(MouseA, 10, 0), 1000);
        Assert.True(acc.SetForegroundApp(42));
        Process(acc, new RawInputBuilder().Move(MouseA, 20, 0), 2000);

        var deltas = Drain(acc);
        Assert.Equal(10, deltas.Single(d => d.AppId == 0).Bucket.PathCounts);
        Assert.Equal(20, deltas.Single(d => d.AppId == 42).Bucket.PathCounts);

        // After a drain the current app stays selected.
        Process(acc, new RawInputBuilder().Move(MouseA, 5, 0), 3000);
        Assert.Equal(42, Drain(acc).Single().AppId);
    }

    [Fact]
    public void AppTable_ReportsFullUntilDrained()
    {
        var acc = CreateAccumulator(out _);
        for (var app = 1; app < InputAccumulator.MaxApps; app++)
        {
            Assert.True(acc.SetForegroundApp(app));
        }

        Assert.False(acc.SetForegroundApp(999));
        acc.Drain([]);
        Assert.True(acc.SetForegroundApp(999));
    }

    [Fact]
    public void MoveSeconds_CountsDistinctSecondsWithMovement()
    {
        var acc = CreateAccumulator(out _);
        Process(acc, new RawInputBuilder().Move(MouseA, 1, 0), 100_000);
        Process(acc, new RawInputBuilder().Move(MouseA, 1, 0), 900_000);   // same second
        Process(acc, new RawInputBuilder().Move(MouseA, 1, 0), 1_100_000); // next second
        Process(acc, new RawInputBuilder().Move(MouseA, 1, 0), 5_000_000);
        Assert.Equal(3, Drain(acc).Single().Bucket.MoveSeconds);
    }

    [Fact]
    public void PeakSpeed_IsTrackedPerBucket()
    {
        var acc = CreateAccumulator(out _);
        for (var t = 0; t <= 200_000; t += 8_000)
        {
            Process(acc, new RawInputBuilder().Move(MouseA, 80, 0), t);
        }

        Assert.Equal(10_000, Drain(acc).Single().Bucket.PeakSpeed, tolerance: 1);
    }

    [Fact]
    public void InjectedLatency_IsMeasuredWhenEnabled()
    {
        var acc = CreateAccumulator(out _);
        acc.MeasureInjectedLatency = true;
        Process(acc, new RawInputBuilder().Move(0, 1, 0, extra: 1_000).Move(MouseA, 1, 0, extra: 1_000), 1_250);
        Assert.Equal(1, acc.LatencySamples);
        Assert.Equal(250, acc.LatencyTicksMax);
    }

    [Fact]
    public void HotPath_DoesNotAllocate()
    {
        var acc = CreateAccumulator(out _);
        var builder = new RawInputBuilder();
        for (var i = 0; i < 64; i++)
        {
            builder.Move(MouseA, i, -i).Move(MouseB, -i, i).Buttons(MouseA, 0x0001);
        }

        var bytes = builder.Build();
        fixed (byte* p = bytes)
        {
            acc.ProcessRawInputBuffer(p, builder.Count, 0); // warm-up resolves the devices
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 1; i <= 20_000; i++)
            {
                acc.ProcessRawInputBuffer(p, builder.Count, i * 125L);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
        }
    }
}
