using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using AimOdometer.Core.Diagnostics;
using AimOdometer.Core.Ipc;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AimOdometer.App.Services;

/// <summary>
/// Live link to the background tracker over its named pipe. Polls once a second only while the window is open
/// (the window is closed most of the time, so this costs nothing in normal use).
/// </summary>
public sealed partial class TrackerConnection : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _polling;

    public TrackerConnection()
    {
        _timer.Tick += async (_, _) => await PollAsync();
    }

    public static string TrackerExePath => Path.Combine(AppContext.BaseDirectory, "AimOdometer.Tracker.exe");

    [ObservableProperty]
    public partial TrackerStatus? Status { get; private set; }

    [ObservableProperty]
    public partial bool IsRunning { get; private set; }

    /// <summary>Raised after each successful poll.</summary>
    public event EventHandler? Updated;

    public void Start()
    {
        _timer.Start();
        _ = PollAsync();
    }

    /// <summary>Starts the tracker if it is not answering and its exe is next to the app (installed layout).</summary>
    public static async Task<bool> EnsureRunningAsync()
    {
        if (await Task.Run(TrackerClient.IsRunning))
        {
            return true;
        }

        if (!File.Exists(TrackerExePath))
        {
            Log.Warning($"Tracker not running and not found at {TrackerExePath}");
            return false;
        }

        Process.Start(new ProcessStartInfo(TrackerExePath) { UseShellExecute = false })?.Dispose();
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(150);
            if (await Task.Run(TrackerClient.IsRunning))
            {
                return true;
            }
        }

        return false;
    }

    public static Task<bool> SendAsync(TrackerCommand command, long argument = 0) =>
        Task.Run(() => TrackerClient.Send(command, argument) is [TrackerProtocol.StatusOk, ..]);

    public async Task PollAsync()
    {
        if (_polling)
        {
            return;
        }

        _polling = true;
        try
        {
            var status = await Task.Run(() =>
            {
                try
                {
                    return TrackerClient.GetStatus();
                }
                catch (InvalidDataException)
                {
                    return null;
                }
            });
            Status = status;
            IsRunning = status is not null;
            Updated?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _polling = false;
        }
    }

    public void Dispose() => _timer.Stop();
}
