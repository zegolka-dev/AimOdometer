<#
.SYNOPSIS
  Publishes the app the way it will be installed: window (WPF, ReadyToRun) and tracker (NativeAOT) in one folder.

.DESCRIPTION
  Output: artifacts/app. The tracker's tray icon opens AimOdometer.App.exe from the same folder, and the window starts
  the tracker if it is not running. Stops a running tracker from that folder first so files can be replaced.
  NativeAOT needs the Visual Studio C++ build tools (see README).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/Publish-Local.ps1
#>
param([string]$Output = (Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts\app"))

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$installer = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
if ((Test-Path $installer) -and ($env:Path -notlike "*$installer*")) { $env:Path += ";$installer" } # vswhere for NativeAOT

$tracker = Join-Path $Output "AimOdometer.Tracker.exe"
if (Test-Path $tracker) {
    & $tracker --stop | Out-Null
    Start-Sleep -Seconds 2
}
Get-Process AimOdometer.App -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Output*" } | Stop-Process -Force

dotnet publish (Join-Path $root "src\AimOdometer.App") -c Release -o $Output --nologo
if ($LASTEXITCODE -ne 0) { throw "App publish failed" }
dotnet publish (Join-Path $root "src\AimOdometer.Tracker") -c Release -o $Output --nologo
if ($LASTEXITCODE -ne 0) { throw "Tracker publish failed" }

Get-ChildItem $Output -Filter *.pdb | Remove-Item
"Published to $Output"
