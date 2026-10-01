namespace AimOdometer.Core.Input;

/// <summary>
/// Sliding window over recent movement used to measure peak "flick" speed without allocations.
/// Each sample is the path moved since the previous sample plus the time it covers, so a batch that
/// arrives late (e.g. after a disk flush) is not mistaken for a speed spike.
/// </summary>
public sealed class FlickWindow
{
    private const int Capacity = 128;

    private readonly long[] _durations = new long[Capacity];
    private readonly double[] _paths = new double[Capacity];
    private readonly long _windowTicks;
    private readonly double _ticksPerSecond;
    private int _head;
    private int _count;
    private long _sumDuration;
    private double _sumPath;
    private long _lastTimestamp = long.MinValue;

    /// <param name="window">Window length; 50 ms holds 6+ samples even with Windows 11 background coalescing (~8 ms).</param>
    /// <param name="ticksPerSecond">Timestamp frequency (e.g. <see cref="System.Diagnostics.Stopwatch.Frequency"/>).</param>
    public FlickWindow(TimeSpan window, long ticksPerSecond)
    {
        _ticksPerSecond = ticksPerSecond;
        _windowTicks = (long)(window.TotalSeconds * ticksPerSecond);
    }

    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Adds movement observed at <paramref name="timestamp"/> and returns the speed over the window in counts per second,
    /// or 0 while the window is not yet filled (too little time covered to be meaningful).
    /// </summary>
    public double Add(long timestamp, double path)
    {
        // The first sample after a pause covers at most one window: we do not know when the movement started.
        var duration = _lastTimestamp == long.MinValue ? _windowTicks : Math.Clamp(timestamp - _lastTimestamp, 0, _windowTicks);
        _lastTimestamp = timestamp;

        if (_count == Capacity)
        {
            RemoveOldest();
        }

        var index = (_head + _count) % Capacity;
        _durations[index] = duration;
        _paths[index] = path;
        _count++;
        _sumDuration += duration;
        _sumPath += path;

        // Keep just enough samples to cover the window.
        while (_count > 1 && _sumDuration - _durations[_head] >= _windowTicks)
        {
            RemoveOldest();
        }

        return _sumDuration >= _windowTicks && _sumDuration > 0 ? _sumPath * _ticksPerSecond / _sumDuration : 0;
    }

    /// <summary>Forgets history, e.g. after a pause or sleep.</summary>
    public void Reset()
    {
        _head = 0;
        _count = 0;
        _sumDuration = 0;
        _sumPath = 0;
        _lastTimestamp = long.MinValue;
    }

    private void RemoveOldest()
    {
        _sumDuration -= _durations[_head];
        _sumPath -= _paths[_head];
        _head = (_head + 1) % Capacity;
        _count--;
        if (_count == 0)
        {
            // Avoid floating-point drift accumulating over long sessions.
            _sumPath = 0;
            _sumDuration = 0;
        }
    }
}
