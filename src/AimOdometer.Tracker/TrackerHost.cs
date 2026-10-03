using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using AimOdometer.Core;
using AimOdometer.Core.Diagnostics;
using AimOdometer.Core.Fun;
using AimOdometer.Core.Games;
using AimOdometer.Core.Input;
using AimOdometer.Core.Ipc;
using AimOdometer.Core.Storage;
using AimOdometer.Win32;

namespace AimOdometer.Tracker;

/// <summary>
/// The background tracker: one hidden window, one thread, event-driven.
/// Raw input is accumulated in memory and written to SQLite once a minute and on sleep, logoff and exit.
/// </summary>
internal sealed unsafe class TrackerHost : IDisposable
{
    private const nuint FlushTimerId = 1;
    private const nuint HourTimerId = 2;
    private const nuint ResumeTimerId = 3;
    private const nuint CrashTestTimerId = 4;
    private const uint FlushIntervalMs = 60_000;
    private const int RawBufferSize = 64 * 1024;
    private const int MaxRetainedFailedRows = 10_000;

    private const int CmdOpenStatistics = 1;
    private const int CmdOpenDataFolder = 2;
    private const int CmdPause15 = 3;
    private const int CmdPause60 = 4;
    private const int CmdPauseUntilRestart = 5;
    private const int CmdResume = 6;
    private const int CmdAutostart = 7;
    private const int CmdExit = 8;
    private const int CmdDpiBase = 1000;

    private static readonly double[] DpiPresets = [400, 800, 1000, 1200, 1600, 2000, 3200, 6400];
    private static TrackerHost? _current;

    private readonly TrackerOptions _options;
    private readonly StatsStore _store;
    private readonly InputAccumulator _accumulator;
    private readonly DeviceResolver _devices;
    private readonly List<UsageDelta> _drainBuffer = new(64);
    private readonly List<(HourKey Hour, UsageDelta Delta)> _failedWrites = [];
    private readonly byte* _rawBuffer;
    private readonly uint _taskbarCreatedMessage;

    private nint _hwnd;
    private TrayIcon? _tray;
    private PipeServer? _pipe;
    private ForegroundTracker? _foreground;
    private bool _locked;
    private bool _sleeping;
    private HourKey _hour;
    private double _storedTodayCm;
    private bool _paused;
    private DateTime? _pausedUntilUtc;
    private TrayStrings _strings = TrayStrings.English;
    private UnitSystem _units;
    private long _lastTooltipUpdate;
    private long _lastBatchTimestamp;
    private GameCatalog? _catalog;
    private DateTime _catalogLoadedUtc;
    private int _flushesSinceAchievementCheck = int.MaxValue;
    private IReadOnlyDictionary<string, string> _texts = new Dictionary<string, string>();
    private bool _notificationsEnabled = true;
    private bool _notificationDeferLogged;
    private long _wakeUps;

    public TrackerHost(TrackerOptions options, StatsStore store)
    {
        _options = options;
        _store = store;
        _accumulator = new InputAccumulator(ResolveDevice, Stopwatch.Frequency)
        {
            MeasureInjectedLatency = options.MeasureLatency,
        };
        _devices = new DeviceResolver(store, _accumulator);
        _rawBuffer = (byte*)NativeMemory.AlignedAlloc(RawBufferSize, 8);
        _taskbarCreatedMessage = User32.RegisterWindowMessageW("TaskbarCreated");
    }

    private int ResolveDevice(nint handle) => _devices.Resolve(handle);

