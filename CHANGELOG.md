# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versions follow [SemVer](https://semver.org/).

## [Unreleased]

### Added
- Phase 1 foundation: .NET 10 solution (`AimOdometer.slnx`) with Core, Win32, Tracker (NativeAOT) and App (WPF) projects.
- Tracker skeleton: single instance per session (named mutex), hidden message window, `--stop` command.
- App skeleton: dark window that shows whether the tracker is running.
- `Distance` conversion (sensor counts → centimeters) with unit tests.
- Logo (`assets/logo.svg`) and `tools/IconGen` to regenerate `assets/icon.ico` from it.
- CI on GitHub Actions: format check, Release build with warnings as errors, tests, NativeAOT publish of the tracker.
- Architecture docs, MIT license, `.env.example`.
