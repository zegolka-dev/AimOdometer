using AimOdometer.Core;
using AimOdometer.Win32;

namespace AimOdometer.Tracker;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--stop")
        {
            return TrackerWindow.RequestStopOfRunningInstance() ? 0 : 1;
        }

        var mutex = Kernel32.CreateMutexW(0, false, AppIdentity.TrackerMutexName);
        if (mutex == 0)
        {
            return 2;
        }

        try
        {
            if (System.Runtime.InteropServices.Marshal.GetLastPInvokeError() == Kernel32.ErrorAlreadyExists)
            {
                // Another tracker already runs in this session; the second launch is a no-op.
                return 0;
            }

            return TrackerWindow.Run();
        }
        finally
        {
            Kernel32.CloseHandle(mutex);
        }
    }
}
