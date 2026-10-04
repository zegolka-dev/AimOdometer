# Architecture

AimOdometer measures the **physical distance a mouse travels on the mouse pad** — not the cursor path — by reading raw sensor counts through the Windows Raw Input API.

```
distance_cm = path_counts / DPI × 2.54        path_counts = Σ sqrt(dx² + dy²)
```

## What the user sees

- AimOdometer starts with Windows and runs in the background; only a tray icon is visible.
- Clicking the tray icon (or the Start menu shortcut) opens the statistics window with all features.
- Closing the window closes only the window; counting continues. "Exit" in the tray menu stops everything.

Internally the always-running part is tiny and separate from the window:

| Process | Tech | Lifetime | Job |
|---|---|---|---|
| `AimOdometer.Tracker.exe` (supervisor) | same exe, NativeAOT | Always (autostart) | Starts the worker and restarts it if it crashes |
| `AimOdometer.Tracker.exe --worker` | C# + Win32 P/Invoke, NativeAOT, no UI framework | Always | Collect input, tray icon, write to SQLite, named pipe |
| `AimOdometer.App.exe` | WPF on .NET 10 | Only while the window is open | Statistics, settings, map, cloud sync |

Measured cost of the always-running pair: 4.2 MB private memory, 0 % CPU when the mouse is still
(see [PERFORMANCE.md](PERFORMANCE.md)).

## Data flow

```
 Mouse(s) ──Raw Input (WM_INPUT, RIDEV_INPUTSINK)──▶ Tracker ──every 60 s / sleep / logoff──▶ SQLite (WAL)
 Foreground window ──SetWinEventHook──────────────▶ Tracker                                     │ read
                                                      │ named pipe (live counter, commands)     ▼
                                                      └──────────────────────────────────────▶ App ──HTTPS──▶ Supabase / Steam / maps
```

- The tracker never talks to the network. Only the App does: update checks, the Map tab (after allowing it), and the cloud after Steam sign-in.
- The keyboard is never read.

## Tracker design rules

1. **Event-driven only.** No polling loops, no short timers (only a 60 s flush timer and a quarter-hour clock check).
   When the mouse is still, the process sleeps in `GetMessageW`.
2. **Batched wake-ups.** A fast mouse posts a `WM_INPUT` per report. The tracker wakes up at most once per 16 ms and
   takes everything queued with one `GetRawInputBuffer` loop. This cut CPU use by more than 10x; see PERFORMANCE.md.
3. **Zero allocations on the hot path.** Records are parsed in place in a reusable native buffer; devices are resolved
   once per handle. A unit test asserts zero allocated bytes over 20 000 batches.
4. **No hooks.** `SetWindowsHookEx` / `WH_MOUSE_LL` / `WH_KEYBOARD_LL` are forbidden: they add latency system-wide and look suspicious to anti-cheats.
5. **Hidden top-level window, not `HWND_MESSAGE`.** Message-only windows do not receive broadcasts (`WM_POWERBROADCAST`, `WM_QUERYENDSESSION`, `WM_TIMECHANGE`, `TaskbarCreated`), which the tracker needs for sleep, logoff and time-zone handling. The window is never shown and never takes focus.
6. **Single instance per session** via the `Local\AimOdometer.Supervisor` and `Local\AimOdometer.Tracker` mutexes.
   `AimOdometer.Tracker.exe --stop` asks the running worker to exit; the supervisor then exits too.
7. **Crash recovery by a supervisor process**, not `RegisterApplicationRestart`: NativeAOT crashes are fast-fails,
   which Windows Error Reporting does not restart (verified). Restarts at most 5 times in 10 minutes.

### Events the tracker reacts to

