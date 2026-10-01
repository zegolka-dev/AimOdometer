using System.Diagnostics;
using System.Runtime.InteropServices;
using AimOdometer.Core;
using AimOdometer.Core.Diagnostics;
using AimOdometer.Win32;

namespace AimOdometer.Tracker;

/// <summary>
/// Keeps the tracker alive. The exe started by autostart (or by the UI) is this supervisor: it launches the real
/// tracker as a "--worker" child and sleeps in WaitForExit (no window, no database, no CPU). If the worker crashes,
/// it is restarted after a short delay, with a crash-loop limit.
///
/// Why not RegisterApplicationRestart: NativeAOT terminates on unhandled exceptions through a fast-fail
/// (0xC0000409), and Windows Error Reporting does not restart fast-failed processes (verified in phase 2).
/// </summary>
internal static class Supervisor
{
    public const string MutexName = @"Local\AimOdometer.Supervisor";

    private const int MaxRestarts = 5;
    private static readonly TimeSpan RestartWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(2);

    public static int Run(TrackerOptions options)
    {
        var mutex = Kernel32.CreateMutexW(0, false, MutexName);
        if (mutex == 0)
        {
            return 2;
        }

        try
        {
            if (Marshal.GetLastPInvokeError() == Kernel32.ErrorAlreadyExists)
            {
                return 0; // a tracker is already supervised in this session
            }

            Log.Configure(Path.Combine(options.DataDirectory, "logs", "tracker.log"), LogLevel.Warning);
            var restarts = new Queue<DateTime>();
            var arguments = options.WorkerArguments(restarted: false);

            while (true)
            {
                int exitCode;
                using (var worker = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, arguments) { UseShellExecute = false }))
                {
                    if (worker is null)
                    {
                        Log.Error("Could not start the tracker worker");
                        return 5;
                    }

                    worker.WaitForExit();
                    exitCode = worker.ExitCode;
                }

                if (exitCode == 0)
                {
                    return 0; // normal exit: user chose Exit, --stop, or the UI asked for shutdown
                }

                if (User32.GetSystemMetrics(User32.SmShuttingDown) != 0)
                {
                    return 0; // Windows is logging off or shutting down; workers are killed on purpose
                }

                var now = DateTime.UtcNow;
                while (restarts.Count > 0 && now - restarts.Peek() > RestartWindow)
                {
                    restarts.Dequeue();
                }

                if (restarts.Count >= MaxRestarts)
                {
                    Log.Error($"Tracker crashed {MaxRestarts} times within {RestartWindow.TotalMinutes} minutes; giving up until next start");
                    return exitCode;
                }

                restarts.Enqueue(now);
                Log.Warning($"Tracker worker exited with code 0x{exitCode:X8}; restarting");
                Thread.Sleep(RestartDelay);
                arguments = options.WorkerArguments(restarted: true);
            }
        }
        finally
        {
            Kernel32.CloseHandle(mutex);
        }
    }
}
