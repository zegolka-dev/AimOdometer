using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using AimOdometer.Core.Ipc;

namespace AimOdometer.Tools.PerfProbe;

/// <summary>
/// Samples CPU and memory of the running tracker and reads its counters over the pipe.
/// Usage: PerfProbe [--seconds 30] [--label "8000 Hz background"] | --flush | --db path	oimodometer.db
/// CPU is reported as percent of ONE core (100% = one core fully busy).
/// </summary>
internal static unsafe partial class Program
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx2
    {
        public uint Cb;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
        public nuint PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryProcessCycleTime(nint process, ulong* cycles);

    /// <summary>Nominal CPU clock from the registry, used to turn cycles into "percent of one core".</summary>
    private static double CpuHz()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return (key?.GetValue("~MHz") is int mhz && mhz > 0 ? mhz : 3000) * 1_000_000.0;
    }

    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessMemoryInfo(nint process, ProcessMemoryCountersEx2* counters, uint size);

    private static int Main(string[] args)
    {
        if (args is ["--db", var dbPath, ..])
        {
            return DumpDatabase(dbPath);
        }

        if (args is ["--flush", ..])
        {
            return TrackerClient.Send(TrackerCommand.Flush) is [TrackerProtocol.StatusOk, ..] ? 0 : 1;
        }

        var seconds = args.SkipWhile(a => a != "--seconds").Skip(1).Select(a => int.Parse(a, CultureInfo.InvariantCulture)).FirstOrDefault(30);
        var label = args.SkipWhile(a => a != "--label").Skip(1).FirstOrDefault() ?? "run";

        // Two AimOdometer.Tracker processes run (supervisor + worker); measure the worker that answers the pipe.
        if (TrackerClient.GetProcessId() is not { } pid)
        {
            Console.Error.WriteLine("AimOdometer.Tracker is not running.");
            return 1;
        }

        using var tracker = Process.GetProcessById(pid);

        var before = TrackerClient.GetStatus();
        var cpuSamples = new List<double>();
        var privateWs = new List<double>();
        var privateBytes = new List<double>();
        var lastCpu = tracker.TotalProcessorTime;
        var lastWall = Stopwatch.GetTimestamp();
        var cpuHz = CpuHz();
        ulong cyclesStart;
        QueryProcessCycleTime(tracker.Handle, &cyclesStart);
        var wallStart = lastWall;

        for (var i = 0; i < seconds; i++)
        {
            Thread.Sleep(1000);
            tracker.Refresh();
            var cpu = tracker.TotalProcessorTime;
            var wall = Stopwatch.GetTimestamp();
            cpuSamples.Add((cpu - lastCpu).TotalMilliseconds / Stopwatch.GetElapsedTime(lastWall, wall).TotalMilliseconds * 100);
            lastCpu = cpu;
            lastWall = wall;

            var counters = new ProcessMemoryCountersEx2 { Cb = (uint)sizeof(ProcessMemoryCountersEx2) };
            if (GetProcessMemoryInfo(tracker.Handle, &counters, counters.Cb))
            {
                privateWs.Add(counters.PrivateWorkingSetSize / 1048576.0);
                privateBytes.Add(counters.PrivateUsage / 1048576.0);
            }
        }

        ulong cyclesEnd;
        QueryProcessCycleTime(tracker.Handle, &cyclesEnd);
        var cyclePercent = (cyclesEnd - cyclesStart) / cpuHz / Stopwatch.GetElapsedTime(wallStart).TotalSeconds * 100;

        var after = TrackerClient.GetStatus();
        var events = after is not null && before is not null ? after.EventsProcessed - before.EventsProcessed : 0;
        var batches = after is not null && before is not null ? after.WakeUps - before.WakeUps : 0;
        var allocated = after is not null && before is not null ? after.ManagedBytesAllocated - before.ManagedBytesAllocated : 0;

        static string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        Console.WriteLine($"Label:                {label}");
        Console.WriteLine($"Duration:             {seconds} s");
        Console.WriteLine($"CPU cycles (% core):  {cyclePercent:0.000} (precise; {cpuHz / 1e9:0.0} GHz nominal)");
        Console.WriteLine($"CPU time (% core):    avg {F(cpuSamples.Average())}, max {F(cpuSamples.Max())} (coarse, 15.6 ms ticks)");
        Console.WriteLine($"Private working set:  avg {F(privateWs.DefaultIfEmpty().Average())} MB, max {F(privateWs.DefaultIfEmpty().Max())} MB");
        Console.WriteLine($"Private bytes:        max {F(privateBytes.DefaultIfEmpty().Max())} MB");
        Console.WriteLine($"Events received:      {events:N0} ({F(events / (double)seconds)} /s)");
        Console.WriteLine($"Batches (wake-ups):   {batches:N0} ({F(batches / (double)seconds)} /s)");
        Console.WriteLine($"Managed allocations:  {allocated:N0} bytes");
        if (after is { LatencySamples: > 0 })
        {
            Console.WriteLine($"Injected latency:     avg {F(after.LatencyAverageMicroseconds)} us, max {F(after.LatencyMaxMicroseconds)} us ({after.LatencySamples:N0} samples, cumulative)");
        }

        Console.WriteLine();
        Console.WriteLine("| Scenario | CPU % of one core (cycles) | Private WS max MB | Events/s | Wake-ups/s | Alloc bytes |");
        Console.WriteLine($"| {label} | {cyclePercent:0.000} | {F(privateWs.DefaultIfEmpty().Max())} | {F(events / (double)seconds)} | {F(batches / (double)seconds)} | {allocated:N0} |");
        return 0;
    }

    /// <summary>Prints per-device totals from a tracker database (read-only).</summary>
    private static int DumpDatabase(string path)
    {
        using var db = AimOdometer.Core.Storage.SqliteDatabase.Open(path, readOnly: true);
        using var rows = db.Prepare("""
            SELECT d.id, coalesce(d.display_name, d.product_name, d.device_key), d.kind, d.excluded,
                   sum(h.path_counts), sum(h.path_counts / h.dpi) * 2.54, sum(h.abs_events),
                   sum(h.clicks_left + h.clicks_right + h.clicks_middle + h.clicks_x1 + h.clicks_x2),
                   sum(h.wheel_notches), max(h.peak_speed / h.dpi * 2.54), sum(h.move_seconds), count(*)
            FROM devices d LEFT JOIN hourly h ON h.device_id = d.id
            GROUP BY d.id ORDER BY d.id;
            """);
        Console.WriteLine("| Id | Device | Kind | Excluded | Path counts | cm | Abs events | Clicks | Wheel | Peak cm/s | Move s | Rows |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        while (rows.Step())
        {
            static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
            Console.WriteLine($"| {rows.GetInt64(0)} | {rows.GetString(1)} | {rows.GetInt64(2)} | {rows.GetInt64(3)} | {N(rows.GetDouble(4))} | {N(rows.GetDouble(5))} | {rows.GetInt64(6)} | {rows.GetInt64(7)} | {N(rows.GetDouble(8))} | {N(rows.GetDouble(9))} | {rows.GetInt64(10)} | {rows.GetInt64(11)} |");
        }

        return 0;
    }
}
