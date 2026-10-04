using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using AimOdometer.App.Localization;
using AimOdometer.Core;
using AimOdometer.Core.Diagnostics;
using AimOdometer.Core.Ipc;
using AimOdometer.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using Velopack;
using Velopack.Sources;

namespace AimOdometer.App.Services;

/// <summary>
/// Automatic updates from the project's GitHub Releases (Velopack). The window checks when it opens and every hour
/// while it is open; a new version is downloaded (only the changes), the tracker is stopped because its files are
/// replaced, and the app restarts as the new version, which starts the tracker again. The tracker itself never uses the
/// network. Developer builds (not installed by Setup.exe or the portable zip) never check.
/// </summary>
public sealed partial class Updates : ObservableObject, IDisposable
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan RestartNotice = TimeSpan.FromSeconds(4);

    private readonly AppData _data;
    private readonly Action _beforeRestart;
    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private bool _busy;

    public Updates(AppData data, Action beforeRestart)
    {
        _data = data;
        _beforeRestart = beforeRestart;
        _timer = new DispatcherTimer { Interval = CheckInterval };
        _timer.Tick += async (_, _) => await CheckAsync();
    }

    /// <summary>Banner text while an update is downloaded and installed, null otherwise.</summary>
    [ObservableProperty]
    public partial string? Banner { get; set; }

    /// <summary>Answer to the last check started from Settings.</summary>
    [ObservableProperty]
    public partial string? LastResult { get; set; }

    public bool IsEnabled => _data.Setting(SettingKeys.AutoUpdate) != "0";

    public void Start()
    {
        _timer.Start();
        _ = CheckAsync();
    }

    /// <summary>"Check for updates" in Settings: works even with automatic updates switched off.</summary>
    public Task CheckNowAsync() => CheckAsync(manual: true);

    public async Task CheckAsync(bool manual = false)
    {
        if (_busy || (!manual && !IsEnabled))
        {
            return;
        }

        var L = Loc.Instance;
        if (manual)
        {
            LastResult = L["Update.Checking"];
        }

        _busy = true;
        try
        {
            var manager = new UpdateManager(new GithubSource(AppIdentity.Repository, accessToken: null, prerelease: true));
            if (!manager.IsInstalled)
            {
                LastResult = manual ? L["Update.DevBuild"] : null;
                return; // a developer build
            }

            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                LastResult = manual ? L.Format("Update.UpToDate", AppIdentity.Version) : null;
                return;
            }

            var version = update.TargetFullRelease.Version.ToString();
            LastResult = null;
            Banner = L.Format("Update.Downloading", version, 0);
            await manager.DownloadUpdatesAsync(update, percent =>
                _dispatcher.BeginInvoke(() => Banner = L.Format("Update.Downloading", version, percent)));

            Banner = L.Format("Update.Restarting", version);
            Log.Warning($"Updating to {version}");
            await Task.Delay(RestartNotice);
            _beforeRestart();
            await StopTrackerAsync();
            manager.ApplyUpdatesAndRestart(update); // exits this process; the new version starts the tracker
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Offline, GitHub unreachable, a broken download: try again later, never bother the user.
            Log.Warning($"Update check failed: {ex.Message}");
            Banner = null;
            LastResult = manual ? L["Update.Failed"] : null;
        }
        finally
        {
            _busy = false;
        }
    }

    public void Dispose() => _timer.Stop();

    /// <summary>Uninstall: stop the tracker (its files are about to be deleted) and remove it from autostart.</summary>
    public static void PrepareForUninstall()
    {
        try
        {
            StopTrackerAsync().GetAwaiter().GetResult();
            Autostart.Set(false, string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Warning($"Uninstall cleanup failed: {ex.Message}");
        }
    }

    /// <summary>Asks the tracker of this install to exit and waits for it (and its supervisor); kills it after 5 s.</summary>
    internal static async Task StopTrackerAsync()
    {
        var folder = AppContext.BaseDirectory;
        TrackerClient.Send(TrackerCommand.Shutdown);
        for (var i = 0; i < 50; i++)
        {
            if (TrackersIn(folder).Count == 0)
            {
                return;
            }

            await Task.Delay(100);
        }

        foreach (var process in TrackersIn(folder))
        {
            using (process)
            {
                process.Kill();
            }
        }
    }

    private static List<Process> TrackersIn(string folder)
    {
        var result = new List<Process>();
        foreach (var process in Process.GetProcessesByName("AimOdometer.Tracker"))
        {
            string? path = null;
            try
            {
                path = process.MainModule?.FileName;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Exited meanwhile.
            }

            if (path is not null && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(process);
            }
            else
            {
                process.Dispose();
            }
        }

        return result;
    }
}
