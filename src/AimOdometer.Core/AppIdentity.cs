namespace AimOdometer.Core;

/// <summary>Names shared by the tracker and the UI process so they can find each other.</summary>
public static class AppIdentity
{
    public const string ProductName = "AimOdometer";

    /// <summary>Per-session single-instance mutex of the background tracker.</summary>
    public const string TrackerMutexName = @"Local\AimOdometer.Tracker";

    /// <summary>Window class of the tracker's hidden window.</summary>
    public const string TrackerWindowClass = "AimOdometer.Tracker.Window";

    /// <summary>Environment variable that points the app at another data folder (testing, screenshots).</summary>
    public const string DataDirectoryVariable = "AIMODOMETER_DATA_DIR";

    /// <summary>Folder with all user data: %LOCALAPPDATA%\AimOdometer (or <see cref="DataDirectoryVariable"/>).</summary>
    public static string DataDirectory =>
        Environment.GetEnvironmentVariable(DataDirectoryVariable) is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductName);
}
