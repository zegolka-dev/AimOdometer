using Microsoft.Win32;

namespace AimOdometer.App.Services;

/// <summary>
/// Windows 11 hides new notification-area icons behind the "^" arrow. With the user's consent (onboarding or
/// settings) this sets the same per-icon switch as Settings › Personalization › Taskbar › Other system tray icons.
/// </summary>
public static class TrayIconVisibility
{
    private const string SettingsKey = @"Control Panel\NotifyIconSettings";

    /// <summary>Shows the tracker's icon on the taskbar. Returns false until Windows has seen the icon once.</summary>
    public static bool Promote(string trackerExePath, bool visible)
    {
        using var root = Registry.CurrentUser.OpenSubKey(SettingsKey);
        if (root is null)
        {
            return false;
        }

        var found = false;
        foreach (var name in root.GetSubKeyNames())
        {
            using var icon = root.OpenSubKey(name, writable: true);
            if (icon?.GetValue("ExecutablePath") is string path
                && string.Equals(path, trackerExePath, StringComparison.OrdinalIgnoreCase))
            {
                icon.SetValue("IsPromoted", visible ? 1 : 0, RegistryValueKind.DWord);
                found = true;
            }
        }

        return found;
    }

    public static bool IsPromoted(string trackerExePath)
    {
        using var root = Registry.CurrentUser.OpenSubKey(SettingsKey);
        foreach (var name in root?.GetSubKeyNames() ?? [])
        {
            using var icon = root!.OpenSubKey(name);
            if (icon?.GetValue("ExecutablePath") is string path
                && string.Equals(path, trackerExePath, StringComparison.OrdinalIgnoreCase))
            {
                return icon.GetValue("IsPromoted") is 1;
            }
        }

        return false;
    }
}
