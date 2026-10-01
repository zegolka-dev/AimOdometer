using Microsoft.Win32;

namespace AimOdometer.Tracker;

/// <summary>Start-with-Windows through HKCU\...\Run (per user, no admin rights needed).</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AimOdometer";

    public static string Command => $"\"{Environment.ProcessPath}\" --autostart";

    /// <summary>True when the tracker runs from the installed location (not from a developer build folder).</summary>
    public static bool IsInstalledLocation =>
        Environment.ProcessPath?.Contains(@"\AimOdometerApp\", StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            // Rewrites the path too, so it follows the exe if it moved.
            key.SetValue(ValueName, Command, RegistryValueKind.String);
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