| Event | Reaction |
|---|---|
| `WM_INPUT` | Batch-read raw input (see above) |
| `WM_INPUT_DEVICE_CHANGE` | Forget device handles; re-plugged mice are resolved again on their next report |
| `WM_TIMER` 60 s | Write pending data to SQLite |
| `WM_TIMER` quarter hour | Start a new hour bucket when the local hour changed |
| `WM_POWERBROADCAST` suspend / resume | Flush / reset flick windows and re-check the clock |
| `WM_QUERYENDSESSION`, `WM_ENDSESSION`, `WM_WTSSESSION_CHANGE` (lock, logoff, disconnect) | Flush |
| `WM_TIMECHANGE` | Clock or time zone changed: flush under the old hour, start a new one |
| `TaskbarCreated` | Explorer restarted: add the tray icon again |

### Foreground app and games

- `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` and `MINIMIZESTART/END` with `WINEVENT_OUTOFCONTEXT`: Windows calls
  the tracker on its own thread through the message loop; nothing is injected into other processes.
- On each switch the tracker gets the exe path with `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` +
  `QueryFullProcessImageNameW` (the minimal right Task Manager also uses: no memory access) and closes the handle at
  once. If a protected game refuses even that, the exe name is taken from a process snapshot without opening it.
- The tracker stores only the exe path (`apps`) and foreground seconds per app and hour (`app_time`); movement buckets
  carry the app id. Time is not counted while the window is minimized, the session is locked, the PC sleeps or
  tracking is paused.
- Which exe is which game is decided outside the tracker by `GameCatalog` (Core), in this order:
  1. the user's rules (`app_rules`: game / not a game / excluded, optional game key to merge several exes),
  2. Steam: the exe lies inside an installed app's folder (libraries from `libraryfolders.vdf`, apps from
     `appmanifest_*.acf`, read with our own KeyValues parser; no sign-in, no network),
  3. the built-in list `data/games.json` (+ optional user `games.json` in the data folder), matched by exe name,
  4. otherwise "Desktop & apps".
  `game_names` holds user renames. A built-in entry with `steamAppId` shares the Steam game's key, so a copy from
  another store adds up with the Steam version.

### Devices

- Identified by the HID device path (`RIDI_DEVICENAME`): VID/PID and interface, without the USB instance, so the same
  mouse keeps its identity in another USB port. Two identical mice connected at once are told apart by instance.
- Product name from `HidD_GetProductString`. A mouse collection whose physical device also exposes a digitizer
  touch pad is recorded as a touchpad.
- Raw input without a device handle (software-injected input: `SendInput`, remote tools) is recorded as
  "Software input" and excluded from totals by default. `MOUSE_MOVE_ABSOLUTE` reports (tablets, RDP) are counted
  separately and never as distance.
- DPI is chosen per mouse from the tray menu (presets) for now; exact values and calibration come with the UI.

## Windows 11 background input coalescing

Windows 11 coalesces raw mouse input delivered to background listeners to roughly 125 Hz while the focused game still receives the full polling rate. Summed deltas are preserved, so total distance is unaffected except for curvature inside each ~8 ms window. The size of that error is measured in phase 2 and recorded in `docs/PERFORMANCE.md`. The flick-speed window (50 ms) is chosen so it contains at least six coalesced samples.

## Projects

| Project | Purpose | AOT-compatible |
|---|---|---|
| `src/AimOdometer.Core` | Models, distance math, aggregation, storage schema, formatting | yes |
| `src/AimOdometer.Win32` | Internal source-generated P/Invoke declarations (`LibraryImport`) | yes |
| `src/AimOdometer.Tracker` | Background process | NativeAOT |
| `src/AimOdometer.App` | WPF UI | no (ReadyToRun) |
| `tests/AimOdometer.Core.Tests`, `tests/AimOdometer.Tracker.Tests` | xUnit v3 tests on Microsoft Testing Platform | — |
| `tools/IconGen` | Renders `assets/logo.svg` into `assets/icon.ico` | — |
| `tools/InputSimulator` | Synthetic mouse input up to 8000 Hz (`SendInput`) | — |
| `tools/PerfProbe` | CPU/memory of the tracker, pipe diagnostics, database dump | — |
| `tools/InputProbe` | Foreground full-rate reader to measure background coalescing loss | — |
| `tools/Run-Benchmarks.ps1` | Runs the PERFORMANCE.md scenarios | — |

