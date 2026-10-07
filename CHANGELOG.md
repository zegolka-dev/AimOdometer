# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versions follow [SemVer](https://semver.org/).

## [Unreleased]

### Added
- Games from other launchers are detected without a list: anything in the game library folders of the EA app,
  Epic, Ubisoft Connect, GOG, Xbox / Game Pass, Riot or Rockstar counts as a game named after its folder; the
  launchers, anti-cheats, installers and crash reporters next to them do not (`GameCatalog.LauncherGame`).
- Built-in list: Titanfall 2, Battlefield 1, V and 4, STAR WARS Battlefront II, Watch_Dogs 2, Far Cry 5 and 6
  (merged with their Steam versions).
- The tracker notices a game that runs as administrator while it does not (Windows hides the mouse from it then) and
  the overview offers "Turn counting on" for that game by name. First run: "Count games that run as administrator" is
  on by default (one UAC prompt when finishing).

### Fixed
- Full-screen games that Windows did not make the foreground window (the desktop stayed in front): the full-screen
  window under the pointer now gets the movement (`ForegroundTracker.OnScreen`).

## [0.1.0-beta.22] - 2026-10-08

### Added
- "Recommended" tags on "Start with Windows" (settings and first run) and "Install updates automatically", with a
  hint under autostart: without it nothing is counted after a restart until the app is opened.
- The overview warns when start with Windows is off (and the administrator task does not start the tracker either),
  with a "Turn on" button. Released quietly: no What's new entry.

## [0.1.0-beta.21] - 2026-10-05

### Fixed
- Games whose window took the foreground without Windows reporting it (Watch_Dogs 2 started from Steam): the movement
  kept going to the previous window, usually "Desktop and other". The tracker now also checks the foreground window
  once a second (`ForegroundTracker.Recheck`, one GetForegroundWindow call) and logs every change the hook missed.

## [0.1.0-beta.20] - 2026-10-05

### Added
- Demo leaderboards for promo recordings (`DemoSocial`): only in a test window (`AIMODOMETER_DATA_DIR`) and only when
  `AIMODOMETER_DEMO_SOCIAL` names a JSON file with friends and world rows; real users never see it. In the same test
  window `AIMODOMETER_UI_SCALE` (1 to 3) enlarges the whole UI for sharp full-screen promo captures.
- Website download counter for the author: a click on a download button sends the button, page language and the
  referring site's host to the `download-click` Edge Function, which stores it in `site_downloads` (no policies, so no
  client can read it) with a daily-changing hash of the IP instead of the address. `tools/Read-Downloads.ps1` shows
  clicks and people per day, buttons, referrers and GitHub's own download counts. The privacy page says so.
- "Support the author" at the bottom of the sidebar: quiet grey text that lights up violet to pink on hover or keyboard
  focus (a shine runs through the letters and the heart beats once; only the colour fades with Windows animations
  off). It opens a window with the payment services, each with a note on who it suits (Boosty: any country, Russian or
  foreign cards and SBP; PayPal: abroad); each opens the author's page in the browser. Services live in `SupportLinks`; one without an https link is hidden, and with none
  the sidebar link is hidden.
- Streak flames: the daily streak (days in a row with at least 1 m) is a flame that grows and changes its look at 20,
  50, 100, 200, 300, 400, 500 and 1000 days (Spark, Flame, Blaze, Neon, Blue fire, Plasma, Golden fire, Aurora,
  Legend): a hot core, a glow, sparks, a halo, and a gentle flicker from 100 days when shown large. The overview shows
  it with the number of days, the tier and progress to the next flame; the flame is grey until today counts and lights
  up live once the mouse has moved 1 m. Leaderboards show the same flame, small, with the number next to each name.
  `StreakTiers` (Core), `StreakFlame` (vector control), `player_streak` in the cloud (migration
  20261006000000_streaks.sql) and a `streak` field on board rows; older clients ignore it.

### Changed
- English sidebar: "Complaints and suggestions" is now "Complaints and ideas", so it no longer gets cut off.

## [0.1.0-beta.19] - 2026-10-05

### Added
- Gear reminder: when a mouse pad, arm sleeve, set of glides or mouse reaches 90 % of its expected lifetime, the tray
  shows "Time to replace your gear" once per item (same rules as achievement notifications: the setting, never during
  a full-screen game). `GearWear.DueForReplacement`, `SettingKeys.GearAnnounced`.
