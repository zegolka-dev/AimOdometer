using System.Runtime.CompilerServices;

namespace AimOdometer.Core.Input;

/// <summary>Per-hour totals of one device in one foreground app, as kept in memory between flushes.</summary>
public struct UsageBucket
{
    public double PathCounts { get; set; }
    public double XCounts { get; set; }
    public double YCounts { get; set; }
    public long AbsoluteEvents { get; set; }
    public long ClicksLeft { get; set; }
    public long ClicksRight { get; set; }
    public long ClicksMiddle { get; set; }
    public long ClicksX1 { get; set; }
    public long ClicksX2 { get; set; }
    public double WheelNotches { get; set; }
    public int MoveSeconds { get; set; }
    public int ForegroundSeconds { get; set; }
    public double PeakSpeed { get; set; }
    public bool Dirty { get; set; }
}

/// <summary>A bucket ready to be written to the database.</summary>
public readonly record struct UsageDelta(int DeviceSlot, long DeviceId, long AppId, double Dpi, UsageBucket Bucket);

/// <summary>
/// Accumulates raw mouse input in memory. The hot path (<see cref="ProcessRawInputBuffer"/>) performs no allocations:
/// all storage is preallocated and devices are resolved once through a callback.
/// Not thread-safe: call it only from the tracker's message-loop thread.
/// </summary>
public sealed unsafe class InputAccumulator
{
    public const int MaxDevices = 16;
    public const int MaxApps = 32;

    // RAWINPUTHEADER / RAWMOUSE layout on 64-bit Windows.
    private const int HeaderSize = 24;
    private const uint RimTypeMouse = 0;
    private const ushort MouseMoveAbsolute = 0x0001;
    private const ushort ButtonLeftDown = 0x0001;
    private const ushort ButtonRightDown = 0x0004;
    private const ushort ButtonMiddleDown = 0x0010;
    private const ushort ButtonX1Down = 0x0040;
    private const ushort ButtonX2Down = 0x0100;
    private const ushort MouseWheel = 0x0400;
    private const double WheelDelta = 120.0;

    private readonly Func<nint, int> _resolveDevice;
    private readonly long _ticksPerSecond;

    // Raw input device handles seen so far and the slot each maps to (-1 = ignored device).
    private const int MaxHandles = MaxDevices * 2;
    private readonly nint[] _deviceHandles = new nint[MaxHandles];
    private readonly int[] _handleSlots = new int[MaxHandles];
    private readonly DeviceState[] _devices = new DeviceState[MaxDevices];
    private readonly FlickWindow[] _flick = new FlickWindow[MaxDevices];
    private readonly double[] _batchPath = new double[MaxDevices];
    private readonly long[] _appIds = new long[MaxApps];
    private readonly UsageBucket[] _buckets = new UsageBucket[MaxDevices * MaxApps];
    private int _handleCount;
    private int _appCount;
    private int _currentApp;

    /// <param name="resolveDevice">
    /// Called (rarely, outside the steady state) for an unseen device handle. Must call <see cref="ConfigureDevice"/>
    /// for the returned slot and return it, or return -1 to ignore the device.
    /// </param>
    public InputAccumulator(Func<nint, int> resolveDevice, long ticksPerSecond)
    {
        _resolveDevice = resolveDevice;
        _ticksPerSecond = ticksPerSecond;
        for (var i = 0; i < MaxDevices; i++)
        {
            _flick[i] = new FlickWindow(FlickWindow.DefaultWindow, ticksPerSecond);
        }

        _appIds[0] = 0;
        _appCount = 1;
    }

    private struct DeviceState
    {
        public bool InUse;
        public long DeviceId;
        public double Dpi;
        public bool CountsTowardTotals;
        public long LastMoveSecond;
        public bool TouchedInBatch;
        public double BestSpeedSinceStart;
    }

    /// <summary>Total raw input reports processed (diagnostics).</summary>
    public long EventsProcessed { get; private set; }

    /// <summary>Total batches processed (diagnostics).</summary>
    public long BatchesProcessed { get; private set; }

    /// <summary>Optional latency probe for injected input tagged by tools/InputSimulator (diagnostics only).</summary>
    public bool MeasureInjectedLatency { get; set; }

    public long LatencySamples { get; private set; }

    public long LatencyTicksTotal { get; private set; }

    public long LatencyTicksMax { get; private set; }

