using System.Runtime.InteropServices;

namespace AimOdometer.Win32;

/// <summary>Source-generated P/Invoke declarations for kernel32.dll.</summary>
internal static partial class Kernel32
{
    public const int ErrorAlreadyExists = 183;

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateMutexW(nint attributes, [MarshalAs(UnmanagedType.Bool)] bool initialOwner, string name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandleW(string? moduleName);
}
