# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versions follow [SemVer](https://semver.org/).

## [Unreleased]

### Added
- Phase 4 window (WPF, palette "Neon Violet", Fluent dark theme):
  - Overview with a live today counter, week/month/all time, 14-day chart, top games today, streak.
  - Statistics for any period: totals, averages per active and calendar day, records (best day/week, streak,
    fastest flick with game), daily/weekly chart, horizontal vs vertical, clicks per minute and per meter.
  - Games: share donut, distance/time/km/h per game with exe icons, rename, merge, not a game, hide, restore.
  - Gear: mice with exact DPI, presets, DPI calibration wizard (ruler or bank card, 3 tries), include/exclude
    devices (touchpads).
  - Settings: language (live switch), metric/imperial, start with Windows, always show the tray icon, data folder.
  - First-run onboarding in 5 steps; single window instance; the window starts the tracker if needed.
  - English and Russian UI from JSON files; adding a language is one file.
  - `tools/Publish-Local.ps1` publishes window and tracker into one folder (installed layout).
  - Settings › Mouse and DPI: the same per-mouse controls as Gear (manual DPI, presets, measure, include).
  - Onboarding's mouse step lists the mice with manual DPI input instead of "move your mouse".
  - `tools/Test-UiSmoke.ps1`: opens the window on a throw-away data folder, walks onboarding, every page and every
    period switch, and fails on any logged error.
  - `AIMODOMETER_DATA_DIR` environment variable to point the app at another data folder (tests, screenshots).
- Phase 3 games:
  - Foreground app tracking with out-of-context WinEvent hooks; foreground time per app and hour (not counted while
    minimized, locked, asleep or paused); movement is attributed to the active app.
  - Steam library detection without sign-in (own VDF/ACF parser), built-in list of 55 non-Steam games
    (`data/games.json`, extendable with a user file), user rules (game / not a game / excluded / merge / rename).
  - Per-game summary: distance, foreground time, km/h, clicks, peak speed (`PerfProbe --games`); per-app
    diagnostics (`--apps`), live event monitor (`--watch`), settings (`--set`), debug log of foreground changes.
  - Verified with CS2 (VAC) and Apex Legends (EAC, fullscreen).
  - Database schema v2 (`app_time`, `app_rules`, `game_names`); existing databases are upgraded in place.
- Phase 2 tracker core:
  - Raw Input collection for all mice with `RIDEV_INPUTSINK`, batched wake-ups (at most one per 16 ms) and an
    allocation-free accumulator: path, X/Y, clicks per button, wheel notches, active seconds, peak flick speed.
  - Device identification by HID path (VID/PID, product name), touchpad detection, separate counters for absolute
    (tablet/RDP) input, software-injected input recorded separately and excluded by default.
  - Local SQLite database (own NativeAOT-friendly wrapper over `e_sqlite3`, WAL) with hourly buckets by local time and
    UTC offset; flush every 60 s and on sleep, lock, logoff, shutdown and exit; retry on write errors.
  - Tray icon with today's distance, per-mouse DPI presets, pause (15 min / 1 h / until restart), start with Windows,
    open data folder, exit. English and Russian.
  - Supervisor process that restarts the tracker after a crash (with crash-loop limit).
  - Named pipe for the UI and tools (status, flush, pause, resume, reload settings, shutdown).
  - Rotating file log (5 MB x 2).
  - Tools: InputSimulator (up to 8000 Hz), PerfProbe (precise CPU via cycle counts, memory, DB dump),
    InputProbe (coalescing accuracy), `Run-Benchmarks.ps1`; results in `docs/PERFORMANCE.md`.
  - Verified on real hardware: 0.116 % of one core with a ~6000 Hz mouse, no anti-cheat complaints in CS2 (VAC) with
    FACEIT AC running.

### Changed
- Logo recolored to the chosen palette.
- Autostart logic moved to Core (shared by tracker and window).

### Added (data safety)
- Daily database backup in `<data folder>ackups` (newest 7 kept), made with SQLite `VACUUM INTO`.
- The tracker reports its data folder over the pipe; the window warns when it differs from its own.
- The tracker ignores `AIMODOMETER_DATA_DIR` (only `--data-dir` changes its folder) and never passes it to the window it
  opens; a window in test mode does not start the tracker.

### Fixed
- Gear and Settings crashed when showing a mouse: the shared template could not find its converters (moved to
  `Themes/Converters.xaml`, merged before the templates).
- Overview failed when two apps had the same exe name in different folders.
- A failing view no longer floods the screen with error dialogs (one at a time, the rest go to the log).
- Raw mouse input for DPI calibration was never enabled when the window opened with an overlay (the listener was
  created before WPF attached the window's presentation source).

### Known issues
- Windows 11 coalesces background mouse input; during intense aiming about 2 % of the path is not recorded.
- Phase 1 foundation: .NET 10 solution (`AimOdometer.slnx`) with Core, Win32, Tracker (NativeAOT) and App (WPF) projects.
- Tracker skeleton: single instance per session (named mutex), hidden message window, `--stop` command.
- App skeleton: dark window that shows whether the tracker is running.
- `Distance` conversion (sensor counts → centimeters) with unit tests.
- Logo (`assets/logo.svg`) and `tools/IconGen` to regenerate `assets/icon.ico` from it.
- CI on GitHub Actions: format check, Release build with warnings as errors, tests, NativeAOT publish of the tracker.
- Architecture docs, MIT license, `.env.example`.