- Website: a language button (globe and RU/EN) in the hero and on document pages; a browser set to Russian opens the
  Russian pages unless a language was chosen; the features list mentions the gear reminder; tile links are versioned.
- New website design: huge gradient headings, a two-row marquee of app screens that moves with scrolling, a magnetic
  hero picture, text that lights up letter by letter, a light features section with large numbers, stacking cards
  with the app's pages, and the anti-cheat, privacy, FAQ and download sections. Same Neon Violet palette, type Exo 2
  (Latin and Cyrillic). Motion lives in `site/site.js` and stays off with "reduce motion"; tiles are cut from the
  demo screenshots by `tools/make_site_tiles.py`; CSS and JS links carry a content hash so browsers never keep an old
  copy.
- `tools/GameInputProbe`: reads the mouse through Microsoft GameInput and background Raw Input at once. Phase 10 result:
  GameInput is coalesced the same way (0.43 % less path than Raw Input), so the tracker stays on Raw Input.

### Fixed
- `AimOdometer.Tracker --stop` could not stop a tracker running as administrator (window messages to an elevated
  process are dropped); it now falls back to the pipe's Shutdown command. `Run-Benchmarks.ps1` refuses to run unless the
  tracker that answers writes to the benchmark folder; `PerfProbe` prints that folder.

## [0.1.0-beta.18] - 2026-10-05

### Added
- Steam sign-in that has not come back after 20 s shows why that usually happens (providers blocking
  steamcommunity.com: a VPN for that minute helps, the sign-in is kept) and a button to open the Steam page again.
- `tools/Test-Accessibility.ps1`: visits every page and fails on controls without an accessible name or out of keyboard
  reach. Code signing policy page on the site, `docs/CODE_SIGNING.md`, and SignPath steps in `release.yml` (skipped
  until the SignPath variables exist).

### Fixed
- Screen readers announced icon-and-text buttons (Share, Sign in with Steam, Measure DPI, Check for updates,
  Complaints and suggestions, onboarding Next) and date pickers as nameless.
- Contrast to WCAG AA: muted text, white text on violet buttons and on the Creator tag reach 4.5:1.
- What's new and the onboarding mouse list no longer put text under the scroll bar.
- `PerfProbe` works on a tracker running as administrator; `Read-Feedback.ps1` prints Cyrillic correctly.

## [0.1.0-beta.17] - 2026-10-05

### Changed
- No long dashes in anything people read (app texts, tray menu, site, README, docs): a plain hyphen instead, and the
  tray shows a mouse as "Name (800 DPI)". Code comments keep theirs.

## [0.1.0-beta.16] - 2026-10-05

### Added
- "Check for updates" in the sidebar, under the counter status: finds, downloads and installs a newer version right
  away (also with automatic updates off); the answer shows under the link.

### Fixed
- The sidebar's bottom links had no room: more space between the status, the links and the window edge.

## [0.1.0-beta.15] - 2026-10-05

### Added
- Honorary titles on leaderboards (`profiles.titles`, set by the author only; migration `20261005000100_titles.sql`):
  "Creator" in the app's violet with a star and a soft glow, "Beta tester" in sky blue with a bug icon. Everyone signed
  in during the beta is a beta tester. Tags show titles first, then shame badges, always with an icon and a word.

## [0.1.0-beta.14] - 2026-10-05

### Added
- Fair play. Leaderboards show the distance-weighted DPI behind each player's distance and the DPI of their fastest
  flick, so a DPI set far below the mouse's real one is visible. Four "shame" achievements, hidden until earned, shown
  first in red and as badges next to the name on leaderboards (`FairPlay`, `fair_play_badges`):
  Clown (10 km in a day below 200 DPI), Blockhead (100 m below 100 DPI), Fool (a flick over 30 m/s) and Booster
  (a day over 100 km or a flick over 50 m/s, rejected by the sync and remembered in `profiles.boosted_at`).
- Cloud rows carry `dpi` and `peakDpi` (migration `20261005000000_fair_play.sql`); history is uploaded again once so
  older days get their DPI.

