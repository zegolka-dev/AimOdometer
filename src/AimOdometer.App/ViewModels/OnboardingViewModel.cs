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
    private readonly ICalibrationHost _calibration;
    private readonly Action _finished;
    private readonly Dictionary<nint, DeviceRecord?> _deviceByHandle = [];

    public OnboardingViewModel(AppData data, ICalibrationHost calibration, Action finished)
    {
        _data = data;
        _calibration = calibration;
        _finished = finished;
        Languages = Loc.Available();
        Language = Languages.FirstOrDefault(l => l.Code == Loc.Instance.Code) ?? (Languages.Count > 0 ? Languages[0] : null);
        Metric = data.Units == UnitSystem.Metric;
        Autostart = true;
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

    [ObservableProperty]
    public partial bool ShowTrayIcon { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMouse), nameof(MouseText))]
    public partial DeviceRecord? Mouse { get; set; }

    public string MouseText => Mouse?.Name ?? Loc.Instance["Onboarding.MoveYourMouse"];

    [ObservableProperty]
    public partial string DpiText { get; set; } = "800";

    [ObservableProperty]
    public partial string? DpiError { get; set; }

    public bool HasMouse => Mouse is not null;

    public static IReadOnlyList<string> DpiPresets => GearViewModel.DpiPresets;

    partial void OnLanguageChanged(LanguageInfo? value)
    {
        if (value is not null)
        {
            Loc.Instance.SetLanguage(value.Code);
            OnPropertyChanged(nameof(StepText));
            OnPropertyChanged(nameof(MouseText));
        }
    }

    partial void OnMetricChanged(bool value) => Format.Units = value ? UnitSystem.Metric : UnitSystem.Imperial;

    /// <summary>On the mouse step, the mouse the user moves becomes the selected one.</summary>
    public void OnReport(RawMouseReport report)
    {
        if (!IsMouse || report.Device == 0 || report.Absolute || (report.Dx | report.Dy) == 0)
        {
            return;
        }

        if (!_deviceByHandle.TryGetValue(report.Device, out var device))
        {
            device = RawMouseListener.FindDevice(_data.Store, report.Device);
            _deviceByHandle[report.Device] = device;
        }

        if (device is not null && device.Id != Mouse?.Id)
        {
            Mouse = device;
            DpiText = device.Dpi.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    [RelayCommand]
    private void SetPreset(string preset) => DpiText = preset;

    [RelayCommand]
    private void Calibrate()
    {
        if (Mouse is { } mouse)
        {
            _calibration.ShowCalibration(mouse, dpi =>
            {
                if (dpi is { } measured)
                {
                    DpiText = measured.ToString("0", CultureInfo.InvariantCulture);
                }
            });
        }
    }

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
            return;
        }

        Finish();
    }

    [RelayCommand]
    private void Skip() => Finish();

    private bool SaveDpi()
    {
        if (Mouse is null)
        {
            return true; // no mouse moved: keep the default, it can be set later in Gear
        }

        if (!double.TryParse(DpiText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var dpi) || dpi is < 50 or > 50_000)
        {
            DpiError = Loc.Instance["Gear.DpiInvalid"];
            return false;
        }

        DpiError = null;
        _data.Store.SetDeviceDpi(Mouse.Id, dpi);
        _data.NotifyChanged(reloadTracker: true);
        return true;
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
    }
}