    /// <summary>Sets device metadata for a slot. Call from the resolve callback or when settings change.</summary>
    public void ConfigureDevice(int slot, long deviceId, double dpi, bool countsTowardTotals)
    {
        ref var device = ref _devices[slot];
        device.InUse = true;
        device.DeviceId = deviceId;
        device.Dpi = dpi;
        device.CountsTowardTotals = countsTowardTotals;
        device.LastMoveSecond = -1;
    }

    /// <summary>Returns a free slot index, or -1 when all <see cref="MaxDevices"/> slots are used.</summary>
    public int AllocateSlot()
    {
        for (var i = 0; i < MaxDevices; i++)
        {
            if (!_devices[i].InUse)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Finds the slot already holding a database device id, or -1.</summary>
    public int FindSlotByDeviceId(long deviceId)
    {
        for (var i = 0; i < MaxDevices; i++)
        {
            if (_devices[i].InUse && _devices[i].DeviceId == deviceId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Forgets all handle-to-slot mappings (handles change when devices are re-plugged).</summary>
    public void ForgetHandles() => _handleCount = 0;

    public double GetDpi(int slot) => _devices[slot].Dpi;

    /// <summary>Changes the DPI used for future movement of a device. Pending movement must be flushed first.</summary>
    public void SetDpi(int slot, double dpi) => _devices[slot].Dpi = dpi;

    /// <summary>
    /// Makes <paramref name="appId"/> the target of further input. Returns false when the per-flush app table is full;
    /// the caller must then flush (<see cref="Drain"/>) and call again.
    /// </summary>
    public bool SetForegroundApp(long appId)
    {
        for (var i = 0; i < _appCount; i++)
        {
            if (_appIds[i] == appId)
            {
                _currentApp = i;
                return true;
            }
        }

        if (_appCount == MaxApps)
        {
            return false;
        }

        _appIds[_appCount] = appId;
        _currentApp = _appCount++;
        return true;
    }

    /// <summary>Processes <paramref name="count"/> RAWINPUT records laid out as returned by GetRawInputBuffer.</summary>
    public void ProcessRawInputBuffer(byte* buffer, int count, long timestamp)
    {
        var offset = 0;
        for (var i = 0; i < count; i++)
        {
            var record = buffer + offset;
            var size = *(uint*)(record + 4);
            ProcessRecord(record, timestamp);

            // NEXTRAWINPUTBLOCK: records are aligned to 8 bytes on 64-bit Windows.
            offset = (int)((offset + size + 7) & ~7u);
        }

        FinishBatch(timestamp);
    }

    /// <summary>Processes a single RAWINPUT record as returned by GetRawInputData.</summary>
    public void ProcessSingleRawInput(byte* record, long timestamp)
    {
        ProcessRecord(record, timestamp);
        FinishBatch(timestamp);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ProcessRecord(byte* record, long timestamp)
    {
        if (*(uint*)record != RimTypeMouse)
        {
            return;
        }

        var handle = *(nint*)(record + 8);
        var slot = SlotFor(handle);
        if (slot < 0)
        {
            return;
        }

        EventsProcessed++;
        var mouse = record + HeaderSize;
        var flags = *(ushort*)mouse;
        var buttons = *(ushort*)(mouse + 4);
        ref var bucket = ref _buckets[(slot * MaxApps) + _currentApp];

        if ((flags & MouseMoveAbsolute) != 0)
        {
            // Tablets, RDP and some virtual devices report positions, not physical travel.
            bucket.AbsoluteEvents++;
            bucket.Dirty = true;
        }
        else
        {
            var dx = *(int*)(mouse + 12);
            var dy = *(int*)(mouse + 16);
            if ((dx | dy) != 0)
            {
                var path = Distance.PathCounts(dx, dy);
                bucket.PathCounts += path;
                bucket.XCounts += Math.Abs((double)dx);
                bucket.YCounts += Math.Abs((double)dy);
                bucket.Dirty = true;
                _batchPath[slot] += path;
                _devices[slot].TouchedInBatch = true;

                var second = timestamp / _ticksPerSecond;
                if (second != _devices[slot].LastMoveSecond)
                {
                    _devices[slot].LastMoveSecond = second;
                    bucket.MoveSeconds++;
                }
            }
        }

        if (buttons != 0)
        {
            if ((buttons & ButtonLeftDown) != 0)
            {
                bucket.ClicksLeft++;
            }

            if ((buttons & ButtonRightDown) != 0)
            {
                bucket.ClicksRight++;
            }

            if ((buttons & ButtonMiddleDown) != 0)
            {
                bucket.ClicksMiddle++;
            }

            if ((buttons & ButtonX1Down) != 0)
            {
                bucket.ClicksX1++;
            }

            if ((buttons & ButtonX2Down) != 0)
            {
                bucket.ClicksX2++;
            }

            if ((buttons & MouseWheel) != 0)
            {
                bucket.WheelNotches += Math.Abs(*(short*)(mouse + 6) / WheelDelta);
            }

            bucket.Dirty = true;
        }

        if (handle == 0 && MeasureInjectedLatency)
        {
            var sentAt = *(uint*)(mouse + 20);
            if (sentAt != 0)
            {
                // Signed 32-bit difference handles wrap-around; events generated after our timestamp count as 0.
                var latency = Math.Max(0L, unchecked((int)((uint)timestamp - sentAt)));
                LatencySamples++;
                LatencyTicksTotal += latency;
                LatencyTicksMax = Math.Max(LatencyTicksMax, latency);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int SlotFor(nint handle)
    {
        for (var i = 0; i < _handleCount; i++)
        {
            if (_deviceHandles[i] == handle)
            {
                return _handleSlots[i];
            }
        }

        return RegisterHandle(handle);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int RegisterHandle(nint handle)
    {
        if (_handleCount == MaxHandles)
        {
            // Many re-plugs: start over; live handles are re-resolved on their next event.
            _handleCount = 0;
        }

        var slot = _resolveDevice(handle);
        _deviceHandles[_handleCount] = handle;
        _handleSlots[_handleCount] = slot;
        _handleCount++;
        return slot;
    }

    private void FinishBatch(long timestamp)
    {
        BatchesProcessed++;
        for (var slot = 0; slot < MaxDevices; slot++)
        {
            ref var device = ref _devices[slot];
            if (!device.TouchedInBatch)
            {
                continue;
            }

            device.TouchedInBatch = false;
            var speed = _flick[slot].Add(timestamp, _batchPath[slot]);
            _batchPath[slot] = 0;

            ref var bucket = ref _buckets[(slot * MaxApps) + _currentApp];
            if (speed > bucket.PeakSpeed)
            {
                bucket.PeakSpeed = speed;
            }

            if (speed > device.BestSpeedSinceStart)
            {
                device.BestSpeedSinceStart = speed;
            }
        }
    }

    /// <summary>Clears flick history (after pause, sleep, or a long gap) so old movement does not leak into new speed.</summary>
    public void ResetFlickWindows()
    {
        foreach (var window in _flick)
        {
            window.Reset();
        }
    }

    /// <summary>Adds foreground time to the current app for every active device slot (used from phase 3).</summary>
    public void AddForegroundSeconds(int seconds, int slot)
    {
        ref var bucket = ref _buckets[(slot * MaxApps) + _currentApp];
        bucket.ForegroundSeconds += seconds;
        bucket.Dirty = true;
    }

    /// <summary>Centimeters accumulated in memory and not yet flushed, for devices that count toward totals.</summary>
    public double PendingCentimeters()
    {
        var total = 0.0;
        for (var slot = 0; slot < MaxDevices; slot++)
        {
            ref var device = ref _devices[slot];
            if (!device.InUse || !device.CountsTowardTotals || device.Dpi <= 0)
            {
                continue;
            }

            for (var app = 0; app < _appCount; app++)
            {
                total += Distance.CountsToCentimeters(_buckets[(slot * MaxApps) + app].PathCounts, device.Dpi);
            }
        }

        return total;
    }

    /// <summary>True when there is unflushed data.</summary>
    public bool HasPendingData()
    {
        foreach (ref readonly var bucket in _buckets.AsSpan())
        {
            if (bucket.Dirty)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Moves all pending buckets into <paramref name="output"/> and clears them. Keeps the current app selected.
    /// Allocation here is fine: it runs once a minute, not per input event.
    /// </summary>
    public void Drain(List<UsageDelta> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        for (var slot = 0; slot < MaxDevices; slot++)
        {
            ref var device = ref _devices[slot];
            for (var app = 0; app < _appCount; app++)
            {
                ref var bucket = ref _buckets[(slot * MaxApps) + app];
                if (!bucket.Dirty)
                {
                    continue;
                }

                if (device.InUse)
                {
                    output.Add(new UsageDelta(slot, device.DeviceId, _appIds[app], device.Dpi, bucket));
                }

                bucket = default;
            }
        }

        // Compact the app table to just the current app so it never fills up over time.
        var current = _appIds[_currentApp];
        _appIds[0] = current;
        _appCount = 1;
        _currentApp = 0;
    }
}
