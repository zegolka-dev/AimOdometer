using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using AimOdometer.Core;
using AimOdometer.Core.Diagnostics;
using AimOdometer.Core.Ipc;
using AimOdometer.Win32;

namespace AimOdometer.Tracker;

/// <summary>A request handed from the pipe thread to the window thread (lives on the pipe thread's stack).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PipeRequest
{
    public TrackerCommand Command;
    public long Argument;
    public int ResponseLength;
    public fixed byte Response[TrackerProtocol.MaxResponseSize];
}

/// <summary>
/// Serves <see cref="TrackerProtocol"/> on a named pipe that only the current user can open.
/// The pipe thread sleeps in WaitForConnection; each request is executed on the window thread via
/// SendMessageTimeout, so tracker state never needs locks.
/// </summary>
internal sealed unsafe class PipeServer : IDisposable
{
    public const uint RequestMessage = User32.WmApp + 2;

    private readonly nint _hwnd;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;

    public PipeServer(nint hwnd, int sessionId)
    {
        _hwnd = hwnd;
        _pipeName = TrackerProtocol.PipeName(sessionId);
        _thread = new Thread(Run) { IsBackground = true, Name = "AimOdometer pipe" };
        _thread.Start();
    }

    /// <summary>Only the current user may connect.</summary>
    internal static PipeSecurity CreateSecurity()
    {
        var user = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Cannot determine the current user SID.");
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    /// <summary>
    /// One pipe instance. When the tracker runs as administrator ("count games that run as administrator"), the pipe
    /// would get the high integrity label and the normal window could not write to it, so it is lowered to medium.
    /// </summary>
    internal static NamedPipeServerStream CreatePipe(string name, PipeSecurity security, bool elevated)
    {
        var server = NamedPipeServerStreamAcl.Create(
            name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            TrackerProtocol.MaxResponseSize, TrackerProtocol.RequestSize, security,
            additionalAccessRights: elevated ? PipeAccessRights.TakeOwnership : 0);
        if (elevated && !IntegrityLabel.SetMedium(server.SafePipeHandle))
        {
            Log.Warning("Could not lower the pipe's integrity label: the window may not reach this elevated tracker");
        }

        return server;
    }

    private void Run()
    {
        var security = CreateSecurity();
        var elevated = ElevatedTask.IsElevated;

        var buffer = new byte[TrackerProtocol.RequestSize];
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var server = CreatePipe(_pipeName, security, elevated);
                server.WaitForConnectionAsync(_stop.Token).GetAwaiter().GetResult();
                server.ReadExactly(buffer);

                var (command, argument) = TrackerProtocol.ReadRequest(buffer);
                var request = new PipeRequest { Command = command, Argument = argument };
                nuint result;
                var delivered = User32.SendMessageTimeoutW(
                    _hwnd, RequestMessage, 0, (nint)(&request),
                    User32.SmtoBlock | User32.SmtoAbortIfHung, 2000, &result);

                if (delivered == 0 || request.ResponseLength == 0)
                {
                    server.WriteByte(TrackerProtocol.StatusError);
                }
                else
                {
                    server.Write(new ReadOnlySpan<byte>(request.Response, request.ResponseLength));
                }

                server.Flush();
                server.WaitForPipeDrain();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException ex)
            {
                // A client disconnected mid-request; keep serving.
                Log.Debug($"Pipe client error: {ex.Message}");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
            {
                Log.Error("Pipe server stopped", ex);
                return;
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _thread.Join(TimeSpan.FromSeconds(2));
        _stop.Dispose();
    }
}
