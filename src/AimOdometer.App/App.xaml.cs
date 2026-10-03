using System.Diagnostics;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Configure(Path.Combine(AppIdentity.DataDirectory, "logs", "app.log"), LogLevel.Warning);
        DispatcherUnhandledException += OnUnhandledException;

        _instance = SingleInstance.TryAcquire(() => Dispatcher.BeginInvoke(ActivateMainWindow));
        if (_instance is null)
        {
            Shutdown(); // another window is already open; it has been brought to the front
            return;
        }

        StatsStore store;
        try
        {
            store = StatsStore.Open(StatsStore.DefaultPath);
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

        _main = new MainViewModel(_data);
        var window = new MainWindow { DataContext = _main };
        window.ContentRendered += (_, _) =>
        {
            using var process = Process.GetCurrentProcess();
            SettingsViewModel.MeasuredStartup = DateTime.Now - process.StartTime;
        };
        MainWindow = window;
        window.Show();
        _main.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
