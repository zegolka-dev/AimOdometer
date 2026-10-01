using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AimOdometer.Core;
using AimOdometer.Win32;

namespace AimOdometer.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"v{version?.ToString(3)}";

        // Phase 1 shell: only shows whether the background tracker is alive.
        // Real UI text goes through JSON i18n starting with phase 4.
        var trackerRunning = User32.FindWindowW(AppIdentity.TrackerWindowClass, null) != 0;
        TrackerStatusText.Text = trackerRunning ? "Tracker is running" : "Tracker is not running";
        TrackerDot.Fill = trackerRunning ? (Brush)FindResource("AccentBrush") : Brushes.IndianRed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Dwmapi.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
    }
}
