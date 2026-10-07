using System.Runtime.InteropServices;

namespace AimOdometer.Win32;

/// <summary>Notification area (tray) API from shell32.dll.</summary>
internal static unsafe partial class Shell32
{
    public const uint NimAdd = 0;
    public const uint NimModify = 1;
    public const uint NimDelete = 2;
    public const uint NimSetVersion = 4;

    public const uint NifMessage = 0x01;
    public const uint NifIcon = 0x02;
    public const uint NifTip = 0x04;
    public const uint NifInfo = 0x10;
    public const uint NifShowTip = 0x80;

    public const uint NotifyIconVersion4 = 4;

    // Balloon (NIF_INFO) flags
    public const uint NiifInfo = 0x01;
    public const uint NiifRespectQuietTime = 0x80;

    /// <summary>QUNS_ACCEPTS_NOTIFICATIONS: no full-screen app, presentation or quiet time.</summary>
    public const int QunsAcceptsNotifications = 5;

    // Callback events (LOWORD of lParam with NOTIFYICON_VERSION_4)
    public const uint NinSelect = 0x0400;
    public const uint NinKeySelect = 0x0401;

    /// <summary>NIN_BALLOONUSERCLICK: the user clicked our notification.</summary>
    public const uint NinBalloonUserClick = 0x0405;

    [StructLayout(LayoutKind.Sequential)]
    public struct NotifyIconDataW
    {
        public uint CbSize;
        public nint HWnd;
        public uint UId;
        public uint UFlags;
        public uint UCallbackMessage;
        public nint HIcon;
        public fixed char SzTip[128];
        public uint DwState;
        public uint DwStateMask;
        public fixed char SzInfo[256];
        public uint UVersion;
        public fixed char SzInfoTitle[64];
        public uint DwInfoFlags;
        public Guid GuidItem;
        public nint HBalloonIcon;
    }

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Shell_NotifyIconW(uint message, NotifyIconDataW* data);

    /// <summary>Whether Windows would show a notification now (not during full-screen games, presentations, quiet time).</summary>
    [LibraryImport("shell32.dll")]
    public static partial int SHQueryUserNotificationState(int* state);

    /// <summary>Copies text into a fixed-size UTF-16 buffer, truncating and null-terminating.</summary>
    public static void CopyText(char* destination, int capacity, ReadOnlySpan<char> text)
    {
        var length = Math.Min(text.Length, capacity - 1);
        text[..length].CopyTo(new Span<char>(destination, capacity));
        destination[length] = '\0';
    }
}

/// <summary>Session change notifications (lock, logoff, remote disconnect).</summary>
internal static partial class Wtsapi32
{
    public const uint NotifyForThisSession = 0;

    public const nuint WtsConsoleConnect = 0x1;
    public const nuint WtsConsoleDisconnect = 0x2;
    public const nuint WtsRemoteConnect = 0x3;
    public const nuint WtsRemoteDisconnect = 0x4;
    public const nuint WtsSessionLogoff = 0x6;
    public const nuint WtsSessionLock = 0x7;
    public const nuint WtsSessionUnlock = 0x8;

    [LibraryImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSRegisterSessionNotification(nint hwnd, uint flags);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSUnRegisterSessionNotification(nint hwnd);
}

/// <summary>HID helpers used to read a device's product name.</summary>
internal static unsafe partial class Hid
{
    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetProductString(nint device, void* buffer, uint bufferLength);
}
