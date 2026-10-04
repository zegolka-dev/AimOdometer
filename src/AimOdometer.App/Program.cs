using AimOdometer.App.Services;
using Velopack;

namespace AimOdometer.App;

/// <summary>
/// Entry point. Velopack runs first: when Setup.exe, Update.exe or the uninstaller start the app with their hook
/// arguments, it handles them and exits before any WPF window exists.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => Updates.PrepareForUninstall())
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
