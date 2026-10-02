using System.Globalization;
using AimOdometer.Core.Diagnostics;
using Microsoft.Win32;

namespace AimOdometer.Core.Games;

/// <summary>A Steam app installed on this PC.</summary>
public sealed record SteamApp(int AppId, string Name, string InstallDirectory);

/// <summary>
/// Reads locally installed Steam apps without signing in: the Steam folder from the registry,
/// every library from steamapps/libraryfolders.vdf, and each game from steamapps/appmanifest_*.acf.
/// </summary>
public static class SteamLibrary
{
    /// <summary>Steam's install folder, or null when Steam is not installed.</summary>
    public static string? FindSteamRoot()
    {
        using var user = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (user?.GetValue("SteamPath") is string path && Directory.Exists(path))
        {
            return Path.GetFullPath(path);
        }

        using var machine = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
        return machine?.GetValue("InstallPath") is string installPath && Directory.Exists(installPath)
            ? Path.GetFullPath(installPath)
            : null;
    }

    /// <summary>All library folders: the Steam folder itself plus those listed in libraryfolders.vdf.</summary>
    public static IReadOnlyList<string> FindLibraries(string steamRoot)
    {
        ArgumentNullException.ThrowIfNull(steamRoot);
        var libraries = new List<string> { Path.GetFullPath(steamRoot) };
        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            try
            {
                foreach (var folder in KeyValues.Parse(File.ReadAllText(vdf)).Children)
                {
                    // Current format: "0" { "path" "D:\\SteamLibrary" ... }. Old format: "1" "D:\\SteamLibrary".
                    var path = folder.Value ?? folder.GetString("path");
                    if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                    {
                        libraries.Add(Path.GetFullPath(path));
                    }
                }
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                Log.Warning($"Cannot read {vdf}: {ex.Message}");
            }
        }

        return libraries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Installed apps in all libraries. Unreadable manifests are skipped.</summary>
    public static IReadOnlyList<SteamApp> LoadInstalledApps(string steamRoot)
    {
        var apps = new Dictionary<int, SteamApp>();
        foreach (var library in FindLibraries(steamRoot))
        {
            var steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps))
            {
                continue;
            }

            foreach (var manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
            {
                if (ReadManifest(manifest, library) is { } app)
                {
                    apps.TryAdd(app.AppId, app);
                }
            }
        }

        return [.. apps.Values.OrderBy(a => a.AppId)];
    }

    /// <summary>Convenience: installed apps of the local Steam, or none when Steam is not installed.</summary>
    public static IReadOnlyList<SteamApp> LoadLocal() =>
        FindSteamRoot() is { } root ? LoadInstalledApps(root) : [];

    internal static SteamApp? ReadManifest(string manifestPath, string library)
    {
        try
        {
            var state = KeyValues.Parse(File.ReadAllText(manifestPath));
            if (!int.TryParse(state.GetString("appid"), NumberStyles.None, CultureInfo.InvariantCulture, out var appId)
                || state.GetString("installdir") is not { Length: > 0 } installDir)
            {
                return null;
            }

            var name = state.GetString("name") is { Length: > 0 } n ? n : $"Steam app {appId}";
            return new SteamApp(appId, name, Path.Combine(library, "steamapps", "common", installDir));
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            Log.Warning($"Cannot read {manifestPath}: {ex.Message}");
            return null;
        }
    }
}
