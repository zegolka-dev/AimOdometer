using System.Diagnostics;
using AimOdometer.App.Localization;
using AimOdometer.App.Services;
using AimOdometer.Core;
using AimOdometer.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AimOdometer.App.ViewModels;

/// <summary>Language, units, autostart, tray icon visibility, data folder.</summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private bool _loading;

    public SettingsViewModel(AppData data)
        : base(data)
    {
        Languages = [new LanguageInfo("auto", string.Empty), .. Loc.Available()];
    }

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
        var code = Data.Setting(SettingKeys.Language) ?? "auto";
        Language = Languages.FirstOrDefault(l => l.Code == code) ?? Languages[0];
        Metric = Data.Units == UnitSystem.Metric;
        Autostart = Core.Autostart.IsEnabled();
        ShowTrayIcon = TrackerExe() is { } exe && TrayIconVisibility.IsPromoted(exe);
        Version = typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "?";
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

    partial void OnShowTrayIconChanged(bool value)
    {
        if (!_loading && TrackerExe() is { } exe)
        {
            TrayIconVisibility.Promote(exe, value);
        }
    }

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
