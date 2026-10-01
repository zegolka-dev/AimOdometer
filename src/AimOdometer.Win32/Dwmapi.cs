using System.Runtime.InteropServices;

namespace AimOdometer.Win32;

/// <summary>Source-generated P/Invoke declarations for dwmapi.dll.</summary>
internal static partial class Dwmapi
{
    /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 20H1+ and Windows 11).</summary>
    public const int DwmwaUseImmersiveDarkMode = 20;

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    /// <summary>Switches the window caption to the dark system style. Returns false on failure.</summary>
    public static bool UseDarkTitleBar(nint hwnd)
    {
        var enabled = 1;
        return DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) == 0;
    }
}
