<#
.SYNOPSIS
  Map tab test against the live services: allows the map, builds a route between two cities, takes a screenshot,
  leaves the tab and checks that the WebView2 processes of the app's profile are gone. Uses a throw-away data folder.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/Test-Map.ps1 -From "Chisinau" -To "Odesa" -ScreenshotDir artifacts\map
#>
param(
    [string]$From = "Chisinau",
    [string]$To = "Odesa",
    [string]$SeedDatabase = "",
    [string]$App = (Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts\app\AimOdometer.App.exe"),
    [string]$ScreenshotDir = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class MapWin {
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rr,B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
}
"@

$data = Join-Path (Split-Path $PSScriptRoot -Parent) ("artifacts\maptest-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force $data | Out-Null
if ($ScreenshotDir) { New-Item -ItemType Directory -Force $ScreenshotDir | Out-Null }
if ($SeedDatabase) { Copy-Item $SeedDatabase (Join-Path $data "aimodometer.db") }
$profile = Join-Path $data "WebView2"

function Get-MapBrowsers {
    @(Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" | Where-Object { $_.CommandLine -like "*$profile*" })
}

$env:AIMODOMETER_DATA_DIR = $data
$process = Start-Process $App -PassThru
try {
    $deadline = (Get-Date).AddSeconds(15)
    while ($process.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100; $process.Refresh() }
    if ($process.MainWindowHandle -eq 0) { throw "The window did not open." }
    Start-Sleep -Seconds 2
    $root = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)

    function All { @($root.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)) }
    function Id([string]$id) { All | Where-Object { $_.Current.AutomationId -eq $id } | Select-Object -First 1 }
    function Invoke-Element($element) {
        $pattern = $null
        if ($element.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { $pattern.Invoke() }
        elseif ($element.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) { $pattern.Select() }
        Start-Sleep -Milliseconds 700
    }
    function Save-Shot([string]$name) {
        if (-not $ScreenshotDir) { return }
        $r = New-Object MapWin+R; [MapWin]::GetWindowRect($process.MainWindowHandle, [ref]$r) | Out-Null
        $bmp = New-Object Drawing.Bitmap ($r.Rr - $r.L), ($r.B - $r.T)
        $g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
        [MapWin]::PrintWindow($process.MainWindowHandle, $hdc, 2) | Out-Null
        $g.ReleaseHdc($hdc); $bmp.Save((Join-Path $ScreenshotDir "$name.png")); $g.Dispose(); $bmp.Dispose()
    }
    function Wait-For([scriptblock]$condition, [int]$seconds, [string]$what) {
        $until = (Get-Date).AddSeconds($seconds)
        while (-not (& $condition)) {
            if ((Get-Date) -gt $until) { throw "Timed out waiting for $what." }
            Start-Sleep -Milliseconds 500
        }
    }

    # Walk the onboarding if this is a fresh folder.
    while ($next = Id "OnboardingNext") { Invoke-Element $next }

    Invoke-Element (Id "Nav.Map")
    if ((Get-MapBrowsers).Count -ne 0) { throw "WebView2 started before the map was allowed." }
    Invoke-Element (Id "MapEnable")
    Wait-For { (Get-MapBrowsers).Count -gt 0 } 20 "WebView2 to start"
    "map browser processes: $((Get-MapBrowsers).Count)"

    foreach ($city in @($From, $To)) {
        $box = Id "MapSearch"
        $box.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($city)
        Invoke-Element (Id "MapFind")
        Wait-For { (All | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::Button -and $_.Current.Name -like "*,*" }).Count -gt 0 } 20 "search results for $city"
        $hit = All | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::Button -and $_.Current.Name -like "*,*" } | Select-Object -First 1
        "picked: $($hit.Current.Name)"
        Invoke-Element $hit
    }

    Start-Sleep -Seconds 8   # route + tiles
    Save-Shot "map"
    $texts = All | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::Text } | ForEach-Object { $_.Current.Name } | Where-Object { $_ -match '\u2192|km|\u043a\u043c' }
    $texts | ForEach-Object { "caption: $_" }

    Invoke-Element (Id "Nav.Overview")
    Wait-For { (Get-MapBrowsers).Count -eq 0 } 15 "WebView2 processes to exit after leaving the tab"
    "WebView2 processes exited after leaving the tab"

    $log = Join-Path $data "logs\app.log"
    $errors = @(if (Test-Path $log) { Select-String -Path $log -Pattern "\[Error\]" })
    if ($errors.Count -gt 0) { $errors | ForEach-Object { $_.Line }; throw "Errors in $log" }
    if (Test-Path $log) { Select-String -Path $log -Pattern "Router|services.json|Map page" | ForEach-Object { $_.Line } }
    "Map test passed ($data)"
}
finally {
    if (-not $process.HasExited) { $process | Stop-Process -Force }
}