    /// <summary>Creates the window, registers for input and system events, and runs the message loop.</summary>
    public int Run()
    {
        _current = this;
        LoadSettings();
        ApplyAutostartDefault();
        if (_options.EcoQos || _store.GetSetting(SettingKeys.EcoQos) == "1")
        {
            EnableEcoQos();
        }

        _hwnd = CreateHiddenWindow();
        if (_hwnd == 0)
        {
            return Marshal.GetLastPInvokeError();
        }

        _hour = HourKey.FromUtc(DateTime.UtcNow, TimeZoneInfo.Local);
        _storedTodayCm = _store.CentimetersOn(_hour.LocalDate);
        Log.Warning($"Tracker writing to {_options.DataDirectory}");
        BackupDaily();

        if (!RegisterRawInput(enable: true))
        {
            Log.Error($"RegisterRawInputDevices failed: {Marshal.GetLastPInvokeError()}");
            return 3;
        }

        _ = Wtsapi32.WTSRegisterSessionNotification(_hwnd, Wtsapi32.NotifyForThisSession);
        _ = User32.SetTimer(_hwnd, FlushTimerId, FlushIntervalMs, 0);
        ArmHourTimer();

        _tray = new TrayIcon(_hwnd);
        _tray.Show(BuildTooltip());

        if (_options.CrashAfterSeconds > 0)
        {
            // Test hook for the crash-restart path (docs/RELEASE_CHECKLIST.md); never used in normal runs.
            _ = User32.SetTimer(_hwnd, CrashTestTimerId, (uint)_options.CrashAfterSeconds * 1000, 0);
        }

        _foreground = new ForegroundTracker(_store, _accumulator, Flush);
        _foreground.Start();

        using var process = Process.GetCurrentProcess();
        _pipe = new PipeServer(_hwnd, process.SessionId);
        Log.Info($"Tracker started (pid {Environment.ProcessId}, {_options})");

        User32.Msg msg;
        int result;
        while ((result = User32.GetMessageW(&msg, 0, 0, 0)) > 0)
        {
            User32.TranslateMessage(&msg);
            User32.DispatchMessageW(&msg);
        }

        return result < 0 ? Marshal.GetLastPInvokeError() : (int)msg.WParam;
    }

    public void Dispose()
    {
        Flush();
        _foreground?.Dispose();
        _pipe?.Dispose();
        _tray?.Dispose();
        NativeMemory.AlignedFree(_rawBuffer);
        _current = null;
        Log.Info("Tracker stopped");
    }

    // ---------------------------------------------------------------- window and messages

