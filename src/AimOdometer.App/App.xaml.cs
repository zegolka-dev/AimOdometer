using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using AimOdometer.App.Localization;
using AimOdometer.App.Services;
using AimOdometer.App.ViewModels;
using AimOdometer.App.Views;
using AimOdometer.Core;
using AimOdometer.Core.Diagnostics;
using AimOdometer.Core.Storage;

namespace AimOdometer.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "WPF application objects are disposed in OnExit, not through IDisposable.")]
public partial class App : Application
{
    private SingleInstance? _instance;
    private AppData? _data;
    private MainViewModel? _main;
    private CloudService? _cloud;
    private Updates? _updates;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Configure(Path.Combine(AppIdentity.DataDirectory, "logs", "app.log"), LogLevel.Warning);
        DispatcherUnhandledException += OnUnhandledException;

        _instance = SingleInstance.TryAcquire(() => Dispatcher.BeginInvoke(ActivateMainWindow));
        for (var i = 0; _instance is null && e.Args.Contains(LocalData.RestartFlag) && i < 50; i++)
        {
            Thread.Sleep(100); // restarted after "delete my data": the old window is still closing
            _instance = SingleInstance.TryAcquire(() => Dispatcher.BeginInvoke(ActivateMainWindow));
        }

        if (_instance is null)
        {
            Shutdown(); // another window is already open; it has been brought to the front
            return;
        }

        LocalData.ApplyIfRequested();

        StatsStore store;
        try
        {
            store = Backups.OpenOrRecover(StatsStore.DefaultPath);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Error("Cannot open the statistics database", ex);
            MessageBox.Show(ex.Message, "AimOdometer", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _data = new AppData(store);
        Loc.Instance.SetLanguage(Loc.Resolve(_data.Setting(SettingKeys.Language)));
        Format.Units = _data.Units;

        _cloud = new CloudService(_data);
        var cloud = _cloud;
        _updates = new Updates(_data, () => cloud.SyncBeforeExit());
        _main = new MainViewModel(_data, _cloud, _updates);
        var window = new MainWindow { DataContext = _main };
        window.ContentRendered += (_, _) =>
        {
            using var process = Process.GetCurrentProcess();
            SettingsViewModel.MeasuredStartup = DateTime.Now - process.StartTime;
        };
        MainWindow = window;
        window.Show();
        _main.Start();
        _cloud.Start();
        _updates.Start();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, ShowRecoveryNotice);
    }

    /// <summary>Tells the user once that a damaged database was replaced (by this window or by the tracker).</summary>
    private void ShowRecoveryNotice()
    {
        if (_data?.Setting(SettingKeys.RecoveredFrom) is not { Length: > 0 } from)
        {
            return;
        }

        _data.Store.SetSetting(SettingKeys.RecoveredFrom, string.Empty);
        var L = Loc.Instance;
        var text = DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? L.Format("App.RecoveredFromBackup", Format.Date(day))
            : L["App.RecoveredEmpty"];
        MessageBox.Show(MainWindow, text, "AimOdometer", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _updates?.Dispose();
        _cloud?.SyncBeforeExit();
        _cloud?.Dispose();
        _main?.Dispose();
        _data?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void ActivateMainWindow()
    {
        if (MainWindow is not { } window)
        {
            return;
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        window.Topmost = true; // reliably wins the foreground race, then behaves normally again
        window.Topmost = false;
        window.Focus();
    }

    private bool _showingError;

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;

        // One dialog at a time: a broken template can throw on every layout pass, which must not flood the screen.
        if (_showingError)
        {
            return;
        }

        _showingError = true;
        try
        {
            MessageBox.Show(e.Exception.Message, "AimOdometer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _showingError = false;
        }
    }
}
