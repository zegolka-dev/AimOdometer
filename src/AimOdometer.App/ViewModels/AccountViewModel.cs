using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using AimOdometer.App.Localization;
using AimOdometer.App.Services;
using AimOdometer.Cloud;
using AimOdometer.Core.Stats;
using AimOdometer.Core.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AimOdometer.App.ViewModels;

/// <summary>A PC of the account, as listed in Settings.</summary>
public sealed record AccountPc(string Name, string Details, bool IsThisPc);

/// <summary>Settings › Steam account and cloud: sign-in, sync status, totals over all PCs, sign-out, account deletion.</summary>
public sealed partial class AccountViewModel : ObservableObject
{
    private readonly AppData _data;
    private readonly CloudService _cloud;

    public AccountViewModel(AppData data, CloudService cloud)
    {
        _data = data;
        _cloud = cloud;
        cloud.PropertyChanged += OnCloudChanged;
        cloud.Synced += (_, _) => _ = LoadCloudNumbersAsync();
        UpdateFromCloud();
    }

    [ObservableProperty]
    public partial bool IsSignedIn { get; set; }

    [ObservableProperty]
    public partial bool IsSigningIn { get; set; }

    [ObservableProperty]
    public partial bool IsSignedOut { get; set; }

    [ObservableProperty]
    public partial string PersonaName { get; set; } = string.Empty;

    /// <summary>Steam avatar (https), null when there is none.</summary>
    [ObservableProperty]
    public partial Uri? Avatar { get; set; }

    [ObservableProperty]
    public partial string SyncText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Problem { get; set; }

    [ObservableProperty]
    public partial string? TodayAll { get; set; }

    [ObservableProperty]
    public partial string? WeekAll { get; set; }

    [ObservableProperty]
    public partial string? AllTimeAll { get; set; }

    public ObservableCollection<AccountPc> Pcs { get; } = [];

    [ObservableProperty]
    public partial bool ShareWithFriends { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowInWorld { get; set; }

    private bool _loadingPrivacy;

    private static Loc L => Loc.Instance;

    /// <summary>Called when the Settings page opens.</summary>
    public void Refresh()
    {
        UpdateFromCloud();
        if (_cloud.Client.IsSignedIn)
        {
            _ = LoadCloudNumbersAsync();
        }
    }

    [RelayCommand]
    private Task SignInAsync() => _cloud.SignInAsync();

    [RelayCommand]
    private void CancelSignIn() => _cloud.CancelSignIn();

    [RelayCommand]
    private Task SyncNowAsync() => _cloud.SyncAsync();

    [RelayCommand]
    private async Task SignOutAsync()
    {
        await _cloud.SignOutAsync();
        ClearNumbers();
    }

    [RelayCommand]
    private async Task DeleteAccountAsync()
    {
        var answer = MessageBox.Show(Application.Current.MainWindow, L["Cloud.DeleteConfirm"], L["Cloud.DeleteTitle"],
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes && await _cloud.DeleteAccountAsync())
        {
            ClearNumbers();
            MessageBox.Show(Application.Current.MainWindow, L["Cloud.Deleted"], L["Cloud.DeleteTitle"], MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnCloudChanged(object? sender, PropertyChangedEventArgs e) => UpdateFromCloud();

    private void UpdateFromCloud()
    {
        var session = _cloud.Client.Session;
        IsSignedIn = session is not null;
        IsSigningIn = _cloud.State == CloudState.SigningIn;
        IsSignedOut = !IsSignedIn && !IsSigningIn;
        PersonaName = session is null ? string.Empty
            : session.PersonaName.Length > 0 ? session.PersonaName : session.SteamId;
        Avatar = session is not null && Uri.TryCreate(session.AvatarUrl, UriKind.Absolute, out var avatar) && avatar.Scheme == Uri.UriSchemeHttps ? avatar : null;
        Problem = _cloud.Problem;
        SyncText = _cloud.State switch
        {
            CloudState.Syncing => L["Cloud.Syncing"],
            _ when _cloud.LastSync is { } at => L.Format("Cloud.SyncedAt", at.ToLocalTime().ToString("g", L.Culture)),
            _ => L["Cloud.NotSyncedYet"],
        };
    }

    private async Task LoadCloudNumbersAsync()
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var days = await _cloud.Client.GetDailyTotalsAsync(new DateOnly(2020, 1, 1), CancellationToken.None);
            var weekStart = StatsSummary.StartOfWeek(today, CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);
            TodayAll = Format.Distance(days.Where(d => d.Day == today).Sum(d => d.Centimeters));
            WeekAll = Format.Distance(days.Where(d => d.Day >= weekStart && d.Day <= today).Sum(d => d.Centimeters));
            AllTimeAll = Format.Distance(days.Sum(d => d.Centimeters));

            var privacy = await _cloud.Client.GetPrivacyAsync(CancellationToken.None);
            _loadingPrivacy = true;
            ShareWithFriends = privacy.ShareWithFriends;
            ShowInWorld = privacy.ShowInWorld;
            _loadingPrivacy = false;

            var devices = await _cloud.Client.GetDevicesAsync(CancellationToken.None);
            var thisPc = _data.Setting(SettingKeys.CloudPcId);
            Pcs.Clear();
            foreach (var device in devices)
            {
                var isThis = Guid.TryParse(thisPc, out var id) && id == device.PcId;
                var name = isThis ? L["Cloud.ThisPc"] : device.Name.Length > 0 ? device.Name : L["Cloud.OtherPc"];
                Pcs.Add(new AccountPc(name, L.Format("Cloud.PcDetails", Format.Distance(device.Centimeters), device.LastSync.ToLocalTime().ToString("g", L.Culture)), isThis));
            }
        }
        catch (CloudException ex)
        {
            Problem = CloudService.Describe(ex.Error);
        }
    }

    partial void OnShareWithFriendsChanged(bool value) => _ = SavePrivacyAsync();

    partial void OnShowInWorldChanged(bool value) => _ = SavePrivacyAsync();

    private async Task SavePrivacyAsync()
    {
        if (_loadingPrivacy || !_cloud.Client.IsSignedIn)
        {
            return;
        }

        try
        {
            await _cloud.Client.SetPrivacyAsync(new Privacy(ShareWithFriends, ShowInWorld), CancellationToken.None);
        }
        catch (CloudException ex)
        {
            Problem = CloudService.Describe(ex.Error);
        }
    }

    private void ClearNumbers()
    {
        TodayAll = WeekAll = AllTimeAll = null;
        Pcs.Clear();
        UpdateFromCloud();
    }
}
