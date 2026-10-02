using Microsoft.Win32;

namespace AimOdometer.Core;

/// <summary>Start-with-Windows through HKCU\...\Run (per user, no admin rights needed).</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AimOdometer";

    public static string CommandFor(string trackerExePath) => $"\"{trackerExePath}\" --autostart";

    /// <summary>True for the installed copy (not a developer build folder); autostart is on by default only there.</summary>
    public static bool IsInstalledLocation(string? exePath) =>
        exePath?.Contains(@"\AimOdometerApp\", StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    /// <summary>Enables (pointing at <paramref name="trackerExePath"/>) or disables autostart.</summary>
    public static void Set(bool enabled, string trackerExePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            // Rewrites the path too, so it follows the exe if it moved.
            key.SetValue(ValueName, CommandFor(trackerExePath), RegistryValueKind.String);
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
