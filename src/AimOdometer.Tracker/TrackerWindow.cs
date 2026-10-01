using System.Runtime.InteropServices;
using AimOdometer.Core;
using AimOdometer.Win32;

namespace AimOdometer.Tracker;

/// <summary>
/// Hidden top-level window that owns the tracker's message loop.
/// A top-level window (not HWND_MESSAGE) is required to receive broadcast messages
/// such as WM_POWERBROADCAST, WM_QUERYENDSESSION and WM_TIMECHANGE (see docs/ARCHITECTURE.md).
/// </summary>
internal static unsafe class TrackerWindow
{
    public static int Run()
    {
        var instance = Kernel32.GetModuleHandleW(null);

        fixed (char* className = AppIdentity.TrackerWindowClass)
        {
            var wndClass = new User32.WndClassExW
            {
                CbSize = (uint)sizeof(User32.WndClassExW),
                LpfnWndProc = &WndProc,
                HInstance = instance,
                LpszClassName = className,
            };

            if (User32.RegisterClassExW(&wndClass) == 0)
            {
                return Marshal.GetLastPInvokeError();
            }
        }

        // Never shown: no WS_VISIBLE, tool window so it never appears in Alt+Tab or the taskbar.
        var hwnd = User32.CreateWindowExW(
            User32.WsExToolWindow, AppIdentity.TrackerWindowClass, AppIdentity.ProductName, User32.WsPopup,
            0, 0, 0, 0, 0, 0, instance, 0);
        if (hwnd == 0)
        {
            return Marshal.GetLastPInvokeError();
        }

        User32.Msg msg;
        int result;
        while ((result = User32.GetMessageW(&msg, 0, 0, 0)) > 0)
        {
            User32.TranslateMessage(&msg);
            User32.DispatchMessageW(&msg);
        }

        return result < 0 ? Marshal.GetLastPInvokeError() : (int)msg.WParam;
    }

    /// <summary>Asks a tracker running in this session to exit. Returns false if none is running.</summary>
    public static bool RequestStopOfRunningInstance()
    {
        var hwnd = User32.FindWindowW(AppIdentity.TrackerWindowClass, null);
        return hwnd != 0 && User32.PostMessageW(hwnd, User32.WmClose, 0, 0);
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case User32.WmClose:
                User32.DestroyWindow(hwnd);
                return 0;
            case User32.WmDestroy:
                User32.PostQuitMessage(0);
                return 0;
            default:
                return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }
}
