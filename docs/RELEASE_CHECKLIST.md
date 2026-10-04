# Release checklist

## Before tagging

- [ ] `dotnet format --verify-no-changes`, `dotnet build -c Release` (warnings are errors), `dotnet test -c Release`.
- [ ] Cloud changed? `npx supabase db push`, `npx supabase functions deploy --use-api`,
      `powershell -File tools/Test-CloudRls.ps1`, `npx deno test --config supabase/functions/deno.json supabase/functions/tests`.
- [ ] `tools/Publish-Local.ps1`, then `tools/Test-UiSmoke.ps1` (empty) and `tools/Test-UiSmoke.ps1 -SeedDatabase …`
      (seeded) pass; the smoke test also checks that the window opens no network connections.
- [ ] Map changed? `tools/Test-Map.ps1` (live services).
- [ ] UI changed? `tools/Test-Accessibility.ps1 -SeedDatabase …`: every control has a name and is reachable by keyboard.
- [ ] `<Version>` in `Directory.Build.props` bumped (SemVer; `-beta.N` while in beta).
- [ ] `CHANGELOG.md`: a section for the version.
- [ ] `data/whatsnew.json`: the user-facing notes for the version, in English and Russian (shown once after the update).
- [ ] Anti-cheat checklist (`docs/ANTICHEAT.md`) for a release that touches the tracker.

## Release

- [ ] Commit, push, wait for CI to pass.
- [ ] `git tag vX.Y.Z[-beta.N]` and `git push origin vX.Y.Z[-beta.N]`: `release.yml` builds and uploads Setup.exe, the
      portable zip, the full package and the delta to GitHub Releases.

## After

- [ ] An installed copy updates itself (open the window or Settings › Check for updates): the log shows
      `Updating to …`, the window and the tracker restart on the new version, "What's new" appears, statistics are intact.
- [ ] The website's download button shows the new version.
- [ ] Clean install on a fresh Windows user (or VM): Setup.exe → onboarding → tray icon → tracker in autostart
      (`HKCU\…\Run\AimOdometer` points into `%LOCALAPPDATA%\AimOdometerApp\current`).
- [ ] Uninstall from Windows Settings: the tracker stops, the Run value is removed, `%LOCALAPPDATA%\AimOdometer` stays.
