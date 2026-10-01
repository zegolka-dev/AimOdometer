using AimOdometer.Core;

namespace AimOdometer.Tracker;

/// <summary>Command-line options of the tracker.</summary>
internal sealed record TrackerOptions(
    bool Stop,
    bool Worker,
    bool Autostarted,
    bool Restarted,
    bool MeasureLatency,
    bool EcoQos,
    string DataDirectory,
    TimeSpan BatchInterval,
    int CrashAfterSeconds,
    string[] RawArguments)
{
    /// <summary>Default minimum time between raw input batches (see docs/PERFORMANCE.md for how it was chosen).</summary>
    public static readonly TimeSpan DefaultBatchInterval = TimeSpan.FromMilliseconds(16);

    public static TrackerOptions Parse(string[] args)
    {
        var dataDirectory = AppIdentity.DataDirectory;
        var batchInterval = DefaultBatchInterval;
        var crashAfter = 0;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--data-dir")
            {
                dataDirectory = Path.GetFullPath(args[i + 1]);
            }
            else if (args[i] == "--batch-ms"
                && int.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out var ms) && ms is >= 0 and <= 100)
            {
                batchInterval = TimeSpan.FromMilliseconds(ms);
            }
            else if (args[i] == "--crash-after"
                && int.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
            {
                crashAfter = seconds;
            }
        }

        return new TrackerOptions(
            Stop: args.Contains("--stop"),
            Worker: args.Contains("--worker"),
            Autostarted: args.Contains("--autostart"),
            Restarted: args.Contains("--restarted"),
            MeasureLatency: args.Contains("--measure-latency"),
            EcoQos: args.Contains("--ecoqos"),
            DataDirectory: dataDirectory,
            BatchInterval: batchInterval,
            CrashAfterSeconds: crashAfter,
            RawArguments: args);
    }

    /// <summary>
    /// Command line for the worker process started by the supervisor: the same options plus "--worker".
    /// A restarted worker gets "--restarted" and never the one-shot "--crash-after" test hook.
    /// </summary>
    public string WorkerArguments(bool restarted)
    {
        var kept = new List<string>();
        for (var i = 0; i < RawArguments.Length; i++)
        {
            switch (RawArguments[i])
            {
                case "--crash-after" when restarted:
                    i++; // skip its value too
                    break;
                case "--worker" or "--restarted":
                    break;
                default:
                    kept.Add(RawArguments[i].Contains(' ', StringComparison.Ordinal) ? $"\"{RawArguments[i]}\"" : RawArguments[i]);
                    break;
            }
        }

        kept.Add("--worker");
        if (restarted)
        {
            kept.Add("--restarted");
        }

        return string.Join(' ', kept);
    }

    public override string ToString() =>
        $"autostart={Autostarted}, restarted={Restarted}, latency={MeasureLatency}, ecoqos={EcoQos}, batch={BatchInterval.TotalMilliseconds} ms, data={DataDirectory}";
}
