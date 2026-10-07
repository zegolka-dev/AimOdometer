using System.Diagnostics;
using System.Runtime.InteropServices;
using AimOdometer.Core.Diagnostics;
using AimOdometer.Core.Input;
using AimOdometer.Core.Storage;
using AimOdometer.Win32;

namespace AimOdometer.Tracker;

/// <summary>
/// Follows the foreground window with out-of-context WinEvent hooks (event-driven, nothing injected), plus a cheap
/// once-a-second check for changes the hook missed (<see cref="Recheck"/>).
/// Tells the accumulator which app receives the movement and measures foreground time per app.
/// Time does not count while the window is minimized, the session is locked, the PC sleeps, or tracking is paused.
/// Which app is which game is decided later, in the UI; the tracker stores only executable paths.
/// </summary>
internal sealed unsafe class ForegroundTracker : IDisposable
{
    private static ForegroundTracker? _current;

    private readonly StatsStore _store;
    private readonly InputAccumulator _accumulator;
    private readonly Action _flush;
    private readonly Dictionary<string, long> _appIdByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, double> _pendingSeconds = [];
    private readonly char[] _pathBuffer = new char[1024];
    private readonly bool _selfElevated = AimOdometer.Core.ElevatedTask.IsElevated;
    private readonly HashSet<long> _elevationChecked = [];
    private nint _shellCheckWindow;
    private bool _shellCheckResult;

    private nint _foregroundHook;
    private nint _minimizeHook;
    private nint _foregroundWindow;
    private long _currentAppId;
    private long _segmentStart;
    private bool _sessionActive = true;
    private bool _minimized;
    private int _missedEvents;

    /// <param name="flush">Writes pending data; called when the accumulator's per-flush app table is full.</param>
    public ForegroundTracker(StatsStore store, InputAccumulator accumulator, Action flush)
    {
        _store = store;
        _accumulator = accumulator;
        _flush = flush;
    }

    public long CurrentAppId => _currentAppId;

    private bool Counting => _sessionActive && !_minimized && _segmentStart != 0;

    public void Start()
    {
        _current = this;
        var flags = WinEvents.WinEventOutOfContext | WinEvents.WinEventSkipOwnProcess;
        _foregroundHook = WinEvents.SetWinEventHook(
            WinEvents.EventSystemForeground, WinEvents.EventSystemForeground, 0, &OnWinEvent, 0, 0, flags);
        _minimizeHook = WinEvents.SetWinEventHook(
            WinEvents.EventSystemMinimizeStart, WinEvents.EventSystemMinimizeEnd, 0, &OnWinEvent, 0, 0, flags);
        if (_foregroundHook == 0)
        {
            Log.Error("SetWinEventHook(EVENT_SYSTEM_FOREGROUND) failed; per-game statistics are unavailable");
        }

        OnForegroundChanged(WinEvents.GetForegroundWindow());
    }

    /// <summary>
    /// Safety net for foreground changes the hook never reported. Some full-screen games take the foreground without
    /// EVENT_SYSTEM_FOREGROUND reaching an out-of-context hook (seen with Watch_Dogs 2 started from Steam: hours of play
    /// went to explorer.exe). Called once a second; costs one GetForegroundWindow call. A momentary "no foreground
    /// window" during a switch is ignored; the hook reports a real one.
    /// </summary>
    public void Recheck()
    {
        if (!_sessionActive)
        {
            return;
        }

        var hwnd = OnScreen(WinEvents.GetForegroundWindow());
        if (hwnd != 0 && hwnd != _foregroundWindow)
        {
            if (_missedEvents++ < 50)
            {
                Log.Warning($"Foreground changed without an event: 0x{hwnd:X} (was 0x{_foregroundWindow:X})");
            }

            OnForegroundChanged(hwnd);
        }
        else if (hwnd != 0 && WinEvents.IsIconic(hwnd) != _minimized)
        {
            CloseSegment();
            _minimized = !_minimized;
        }
    }

    /// <summary>
    /// The window the user actually sees. Windows sometimes keeps the desktop (explorer.exe) as the foreground window
    /// while a full-screen game covers the whole monitor (seen with Watch_Dogs 2); then the full-screen window under the
    /// pointer is the one that gets the mouse. Any other foreground window is taken as it is.
    /// </summary>
    internal nint OnScreen(nint foreground)
    {
        if (foreground != 0 && !IsShell(foreground))
        {
            return foreground;
        }

        WinEvents.Pt cursor;
        if (!WinEvents.GetCursorPos(&cursor))
        {
            return foreground;
        }

        var root = WinEvents.GetAncestor(WinEvents.WindowFromPoint(cursor), WinEvents.GaRoot);
        return root != 0 && root != foreground && !IsShell(root) && CoversItsMonitor(root) ? root : foreground;
    }

