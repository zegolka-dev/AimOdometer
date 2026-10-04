using System.Runtime.InteropServices;
using AimOdometer.Core;
using AimOdometer.Core.Diagnostics;
using AimOdometer.Core.Storage;
using AimOdometer.Win32;

namespace AimOdometer.Tracker;

internal static class Program
{
    private static int Main(string[] args)
    {
        var options = TrackerOptions.Parse(args);
        if (options.Stop)
        {
            return TrackerHost.RequestStopOfRunningInstance() ? 0 : 1;
        }

        if (!options.Worker)
        {
            return Supervisor.Run(options);
        }

        var mutex = Kernel32.CreateMutexW(0, false, AppIdentity.TrackerMutexName);
        if (mutex == 0)
        {
            return 2;
        }

        try
        {
            if (Marshal.GetLastPInvokeError() == Kernel32.ErrorAlreadyExists)
            {
                // Another tracker already runs in this session; the second launch is a no-op.
                return 0;
            }

            Log.Configure(Path.Combine(options.DataDirectory, "logs", "tracker.log"), LogLevel.Warning);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Log.Error("Unhandled exception", e.ExceptionObject as Exception);

            StatsStore store;
            try
            {
                store = Backups.OpenOrRecover(Path.Combine(options.DataDirectory, "aimodometer.db"));
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Log.Error("Cannot open the statistics database", ex);
                var strings = TrayStrings.For(null);
                User32.MessageBoxW(0, ex.Message, strings.FatalStartTitle, User32.MbIconError);
                return 4;
            }

            using (store)
            using (var host = new TrackerHost(options, store))
            {
                return host.Run();
            }
        }
        finally
        {
            Kernel32.CloseHandle(mutex);
        }
    }
}
