using System.Collections.ObjectModel;
using AimOdometer.App.Localization;
using AimOdometer.App.Services;
using AimOdometer.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AimOdometer.App.ViewModels;

/// <summary>A reference length the user moves the mouse along.</summary>
public sealed record CalibrationReference(string NameKey, double Centimeters)
{
    public string Name => Loc.Instance[NameKey];
}

/// <summary>
/// DPI calibration: hold the left button, move the mouse exactly along a known length, release. Three tries,
/// averaged. Uses the straight-line displacement of each stroke, so small wobbles do not inflate the result.
/// </summary>
public sealed partial class CalibrationViewModel : ObservableObject
{
    public const int Attempts = 3;

    private readonly AppData _data;
    private readonly Action<double?> _close;
    private readonly Dictionary<nint, long?> _deviceByHandle = [];
    private bool _measuring;
    private long _sumX;
    private long _sumY;

    public CalibrationViewModel(AppData data, DeviceRecord device, Action<double?> close)
    {
        _data = data;
        _close = close;
        Device = device;
        Reference = References[0];
    }

    public DeviceRecord Device { get; }

    public static IReadOnlyList<CalibrationReference> References { get; } =
    [
        new("Calibration.Ruler10", 10),
        new("Calibration.BankCard", 8.56),
    ];

    [ObservableProperty]
    public partial CalibrationReference Reference { get; set; }

    [ObservableProperty]
    public partial string Instruction { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LiveCounts { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Result { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsDone { get; set; }

    [ObservableProperty]
    public partial bool IsSpreadHigh { get; set; }

    public ObservableCollection<string> Samples { get; } = [];

    private readonly List<double> _dpiSamples = [];

    public double? MeasuredDpi => _dpiSamples.Count == Attempts ? _dpiSamples.Average() : null;

    partial void OnReferenceChanged(CalibrationReference value) => Restart();

    public void Start() => Restart();

    /// <summary>Feeds raw reports while the calibration screen is open.</summary>
    public void OnReport(RawMouseReport report)
    {
        if (IsDone || !IsTargetDevice(report.Device))
        {
            return;
        }

        if ((report.ButtonFlags & RawMouseReport.LeftDown) != 0)
        {
            _measuring = true;
            _sumX = 0;
            _sumY = 0;
        }

        if (_measuring && !report.Absolute)
        {
            _sumX += report.Dx;
            _sumY += report.Dy;
            LiveCounts = Loc.Instance.Format("Calibration.Counts", Format.Number(Math.Sqrt(((double)_sumX * _sumX) + ((double)_sumY * _sumY))));
        }

        if ((report.ButtonFlags & RawMouseReport.LeftUp) != 0 && _measuring)
        {
            _measuring = false;
            var counts = Math.Sqrt(((double)_sumX * _sumX) + ((double)_sumY * _sumY));
            if (counts < 50)
            {
                Instruction = Loc.Instance["Calibration.TooShort"];
                return;
            }

            var dpi = counts / (Reference.Centimeters / 2.54);
            _dpiSamples.Add(dpi);
            Samples.Add(Loc.Instance.Format("Calibration.Sample", _dpiSamples.Count, Format.Number(dpi)));
            UpdateState();
        }
    }

    [RelayCommand]
    private void Restart()
    {
        _dpiSamples.Clear();
        Samples.Clear();
        _measuring = false;
        IsDone = false;
        IsSpreadHigh = false;
        Result = string.Empty;
        LiveCounts = string.Empty;
        UpdateState();
    }

    [RelayCommand]
    private void Apply()
    {
        if (MeasuredDpi is { } dpi)
        {
            _close(Math.Round(dpi));
        }
    }

    [RelayCommand]
    private void Cancel() => _close(null);

    private void UpdateState()
    {
        var L = Loc.Instance;
        if (_dpiSamples.Count < Attempts)
        {
            Instruction = L.Format("Calibration.Instruction", Format.Number(Reference.Centimeters, "0.##"), _dpiSamples.Count + 1, Attempts);
            return;
        }

        var average = _dpiSamples.Average();
        var spread = (_dpiSamples.Max() - _dpiSamples.Min()) / average;
        var deviation = (average - Device.Dpi) / Device.Dpi;
        IsDone = true;
        IsSpreadHigh = spread > 0.05;
        Instruction = IsSpreadHigh ? L["Calibration.SpreadHigh"] : L["Calibration.Done"];
        Result = L.Format("Calibration.Result", Format.Number(average), Format.Percent(spread),
            Format.Number(Device.Dpi), (deviation >= 0 ? "+" : string.Empty) + Format.Percent(deviation));
    }

    private bool IsTargetDevice(nint handle)
    {
        if (!_deviceByHandle.TryGetValue(handle, out var id))
        {
            id = RawMouseListener.FindDevice(_data.Store, handle)?.Id;
            _deviceByHandle[handle] = id;
        }

        return id == Device.Id;
    }
}
