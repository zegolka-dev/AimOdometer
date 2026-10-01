using AimOdometer.Tracker;

namespace AimOdometer.Tracker.Tests;

public class TrackerOptionsTests
{
    [Fact]
    public void Defaults()
    {
        var options = TrackerOptions.Parse([]);
        Assert.False(options.Stop);
        Assert.Equal(TrackerOptions.DefaultBatchInterval, options.BatchInterval);
        Assert.Equal(0, options.CrashAfterSeconds);
        Assert.EndsWith("AimOdometer", options.DataDirectory, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsesValues()
    {
        var options = TrackerOptions.Parse(["--data-dir", @"C:\Temp\x", "--batch-ms", "25", "--measure-latency", "--ecoqos"]);
        Assert.Equal(@"C:\Temp\x", options.DataDirectory);
        Assert.Equal(TimeSpan.FromMilliseconds(25), options.BatchInterval);
        Assert.True(options.MeasureLatency);
        Assert.True(options.EcoQos);
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("500")]
    [InlineData("abc")]
    public void IgnoresOutOfRangeBatchInterval(string value)
    {
        Assert.Equal(TrackerOptions.DefaultBatchInterval, TrackerOptions.Parse(["--batch-ms", value]).BatchInterval);
    }

    [Fact]
    public void WorkerArguments_FirstStartKeepsEverything()
    {
        var options = TrackerOptions.Parse(["--autostart", "--data-dir", @"C:\My Data", "--crash-after", "70"]);
        Assert.Equal("--autostart --data-dir \"C:\\My Data\" --crash-after 70 --worker", options.WorkerArguments(restarted: false));
    }

    [Fact]
    public void WorkerArguments_RestartDropsCrashHookAndMarksRestart()
    {
        var options = TrackerOptions.Parse(["--data-dir", @"C:\My Data", "--crash-after", "70", "--batch-ms", "16"]);
        Assert.Equal("--data-dir \"C:\\My Data\" --batch-ms 16 --worker --restarted", options.WorkerArguments(restarted: true));
    }

    [Fact]
    public void WorkerArguments_DoNotAccumulateFlags()
    {
        var options = TrackerOptions.Parse(["--worker", "--restarted"]);
        Assert.Equal("--worker --restarted", options.WorkerArguments(restarted: true));
    }
}
