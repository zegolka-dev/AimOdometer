using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AimOdometer.Core;
using AimOdometer.Core.Ipc;
using AimOdometer.Core.Storage;

namespace AimOdometer.Tools.InputProbe;

/// <summary>
/// Foreground raw input reader (full polling rate, no background coalescing) compared against the background
/// tracker's database for the same period. Software-injected input is ignored on both sides.
/// </summary>
public unsafe partial class MainWindow : Window
{
    private const int WmInput = 0x00FF;
    private const uint RidInput = 0x10000003;

    private readonly byte[] _buffer = new byte[256];
    private bool _running;
    private double _path;
    private long _events;
    private double _trackerBaseline;
    private long _startTimestamp;

    public MainWindow()
    {
        InitializeComponent();
        Output.Text = "Ready. Tracker running: " + (TrackerClient.IsRunning() ? "yes" : "NO - start it first");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint HwndTarget;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterRawInputDevices(RawInputDevice* devices, uint count, uint size);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint GetRawInputData(nint rawInput, uint command, void* data, uint* size, uint headerSize);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        source.AddHook(WndProc);

        // Flags 0: input only while this window is in the foreground, at the full polling rate.
        var device = new RawInputDevice { UsagePage = 0x01, Usage = 0x02, Flags = 0, HwndTarget = source.Handle };
        if (!RegisterRawInputDevices(&device, 1, (uint)sizeof(RawInputDevice)))
        {
            Output.Text = $"RegisterRawInputDevices failed: {Marshal.GetLastPInvokeError()}";
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WmInput || !_running)
        {
            return 0;
        }

        uint size = (uint)_buffer.Length;
        fixed (byte* p = _buffer)
        {
            if (GetRawInputData(lParam, RidInput, p, &size, 24) is 0 or uint.MaxValue)
            {
                return 0;
            }

            var isMouse = *(uint*)p == 0;
            var device = *(nint*)(p + 8);
            var flags = *(ushort*)(p + 24);
            if (isMouse && device != 0 && (flags & 1) == 0)
            {
                var dx = *(int*)(p + 24 + 12);
                var dy = *(int*)(p + 24 + 16);
                if ((dx | dy) != 0)
                {
                    _path += Distance.PathCounts(dx, dy);
                    _events++;
                }
            }
        }

        return 0;
    }

    private static double TrackerPathCounts()
    {
        _ = TrackerClient.Send(TrackerCommand.Flush);
        using var db = SqliteDatabase.Open(StatsStore.DefaultPath, readOnly: true);
        using var query = db.Prepare("""
            SELECT coalesce(sum(h.path_counts), 0)
            FROM hourly h JOIN devices d ON d.id = h.device_id
            WHERE d.kind <> 2;
            """);
        return query.Step() ? query.GetDouble(0) : 0;
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        if (!TrackerClient.IsRunning())
        {
            Output.Text = "The tracker is not running. Start AimOdometer.Tracker.exe first.";
            return;
        }

        _trackerBaseline = TrackerPathCounts();
        _path = 0;
        _events = 0;
        _startTimestamp = Stopwatch.GetTimestamp();
        _running = true;
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        Output.Text = "Measuring... keep this window focused and move the mouse like in a game, then press Stop.";
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        _running = false;
        var seconds = Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds;
        var tracker = TrackerPathCounts() - _trackerBaseline;
        var difference = _path > 0 ? (tracker - _path) / _path * 100 : 0;
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;

        Output.Text = string.Create(CultureInfo.InvariantCulture, $"""
            Duration:                 {seconds:0.0} s
            Foreground (full rate):   {_path:N0} counts from {_events:N0} reports ({_events / seconds:N0} reports/s)
            Background tracker:       {tracker:N0} counts
            Difference:               {difference:+0.000;-0.000;0.000} %

            Negative = the tracker recorded less path than the full-rate reader (coalescing loss).
            Use one mouse only during the measurement; other pointing devices also count for the tracker.
            """);
    }

    private void OnCopy(object sender, RoutedEventArgs e) => Clipboard.SetText(Output.Text);
}
