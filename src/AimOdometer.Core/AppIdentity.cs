namespace AimOdometer.Core;

/// <summary>Names shared by the tracker and the UI process so they can find each other.</summary>
public static class AppIdentity
{
    public const string ProductName = "AimOdometer";

    /// <summary>Project website (GitHub Pages), printed on share cards.</summary>
    public const string Website = "https://zegolka-dev.github.io/AimOdometer";

    /// <summary>Source code; sent in the User-Agent of map requests, as the services' usage policies ask.</summary>
    public const string Repository = "https://github.com/zegolka-dev/AimOdometer";

    /// <summary>Product version as released (SemVer, e.g. "0.1.0-beta.1"), without build metadata.</summary>
    public static string Version =>
        (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(AppIdentity).Assembly)
            ?.InformationalVersion ?? "0.0.0").Split('+')[0];

    /// <summary>Per-session single-instance mutex of the background tracker.</summary>
    public const string TrackerMutexName = @"Local\AimOdometer.Tracker";

    /// <summary>Window class of the tracker's hidden window.</summary>
    public const string TrackerWindowClass = "AimOdometer.Tracker.Window";

    /// <summary>Environment variable that points the app at another data folder (testing, screenshots).</summary>
    public const string DataDirectoryVariable = "AIMODOMETER_DATA_DIR";

    /// <summary>The real per-user data folder: %LOCALAPPDATA%\AimOdometer. The tracker always uses this (or --data-dir).</summary>
    public static string UserDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductName);

    /// <summary>True when <see cref="DataDirectoryVariable"/> points the window and tools at another folder (tests).</summary>
    public static bool IsDataDirectoryOverridden =>
        Environment.GetEnvironmentVariable(DataDirectoryVariable) is { Length: > 0 };

    /// <summary>
    /// Data folder for the statistics window and tools: <see cref="UserDataDirectory"/>, or <see cref="DataDirectoryVariable"/>
    /// for tests and screenshots. The tracker ignores the variable on purpose, so a test can never redirect real tracking.
    /// </summary>
    public static string DataDirectory =>
        Environment.GetEnvironmentVariable(DataDirectoryVariable) is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : UserDataDirectory;

    /// <summary>Two folder paths refer to the same folder (case-insensitive, trailing separators ignored).</summary>
    public static bool SameFolder(string a, string b) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
        StringComparison.OrdinalIgnoreCase);
}
