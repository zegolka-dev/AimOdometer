# Anti-cheat compatibility

AimOdometer reads the mouse with Windows Raw Input in background mode (`RIDEV_INPUTSINK`) and asks Windows which
window is in front (`GetForegroundWindow` → the process's image path). It installs no hooks (`SetWindowsHookEx`), no
drivers, injects nothing, never opens game processes, never reads or writes their memory, draws no overlay, simulates
no input and never reads the keyboard. Achievement notifications wait until a full-screen game is left
(`SHQueryUserNotificationState`). The public version of this page is `site/content/anticheat.*.html`.

## Manual test checklist (every release candidate)

For each game: start the tracker before the game; play at least 30 minutes (FACEIT / Vanguard: two matches); then check:

- [ ] The game started without anti-cheat warnings; no "untrusted", kick or crash.
- [ ] The session's distance is recorded under that game, and matches expectations (DPI × movements).
- [ ] FPS and input feel: average FPS / 1% low with and without the tracker (CapFrameX or the in-game counter) differ
      only within noise.
- [ ] No achievement popped up during the game; it appeared after leaving it.
- [ ] Record the result below (date, version, result, notes).

Games: **CS2** (VAC, normal mode, no `-insecure`), **CS2 on FACEIT** (client + match), **Apex Legends** (EAC),
**VALORANT** (Vanguard, after a reboot with the tracker running), if possible **Fortnite** (EAC / BattlEye).

## Results

| Game | Anti-cheat | Result | Version | Date | Notes |
| --- | --- | --- | --- | --- | --- |
| Counter-Strike 2 | VAC | OK | 0.0.1 | 2026-10-02 | 30+ min, distance recorded |
| Counter-Strike 2 on FACEIT | FACEIT AC | OK | 0.0.1 | 2026-10-02 | anti-cheat client running, no warnings |
| Apex Legends | Easy Anti-Cheat | OK | 0.0.1 | 2026-10-03 | full screen, distance recorded |
| Apex Legends | Easy Anti-Cheat | OK | 0.1.0-beta.15 to .17 | 2026-10-04 | author's PC, tracker running as administrator, about 6 h, 1.2 km recorded |
| VALORANT | Vanguard | OK | 0.1.0-beta | 2026-10-04 | a beta tester's PC, reported by the author: no warnings or kicks, distance recorded |
| Fortnite | EAC / BattlEye | OK | 0.1.0-beta | 2026-10-04 | a beta tester's PC, reported by the author |
| Counter-Strike 2 | VAC | OK | 0.1.0-beta | 2026-10-04 | retest on the beta, a beta tester's PC, reported by the author |
| Counter-Strike 2 on FACEIT | FACEIT AC | OK | 0.1.0-beta | 2026-10-04 | retest on the beta, a beta tester's PC, reported by the author |

When a result changes, update this table and the table on the website (`site/content/anticheat.*.html`, then
`python tools/build_site.py`).
