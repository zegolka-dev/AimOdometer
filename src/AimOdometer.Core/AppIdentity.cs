namespace AimOdometer.Core;

/// <summary>Names shared by the tracker and the UI process so they can find each other.</summary>
public static class AppIdentity
{
    public const string ProductName = "AimOdometer";

    /// <summary>Per-session single-instance mutex of the background tracker.</summary>
    public const string TrackerMutexName = @"Local\AimOdometer.Tracker";

    /// <summary>Window class of the tracker's hidden window.</summary>
    public const string TrackerWindowClass = "AimOdometer.Tracker.Window";

    /// <summary>Folder with all user data: %LOCALAPPDATA%\AimOdometer.</summary>
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductName);
}
