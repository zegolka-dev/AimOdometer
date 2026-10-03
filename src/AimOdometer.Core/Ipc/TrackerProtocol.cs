using System.Buffers.Binary;
using System.Globalization;

namespace AimOdometer.Core.Ipc;

/// <summary>Commands the UI (or tools) can send to the tracker over its named pipe.</summary>
public enum TrackerCommand : byte
{
    Ping = 1,
    GetStatus = 2,
    Flush = 3,

    /// <summary>Argument: minutes; 0 pauses until the tracker restarts.</summary>
    Pause = 4,
    Resume = 5,
    ReloadSettings = 6,
    Shutdown = 7,
}

/// <summary>Live state reported by <see cref="TrackerCommand.GetStatus"/>.</summary>
public sealed record TrackerStatus(
    bool Paused,
    DateTime? PausedUntilUtc,
    double TodayCentimeters,
    long EventsProcessed,
    long WakeUps,
    long LatencySamples,
    double LatencyAverageMicroseconds,
    double LatencyMaxMicroseconds,
    long ManagedBytesAllocated);

/// <summary>
/// Wire format shared by the tracker and its clients. Request: 1-byte command + 8-byte argument.
/// Response: 1-byte status + fixed payload. Little-endian, versioned via <see cref="Version"/>.
/// </summary>
public static class TrackerProtocol
{
    public const int Version = 2;
    public const int RequestSize = 9;
    public const int MaxResponseSize = 1024;

    public const byte StatusOk = 0;
    public const byte StatusUnknownCommand = 1;
    public const byte StatusError = 2;

    /// <summary>Pipe names are machine-wide, so include the session id (fast user switching, RDP).</summary>
    public static string PipeName(int sessionId) =>
        string.Create(CultureInfo.InvariantCulture, $"AimOdometer.Tracker.{sessionId}");

    public static void WriteRequest(Span<byte> buffer, TrackerCommand command, long argument)
    {
        buffer[0] = (byte)command;
        BinaryPrimitives.WriteInt64LittleEndian(buffer[1..], argument);
    }

    public static (TrackerCommand Command, long Argument) ReadRequest(ReadOnlySpan<byte> buffer) =>
        ((TrackerCommand)buffer[0], BinaryPrimitives.ReadInt64LittleEndian(buffer[1..]));

    /// <summary>Ping response: protocol version, process id and the folder the tracker writes to.</summary>
    public static int WritePing(Span<byte> buffer, int processId, string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);
        buffer[0] = StatusOk;
        BinaryPrimitives.WriteInt32LittleEndian(buffer[1..], Version);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[5..], processId);
        var length = System.Text.Encoding.UTF8.GetBytes(dataDirectory, buffer[11..]);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[9..], (ushort)length);
        return 11 + length;
    }

    public static (int Version, int ProcessId, string? DataDirectory) ReadPing(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 9 || buffer[0] != StatusOk)
        {
            throw new InvalidDataException("Malformed tracker ping response.");
        }

        var version = BinaryPrimitives.ReadInt32LittleEndian(buffer[1..]);
        var pid = BinaryPrimitives.ReadInt32LittleEndian(buffer[5..]);
        string? folder = null;
        if (buffer.Length >= 11)
        {
            var length = BinaryPrimitives.ReadUInt16LittleEndian(buffer[9..]);
            if (buffer.Length >= 11 + length)
            {
                folder = System.Text.Encoding.UTF8.GetString(buffer.Slice(11, length));
            }
        }

        return (version, pid, folder);
    }

    /// <summary>Writes a status payload; returns the number of bytes written.</summary>
    public static int WriteStatus(Span<byte> buffer, TrackerStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        buffer[0] = StatusOk;
        buffer[1] = status.Paused ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt64LittleEndian(buffer[2..], status.PausedUntilUtc?.Ticks ?? 0);
        BinaryPrimitives.WriteDoubleLittleEndian(buffer[10..], status.TodayCentimeters);
        BinaryPrimitives.WriteInt64LittleEndian(buffer[18..], status.EventsProcessed);
        BinaryPrimitives.WriteInt64LittleEndian(buffer[26..], status.WakeUps);
        BinaryPrimitives.WriteInt64LittleEndian(buffer[34..], status.LatencySamples);
        BinaryPrimitives.WriteDoubleLittleEndian(buffer[42..], status.LatencyAverageMicroseconds);
        BinaryPrimitives.WriteDoubleLittleEndian(buffer[50..], status.LatencyMaxMicroseconds);
        BinaryPrimitives.WriteInt64LittleEndian(buffer[58..], status.ManagedBytesAllocated);
        return 66;
    }

    public static TrackerStatus ReadStatus(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 66 || buffer[0] != StatusOk)
        {
            throw new InvalidDataException("Malformed tracker status response.");
        }

        var pausedTicks = BinaryPrimitives.ReadInt64LittleEndian(buffer[2..]);
        return new TrackerStatus(
            Paused: buffer[1] != 0,
            PausedUntilUtc: pausedTicks == 0 ? null : new DateTime(pausedTicks, DateTimeKind.Utc),
            TodayCentimeters: BinaryPrimitives.ReadDoubleLittleEndian(buffer[10..]),
            EventsProcessed: BinaryPrimitives.ReadInt64LittleEndian(buffer[18..]),
            WakeUps: BinaryPrimitives.ReadInt64LittleEndian(buffer[26..]),
            LatencySamples: BinaryPrimitives.ReadInt64LittleEndian(buffer[34..]),
            LatencyAverageMicroseconds: BinaryPrimitives.ReadDoubleLittleEndian(buffer[42..]),
            LatencyMaxMicroseconds: BinaryPrimitives.ReadDoubleLittleEndian(buffer[50..]),
            ManagedBytesAllocated: BinaryPrimitives.ReadInt64LittleEndian(buffer[58..]));
    }
}
