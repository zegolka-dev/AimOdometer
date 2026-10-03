using AimOdometer.App.Localization;
using AimOdometer.App.Services;
using AimOdometer.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AimOdometer.App.ViewModels;

/// <summary>The window: navigation, live tracker status, onboarding and calibration overlays.</summary>
public sealed partial class MainViewModel : ObservableObject, ICalibrationHost, IDisposable
{
    private readonly AppData _data;

    public MainViewModel(AppData data, CloudService cloud)
    {
        _data = data;
        Tracker = new TrackerConnection();
        Pages =
        [
            new OverviewViewModel(data),
            new GamesViewModel(data),
            new StatisticsViewModel(data),
            new AchievementsViewModel(data),
            new MapViewModel(data),
            new GearViewModel(data, this),
            new SettingsViewModel(data, this, cloud),
        ];
        CurrentPage = Pages[0];
        foreach (var page in Pages)
        {
            // Mouse, keyboard (arrow keys in the radio group) and accessibility tools all select through IsCurrent.
            page.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(PageViewModel.IsCurrent) && sender is PageViewModel { IsCurrent: true } selected && selected != CurrentPage)
                {
                    CurrentPage = selected;
                }
            };
        }

        if (data.Setting(OnboardingViewModel.DoneSettingKey) != "1")
        {
            Onboarding = new OnboardingViewModel(data, this, () => Onboarding = null);
        }

        Tracker.Updated += (_, _) => OnTrackerUpdated();
        Loc.Instance.LanguageChanged += (_, _) => CurrentPage.Refresh();
        data.Changed += (_, _) => CurrentPage.Refresh();
    }

    public IReadOnlyList<PageViewModel> Pages { get; }

    public TrackerConnection Tracker { get; }

    [ObservableProperty]
    public partial PageViewModel CurrentPage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverlay), nameof(IsOnboardingVisible), nameof(IsCalibrationVisible))]
    public partial OnboardingViewModel? Onboarding { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverlay), nameof(IsOnboardingVisible), nameof(IsCalibrationVisible))]
    public partial CalibrationViewModel? Calibration { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverlay), nameof(IsShareVisible))]
    public partial ShareViewModel? Share { get; set; }

    public bool IsShareVisible => Share is not null;

    /// <summary>Calibration can open on top of onboarding; then only calibration is shown.</summary>
    public bool IsOnboardingVisible => Onboarding is not null && Calibration is null;

    public bool IsCalibrationVisible => Calibration is not null;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial StatusKind Status { get; set; } = StatusKind.Unknown;

    /// <summary>True while a modal overlay (onboarding, calibration) is shown.</summary>
    public bool HasOverlay => Onboarding is not null || Calibration is not null || Share is not null;

    /// <summary>Raw mouse reports from the window, routed to whichever overlay is open.</summary>
    public void OnRawReport(RawMouseReport report)
    {
        Calibration?.OnReport(report);
    }

    public void Start()
    {
        CurrentPage.Refresh();
        _ = StartTrackerLinkAsync();
    }

    /// <summary>Checks which folder the tracker writes to before showing any live numbers from it.</summary>
    private async Task StartTrackerLinkAsync()
    {
        await EnsureTrackerAsync();
        Tracker.Start();
    }

    partial void OnCurrentPageChanged(PageViewModel oldValue, PageViewModel newValue)
    {
        if (oldValue is null) // the generator passes null on the very first assignment
        {
            newValue.IsCurrent = true;
            return; // first assignment in the constructor; Start() refreshes once the window exists
        }

        oldValue.IsCurrent = false;
        newValue.IsCurrent = true;
        newValue.Refresh();
    }

    partial void OnOnboardingChanged(OnboardingViewModel? value)
    {
        if (value is null)
        {
            CurrentPage.Refresh(); // onboarding may have changed units, language or DPI
        }
    }

    /// <summary>Opens the share overlay with a card kind (name of a <see cref="ShareKind"/>).</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void OpenShare(string kind) =>
        Share = new ShareViewModel(_data, Enum.TryParse<ShareKind>(kind, out var k) ? k : ShareKind.Day, () => Share = null);

    public void ShowCalibration(DeviceRecord device, Action<double?> onClosed)
    {
        Calibration = new CalibrationViewModel(_data, device, dpi =>
        {
            Calibration = null;
            onClosed(dpi);
        });
        Calibration.Start();
    }

    private string? _trackerFolder;

    private async Task EnsureTrackerAsync()
    {
        await TrackerConnection.EnsureRunningAsync();
        _trackerFolder = (await Task.Run(Core.Ipc.TrackerClient.GetInfo))?.DataDirectory;
        await Tracker.PollAsync();
    }

    /// <summary>The running tracker writes somewhere else than this window reads: say so instead of showing empty data.</summary>
    private bool FolderMismatch =>
        _trackerFolder is { } folder && !Core.AppIdentity.SameFolder(folder, Core.AppIdentity.DataDirectory);

    private void OnTrackerUpdated()
    {
        var L = Loc.Instance;
        if (Tracker.Status is not { } status)
        {
            Status = StatusKind.Stopped;
            StatusText = L["Status.NotRunning"];
            return;
        }

        if (FolderMismatch)
        {
            Status = StatusKind.Stopped;
            StatusText = L.Format("Status.OtherFolder", _trackerFolder);
            return;
        }

        if (status.Paused)
        {
            Status = StatusKind.Paused;
            StatusText = status.PausedUntilUtc is { } until
                ? L.Format("Status.PausedUntil", until.ToLocalTime().ToString("t", L.Culture))
                : L["Status.Paused"];
        }
        else
        {
            Status = StatusKind.Tracking;
            StatusText = L["Status.Tracking"];
        }

        CurrentPage.OnLiveUpdate(Tracker);
    }

    public void Dispose() => Tracker.Dispose();
}

public enum StatusKind
{
    Unknown,
    Tracking,
    Paused,
    Stopped,
}
