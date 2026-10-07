using System.Globalization;
using AimOdometer.App.Localization;
using AimOdometer.App.Services;
using AimOdometer.Core;
using AimOdometer.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AimOdometer.App.ViewModels;

/// <summary>
/// First run: tray icon → language → units → mouse and DPI → autostart. Short, and every choice can be changed
/// later in Settings / Gear. Steam sign-in is added here in phase 7.
/// </summary>
public sealed partial class OnboardingViewModel : ObservableObject
{
    public const string DoneSettingKey = "onboarding_done";
    public const int StepCount = 5;

    private readonly AppData _data;
    private readonly Action _finished;

    public OnboardingViewModel(AppData data, ICalibrationHost calibration, Action finished)
    {
        _data = data;
        _finished = finished;
        Mice = new GearViewModel(data, calibration);
        Languages = Loc.Available();
        Language = Languages.FirstOrDefault(l => l.Code == Loc.Instance.Code) ?? (Languages.Count > 0 ? Languages[0] : null);
        Metric = data.Units == UnitSystem.Metric;
        Autostart = true;
        CountElevatedGames = !ElevatedTask.IsElevated;
        ShowTrayIcon = true;
    }

    public IReadOnlyList<LanguageInfo> Languages { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcome), nameof(IsLanguage), nameof(IsUnits), nameof(IsMouse), nameof(IsAutostart), nameof(IsLast), nameof(StepText), nameof(CanGoBack))]
    public partial int Step { get; set; }

    public bool IsWelcome => Step == 0;

    public bool IsLanguage => Step == 1;

    public bool IsUnits => Step == 2;

    public bool IsMouse => Step == 3;

    public bool IsAutostart => Step == 4;

    public bool IsLast => Step == StepCount - 1;

    public bool CanGoBack => Step > 0;

    public string StepText => Loc.Instance.Format("Onboarding.Step", Step + 1, StepCount);

    [ObservableProperty]
    public partial LanguageInfo? Language { get; set; }

    [ObservableProperty]
    public partial bool Metric { get; set; }

    [ObservableProperty]
    public partial bool Autostart { get; set; }

    /// <summary>On by default: games that run as administrator (Genshin, some EA app games) are counted too.</summary>
    [ObservableProperty]
    public partial bool CountElevatedGames { get; set; }

    [ObservableProperty]
    public partial bool ShowTrayIcon { get; set; }

    /// <summary>The user's mice with editable DPI (same controls as Settings and Gear).</summary>
    public GearViewModel Mice { get; }

    partial void OnLanguageChanged(LanguageInfo? value)
    {
        if (value is not null)
        {
            Loc.Instance.SetLanguage(value.Code);
            OnPropertyChanged(nameof(StepText));
        }
    }

    partial void OnMetricChanged(bool value) => Format.Units = value ? UnitSystem.Metric : UnitSystem.Imperial;

    [RelayCommand]
    private void Back()
    {
        if (Step > 0)
        {
            Step--;
        }
    }

    [RelayCommand]
    private void Next()
    {
        if (IsMouse && !SaveDpi())
        {
            return;
        }

        if (!IsLast)
        {
            Step++;
            if (IsMouse)
            {
                Mice.Refresh();
            }

            return;
        }

        Finish();
    }

    [RelayCommand]
    private void Skip() => Finish();

    /// <summary>Applies DPI values typed but not saved yet. Returns false when one of them is invalid.</summary>
    private bool SaveDpi()
    {
        foreach (var row in Mice.Devices)
        {
            if (row.DpiText != row.Device.Dpi.ToString("0.##", CultureInfo.InvariantCulture))
            {
                row.SaveDpiCommand.Execute(null);
            }
        }

        return Mice.Devices.All(r => r.DpiError is null);
    }

    private void Finish()
    {
        _data.SetSetting(SettingKeys.Language, Language?.Code ?? "auto");
        _data.SetSetting(SettingKeys.Units, Metric ? "metric" : "imperial");
        if (SettingsViewModel.TrackerExe() is { } exe)
        {
            Core.Autostart.Set(Autostart, exe);
            TrayIconVisibility.Promote(exe, ShowTrayIcon);
        }

        _data.SetSetting(SettingKeys.Autostart, Autostart ? "1" : "0");
        _data.SetSetting(DoneSettingKey, "1");
        _finished();
        if (CountElevatedGames && !AppIdentity.IsDataDirectoryOverridden && _data.Setting(SettingKeys.ElevatedTracker) != "1")
        {
            _ = SettingsViewModel.SetElevatedAsync(_data, true); // one UAC prompt; declining keeps the normal tracker
        }
    }
}
