using System.Runtime.InteropServices;

namespace AimOdometer.Win32;

/// <summary>Source-generated P/Invoke declarations for user32.dll.</summary>
internal static unsafe partial class User32
{
    // Window messages
    public const uint WmDestroy = 0x0002;
    public const uint WmClose = 0x0010;
    public const uint WmQueryEndSession = 0x0011;
    public const uint WmEndSession = 0x0016;
    public const uint WmTimeChange = 0x001E;
    public const uint WmContextMenu = 0x007B;
    public const uint WmInputDeviceChange = 0x00FE;
    public const uint WmInput = 0x00FF;
    public const uint WmTimer = 0x0113;
    public const uint WmMouseMove = 0x0200;
    public const uint WmPowerBroadcast = 0x0218;
    public const uint WmWtsSessionChange = 0x02B1;
    public const uint WmApp = 0x8000;

    // Window styles
    public const uint WsExToolWindow = 0x00000080;
    public const uint WsPopup = 0x80000000;

    // Raw input
    public const uint RidevRemove = 0x00000001;
    public const uint RidevInputSink = 0x00000100;
    public const uint RidevDevNotify = 0x00002000;
    public const uint RidInput = 0x10000003;
    public const uint RidiDeviceName = 0x20000007;
    public const uint RidiDeviceInfo = 0x2000000b;
    public const uint RimTypeMouse = 0;
    public const uint RimTypeHid = 2;
    public const nuint RimInput = 0;
    public const nuint GidcArrival = 1;
    public const nuint GidcRemoval = 2;
    public const int RawInputHeaderSize = 24;

    // Menus
    public const uint MfString = 0x0000;
    public const uint MfGrayed = 0x0001;
    public const uint MfChecked = 0x0008;
    public const uint MfPopup = 0x0010;
    public const uint MfSeparator = 0x0800;
    public const uint TpmRightButton = 0x0002;
    public const uint TpmReturnCmd = 0x0100;
    public const uint TpmBottomAlign = 0x0020;

    // Icons
    public const uint ImageIcon = 1;
    public const uint LrDefaultColor = 0;
    public const int SmCxSmIcon = 49;
    public const int SmCySmIcon = 50;
    public const int SmShuttingDown = 0x2000;

    // SendMessageTimeout
    public const uint SmtoBlock = 0x0001;
    public const uint SmtoAbortIfHung = 0x0002;

    // MessageBox
    public const uint MbIconError = 0x00000010;

    [StructLayout(LayoutKind.Sequential)]
    public struct WndClassExW
    {
        public uint CbSize;
        public uint Style;
        public delegate* unmanaged<nint, uint, nuint, nint, nint> LpfnWndProc;
        public int CbClsExtra;
        public int CbWndExtra;
        public nint HInstance;
        public nint HIcon;
        public nint HCursor;
        public nint HbrBackground;
        public char* LpszMenuName;
        public char* LpszClassName;
        public nint HIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int PtX;
        public int PtY;
        public uint LPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint HwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawInputDeviceList
    {
        public nint Device;
        public uint Type;
    }

    /// <summary>RID_DEVICE_INFO with only the fields this app reads (HID usage and mouse info share the union).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct RidDeviceInfo
    {
        [FieldOffset(0)] public uint CbSize;
        [FieldOffset(4)] public uint Type;
        [FieldOffset(8)] public uint HidVendorId;
        [FieldOffset(12)] public uint HidProductId;
        [FieldOffset(20)] public ushort HidUsagePage;
        [FieldOffset(22)] public ushort HidUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial ushort RegisterClassExW(WndClassExW* wndClass);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(
        uint exStyle, string className, string? windowName, uint style,
        int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint DefWindowProcW(nint hwnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial int GetMessageW(Msg* msg, nint hwnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(Msg* msg);

    [LibraryImport("user32.dll")]
    public static partial nint DispatchMessageW(Msg* msg);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindWindowW(string className, string? windowName);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(nint hwnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint SendMessageTimeoutW(
        nint hwnd, uint msg, nuint wParam, nint lParam, uint flags, uint timeoutMs, nuint* result);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterRawInputDevices(RawInputDevice* devices, uint count, uint size);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetRawInputData(nint rawInput, uint command, void* data, uint* size, uint headerSize);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetRawInputBuffer(void* data, uint* size, uint headerSize);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetRawInputDeviceInfoW(nint device, uint command, void* data, uint* size);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetRawInputDeviceList(RawInputDeviceList* list, uint* count, uint size);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nuint SetTimer(nint hwnd, nuint id, uint elapseMs, nint timerProc);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool KillTimer(nint hwnd, nuint id);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AppendMenuW(nint menu, uint flags, nuint idOrSubmenu, string? text);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(Point* point);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint LoadImageW(nint instance, nint name, uint type, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBoxW(nint hwnd, string text, string caption, uint type);
}