### Changed
- The social pgTAP test hides real players inside its rolled-back transaction, so it passes on a live project.

## [0.1.0-beta.13] - 2026-10-04

### Added
- Settings › "Show an example": the tracker shows a sample achievement notification (new pipe command
  `TestNotification`), with a hint about Windows notification settings when nothing appears.

### Fixed
- The account card no longer says friends and leaderboards are coming: they are here.
- The notifications hint says they appear right away unless a full-screen game is in front.

## [0.1.0-beta.12] - 2026-10-04

### Changed
- Overview numbers roll like an odometer: from 0 when the page appears, then only the difference on live updates
  (`Motion.CountUp`).
- "Of the way to the Moon" shows three significant digits in the culture's percent style ("0,0259 %", not "0,025854%").

## [0.1.0-beta.11] - 2026-10-04

### Changed
- The activity heat map lights up from midnight to 23:00 when it appears or the period changes.

## [0.1.0-beta.10] - 2026-10-04

### Changed
- Dialogs (What's new, feedback, share, onboarding, DPI measurement) fade and zoom in over a fading backdrop
  (`Motion.Appear` attached property).
- Progress bars fill from the left when a page appears; the share donut sweeps in.

## [0.1.0-beta.9] - 2026-10-04

### Changed
- Motion: buttons, navigation items and period pickers fade their hover state in and out and shrink slightly when
  pressed; the selected navigation item's accent bar grows in; bar charts grow in left to right when they appear or the
  period changes (skipped when Windows animations are off).
- The focus ring shows only for keyboard focus (Tab), no longer around an item clicked with the mouse.

## [0.1.0-beta.8] - 2026-10-04

### Fixed
- A damaged statistics file (power cut mid-write) stopped both the window and the counter for good with a raw SQLite
  error. It is now kept as `aimodometer.db.broken-<time>` and the newest readable daily backup takes its place (or an
  empty database when there is none); the window says so once.
- A database that failed to open kept its file handle, so it could not be moved or replaced.
- Typing "NaN" as DPI crashed the window; "NaN" or "Infinity" as gear lifetime saved a broken item.
- A pipe client that connected and never wrote blocked the tracker's pipe for good (now dropped after 2 s).
- Name, DPI, lifetime and map search fields have length limits.

## [0.1.0-beta.7] - 2026-10-04

### Changed
- Gear: own line icons for mouse pads, sleeves, glides and mice (the sleeve used a hand pointer); "Measure DPI" shows a
  ruler instead of the hand pointer.

## [0.1.0-beta.6] - 2026-10-04

### Added
- "Complaints and suggestions": a message to the author from the app (bug / idea / other, optional contact, version),
  stored in the cloud (`feedback` table and Edge Function, rate limited); `tools/Read-Feedback.ps1` lists them.
- Settings › Startup › "Count games that run as administrator": Windows does not deliver raw mouse input to a normal
  program while an elevated window is in front, so elevated games (Genshin Impact, Honkai: Star Rail, ZZZ) showed 0 m.
  The tracker can now run elevated through a Task Scheduler task (one UAC prompt to switch on or off); its pipe gets a
  medium integrity label so the normal window still reaches it, and it opens the window de-elevated through Explorer.

### Fixed
- Updates and uninstall handle a tracker running with administrator rights (cannot be inspected or killed from the
  window: it is asked to exit over the pipe).

## [0.1.0-beta.5] - 2026-10-04

### Added
- Website (`site/`, EN and RU): landing page, privacy policy, anti-cheat page; GitHub Pages deployment with
  `services.json`; generated by `tools/build_site.py` with demo-data screenshots.
- Settings › Your data: export to CSV in the regional format, and "Delete statistics on this PC" (stops the tracker,
  deletes everything but the logs, restarts the window).
- `PRIVACY.md`, `docs/ANTICHEAT.md` (checklist and results), `docs/RELEASE_CHECKLIST.md`.

## [0.1.0-beta.4] - 2026-10-04

### Added
- Phase 8: "Friends" tab with leaderboards (Steam friends who use AimOdometer, and the world), by week, month or all
  time, overall or per game; your world place even outside the top 100; invite link; privacy switches in Settings
  (friends see you by default, the world board is opt-in).
- Cloud: `social` Edge Function, Steam friend lists cached for an hour, world ranks precomputed every 15 minutes
  (materialized view + pg_cron), service-only board functions that never expose other players' ids; 16 more pgTAP and
  3 more Deno tests.

## [0.1.0-beta.3] - 2026-10-04

### Added
- "What's new" window on the first start after an update (notes in `data/whatsnew.json`).

## [0.1.0-beta.2] - 2026-10-04

### Added
- Settings › About: "Check for updates" button with the answer (latest version, found, failed).

## [0.1.0-beta.1] - 2026-10-04

First beta for friends: everything from phases 1-7 plus automatic updates.

### Added
- Releases and automatic updates (Velopack): Setup.exe and a portable zip on GitHub Releases, built by CI from a
  `v*` tag; the window looks for a new version when it opens and every hour, downloads only the changes, stops the
  tracker, installs and restarts (Settings › Install updates automatically). The window is self-contained (.NET
  included). Uninstalling stops the tracker and removes it from autostart; statistics stay.
- Phase 7 cloud:
  - Sign in through Steam (Settings › Steam account and cloud): the browser signs in on Steam's own page, the window
    gets a Supabase session through a loopback redirect with a PKCE-style verifier; the session is stored with DPAPI.
  - Sync of daily totals (date x game x mouse) from the window: on open, every 15 minutes while open and on close;
    totals of all your PCs and the list of PCs in Settings; sign out; delete the cloud account and its data.
  - Supabase backend in `supabase/`: schema with row level security on every table, Edge Functions `auth-steam`,
    `sync` (plausibility checks, rate limits), `profile`, `account-delete`; pgTAP, Deno and .NET tests; CI job with a
    secret scan.
- Phase 6 world map ("Where would you get?"):
  - Pick your city and the map walks towards a random well-known city right away ("Another direction" for a new
    one); your own destination and further cities are optional. The distance of today, this week, this month or all
    time is shown as a walk along real roads, with the reached place named and markers for every period.
  - Loads nothing until allowed on the tab; WebView2 and its processes exist only while the tab is open.
  - OpenFreeMap dark map (MapLibre GL JS bundled), Nominatim search, FOSSGIS/OSRM walking routes; one request per
    second at most, every answer cached; straight line across oceans; service addresses replaceable via services.json.
  - `tools/Test-Map.ps1` (live services) and a no-network check in the UI smoke test.
- Phase 5 fun features:
  - Achievements: 39 in `data/achievements.json` (distance, day distance, streaks, flicks, games, night owl,
    clicks, wheel, per-mouse, active days) with progress bars on a new Achievements page. The tracker checks them
    after a flush and shows one tray notification, never during a full-screen game (it waits until you leave);
    can be turned off in Settings.
  - Comparisons ("That's 1.3 Burj Khalifas") and the share of the way to the Moon on the overview
    (`data/comparisons.json`).
  - Activity heat map (weekday × hour) on the statistics page.
  - Gear wear: mouse pads, arm sleeves, mice and glides with a lifetime, wear progress, "time to replace", retire and delete.
  - Share cards 1080×1080 and 1080×1920 (day, week, month, all time, top games, latest achievement): copy to the
    clipboard, save PNG to Pictures\AimOdometer, open the folder.
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
- Daily database backup in `<data folder>\backups` (newest 7 kept), made with SQLite `VACUUM INTO`.
- The tracker reports its data folder over the pipe; the window warns when it differs from its own.
- The tracker ignores `AIMODOMETER_DATA_DIR` (only `--data-dir` changes its folder) and never passes it to the window it
  opens; a window in test mode does not start the tracker.

### Fixed
- Steam name and avatar stayed empty when the Steam Web API key secret had a trailing newline (the server trims it
  now); the window asks for the profile on start and after signing in, and logs why Steam could not be asked.
- The tracker logs why it stopped (tray menu Exit, update, Windows shutdown), so a missing tray icon can be explained.
- The window could crash with a stack overflow when a search result was announced to accessibility tools (a map
  point printed itself recursively).
- The achievement share card printed the achievement name at number size, so it ran off the card.
- The statistics chart crashed when the period switched to a shorter one while the mouse was over a bar.
- A test window on another data folder activated the real window instead of opening.
- Date pickers followed en-US formatting instead of the UI language.
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