Projects listed in the plan but not yet present (`AimOdometer.Cloud`, a BenchmarkDotNet project) are created in the
phase that first needs them, so the repository never contains empty shells.

## Storage

- Data folder: `%LOCALAPPDATA%\AimOdometer\` (separate from the install folder, so reinstalling keeps statistics).
- SQLite in WAL mode: the tracker writes, the App reads concurrently.
- The tracker stores raw **counts** per local hour x device x foreground app x DPI, plus the local date/hour and the
  UTC offset at the time of movement. Centimeters are `path_counts / dpi * 2.54` per row, so a DPI change applies from
  that moment on, and a retroactive correction is a single `UPDATE` of the `dpi` column.
- Game names and everything else are derived in the App from the stored executable ids.
- Schema migrations are numbered SQL scripts (`PRAGMA user_version`); see `src/AimOdometer.Core/Storage/Schema.cs`.

## Map tab

- Nothing is loaded until the user allows the map on the tab (`map_enabled` setting). The UI smoke test checks that the
  window has no network connections and no browser processes otherwise.
- A WebView2 control is created when the tab is shown and disposed when it is left, which ends its `msedgewebview2`
  processes (`tools/Test-Map.ps1` checks this). Its profile lives in `<data folder>\WebView2` with a 64 MB disk cache.
- The page (`src/AimOdometer.App/MapWeb`, MapLibre GL JS bundled, no CDN) is served from the virtual host
  `map.aimodometer.example`; navigation elsewhere is blocked and links open in the default browser. It only draws what
  the window sends (`init`, `route`, `progress` messages) and reports `ready`, `loaded` and errors (no WebGL, tiles).
- Geocoding and routing run in the window, not in the page (`Core/Map/MapClient.cs`): identifying User-Agent, at most one
  request per second per host, every answer cached in the `geo_cache` table (search results and routes never expire).
  Routes come from the FOSSGIS walking router with the OSRM demo server as fallback; with no road (an ocean) the leg is a
  great-circle line, remembered; when no router is reachable it is a straight line for now and retried next time.
- Service addresses come from `data/services.json` built into the app, overridden by `services.json` on the project
  website when it is valid (checked at most once a day), so a provider can be replaced without a release.
- With only a start chosen, the walk goes towards a random well-known city (`data/cities.json`, names in the language
  files): 300 km to ~2500 km away as the crow flies and farther than the all-time distance, remembered per start;
  a city with no walking route (sea, closed border) is replaced, up to four tries. "Another direction" picks again.
- Routes use the full geometry (polyline6), so a position near the start stays on the road.
- The journey is a chain of places (start, destination, next destinations). A period's distance is located along the
  legs proportionally within each leg (the drawn line is simplified, the road distance is exact); the reached point is
  named with a reverse lookup cached per ~1 km cell.

## Cloud (Supabase)

- Project `tphrgryvyxgldozymqzk` (Frankfurt). Schema, functions and tests live in `supabase/`; the CLI is pinned in
  `package.json` (`npm ci`, then `npx supabase …`). Deploy: `npx supabase db push`, `npx supabase functions deploy --use-api`.
- **Sign in with Steam** (OpenID 2.0, not OIDC, so Supabase Auth cannot do it alone), RFC 8252 style:
  1. The window opens a socket on `127.0.0.1:<random port>` and the browser at `auth-steam/start` with
     `challenge = base64url(SHA-256(verifier))`, the port and a random `state`.
  2. `start` stores the handshake (`auth_pending`, 5 minutes) and redirects to Steam.
  3. `callback` checks the assertion (mode, endpoint, exact `return_to`, signed fields, SteamID64) and confirms it with
     Steam (`check_authentication`), creates or finds the user (service email `steam_<id>@users.aimodometer.invalid`,
     email sign-up is disabled) and redirects the browser to the loopback with a one-time code.
  4. The window posts code + verifier to `exchange`; the function checks `S256(verifier)`, deletes the handshake, makes a
     magic-link token for the user with the admin API and verifies it at once: the window gets an ordinary Supabase
     session (access + refresh token), stored with DPAPI.
- **Data**: `daily_stats` = PC x local date x game key x hashed mouse key, absolute daily values. Clients only read
  their own rows (RLS) and write nothing directly; `sync` validates (no future days, ≤ 100 km and ≤ 2M clicks a day,
  peak ≤ 50 m/s …), drops implausible rows and replaces whole days atomically. Rate limits per user and per IP hash.
- **Only the window talks to the cloud**, and only when signed in: on open, every 15 minutes while open, on close.
- Account deletion deletes the auth user; every table cascades from it.

## Releases and updates (Velopack)

- Publishing a version = pushing a tag `vX.Y.Z[-beta.N]`. `.github/workflows/release.yml` tests, publishes the window
  (self-contained, .NET included) and the tracker (NativeAOT) into one folder, packs it with `vpk` (pinned in
  `dotnet-tools.json`; package id `AimOdometerApp`) and uploads Setup.exe, the portable zip, the full package and a
  delta to GitHub Releases (prerelease for `-` versions). Bump `<Version>` in `Directory.Build.props` and CHANGELOG.
- Installed: `%LOCALAPPDATA%\AimOdometerApp\current\…` next to `Update.exe`; portable: the same layout inside the
  unzipped folder (`AimOdometer.exe` is Velopack's launcher). Statistics stay in `%LOCALAPPDATA%\AimOdometer`.
- `Program.Main` runs Velopack first (install/update/uninstall hooks; uninstall stops the tracker and removes the Run
  value). The window (`Services/Updates.cs`) checks GitHub when it opens and every hour, downloads the update, waits a
  few seconds with a banner, syncs the cloud, stops the tracker (its files are replaced; pipe `Shutdown`, then kill
  after 5 s) and calls `ApplyUpdatesAndRestart`. The new window starts the new tracker, which rewrites the Run value.
  Developer builds (not installed) never check. Verified 2026-10-04: beta.1 → beta.2 with a 0.2 MB delta in 8 s.

## Named pipe

`\\.\pipe\AimOdometer.Tracker.<session id>`, accessible only to the current user. Request: 1-byte command + 8-byte
argument; response: status byte + fixed payload (`src/AimOdometer.Core/Ipc/TrackerProtocol.cs`). Commands: `Ping`,
`GetStatus` (today's distance including unsaved data, pause state, diagnostics), `Flush`, `Pause`, `Resume`,
`ReloadSettings`, `Shutdown`. Requests are executed on the tracker's window thread via `SendMessageTimeout`, so no
tracker state is shared between threads.

## Building

```bash
dotnet build -c Release
dotnet test -c Release
dotnet publish src/AimOdometer.Tracker -c Release -o artifacts/tracker
```

The NativeAOT publish requires the MSVC linker ("Desktop development with C++" / VS Build Tools). A normal `dotnet build`/`dotnet run` of the tracker works without it.

### Developer pitfall: packaged terminals see a different %LOCALAPPDATA%

If you run builds from a terminal that belongs to an MSIX-packaged app (for example a terminal inside a packaged IDE
or AI assistant), Windows virtualizes `%LOCALAPPDATA%` for every process started from it: the tracker then writes to
`%LOCALAPPDATA%\Packages\<package>\LocalCache\Local\AimOdometer` while the window you open from Explorer reads the
real `%LOCALAPPDATA%\AimOdometer`. Two databases appear, and SQLite `-wal` files can get mixed between them.
Start the tracker and the window through Explorer (`explorer.exe "C:\path\AimOdometer.Tracker.exe"`), and use
`AIMODOMETER_DATA_DIR` (outside AppData) for experiments. Installed copies are never affected.

To regenerate the icon after editing `assets/logo.svg`:

```bash
dotnet run --project tools/IconGen -c Release -- assets/logo.svg assets/icon.ico assets/logo-512.png
```