    private bool IsShell(nint hwnd)
    {
        if (hwnd != _shellCheckWindow)
        {
            uint processId = 0;
            _ = WinEvents.GetWindowThreadProcessId(hwnd, &processId);
            var path = processId == 0 ? null : QueryImagePath(processId) ?? QueryExeName(processId);
            _shellCheckWindow = hwnd;
            _shellCheckResult = path is not null && Path.GetFileName(path).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);
        }

        return _shellCheckResult;
    }

    private static bool CoversItsMonitor(nint hwnd)
    {
        WinEvents.Rect window;
        var info = new WinEvents.MonitorInfo { Size = (uint)sizeof(WinEvents.MonitorInfo) };
        var monitor = WinEvents.MonitorFromWindow(hwnd, WinEvents.MonitorDefaultToNearest);
        if (!WinEvents.GetWindowRect(hwnd, &window) || monitor == 0 || !WinEvents.GetMonitorInfo(monitor, &info))
        {
            return false;
        }

        return window.Left <= info.Monitor.Left && window.Top <= info.Monitor.Top
            && window.Right >= info.Monitor.Right && window.Bottom >= info.Monitor.Bottom;
    }

    /// <summary>
    /// True when the process runs with administrator rights while this tracker does not: Windows then hides the mouse
    /// from us whenever its window is in front. A process whose token we may not even read counts as elevated.
    /// </summary>
    internal static bool IsElevatedProcess(uint processId)
    {
        var process = WinEvents.OpenProcess(WinEvents.ProcessQueryLimitedInformation, false, processId);
        if (process == 0)
        {
            return false;
        }

        try
        {
            nint token;
            if (!WinEvents.OpenProcessToken(process, WinEvents.TokenQuery, &token))
            {
                return Marshal.GetLastPInvokeError() == 5; // ERROR_ACCESS_DENIED: a higher integrity level than ours
            }

            try
            {
                int elevated = 0;
                uint size = 0;
                return WinEvents.GetTokenInformation(token, WinEvents.TokenElevationClass, &elevated, sizeof(int), &size) && elevated != 0;
            }
            finally
            {
                Kernel32.CloseHandle(token);
            }
        }
        finally
        {
            Kernel32.CloseHandle(process);
        }
    }

    /// <summary>Remembers an app that runs as administrator, for the window to offer counting it.</summary>
    private void RecordElevated(long appId, string path)
    {
        Log.Warning($"{path} runs as administrator: Windows hides the mouse from this tracker while it is in front");
        try
        {
            var known = (_store.GetSetting(SettingKeys.ElevatedApps) ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            var id = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!known.Contains(id))
            {
                known.Add(id);
                _store.SetSetting(SettingKeys.ElevatedApps, string.Join(',', known.TakeLast(50)));
            }
        }
        catch (SqliteException ex)
        {
            Log.Error("Cannot remember an app that runs as administrator", ex);
        }
    }

    /// <summary>Session lock/unlock, sleep/resume, pause/resume.</summary>
    public void SetSessionActive(bool active)
    {
        if (active == _sessionActive)
        {
            return;
        }

        CloseSegment();
        _sessionActive = active;
        if (active)
        {
            OnForegroundChanged(WinEvents.GetForegroundWindow());
        }
    }

    /// <summary>Moves the time measured so far into the pending table; returns and clears it (for flushing).</summary>
    public IReadOnlyCollection<KeyValuePair<long, double>> TakePendingSeconds()
    {
        CloseSegment();
        var result = _pendingSeconds.ToArray();
        _pendingSeconds.Clear();
        return result;
    }

    /// <summary>Puts time back after a failed write so it is retried with the next flush.</summary>
    public void RestorePendingSeconds(IEnumerable<KeyValuePair<long, double>> seconds)
    {
        foreach (var (appId, value) in seconds)
        {
            _pendingSeconds[appId] = _pendingSeconds.GetValueOrDefault(appId) + value;
        }
    }

    public void Dispose()
    {
        if (_foregroundHook != 0)
        {
            WinEvents.UnhookWinEvent(_foregroundHook);
        }

        if (_minimizeHook != 0)
        {
            WinEvents.UnhookWinEvent(_minimizeHook);
        }

        _current = null;
    }

    [UnmanagedCallersOnly]
    private static void OnWinEvent(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint thread, uint time)
    {
        const int ObjIdWindow = 0;
        if (_current is not { } tracker || objectId != ObjIdWindow)
        {
            return;
        }

        try
        {
            if (eventType == WinEvents.EventSystemForeground)
            {
                tracker.OnForegroundChanged(hwnd);
            }
            else if (hwnd == tracker._foregroundWindow)
            {
                tracker.CloseSegment();
                tracker._minimized = eventType == WinEvents.EventSystemMinimizeStart;
                Log.Debug($"Foreground window 0x{hwnd:X} {(tracker._minimized ? "minimized" : "restored")}");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Foreground tracking failed", ex);
        }
    }

    private void OnForegroundChanged(nint hwnd)
    {
        CloseSegment();
        _foregroundWindow = hwnd;
        _minimized = hwnd != 0 && WinEvents.IsIconic(hwnd);

        var appId = hwnd == 0 ? 0 : ResolveAppId(hwnd);
        if (Log.MinimumLevel <= LogLevel.Debug)
        {
            Log.Debug($"Foreground window 0x{hwnd:X}: app {appId}{(_minimized ? " (minimized)" : string.Empty)}");
        }

        if (appId != _currentAppId)
        {
            _currentAppId = appId;
            if (!_accumulator.SetForegroundApp(appId))
            {
                _flush(); // the per-minute app table is full: write it out, then switch
                _accumulator.SetForegroundApp(appId);
            }
        }

        _segmentStart = Stopwatch.GetTimestamp();
    }

    private void CloseSegment()
    {
        if (Counting)
        {
            var now = Stopwatch.GetTimestamp();
            var seconds = Stopwatch.GetElapsedTime(_segmentStart, now).TotalSeconds;
            _pendingSeconds[_currentAppId] = _pendingSeconds.GetValueOrDefault(_currentAppId) + seconds;
            _segmentStart = now;
        }
        else
        {
            _segmentStart = Stopwatch.GetTimestamp();
        }
    }

    private long ResolveAppId(nint hwnd)
    {
        uint processId = 0;
        if (WinEvents.GetWindowThreadProcessId(hwnd, &processId) == 0 || processId == 0)
        {
            return 0;
        }

        var path = QueryImagePath(processId) ?? QueryExeName(processId);
        if (path is null)
        {
            return 0;
        }

        Log.Debug($"Process {processId}: {path}");
        if (!_appIdByPath.TryGetValue(path, out var appId))
        {
            try
            {
                appId = _store.GetOrCreateApp(path, DateTime.UtcNow);
                _appIdByPath[path] = appId;
            }
            catch (SqliteException ex)
            {
                Log.Error($"Cannot record app '{path}'", ex);
                return 0;
            }
        }

        if (!_selfElevated && _elevationChecked.Add(appId) && IsElevatedProcess(processId))
        {
            RecordElevated(appId, path);
        }

        return appId;
    }

    /// <summary>Full exe path with the minimal right (PROCESS_QUERY_LIMITED_INFORMATION): no memory access.</summary>
    private string? QueryImagePath(uint processId)
    {
        var process = WinEvents.OpenProcess(WinEvents.ProcessQueryLimitedInformation, false, processId);
        if (process == 0)
        {
            return null;
        }

        try
        {
            fixed (char* buffer = _pathBuffer)
            {
                var size = (uint)_pathBuffer.Length;
                return WinEvents.QueryFullProcessImageNameW(process, 0, buffer, &size) ? new string(buffer, 0, (int)size) : null;
            }
        }
        finally
        {
            Kernel32.CloseHandle(process);
        }
    }

    /// <summary>
    /// Fallback for processes that refuse even limited queries (some protected games):
    /// the exe name from a process snapshot, without opening the process at all.
    /// </summary>
    private static string? QueryExeName(uint processId)
    {
        var snapshot = WinEvents.CreateToolhelp32Snapshot(WinEvents.Th32csSnapProcess, 0);
        if (snapshot == Kernel32.InvalidHandleValue)
        {
            return null;
        }

        try
        {
            var entry = new WinEvents.ProcessEntry32W { Size = (uint)sizeof(WinEvents.ProcessEntry32W) };
            for (var ok = WinEvents.Process32FirstW(snapshot, &entry); ok; ok = WinEvents.Process32NextW(snapshot, &entry))
            {
                if (entry.ProcessId == processId)
                {
                    return new string(entry.ExeFile);
                }
            }

            Log.Debug($"Process {processId} not found in snapshot ({Marshal.GetLastPInvokeError()})");
            return null;
        }
        finally
        {
            Kernel32.CloseHandle(snapshot);
        }
    }
}
