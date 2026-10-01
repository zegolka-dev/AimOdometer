# Architecture

AimOdometer measures the **physical distance a mouse travels on the mouse pad** — not the cursor path — by reading raw sensor counts through the Windows Raw Input API.

```
distance_cm = path_counts / DPI × 2.54        path_counts = Σ sqrt(dx² + dy²)
```

## What the user sees

- AimOdometer starts with Windows and runs in the background; only a tray icon is visible.
- Clicking the tray icon (or the Start menu shortcut) opens the statistics window with all features.
- Closing the window closes only the window; counting continues. "Exit" in the tray menu stops everything.

Internally this is **two processes**, so the always-running part stays tiny:

| Process | Tech | Lifetime | Job |
|---|---|---|---|
| `AimOdometer.Tracker.exe` | C# + Win32 P/Invoke, NativeAOT, no UI framework | Always (autostart) | Collect input, tray icon, write to SQLite |
| `AimOdometer.App.exe` | WPF on .NET 10 | Only while the window is open | Statistics, settings, map, cloud sync |

## Data flow

```
 Mouse(s) ──Raw Input (WM_INPUT, RIDEV_INPUTSINK)──▶ Tracker ──every 60 s / sleep / logoff──▶ SQLite (WAL)
 Foreground window ──SetWinEventHook──────────────▶ Tracker                                     │ read
                                                      │ named pipe (live counter, commands)     ▼
                                                      └──────────────────────────────────────▶ App ──HTTPS──▶ Supabase / Steam / maps
```

- The tracker never talks to the network. Only the App does: update checks, the Map tab, and cloud features after Steam sign-in.
- The keyboard is never read.

## Tracker design rules

1. **Event-driven only.** No polling loops, no short timers. When the mouse is still, the process sleeps in `GetMessageW`.
2. **Zero allocations on the hot path.** Raw input is read in batches with `GetRawInputBuffer` into a reusable native buffer.
3. **No hooks.** `SetWindowsHookEx` / `WH_MOUSE_LL` / `WH_KEYBOARD_LL` are forbidden: they add latency system-wide and look suspicious to anti-cheats.
4. **Hidden top-level window, not `HWND_MESSAGE`.** Message-only windows do not receive broadcasts (`WM_POWERBROADCAST`, `WM_QUERYENDSESSION`, `WM_TIMECHANGE`, `TaskbarCreated`), which the tracker needs for sleep, logoff and time-zone handling. The window is never shown and never takes focus.
5. **Single instance per session** via the `Local\AimOdometer.Tracker` mutex. `AimOdometer.Tracker.exe --stop` asks the running instance to exit.

## Windows 11 background input coalescing

Windows 11 coalesces raw mouse input delivered to background listeners to roughly 125 Hz while the focused game still receives the full polling rate. Summed deltas are preserved, so total distance is unaffected except for curvature inside each ~8 ms window. The size of that error is measured in phase 2 and recorded in `docs/PERFORMANCE.md`. The flick-speed window (50 ms) is chosen so it contains at least six coalesced samples.

## Projects

| Project | Purpose | AOT-compatible |
|---|---|---|
| `src/AimOdometer.Core` | Models, distance math, aggregation, storage schema, formatting | yes |
| `src/AimOdometer.Win32` | Internal source-generated P/Invoke declarations (`LibraryImport`) | yes |
| `src/AimOdometer.Tracker` | Background process | NativeAOT |
| `src/AimOdometer.App` | WPF UI | no (ReadyToRun) |
| `tests/*` | xUnit v3 tests on Microsoft Testing Platform | — |
| `tools/IconGen` | Renders `assets/logo.svg` into `assets/icon.ico` | — |

Projects listed in the plan but not yet present (`AimOdometer.Cloud`, `AimOdometer.Tracker.Tests`, benchmarks, input simulator) are created in the phase that first needs them, so the repository never contains empty shells.

## Storage

- Data folder: `%LOCALAPPDATA%\AimOdometer\` (separate from the install folder, so reinstalling keeps statistics).
- SQLite in WAL mode: the tracker writes, the App reads concurrently.
- The tracker stores raw **counts**, the local date/hour and the UTC offset at the time of movement, plus short device and executable ids. Centimeters, game names and everything else are derived in the App. Changing DPI later can therefore be applied retroactively.

## Building

```bash
dotnet build -c Release
dotnet test -c Release
dotnet publish src/AimOdometer.Tracker -c Release -o artifacts/tracker
```

The NativeAOT publish requires the MSVC linker ("Desktop development with C++" / VS Build Tools). A normal `dotnet build`/`dotnet run` of the tracker works without it.

To regenerate the icon after editing `assets/logo.svg`:

```bash
dotnet run --project tools/IconGen -c Release -- assets/logo.svg assets/icon.ico assets/logo-512.png
```
