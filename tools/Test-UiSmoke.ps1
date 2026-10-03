<#
.SYNOPSIS
  UI smoke test: opens the statistics window on a throw-away data folder, walks the onboarding, every page and every
  period switch, then fails if the app logged any error.

.PARAMETER SeedDatabase
  Optional database to copy into the test folder (to test with real-looking data). Without it, the app starts empty.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/Test-UiSmoke.ps1
  powershell -ExecutionPolicy Bypass -File tools/Test-UiSmoke.ps1 -SeedDatabase "C:\backup\aimodometer.db"
#>
param(
    [string]$SeedDatabase = "",
    [string]$App = (Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts\app\AimOdometer.App.exe"),
    [string]$ScreenshotDir = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class SmokeWin {
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rr,B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
}
"@

# Data folder outside %LOCALAPPDATA% on purpose (packaged terminals virtualize AppData, see docs/ARCHITECTURE.md).
$data = Join-Path (Split-Path $PSScriptRoot -Parent) ("artifacts\uismoke-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force $data | Out-Null
if ($SeedDatabase) { Copy-Item $SeedDatabase (Join-Path $data "aimodometer.db") }

$env:AIMODOMETER_DATA_DIR = $data
$process = Start-Process $App -PassThru
try {
    $deadline = (Get-Date).AddSeconds(15)
    while ($process.MainWindowHandle -eq 0 -and -not $process.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100; $process.Refresh() }
    if ($process.MainWindowHandle -eq 0) { throw "The window did not open." }
    Start-Sleep -Seconds 2
    $root = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $scope = [Windows.Automation.TreeScope]::Descendants

    function Find-All([Windows.Automation.ControlType]$type) {
        $cond = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, $type)
        return @($root.FindAll($scope, $cond))
    }
    function Invoke-Element($element) {
        $pattern = $null
        if ($element.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { $pattern.Invoke() }
        elseif ($element.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) { $pattern.Select() }
        Start-Sleep -Milliseconds 700
    }
    function Save-Shot([string]$name) {
        if (-not $ScreenshotDir) { return }
        $r = New-Object SmokeWin+R; [SmokeWin]::GetWindowRect($process.MainWindowHandle, [ref]$r) | Out-Null
        $bmp = New-Object Drawing.Bitmap ($r.Rr - $r.L), ($r.B - $r.T)
        $g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
        [SmokeWin]::PrintWindow($process.MainWindowHandle, $hdc, 2) | Out-Null
        $g.ReleaseHdc($hdc); $bmp.Save((Join-Path $ScreenshotDir "$name.png"))
    }

    # Onboarding (only on a fresh database): press its Next button (AutomationId, language-independent) until it closes.
    for ($i = 0; $i -lt 6; $i++) {
        $default = Find-All ([Windows.Automation.ControlType]::Button) | Where-Object { $_.Current.AutomationId -eq 'OnboardingNext' } | Select-Object -First 1
        if (-not $default) { break }
        Save-Shot "onboarding-$i"
        Invoke-Element $default
    }

    # Pages: the first five radio buttons are the navigation.
    $nav = (Find-All ([Windows.Automation.ControlType]::RadioButton)) | Select-Object -First 5
    $pageIndex = 0
    foreach ($item in $nav) {
        $name = $item.Current.Name
        Invoke-Element $item
        Save-Shot ("page-{0}" -f $pageIndex++)
        # Every period/segment switch on the page (radio buttons after the navigation).
        $segments = (Find-All ([Windows.Automation.ControlType]::RadioButton)) | Select-Object -Skip 5
        foreach ($segment in $segments) { Invoke-Element $segment }
        "visited: $name ($($segments.Count) switches)"
    }
}
finally {
    if (-not $process.HasExited) { $process | Stop-Process -Force }
}

$log = Join-Path $data "logs\app.log"
$errors = if (Test-Path $log) { @(Select-String -Path $log -Pattern "\[Error\]") } else { @() }
if ($errors.Count -gt 0) {
    $errors | Select-Object -First 5 | ForEach-Object { $_.Line }
    throw "UI smoke test failed: $($errors.Count) error(s) in $log"
}
"UI smoke test passed ($data)"
