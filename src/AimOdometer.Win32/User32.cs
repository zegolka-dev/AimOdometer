using System.Runtime.InteropServices;

namespace AimOdometer.Win32;

/// <summary>Source-generated P/Invoke declarations for user32.dll.</summary>
internal static unsafe partial class User32
{
    public const uint WmDestroy = 0x0002;
    public const uint WmClose = 0x0010;

    public const uint WsExToolWindow = 0x00000080;
    public const uint WsPopup = 0x80000000;

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
}
