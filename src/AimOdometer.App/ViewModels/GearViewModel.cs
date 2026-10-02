using System.Collections.ObjectModel;
using System.Globalization;
using AimOdometer.App.Localization;
using AimOdometer.App.Services;
using AimOdometer.Core.Input;
using AimOdometer.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AimOdometer.App.ViewModels;

/// <summary>Opens the DPI calibration over the window and reports the measured DPI (null if cancelled).</summary>
public interface ICalibrationHost
{
    void ShowCalibration(DeviceRecord device, Action<double?> onClosed);
}

/// <summary>One mouse (or touchpad) with its DPI, name and whether it counts toward statistics.</summary>
public sealed partial class DeviceRowViewModel : ObservableObject
{
    private readonly GearViewModel _owner;
    private bool _loading = true;

    public DeviceRowViewModel(GearViewModel owner, DeviceRecord device, double centimeters)
    {
        _owner = owner;
        Device = device;
        Name = device.Name;
        DpiText = device.Dpi.ToString("0.##", CultureInfo.InvariantCulture);
        Included = !device.Excluded;
        Distance = Format.Distance(centimeters);
        _loading = false;
    }

    public DeviceRecord Device { get; }

    public string Distance { get; }

    public string KindText => Device.Kind switch
    {
        DeviceKind.Touchpad => Loc.Instance["Gear.Touchpad"],
        DeviceKind.Software => Loc.Instance["Gear.Software"],
        _ => Loc.Instance["Gear.Mouse"],
    };

    public string Glyph => Device.Kind == DeviceKind.Touchpad ? "" : "";

    public bool CanCalibrate => Device.Kind != DeviceKind.Software;

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string DpiText { get; set; }

    [ObservableProperty]
    public partial bool Included { get; set; }

    [ObservableProperty]
    public partial string? DpiError { get; set; }

    partial void OnIncludedChanged(bool value)
    {
        if (!_loading)
        {
            _owner.SetIncluded(this, value);
        }
    }

    [RelayCommand]
    private void SaveName() => _owner.Rename(this, Name);

    [RelayCommand]
    private void SaveDpi()
    {
        if (!double.TryParse(DpiText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var dpi) || dpi is < 50 or > 50_000)
        {
            DpiError = Loc.Instance["Gear.DpiInvalid"];
            return;
        }

        DpiError = null;
        _owner.SetDpi(this, dpi);
    }

    [RelayCommand]
    private void SetPreset(string preset)
    {
        DpiText = preset;
        SaveDpi();
    }

    [RelayCommand]
    private void Calibrate() => _owner.Calibrate(this);
}

/// <summary>Mice and their DPI. Pads and glide wear come in phase 5.</summary>
public sealed partial class GearViewModel(AppData data, ICalibrationHost calibration) : PageViewModel(data)
{
    public static IReadOnlyList<string> DpiPresets { get; } = ["400", "800", "1200", "1600", "3200"];

    public override string TitleKey => "Nav.Gear";

    public override string Icon => "";

    public ObservableCollection<DeviceRowViewModel> Devices { get; } = [];

    [ObservableProperty]
    public partial string? Saved { get; set; }

    public override void Refresh()
    {
        Devices.Clear();
        foreach (var (device, centimeters) in Data.Store.GetDeviceTotals().Where(d => d.Device.Kind != DeviceKind.Software || d.Centimeters > 0))
        {
            Devices.Add(new DeviceRowViewModel(this, device, centimeters));
        }
    }

    internal void Rename(DeviceRowViewModel row, string name)
    {
        Data.Store.SetDeviceName(row.Device.Id, name);
        Data.NotifyChanged(reloadTracker: true);
        Saved = Loc.Instance["Common.Saved"];
    }

    internal void SetDpi(DeviceRowViewModel row, double dpi)
    {
        // The tracker flushes movement measured with the old DPI first, then switches (ReloadSettings).
        Data.Store.SetDeviceDpi(row.Device.Id, dpi);
        Data.NotifyChanged(reloadTracker: true);
        Saved = Loc.Instance.Format("Gear.DpiSaved", row.Name, Format.Number(dpi));
    }

    internal void SetIncluded(DeviceRowViewModel row, bool included)
    {
        Data.Store.SetDeviceExcluded(row.Device.Id, !included);
        Data.NotifyChanged(reloadTracker: true);
    }

    internal void Calibrate(DeviceRowViewModel row) => calibration.ShowCalibration(row.Device, dpi =>
    {
        if (dpi is { } measured)
        {
            row.DpiText = measured.ToString("0", CultureInfo.InvariantCulture);
            SetDpi(row, measured);
            Refresh();
        }
    });
}
