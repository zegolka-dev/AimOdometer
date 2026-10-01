using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace AimOdometer.Tools.InputSimulator;

/// <summary>
/// Generates synthetic relative mouse movement with SendInput at a fixed rate, for stress tests and accuracy checks.
/// The cursor returns to where it started (patterns are closed loops).
/// Usage: InputSimulator [--rate 8000] [--seconds 10] [--pattern line|circle] [--step 4] [--tag-latency]
/// </summary>
internal static unsafe partial class Program
{
    private const uint InputMouse = 0;
    private const uint MouseEventFMove = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse; // MOUSEINPUT is the largest union member, so sizeof(Input) == 40 on x64
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint count, Input* inputs, int size);

    [LibraryImport("winmm.dll")]
    private static partial uint timeBeginPeriod(uint period);

    [LibraryImport("winmm.dll")]
    private static partial uint timeEndPeriod(uint period);

    private static int Main(string[] args)
    {
        var rate = IntArg(args, "--rate", 1000);
        var seconds = DoubleArg(args, "--seconds", 10);
        var step = IntArg(args, "--step", 4);
        var pattern = StringArg(args, "--pattern", "line");
        var tagLatency = args.Contains("--tag-latency");

        if (rate <= 0 || seconds <= 0 || step <= 0 || pattern is not ("line" or "circle"))
        {
            Console.Error.WriteLine("Usage: InputSimulator [--rate 8000] [--seconds 10] [--pattern line|circle] [--step 4] [--tag-latency]");
            return 1;
        }

        var moves = BuildPattern(pattern, step);
        var total = (long)(rate * seconds);

        // SendInput manages only a few thousand calls per second, so above 1000 Hz several reports are sent per call
        // every millisecond. To the receiver this looks like a fast mouse whose reports arrive in 1 ms bursts.
        var perCall = Math.Max(1, (int)Math.Ceiling(rate / 1000.0));
        var interval = Stopwatch.Frequency * perCall / (double)rate;
        var expectedPath = 0.0;
        var sent = 0L;
        var inputs = new Input[perCall];

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Sending {total:N0} moves at {rate} Hz for {seconds} s, pattern {pattern}, step {step}..."));
        _ = timeBeginPeriod(1);
        Thread.CurrentThread.Priority = ThreadPriority.Highest;
        var start = Stopwatch.GetTimestamp();
        try
        {
            for (var call = 0L; call * perCall < total; call++)
            {
                var due = start + (long)(call * interval);
                while (Stopwatch.GetTimestamp() < due)
                {
                    Thread.SpinWait(20);
                }

                var count = (int)Math.Min(perCall, total - (call * perCall));
                var tag = tagLatency ? (nuint)(uint)Stopwatch.GetTimestamp() : 0;
                for (var k = 0; k < count; k++)
                {
                    var (dx, dy) = moves[(int)(((call * perCall) + k) % moves.Length)];
                    inputs[k] = new Input { Type = InputMouse, Mouse = new MouseInput { Dx = dx, Dy = dy, Flags = MouseEventFMove, ExtraInfo = tag } };
                }

                fixed (Input* p = inputs)
                {
                    var accepted = SendInput((uint)count, p, sizeof(Input));
                    for (var k = 0; k < accepted; k++)
                    {
                        sent++;
                        expectedPath += Math.Sqrt(((double)p[k].Mouse.Dx * p[k].Mouse.Dx) + ((double)p[k].Mouse.Dy * p[k].Mouse.Dy));
                    }
                }
            }
        }
        finally
        {
            _ = timeEndPeriod(1);
        }

        var elapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Sent {sent:N0} moves in {elapsed:0.00} s ({sent / elapsed:N0} Hz). Expected path: {expectedPath:F1} counts"));
        return sent == total ? 0 : 2;
    }

    /// <summary>A closed loop of small relative moves, so the cursor ends where it started.</summary>
    private static (int Dx, int Dy)[] BuildPattern(string pattern, int step)
    {
        if (pattern == "line")
        {
            // 50 moves right, then 50 moves left.
            return [.. Enumerable.Repeat((step, 0), 50), .. Enumerable.Repeat((-step, 0), 50)];
        }

        // Circle approximated by 64 integer chords whose sum is exactly zero.
        const int Segments = 64;
        var radius = step * Segments / (2 * Math.PI);
        var points = Enumerable.Range(0, Segments + 1)
            .Select(i => (X: (int)Math.Round(radius * Math.Cos(2 * Math.PI * i / Segments)),
                          Y: (int)Math.Round(radius * Math.Sin(2 * Math.PI * i / Segments))))
            .ToArray();
        return [.. Enumerable.Range(0, Segments).Select(i => (points[i + 1].X - points[i].X, points[i + 1].Y - points[i].Y))];
    }

    private static string? Find(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int IntArg(string[] args, string name, int fallback) =>
        int.TryParse(Find(args, name), CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static double DoubleArg(string[] args, string name, double fallback) =>
        double.TryParse(Find(args, name), CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static string StringArg(string[] args, string name, string fallback) => Find(args, name) ?? fallback;
}
