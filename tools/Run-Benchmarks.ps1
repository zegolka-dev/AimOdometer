<#
.SYNOPSIS
  Measures CPU and memory of the NativeAOT tracker with synthetic mouse input (see docs/PERFORMANCE.md).

.DESCRIPTION
  Publishes nothing: run `dotnet build -c Release` and
  `dotnet publish src/AimOdometer.Tracker -c Release -o artifacts/tracker` first.
  Uses a throw-away data folder, so your real statistics are not touched.
  Stops any running tracker first. The mouse cursor jitters while the input simulator runs.

.EXAMPLE
  pwsh tools/Run-Benchmarks.ps1 -Seconds 20
#>
param(
    [int]$Seconds = 20,
    [string]$TrackerArgs = "",
    [string]$DataDir = (Join-Path ([IO.Path]::GetTempPath()) ("aimodometer-bench-" + (Get-Date -Format "yyyyMMdd-HHmmss")))
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$probe = Join-Path $root "tools\PerfProbe\bin\Release\net10.0-windows\win-x64\PerfProbe.exe"
$sim = Join-Path $root "tools\InputSimulator\bin\Release\net10.0-windows\win-x64\InputSimulator.exe"
$tracker = Join-Path $root "artifacts\tracker\AimOdometer.Tracker.exe"
foreach ($exe in $probe, $sim, $tracker) {
    if (-not (Test-Path $exe)) { throw "Missing $exe - build and publish first." }
}

function Restart-Tracker([string]$Extra) {
    & $tracker --stop | Out-Null
    Start-Sleep -Seconds 1
    Start-Process $tracker -ArgumentList "--data-dir `"$DataDir`" --measure-latency $TrackerArgs $Extra" | Out-Null
    Start-Sleep -Seconds 2
}

function Invoke-Scenario([string]$Label, [int]$Rate) {
    $out = [IO.Path]::GetTempFileName()
    $p = Start-Process $probe -ArgumentList "--seconds $Seconds --label `"$Label`"" -RedirectStandardOutput $out -PassThru -NoNewWindow
    $sent = ""
    if ($Rate -gt 0) {
        Start-Sleep -Milliseconds 300
        $sent = & $sim --rate $Rate --seconds ($Seconds - 1) --pattern circle --step 3 --tag-latency | Select-Object -Last 1
    }
    $p.WaitForExit()
    $row = Get-Content $out | Select-Object -Last 1
    Remove-Item $out
    $latency = & $probe --seconds 1 | Select-String "Injected latency"
    [pscustomobject]@{ Row = $row; Latency = "$latency".Trim(); Sent = "$Label -> $sent" }
}

"| Scenario | CPU % of one core (cycles) | Private WS max MB | Events/s | Wake-ups/s | Alloc bytes |"
"|---|---|---|---|---|---|"
Restart-Tracker ""
$results = @(
    Invoke-Scenario "idle" 0
    Invoke-Scenario "1000 Hz" 1000
    Invoke-Scenario "8000 Hz" 8000
)
Restart-Tracker "--ecoqos"
$results += Invoke-Scenario "8000 Hz + EcoQoS" 8000
& $tracker --stop | Out-Null

$results | ForEach-Object { $_.Row }
""
"Simulator:"
$results | Where-Object { $_.Sent -match 'Sent' } | ForEach-Object { "  " + $_.Sent }
"Latency (cumulative per tracker run):"
$results | Where-Object { $_.Latency } | ForEach-Object { "  " + $_.Latency }
"Data folder: $DataDir"
