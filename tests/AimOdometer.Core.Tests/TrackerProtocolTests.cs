using AimOdometer.Core.Ipc;

namespace AimOdometer.Core.Tests;

public class TrackerProtocolTests
{
    [Fact]
    public void Request_RoundTrips()
    {
        Span<byte> buffer = stackalloc byte[TrackerProtocol.RequestSize];
        TrackerProtocol.WriteRequest(buffer, TrackerCommand.Pause, 15);
        Assert.Equal((TrackerCommand.Pause, 15L), TrackerProtocol.ReadRequest(buffer));
    }

    [Fact]
    public void Status_RoundTrips()
    {
        var status = new TrackerStatus(
            Paused: true,
            PausedUntilUtc: new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc),
            TodayCentimeters: 12345.6,
            EventsProcessed: 1_000_000,
            WakeUps: 4_000,
            LatencySamples: 7,
            LatencyAverageMicroseconds: 120.5,
            LatencyMaxMicroseconds: 900,
            ManagedBytesAllocated: 4096);

        var buffer = new byte[TrackerProtocol.MaxResponseSize];
        var length = TrackerProtocol.WriteStatus(buffer, status);
        Assert.Equal(status, TrackerProtocol.ReadStatus(buffer.AsSpan(0, length)));
    }

    [Fact]
    public void Status_WithoutPauseDeadline_RoundTripsAsNull()
    {
        var status = new TrackerStatus(false, null, 0, 0, 0, 0, 0, 0, 0);
        var buffer = new byte[TrackerProtocol.MaxResponseSize];
        var length = TrackerProtocol.WriteStatus(buffer, status);
        Assert.Null(TrackerProtocol.ReadStatus(buffer.AsSpan(0, length)).PausedUntilUtc);
    }

    [Fact]
    public void Status_RejectsErrorResponse()
    {
        Assert.Throws<InvalidDataException>(() => TrackerProtocol.ReadStatus([TrackerProtocol.StatusError]));
    }

    [Fact]
    public void PipeName_IncludesSession()
    {
        Assert.NotEqual(TrackerProtocol.PipeName(1), TrackerProtocol.PipeName(2));
    }
}