    private nint CreateHiddenWindow()
    {
        var instance = Kernel32.GetModuleHandleW(null);
        fixed (char* className = AppIdentity.TrackerWindowClass)
        {
            var wndClass = new User32.WndClassExW
            {
                CbSize = (uint)sizeof(User32.WndClassExW),
                LpfnWndProc = &WndProc,
                HInstance = instance,
                LpszClassName = className,
            };
            if (User32.RegisterClassExW(&wndClass) == 0)
            {
                return 0;
            }
        }

        // Never shown; a top-level window (not HWND_MESSAGE) so broadcasts like WM_POWERBROADCAST arrive.
        return User32.CreateWindowExW(
            User32.WsExToolWindow, AppIdentity.TrackerWindowClass, AppIdentity.ProductName, User32.WsPopup,
            0, 0, 0, 0, 0, 0, instance, 0);
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        var host = _current;
        if (host is null || host._hwnd == 0)
        {
            return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        try
        {
            return host.HandleMessage(hwnd, msg, wParam, lParam);
        }
        catch (Exception ex)
        {
            // An exception must never escape into native code: that would terminate the process.
            Log.Error($"Unhandled exception while handling message 0x{msg:X}", ex);
            return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    private nint HandleMessage(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case User32.WmInput:
                OnRawInput(lParam);
                return User32.DefWindowProcW(hwnd, msg, wParam, lParam); // lets Windows free the input

            case User32.WmInputDeviceChange:
                if (wParam == User32.GidcRemoval)
                {
                    _devices.OnDeviceRemoved(lParam);
                    _accumulator.ForgetHandles();
                }

                return 0;

            case User32.WmTimer:
                OnTimer(wParam);
                return 0;

            case TrayIcon.CallbackMessage:
                OnTrayEvent((uint)(lParam & 0xFFFF));
                return 0;

            case PipeServer.RequestMessage:
                OnPipeRequest((PipeRequest*)lParam);
                return 0;

            case User32.WmPowerBroadcast:
                OnPower(wParam);
                return 1;

            case User32.WmQueryEndSession:
                Flush();
                return 1;

            case User32.WmEndSession:
                if (wParam != 0)
                {
                    Log.Warning("Tracker stopped: Windows is signing out or shutting down");
                    Flush();
                }

                return 0;

            case User32.WmWtsSessionChange:
                if (wParam is Wtsapi32.WtsSessionLock or Wtsapi32.WtsSessionLogoff
                    or Wtsapi32.WtsConsoleDisconnect or Wtsapi32.WtsRemoteDisconnect)
                {
                    _locked = true;
                    UpdateForegroundCounting();
                    Flush();
                }
                else if (wParam is Wtsapi32.WtsSessionUnlock or Wtsapi32.WtsConsoleConnect or Wtsapi32.WtsRemoteConnect)
                {
                    _locked = false;
                    UpdateForegroundCounting();
                }

                return 0;

            case User32.WmTimeChange:
                // Covers manual clock changes and time-zone changes.
                TimeZoneInfo.ClearCachedData();
                CheckHour();
                ArmHourTimer();
                return 0;

            case User32.WmClose:
                User32.DestroyWindow(hwnd);
                return 0;

            case User32.WmDestroy:
                _ = Wtsapi32.WTSUnRegisterSessionNotification(hwnd);
                RegisterRawInput(enable: false);
                User32.PostQuitMessage(0);
                return 0;

            default:
                if (msg == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
                {
                    _tray?.Show(BuildTooltip()); // Explorer restarted: the icon must be added again
                    return 0;
                }

                return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    // ---------------------------------------------------------------- raw input (hot path)

    private bool RegisterRawInput(bool enable)
    {
        var device = new User32.RawInputDevice
        {
            UsagePage = 0x01, // Generic Desktop
            Usage = 0x02,     // Mouse
            Flags = enable ? User32.RidevInputSink | User32.RidevDevNotify : User32.RidevRemove,
            HwndTarget = enable ? _hwnd : 0,
        };
        return User32.RegisterRawInputDevices(&device, 1, (uint)sizeof(User32.RawInputDevice));
    }

    private void OnRawInput(nint rawInputHandle)
    {
        // Coalesce wake-ups: with a fast mouse (1000-8000 Hz) a WM_INPUT arrives for every report. Instead of
        // waking up for each one, sleep briefly and then take everything that queued up in one GetRawInputBuffer
        // batch. Windows buffers raw input meanwhile and delivers the game's own input independently, so this only
        // delays our bookkeeping, never the game. Caps wake-ups at roughly 1 / BatchInterval per second.
        var sinceLast = Stopwatch.GetElapsedTime(_lastBatchTimestamp);
        if (sinceLast < _options.BatchInterval)
        {
            Thread.Sleep(_options.BatchInterval - sinceLast);
        }

        var timestamp = Stopwatch.GetTimestamp();
        _lastBatchTimestamp = timestamp;
        _wakeUps++;

        // The message that woke us up is no longer in the queue, so read it individually first...
        var size = (uint)RawBufferSize;
        if (User32.GetRawInputData(rawInputHandle, User32.RidInput, _rawBuffer, &size, User32.RawInputHeaderSize) is not (0 or uint.MaxValue))
        {
            _accumulator.ProcessSingleRawInput(_rawBuffer, timestamp);
        }

        // ...then drain everything else that queued up, in batches, without one message per event.
        while (true)
        {
            size = RawBufferSize;
            var count = User32.GetRawInputBuffer(_rawBuffer, &size, User32.RawInputHeaderSize);
            if (count is 0 or uint.MaxValue)
            {
                break;
            }

            _accumulator.ProcessRawInputBuffer(_rawBuffer, (int)count, timestamp);
        }
    }

    // ---------------------------------------------------------------- timers, hours, flushing

    private void OnTimer(nuint id)
    {
        switch (id)
        {
            case FlushTimerId:
                CheckHour();
                Flush();
                CheckAchievements();
                break;
            case HourTimerId:
                CheckHour();
                ArmHourTimer();
                break;
            case ResumeTimerId:
                Resume();
                break;
            case CrashTestTimerId:
                Flush();
                Log.Warning("Crash test requested with --crash-after; terminating abnormally");
                Environment.FailFast("AimOdometer crash test (--crash-after)");
                break;
        }
    }

    private void ArmHourTimer()
    {
        var now = DateTime.UtcNow;
        var due = (HourKey.NextCheckUtc(now) - now).TotalMilliseconds + 50;
        _ = User32.SetTimer(_hwnd, HourTimerId, (uint)Math.Max(1000, due), 0);
    }

    /// <summary>When the local hour (or date, or UTC offset) changed, writes pending data under the old hour.</summary>
    private void CheckHour()
    {
        var current = HourKey.FromUtc(DateTime.UtcNow, TimeZoneInfo.Local);
        if (current == _hour)
        {
            return;
        }

        Flush();
        var dateChanged = current.LocalDate != _hour.LocalDate;
        _hour = current;
        if (dateChanged)
        {
            _storedTodayCm = SafeCentimetersToday();
            BackupDaily();
        }
    }

    private void Flush()
    {
        var appSeconds = _foreground?.TakePendingSeconds() ?? [];
        if (!_accumulator.HasPendingData() && _failedWrites.Count == 0 && appSeconds.Count == 0)
        {
            return;
        }

        _drainBuffer.Clear();
        _accumulator.Drain(_drainBuffer);
        try
        {
            RetryFailedWrites();
            _store.WriteHour(_hour, _drainBuffer);
            _store.WriteAppTime(_hour, appSeconds);
            _storedTodayCm = _store.CentimetersOn(_hour.LocalDate);
        }
        catch (SqliteException ex)
        {
            // Keep the data in memory and retry on the next flush (e.g. the disk was briefly unavailable).
            Log.Error("Writing statistics failed; will retry", ex);
            _foreground?.RestorePendingSeconds(appSeconds);
            if (_failedWrites.Count < MaxRetainedFailedRows)
            {
                foreach (var delta in _drainBuffer)
                {
                    _failedWrites.Add((_hour, delta));
                }
            }
        }
    }

    private void RetryFailedWrites()
    {
        if (_failedWrites.Count == 0)
        {
            return;
        }

        foreach (var group in _failedWrites.GroupBy(f => f.Hour))
        {
            _store.WriteHour(group.Key, group.Select(g => g.Delta).ToList());
        }

        _failedWrites.Clear();
    }

    /// <summary>Keeps a copy of the database per day (last 7) so no bug or mistake can wipe more than a day.</summary>
    private void BackupDaily()
    {
        try
        {
            if (Backups.CreateDaily(_store, Backups.FolderFor(_options.DataDirectory), _hour.LocalDate) is { } path)
            {
                Log.Info($"Backup written: {path}");
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            Log.Error("Daily backup failed", ex);
        }
    }

    private double SafeCentimetersToday()
    {
        try
        {
            return _store.CentimetersOn(_hour.LocalDate);
        }
        catch (SqliteException ex)
        {
            Log.Error("Reading today's distance failed", ex);
            return 0;
        }
    }

    private void OnPower(nuint eventType)
    {
        const nuint PbtApmSuspend = 0x4;
        const nuint PbtApmResumeSuspend = 0x7;
        const nuint PbtApmResumeAutomatic = 0x12;

        if (eventType == PbtApmSuspend)
        {
            _sleeping = true;
            UpdateForegroundCounting();
            Flush();
        }
        else if (eventType is PbtApmResumeAutomatic or PbtApmResumeSuspend)
        {
            _sleeping = false;
            UpdateForegroundCounting();
            _accumulator.ResetFlickWindows();
            CheckHour();
            ArmHourTimer();
            if (_paused && _pausedUntilUtc is { } until && DateTime.UtcNow >= until)
            {
                Resume();
            }
        }
    }

    // ---------------------------------------------------------------- pause

    private void Pause(int minutes)
    {
        Flush();
        _paused = true;
        UpdateForegroundCounting();
        RegisterRawInput(enable: false); // no input at all while paused: zero CPU
        _accumulator.ResetFlickWindows();
        if (minutes > 0)
        {
            _pausedUntilUtc = DateTime.UtcNow.AddMinutes(minutes);
            _ = User32.SetTimer(_hwnd, ResumeTimerId, (uint)(minutes * 60_000), 0);
        }
        else
        {
            _pausedUntilUtc = null;
            _ = User32.KillTimer(_hwnd, ResumeTimerId);
        }

        UpdateTooltip(force: true);
        Log.Info(minutes > 0 ? $"Paused for {minutes} min" : "Paused until restart");
    }

    private void Resume()
    {
        _ = User32.KillTimer(_hwnd, ResumeTimerId);
        if (!_paused)
        {
            return;
        }

        _paused = false;
        _pausedUntilUtc = null;
        UpdateForegroundCounting();
        RegisterRawInput(enable: true);
        UpdateTooltip(force: true);
        Log.Info("Resumed");
    }

    /// <summary>Foreground time counts only while unlocked, awake and not paused.</summary>
    private void UpdateForegroundCounting() => _foreground?.SetSessionActive(!_locked && !_sleeping && !_paused);

    // ---------------------------------------------------------------- tray

    private string TodayText() =>
        DistanceFormat.Format(_storedTodayCm + _accumulator.PendingCentimeters(), _units, _strings.Units, TrayStrings.Culture);

    private string BuildTooltip()
    {
        var today = string.Format(TrayStrings.Culture, _strings.TodayFormat, TodayText());
        var tooltip = $"{AppIdentity.ProductName}\n{today}";
        if (_paused)
        {
            tooltip += "\n" + (_pausedUntilUtc is { } until
                ? string.Format(TrayStrings.Culture, _strings.PausedUntilFormat, until.ToLocalTime())
                : _strings.Paused);
        }

        return tooltip;
    }

    private void UpdateTooltip(bool force)
    {
        // Hover produces a stream of WM_MOUSEMOVE callbacks; refresh at most twice a second.
        var now = Stopwatch.GetTimestamp();
        if (!force && Stopwatch.GetElapsedTime(_lastTooltipUpdate, now) < TimeSpan.FromMilliseconds(500))
        {
            return;
        }

        _lastTooltipUpdate = now;
        _tray?.SetTooltip(BuildTooltip());
    }

    private void OnTrayEvent(uint trayEvent)
    {
        switch (trayEvent)
        {
            case User32.WmMouseMove:
                UpdateTooltip(force: false);
                break;
            case Shell32.NinSelect or Shell32.NinKeySelect:
                if (!TryOpenStatistics())
                {
                    ShowMenu();
                }

                break;
            case User32.WmContextMenu:
                ShowMenu();
                break;
        }
    }

    private static string? AppExePath
    {
        get
        {
            var directory = Path.GetDirectoryName(Environment.ProcessPath);
            var path = directory is null ? null : Path.Combine(directory, "AimOdometer.App.exe");
            return path is not null && File.Exists(path) ? path : null;
        }
    }

    private static bool TryOpenStatistics()
    {
        if (AppExePath is not { } app)
        {
            return false;
        }

        // The window must use the real data folder: never pass a test override from our own environment.
        var start = new ProcessStartInfo(app) { UseShellExecute = false };
        start.Environment.Remove(AppIdentity.DataDirectoryVariable);
        Process.Start(start)?.Dispose();
        return true;
    }

    private void ShowMenu()
    {
        var menu = User32.CreatePopupMenu();
        var miceMenu = User32.CreatePopupMenu();
        var miceAttached = false;
        try
        {
            User32.AppendMenuW(menu, User32.MfString | User32.MfGrayed, 0, string.Format(TrayStrings.Culture, _strings.TodayFormat, TodayText()));
            User32.AppendMenuW(menu, User32.MfSeparator, 0, null);
            if (AppExePath is not null)
            {
                User32.AppendMenuW(menu, User32.MfString, CmdOpenStatistics, _strings.OpenStatistics);
            }

            foreach (var (slot, device) in _devices.Slots.OrderBy(s => s.Key))
            {
                if (device.Kind == DeviceKind.Software)
                {
                    continue;
                }

                var dpiMenu = User32.CreatePopupMenu();
                foreach (var (preset, index) in DpiPresets.Select((p, i) => (p, i)))
                {
                    var flags = User32.MfString | (preset == device.Dpi ? User32.MfChecked : 0);
                    User32.AppendMenuW(dpiMenu, flags, (nuint)(CmdDpiBase + (slot * 100) + index), preset.ToString("0", CultureInfo.InvariantCulture));
                }

                var label = string.Format(TrayStrings.Culture, _strings.DeviceDpiFormat, device.Name, device.Dpi.ToString("0.##", CultureInfo.InvariantCulture));
                User32.AppendMenuW(miceMenu, User32.MfPopup, (nuint)dpiMenu, label);
            }

            if (_devices.Slots.Values.Any(d => d.Kind != DeviceKind.Software))
            {
                miceAttached = User32.AppendMenuW(menu, User32.MfPopup, (nuint)miceMenu, _strings.Mice);
            }

            if (_paused)
            {
                User32.AppendMenuW(menu, User32.MfString, CmdResume, _strings.Resume);
            }
            else
            {
                User32.AppendMenuW(menu, User32.MfString, CmdPause15, _strings.Pause15Minutes);
                User32.AppendMenuW(menu, User32.MfString, CmdPause60, _strings.Pause1Hour);
                User32.AppendMenuW(menu, User32.MfString, CmdPauseUntilRestart, _strings.PauseUntilRestart);
            }

            User32.AppendMenuW(menu, User32.MfSeparator, 0, null);
            User32.AppendMenuW(menu, User32.MfString | (Autostart.IsEnabled() ? User32.MfChecked : 0), CmdAutostart, _strings.StartWithWindows);
            User32.AppendMenuW(menu, User32.MfString, CmdOpenDataFolder, _strings.OpenDataFolder);
            User32.AppendMenuW(menu, User32.MfSeparator, 0, null);
            User32.AppendMenuW(menu, User32.MfString, CmdExit, _strings.Exit);

            User32.Point cursor;
            User32.GetCursorPos(&cursor);
            User32.SetForegroundWindow(_hwnd); // required, otherwise the menu does not close on outside clicks
            var command = User32.TrackPopupMenu(
                menu, User32.TpmReturnCmd | User32.TpmRightButton | User32.TpmBottomAlign, cursor.X, cursor.Y, 0, _hwnd, 0);
            ExecuteMenuCommand(command);
        }
        finally
        {
            User32.DestroyMenu(menu); // also destroys attached submenus
            if (!miceAttached)
            {
                User32.DestroyMenu(miceMenu);
            }
        }
    }

    private void ExecuteMenuCommand(int command)
    {
        switch (command)
        {
            case 0:
                return;
            case CmdOpenStatistics:
                TryOpenStatistics();
                break;
            case CmdOpenDataFolder:
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_options.DataDirectory}\"") { UseShellExecute = false })?.Dispose();
                break;
            case CmdPause15:
                Pause(15);
                break;
            case CmdPause60:
                Pause(60);
                break;
            case CmdPauseUntilRestart:
                Pause(0);
                break;
            case CmdResume:
                Resume();
                break;
            case CmdAutostart:
                var enable = !Autostart.IsEnabled();
                Autostart.Set(enable, Environment.ProcessPath!);
                _store.SetSetting(SettingKeys.Autostart, enable ? "1" : "0");
                break;
            case CmdExit:
                // Logged at the default level: "where did the tray icon go?" must be answerable from the log.
                Log.Warning("Tracker stopped: Exit was chosen in the tray menu");
                User32.PostMessageW(_hwnd, User32.WmClose, 0, 0);
                break;
            case >= CmdDpiBase:
                var slot = (command - CmdDpiBase) / 100;
                var index = (command - CmdDpiBase) % 100;
                if (index < DpiPresets.Length)
                {
                    Flush(); // movement so far belongs to the old DPI
                    _devices.SetDpi(slot, DpiPresets[index]);
                    _storedTodayCm = SafeCentimetersToday();
                }

                break;
        }
    }

    // ---------------------------------------------------------------- achievements

    /// <summary>Every 5 minutes: record newly reached achievements, then notify (if allowed right now).</summary>
    private void CheckAchievements()
    {
        const int EveryFlushes = 5;
        if (++_flushesSinceAchievementCheck >= EveryFlushes)
        {
            _flushesSinceAchievementCheck = 0;
            try
            {
                if (_catalog is null || DateTime.UtcNow - _catalogLoadedUtc > TimeSpan.FromHours(1))
                {
                    _catalog = GameCatalog.Create(_store); // Steam libraries and user rules change rarely
                    _catalogLoadedUtc = DateTime.UtcNow;
                }

                var newly = AchievementService.UnlockNew(_store, _catalog, _hour.LocalDate, DateTime.UtcNow);
                if (newly.Count > 0)
                {
                    Log.Info($"Achievements unlocked: {string.Join(", ", newly.Select(a => a.Id))}");
                }
            }
            catch (Exception ex) when (ex is SqliteException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Log.Error("Achievement check failed", ex);
            }
        }

        NotifyPendingAchievements();
    }

    /// <summary>
    /// Shows one notification for achievements not announced yet. Never while a full-screen game, a presentation or
    /// quiet time is active (SHQueryUserNotificationState); they wait for the next minute after that.
    /// </summary>
    private void NotifyPendingAchievements()
    {
        try
        {
            var pending = _store.GetAchievements().Where(a => !a.Value.Notified).OrderBy(a => a.Value.UnlockedAtUtc).Select(a => a.Key).ToList();
            if (pending.Count == 0)
            {
                return;
            }

            if (_notificationsEnabled)
            {
                int state;
                if (Shell32.SHQueryUserNotificationState(&state) != 0 || state != Shell32.QunsAcceptsNotifications || _tray is null)
                {
                    if (!_notificationDeferLogged)
                    {
                        Log.Warning($"Achievement notification deferred (notification state {state}): {string.Join(", ", pending)}");
                        _notificationDeferLogged = true;
                    }

                    return; // try again later
                }

                // Newest first is the most relevant; the rest are summarized.
                var name = _texts.GetValueOrDefault($"Ach.{pending[^1]}.Name", pending[^1]);
                var text = pending.Count == 1 ? name : string.Format(TrayStrings.Culture, _strings.MoreAchievementsFormat, name, pending.Count - 1);
                _tray.ShowBalloon(_strings.AchievementTitle, text);
            }

            // Rare event, logged at the default level so "did the notification come?" can be answered from the log.
            Log.Warning($"Achievements unlocked ({(_notificationsEnabled ? "notification shown" : "notifications off")}): {string.Join(", ", pending)}");
            _notificationDeferLogged = false;

            _store.MarkAchievementsNotified(pending);
        }
        catch (SqliteException ex)
        {
            Log.Error("Achievement notification failed", ex);
        }
    }

    // ---------------------------------------------------------------- pipe commands

    private void OnPipeRequest(PipeRequest* request)
    {
        var response = new Span<byte>(request->Response, TrackerProtocol.MaxResponseSize);
        response[0] = TrackerProtocol.StatusOk;
        request->ResponseLength = 1;

        switch (request->Command)
        {
            case TrackerCommand.Ping:
                request->ResponseLength = TrackerProtocol.WritePing(response, Environment.ProcessId, _options.DataDirectory);
                break;
            case TrackerCommand.GetStatus:
                request->ResponseLength = TrackerProtocol.WriteStatus(response, BuildStatus());
                break;
            case TrackerCommand.Flush:
                Flush();
                break;
            case TrackerCommand.Pause:
                Pause((int)Math.Clamp(request->Argument, 0, 24 * 60));
                break;
            case TrackerCommand.Resume:
                Resume();
                break;
            case TrackerCommand.ReloadSettings:
                Flush();
                LoadSettings();
                _devices.Refresh();
                _storedTodayCm = SafeCentimetersToday();
                UpdateTooltip(force: true);
                break;
            case TrackerCommand.Shutdown:
                Log.Warning("Tracker stopped: shutdown requested over the pipe (update or reinstall)");
                User32.PostMessageW(_hwnd, User32.WmClose, 0, 0);
                break;
            default:
                response[0] = TrackerProtocol.StatusUnknownCommand;
                break;
        }
    }

    private TrackerStatus BuildStatus()
    {
        var ticksToMicroseconds = 1_000_000.0 / Stopwatch.Frequency;
        var samples = _accumulator.LatencySamples;
        return new TrackerStatus(
            Paused: _paused,
            PausedUntilUtc: _pausedUntilUtc,
            TodayCentimeters: _storedTodayCm + _accumulator.PendingCentimeters(),
            EventsProcessed: _accumulator.EventsProcessed,
            WakeUps: _wakeUps,
            LatencySamples: samples,
            LatencyAverageMicroseconds: samples == 0 ? 0 : _accumulator.LatencyTicksTotal * ticksToMicroseconds / samples,
            LatencyMaxMicroseconds: _accumulator.LatencyTicksMax * ticksToMicroseconds,
            ManagedBytesAllocated: GC.GetTotalAllocatedBytes());
    }

    // ---------------------------------------------------------------- settings

    private void LoadSettings()
    {
        var language = _store.GetSetting(SettingKeys.Language);
        _strings = TrayStrings.For(language);
        _texts = StringTable.Load(AppContext.BaseDirectory, TrayStrings.Code(language));
        _notificationsEnabled = _store.GetSetting(SettingKeys.Notifications) != "0";
        _units = DistanceFormat.ParseUnits(_store.GetSetting(SettingKeys.Units));
        if (Log.TryParseLevel(_store.GetSetting(SettingKeys.LogLevel), out var level))
        {
            Log.MinimumLevel = level;
        }
    }

    private void ApplyAutostartDefault()
    {
        var setting = _store.GetSetting(SettingKeys.Autostart);
        if (setting is null)
        {
            // On by default for installed copies; developer builds never register themselves.
            var enable = Autostart.IsInstalledLocation(Environment.ProcessPath);
            _store.SetSetting(SettingKeys.Autostart, enable ? "1" : "0");
            if (enable)
            {
                Autostart.Set(true, Environment.ProcessPath!);
            }
        }
        else if (setting == "1")
        {
            Autostart.Set(true, Environment.ProcessPath!); // refresh the path in case the exe moved after an update
        }
    }

    private static void EnableEcoQos()
    {
        var state = new Kernel32.ProcessPowerThrottlingState
        {
            Version = Kernel32.ProcessPowerThrottlingCurrentVersion,
            ControlMask = Kernel32.ProcessPowerThrottlingExecutionSpeed,
            StateMask = Kernel32.ProcessPowerThrottlingExecutionSpeed,
        };
        if (!Kernel32.SetProcessInformation(
                Kernel32.GetCurrentProcess(), Kernel32.ProcessPowerThrottling, &state, (uint)sizeof(Kernel32.ProcessPowerThrottlingState)))
        {
            Log.Warning($"EcoQoS could not be enabled: {Marshal.GetLastPInvokeError()}");
        }
        else
        {
            Log.Info("EcoQoS enabled");
        }
    }

    /// <summary>Asks a tracker running in this session to exit. Returns false if none is running.</summary>
    public static bool RequestStopOfRunningInstance()
    {
        var hwnd = User32.FindWindowW(AppIdentity.TrackerWindowClass, null);
        return hwnd != 0 && User32.PostMessageW(hwnd, User32.WmClose, 0, 0);
    }
}
