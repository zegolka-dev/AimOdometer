using AimOdometer.Core.Diagnostics;

namespace AimOdometer.Core.Tests;

// Log is static; keep its tests in one non-parallel collection.
[Collection(nameof(LogTests))]
[CollectionDefinition(nameof(LogTests), DisableParallelization = true)]
public sealed class LogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aimodometer-log-{Guid.NewGuid():N}");

    private string LogPath => Path.Combine(_directory, "test.log");

    public void Dispose()
    {
        Log.Configure(Path.Combine(_directory, "unused.log"), LogLevel.Warning);
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void FiltersBelowMinimumLevel()
    {
        Log.Configure(LogPath, LogLevel.Warning);
        Log.Info("hidden");
        Log.Warning("shown");

        var text = File.ReadAllText(LogPath);
        Assert.DoesNotContain("hidden", text, StringComparison.Ordinal);
        Assert.Contains("[Warning] shown", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RotatesWhenFull()
    {
        Log.Configure(LogPath, LogLevel.Debug, maxBytes: 1024);
        for (var i = 0; i < 100; i++)
        {
            Log.Warning(new string('x', 50));
        }

        Assert.True(new FileInfo(LogPath).Length <= 1024);
        Assert.True(File.Exists(LogPath + ".1"));
        Assert.Equal(2, Directory.GetFiles(_directory).Length);
    }

    [Theory]
    [InlineData("warning", true, LogLevel.Warning)]
    [InlineData("Debug", true, LogLevel.Debug)]
    [InlineData("verbose", false, LogLevel.Debug)]
    [InlineData("42", false, LogLevel.Debug)]
    public void ParsesLevels(string text, bool ok, LogLevel expected)
    {
        Assert.Equal(ok, Log.TryParseLevel(text, out var level));
        if (ok)
        {
            Assert.Equal(expected, level);
        }
    }
}
