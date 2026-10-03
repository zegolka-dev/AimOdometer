using System.Diagnostics;
using System.Windows.Threading;
using AimOdometer.App.Localization;
using AimOdometer.Cloud;
using AimOdometer.Core.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AimOdometer.App.Services;

public enum CloudState
{
    SignedOut,
    SigningIn,
    Syncing,
    Ready,
}

/// <summary>
/// The window's side of the cloud: Steam sign-in, and sync on open, every 15 minutes while open and on close
/// (assumption A9: the tracker never syncs). Without a session nothing here touches the network.
/// </summary>
public sealed partial class CloudService : ObservableObject, IDisposable
{
    public static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(15);

    private readonly AppData _data;
    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private CancellationTokenSource? _signIn;
    private bool _syncing;

    public CloudService(AppData data)
    {
        _data = data;
        Client = new CloudClient(new SessionStore(SessionStore.DefaultPath));
        // The client may report from a pool thread (token refresh, sign-out on 401).
        Client.SessionChanged += (_, _) => _dispatcher.BeginInvoke(UpdateState);
        // Changed rules, devices or DPI change history: upload everything again next time.
        data.Changed += (_, _) => CloudSync.RequestFullUpload(data.Store);
        _timer = new DispatcherTimer { Interval = SyncInterval };
        _timer.Tick += async (_, _) => await SyncAsync();
        LastSync = CloudSync.LastSync(data.Store);
        UpdateState();
    }

    public CloudClient Client { get; }

    [ObservableProperty]
    public partial CloudState State { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? LastSync { get; set; }

    /// <summary>Last error in the user's language, null after a success.</summary>
    [ObservableProperty]
    public partial string? Problem { get; set; }

    /// <summary>Raised after a sync uploaded something, so pages can reload cloud numbers.</summary>
    public event EventHandler? Synced;

    public void Start()
    {
        _timer.Start();
        _ = RefreshProfileThenSyncAsync();
    }

    /// <summary>Name and avatar from Steam (the server asks Steam at most every six hours), then a sync.</summary>
    private async Task RefreshProfileThenSyncAsync()
    {
        if (Client.IsSignedIn)
        {
            try
            {
                await Client.RefreshProfileAsync(CancellationToken.None);
            }
            catch (CloudException ex)
            {
                Log.Info($"Profile refresh failed: {ex.Error} {ex.Message}");
            }
        }

        await SyncAsync();
    }

    public async Task SignInAsync()
    {
        if (State == CloudState.SigningIn)
        {
            return;
        }

        var L = Loc.Instance;
        _signIn = new CancellationTokenSource();
        State = CloudState.SigningIn;
        Problem = null;
        try
        {
            var pages = new SignInPages(L["Cloud.PageSuccessTitle"], L["Cloud.PageSuccessText"], L["Cloud.PageFailureTitle"], L["Cloud.PageFailureText"]);
            await SteamSignIn.SignInAsync(Client, OpenBrowser, pages, _signIn.Token);
            UpdateState();
            await RefreshProfileThenSyncAsync();
        }
        catch (OperationCanceledException)
        {
            // The user pressed Cancel.
        }
        catch (CloudException ex)
        {
            Log.Warning($"Steam sign-in failed: {ex.Error} {ex.Message}");
            Problem = Describe(ex.Error);
        }
        finally
        {
            _signIn.Dispose();
            _signIn = null;
            UpdateState();
        }
    }

    public void CancelSignIn() => _signIn?.Cancel();

    public async Task SyncAsync()
    {
        if (!Client.IsSignedIn || _syncing)
        {
            return;
        }

        _syncing = true;
        State = CloudState.Syncing;
        try
        {
            var outcome = await CloudSync.RunAsync(Client, _data.Store, _data.Catalog, TimeProvider.System, CancellationToken.None);
            LastSync = outcome.At;
            Problem = null;
            if (outcome.Rejected > 0)
            {
                Log.Warning($"Cloud sync: the server rejected {outcome.Rejected} implausible rows");
            }

            Synced?.Invoke(this, EventArgs.Empty);
        }
        catch (CloudException ex)
        {
            Log.Warning($"Cloud sync failed: {ex.Error} {ex.Message}");
            Problem = Describe(ex.Error);
        }
        finally
        {
            _syncing = false;
            UpdateState();
        }
    }

    /// <summary>On exit: one last upload, waiting a few seconds at most (it is retried next time anyway).</summary>
    public void SyncBeforeExit()
    {
        if (!Client.IsSignedIn || _syncing)
        {
            return;
        }

        try
        {
            // No synchronization context in Task.Run, and the window thread just waits: the database is not shared.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            Task.Run(() => CloudSync.RunAsync(Client, _data.Store, _data.Catalog, TimeProvider.System, timeout.Token))
                .Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException ex)
        {
            Log.Info($"Sync on exit did not finish: {ex.InnerException?.Message}");
        }
    }

    public async Task SignOutAsync()
    {
        await Client.SignOutAsync(CancellationToken.None);
        Problem = null;
    }

    public async Task<bool> DeleteAccountAsync()
    {
        try
        {
            await Client.DeleteAccountAsync(CancellationToken.None);
            _data.Store.SetSetting(Core.Storage.SettingKeys.CloudSyncedUser, string.Empty);
            Problem = null;
            return true;
        }
        catch (CloudException ex)
        {
            Log.Warning($"Account deletion failed: {ex.Error} {ex.Message}");
            Problem = Describe(ex.Error);
            return false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _signIn?.Cancel();
        Client.Dispose();
    }

    public static string Describe(CloudError error) => Loc.Instance[$"Cloud.Error.{error}"];

    private void UpdateState()
    {
        State = _signIn is not null ? CloudState.SigningIn
            : !Client.IsSignedIn ? CloudState.SignedOut
            : _syncing ? CloudState.Syncing
            : CloudState.Ready;
        OnPropertyChanged(nameof(Client)); // the session (name, avatar) may have changed without a state change
    }

    private static bool OpenBrowser(Uri url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Warning($"Could not open the browser: {ex.Message}");
            return false;
        }
    }
}
