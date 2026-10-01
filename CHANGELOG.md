# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versions follow [SemVer](https://semver.org/).

## [Unreleased]

### Added
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
- Phase 1 foundation: .NET 10 solution (`AimOdometer.slnx`) with Core, Win32, Tracker (NativeAOT) and App (WPF) projects.
- Tracker skeleton: single instance per session (named mutex), hidden message window, `--stop` command.
- App skeleton: dark window that shows whether the tracker is running.
- `Distance` conversion (sensor counts → centimeters) with unit tests.
- Logo (`assets/logo.svg`) and `tools/IconGen` to regenerate `assets/icon.ico` from it.
- CI on GitHub Actions: format check, Release build with warnings as errors, tests, NativeAOT publish of the tracker.
- Architecture docs, MIT license, `.env.example`.
