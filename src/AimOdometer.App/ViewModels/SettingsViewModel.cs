using System.Diagnostics;
using AimOdometer.App.Localization;
using AimOdometer.App.Services;
using AimOdometer.Core;
using AimOdometer.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AimOdometer.App.ViewModels;

/// <summary>Steam account and cloud, language, units, mice, autostart, tray icon visibility, data folder.</summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private bool _loading;

    public SettingsViewModel(AppData data, ICalibrationHost calibration, CloudService cloud, Updates updates)
        : base(data)
    {
        Updates = updates;
        Languages = [new LanguageInfo("auto", string.Empty), .. Loc.Available()];
        Mice = new GearViewModel(data, calibration);
        Account = new AccountViewModel(data, cloud);
    }

    /// <summary>Automatic updates; "Check for updates" and its answer.</summary>
    public Updates Updates { get; }

    /// <summary>Sign in with Steam, sync status and the account's PCs.</summary>
    public AccountViewModel Account { get; }

    /// <summary>The same mouse and DPI controls as on the Gear page.</summary>
    public GearViewModel Mice { get; }

    public override string TitleKey => "Nav.Settings";

    public override string Icon => "";

    public IReadOnlyList<LanguageInfo> Languages { get; }

    [ObservableProperty]
    public partial LanguageInfo? Language { get; set; }

    [ObservableProperty]
    public partial bool Metric { get; set; } = true;

    [ObservableProperty]
    public partial bool Autostart { get; set; }

    [ObservableProperty]
    public partial bool ShowTrayIcon { get; set; }

    [ObservableProperty]
    public partial bool Notifications { get; set; } = true;

    [ObservableProperty]
    public partial bool AutoUpdate { get; set; } = true;

    /// <summary>"Count games that run as administrator": the tracker runs elevated through a Task Scheduler task.</summary>
    [ObservableProperty]
    public partial bool CountElevatedGames { get; set; }

    [ObservableProperty]
    public partial string? ElevatedMessage { get; set; }

    [ObservableProperty]
    public partial string Version { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StartupTime { get; set; } = string.Empty;

    /// <summary>Measured time from process start to the first rendered frame.</summary>
    public static TimeSpan? MeasuredStartup { get; set; }

    public static string LanguageLabel(LanguageInfo language) =>
        language.Code == "auto" ? Loc.Instance["Settings.LanguageAuto"] : language.NativeName;

    public override void Refresh()
    {
        _loading = true;
        Mice.Refresh();
        Account.Refresh();
        var code = Data.Setting(SettingKeys.Language) ?? "auto";
        Language = Languages.FirstOrDefault(l => l.Code == code) ?? Languages[0];
        Metric = Data.Units == UnitSystem.Metric;
        Autostart = Core.Autostart.IsEnabled();
        ShowTrayIcon = TrackerExe() is { } exe && TrayIconVisibility.IsPromoted(exe);
        Notifications = Data.Setting(SettingKeys.Notifications) != "0";
        AutoUpdate = Data.Setting(SettingKeys.AutoUpdate) != "0";
        CountElevatedGames = Data.Setting(SettingKeys.ElevatedTracker) == "1";
        Version = AppIdentity.Version;
        StartupTime = MeasuredStartup is { } t ? Loc.Instance.Format("Settings.StartupTime", Format.Number(t.TotalMilliseconds)) : string.Empty;
        _loading = false;
    }

    partial void OnLanguageChanged(LanguageInfo? value)
    {
        if (_loading || value is null)
        {
            return;
        }

        Data.SetSetting(SettingKeys.Language, value.Code);
        Loc.Instance.SetLanguage(Loc.Resolve(value.Code));
    }

    partial void OnMetricChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        Data.SetSetting(SettingKeys.Units, value ? "metric" : "imperial");
        Format.Units = value ? UnitSystem.Metric : UnitSystem.Imperial;
        Data.NotifyChanged(reloadTracker: false);
    }

    partial void OnAutostartChanged(bool value)
    {
        if (_loading || TrackerExe() is not { } exe)
        {
            return;
        }

        Core.Autostart.Set(value, exe);
        Data.SetSetting(SettingKeys.Autostart, value ? "1" : "0");
    }

    partial void OnNotificationsChanged(bool value)
    {
        if (!_loading)
        {
            Data.SetSetting(SettingKeys.Notifications, value ? "1" : "0");
        }
    }

    [RelayCommand]
    private Task CheckUpdatesAsync() => Updates.CheckNowAsync();

    partial void OnCountElevatedGamesChanged(bool value)
    {
        if (!_loading)
        {
            _ = SwitchElevatedAsync(value);
        }
    }

    /// <summary>
    /// On: register the elevated task (one UAC prompt), stop the normal tracker, start the elevated one.
    /// Off: delete the task (one UAC prompt), stop the elevated tracker, start the normal one.
    /// </summary>
    private async Task SwitchElevatedAsync(bool on)
    {
        var L = Loc.Instance;
        if (AppIdentity.IsDataDirectoryOverridden)
        {
            _loading = true;
            CountElevatedGames = !on; // a test window never touches the real tracker or Task Scheduler
            _loading = false;
            return;
        }

        ElevatedMessage = L["Settings.ElevatedWorking"];
        var tracker = TrackerConnection.TrackerExePath;
        var ok = on
            ? System.IO.File.Exists(tracker) && await Task.Run(() => ElevatedTask.Create(tracker))
            : await Task.Run(ElevatedTask.Delete) || !await Task.Run(ElevatedTask.Exists);
        if (!ok)
        {
            _loading = true;
            CountElevatedGames = !on; // UAC was declined: nothing changed
            _loading = false;
            ElevatedMessage = L["Settings.ElevatedDeclined"];
            return;
        }

        Data.Store.SetSetting(SettingKeys.ElevatedTracker, on ? "1" : "0");
        await Updates.StopTrackerAsync();
        var running = await TrackerConnection.EnsureRunningAsync(elevated: on);
        ElevatedMessage = !running ? L["Settings.ElevatedNotRunning"] : on ? L["Settings.ElevatedOn"] : L["Settings.ElevatedOff"];
    }

    partial void OnAutoUpdateChanged(bool value)
    {
        if (!_loading)
        {
            Data.Store.SetSetting(SettingKeys.AutoUpdate, value ? "1" : "0"); // the window's own setting
        }
    }

    partial void OnShowTrayIconChanged(bool value)
    {
        if (!_loading && TrackerExe() is { } exe)
        {
            TrayIconVisibility.Promote(exe, value);
        }
    }

    [RelayCommand]
    private void ExportCsv()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"AimOdometer-{DateTime.Now:yyyy-MM-dd}.csv",
            Filter = "CSV (*.csv)|*.csv",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            LocalData.ExportCsv(Data, dialog.FileName);
            DataMessage = Loc.Instance.Format("Settings.Exported", dialog.FileName);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            DataMessage = ex.Message;
        }
    }

    [RelayCommand]
    private static async Task DeleteLocalDataAsync()
    {
        var L = Loc.Instance;
        var answer = System.Windows.MessageBox.Show(System.Windows.Application.Current.MainWindow, L["Settings.DeleteConfirm"], L["Settings.DeleteTitle"],
            System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
        if (answer == System.Windows.MessageBoxResult.Yes)
        {
            await LocalData.DeleteAllAndRestartAsync();
        }
    }

    [ObservableProperty]
    public partial string? DataMessage { get; set; }

    [RelayCommand]
    private static void OpenDataFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppIdentity.DataDirectory}\"") { UseShellExecute = false })?.Dispose();

    /// <summary>The tracker exe: next to the app when installed, otherwise the running tracker's own path.</summary>
    public static string? TrackerExe()
    {
        if (System.IO.File.Exists(TrackerConnection.TrackerExePath))
        {
            return TrackerConnection.TrackerExePath;
        }

        if (Core.Ipc.TrackerClient.GetProcessId() is not { } pid)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
