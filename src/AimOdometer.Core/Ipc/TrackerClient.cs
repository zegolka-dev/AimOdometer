using System.IO.Pipes;

namespace AimOdometer.Core.Ipc;

/// <summary>Talks to the tracker running in the current Windows session.</summary>
public static class TrackerClient
{
    /// <summary>Sends a command and returns the raw response, or null if no tracker answered in time.</summary>
    public static byte[]? Send(TrackerCommand command, long argument = 0, int timeoutMs = 1000)
    {
        var pipeName = TrackerProtocol.PipeName(CurrentSessionId());
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            pipe.Connect(timeoutMs);

            Span<byte> request = stackalloc byte[TrackerProtocol.RequestSize];
            TrackerProtocol.WriteRequest(request, command, argument);
            pipe.Write(request);
            pipe.Flush();

            var response = new byte[TrackerProtocol.MaxResponseSize];
            var total = 0;
            int read;
            while (total < response.Length && (read = pipe.Read(response, total, response.Length - total)) > 0)
            {
                total += read;
            }

            return total == 0 ? null : response[..total];
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Process id of the tracker worker that serves the pipe, or null when none is running.</summary>
    public static int? GetProcessId() => GetInfo()?.ProcessId;

    /// <summary>Who answers the pipe and which data folder it writes to; null when no tracker runs.</summary>
    public static (int ProcessId, string? DataDirectory)? GetInfo()
    {
        if (Send(TrackerCommand.Ping, timeoutMs: 500) is not [TrackerProtocol.StatusOk, ..] response)
        {
            return null;
        }

        var (_, pid, folder) = TrackerProtocol.ReadPing(response);
        return (pid, folder);
    }

    public static bool IsRunning() => Send(TrackerCommand.Ping, timeoutMs: 300) is [TrackerProtocol.StatusOk, ..];

    public static TrackerStatus? GetStatus() =>
        Send(TrackerCommand.GetStatus) is { } response ? TrackerProtocol.ReadStatus(response) : null;

    private static int CurrentSessionId()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return process.SessionId;
    }
}
