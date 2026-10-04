<p align="center"><img src="assets/logo-512.png" width="128" alt="AimOdometer logo"></p>

<h1 align="center">AimOdometer</h1>

<p align="center">How far does your mouse really travel? Real centimeters on the pad — per game, per mouse, per day.</p>

<p align="center"><a href="README.ru.md">Русская версия</a></p>

> **Website: [zegolka-dev.github.io/AimOdometer](https://zegolka-dev.github.io/AimOdometer/)** ·
> [Privacy](PRIVACY.md) · [Anti-cheat](docs/ANTICHEAT.md)
>
> **Status: beta.** See [docs/PLAN.md](docs/PLAN.md) for the roadmap (in Russian) and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the design.

## Download

Windows 10 22H2+ / Windows 11, 64-bit. Get the latest version from
[Releases](https://github.com/zegolka-dev/AimOdometer/releases):

- **`AimOdometerApp-win-Setup.exe`**: installs for your user (no admin rights), adds Start menu and desktop shortcuts and
  starts with Windows.
- **`AimOdometerApp-win-Portable.zip`**: unzip anywhere and run `AimOdometer.exe`.

Both update themselves: when the window opens (and every hour while it is open) AimOdometer looks for a new release,
installs it and restarts. This can be switched off in Settings. .NET is included, nothing else to install.

The builds are not code-signed yet, so Windows SmartScreen may say "Windows protected your PC": click
**More info → Run anyway**.

## Why another mouse odometer?

Classic tools (Mousotron, Mouse Odometer, …) measure the **cursor** in screen pixels. In shooters like CS2, Apex or Valorant the cursor is locked to the center while the game reads raw input, so those tools miss most of the movement.

AimOdometer reads **raw sensor counts** through the Windows Raw Input API and converts them using your mouse DPI:

```
centimeters = counts / DPI × 2.54
```

## Principles

- **Featherweight background process.** A tiny NativeAOT tracker, event-driven, no polling. Target: ≤ 15 MB RAM, ~0% CPU when idle.
- **Anti-cheat friendly.** No hooks, no injection, no overlays, no game memory access. Only passive Raw Input and the name of the active app.
- **Private.** The keyboard is never read. Signing in with Steam is optional; it syncs daily totals per game and mouse to the AimOdometer cloud so you see all your PCs together. Without it, AimOdometer makes no network requests except an optional update check and the Map tab, which loads only after you allow it there (map tiles from OpenFreeMap, city search with Nominatim, walking routes with OSRM; they see your IP address and the cities you search, nothing else).

## Known limitations

- On Windows 11, background apps receive mouse input merged into ~125 reports per second. Straight movements are
  measured exactly, but during intense aiming the tracker records about 2 % less path than the mouse really travelled
  (measured: −2.3 %). See [docs/PERFORMANCE.md](docs/PERFORMANCE.md).
- If you change DPI with a button on the mouse, AimOdometer cannot see it: set the new DPI in the tray menu.

## Building from source

Requirements: Windows 10 22H2+ / 11 x64, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Publishing the NativeAOT tracker also needs the Visual Studio Build Tools C++ workload.

```bash
dotnet build -c Release
dotnet test -c Release
```

## License

[MIT](LICENSE)
