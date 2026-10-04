# Code signing (SignPath Foundation)

Unsigned programs trigger Windows SmartScreen ("Windows protected your PC") and look suspicious to antivirus
software. Open-source projects can get a free certificate from the [SignPath Foundation](https://signpath.org);
the signing itself runs on [SignPath.io](https://signpath.io), triggered from GitHub Actions.

Public policy page: [site/content/code-signing.en.html](../site/content/code-signing.en.html), published at
https://zegolka-dev.github.io/AimOdometer/code-signing.html.

## What the Foundation asks for, and where it is

| Requirement | Status |
|---|---|
| OSI-approved license | MIT ([LICENSE](../LICENSE)) |
| Public repository, released software | GitHub, releases since 0.1.0-beta.1 |
| Code signing policy on the project website, with the sentence "Free code signing provided by SignPath.io, certificate by SignPath Foundation" and team roles | `code-signing.html` (EN and RU) |
| Privacy statement: what the program sends over the network | `code-signing.html` › Privacy, and `privacy.html` |
| Builds only in a trusted CI system, signing requests from it | `.github/workflows/release.yml` (SignPath GitHub action) |
| Every signing request approved by a person | SignPath signing policy with manual approval |
| Multi-factor authentication for every team member (GitHub and SignPath) | the author has to switch it on |

## Steps for the author

1. Turn on two-factor authentication on GitHub (Settings › Password and authentication).
2. Apply at https://signpath.org/apply with the repository URL and the policy page URL.
3. After approval, in SignPath: create the project `aimodometer`, a signing policy `release-signing` with manual
   approval, and two artifact configurations:
   - `binaries`: a zip of PE files (`AimOdometer.*.exe`, `AimOdometer.*.dll`), Authenticode signing of each file;
   - `installer`: a single PE file (`AimOdometerApp-win-Setup.exe`), Authenticode signing.
4. Connect the GitHub repository as a trusted build system in SignPath and create a CI user API token.
5. In the GitHub repository (Settings › Secrets and variables › Actions):
   - secret `SIGNPATH_API_TOKEN`;
   - variables `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG` (`aimodometer`), `SIGNPATH_POLICY_SLUG` (`release-signing`).

As soon as `SIGNPATH_ORGANIZATION_ID` exists, the release workflow signs AimOdometer's own binaries before packing and
the installer after packing; each run waits (up to an hour) for the approval in SignPath. Without the variable the
steps are skipped and releases stay unsigned, as now.

## Limits

- Velopack's own `Update.exe` inside the packages is not signed by this flow (vpk signs it only with a local signing
  tool). It runs only for installing updates; SmartScreen judges the installer and the app.
- Third-party files (the .NET runtime, SQLite, the WebView2 loader, MapLibre) are never signed with AimOdometer's
  certificate.
- SmartScreen reputation still builds up over downloads; a signed file starts with far fewer warnings than an unsigned
  one, but the very first downloads of a new certificate may still show a warning.
