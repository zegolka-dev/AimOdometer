<#
.SYNOPSIS
  Accessibility check: opens the statistics window on a throw-away data folder, visits every page and lists the
  controls a screen reader cannot name (buttons, check boxes, inputs, lists without an accessible name) and the ones
  the keyboard cannot reach. Fails when it finds any.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/Test-Accessibility.ps1 -SeedDatabase "C:\demo\aimodometer.db"
#>
param(
    [string]$SeedDatabase = "",
    [string]$App = (Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts\app\AimOdometer.App.exe")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

# Data folder outside %LOCALAPPDATA% on purpose (packaged terminals virtualize AppData, see docs/ARCHITECTURE.md).
$data = Join-Path (Split-Path $PSScriptRoot -Parent) ("artifacts\a11y-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force $data | Out-Null
if ($SeedDatabase) { Copy-Item $SeedDatabase (Join-Path $data "aimodometer.db") }

$env:AIMODOMETER_DATA_DIR = $data
$process = Start-Process $App -PassThru
$A = [Windows.Automation.AutomationElement]
$types = [Windows.Automation.ControlType]
$interactive = @($types::Button, $types::CheckBox, $types::ComboBox, $types::Edit, $types::RadioButton, $types::Slider, $types::Hyperlink)
$problems = New-Object System.Collections.Generic.List[string]
try {
    $deadline = (Get-Date).AddSeconds(15)
    while ($process.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100; $process.Refresh() }
    Start-Sleep -Seconds 2
    $root = $A::FromHandle($process.MainWindowHandle)

    function Find-All() { $root.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) }

    function Check-Page([string]$page) {
        foreach ($e in Find-All) {
            $c = $e.Current
            if ($interactive -notcontains $c.ControlType -or $c.IsOffscreen) { continue }
            if ($c.AutomationId -in "PageUp", "PageDown", "LineUp", "LineDown", "PART_ButtonScrollUp", "PART_ButtonScrollDown") { continue } # scroll bar parts: the keyboard scrolls with arrow keys
            $label = "$page / $($c.ControlType.ProgrammaticName.Replace('ControlType.', '')) '$($c.AutomationId)'"
            if ([string]::IsNullOrWhiteSpace($c.Name)) { $problems.Add("no name: $label") }
            elseif ($c.IsEnabled -and -not $c.IsKeyboardFocusable) { $problems.Add("not reachable by keyboard: $label ($($c.Name))") }
        }
    }

    # Close the onboarding or What's new if they are open, then visit every page.
    foreach ($id in "OnboardingNext", "WhatsNewClose") {
        for ($i = 0; $i -lt 8; $i++) {
            $b = Find-All | Where-Object { $_.Current.AutomationId -eq $id } | Select-Object -First 1
            if (-not $b) { break }
            $b.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Milliseconds 400
        }
    }

    $nav = Find-All | Where-Object { $_.Current.AutomationId -like "Nav.*" }
    foreach ($item in $nav) {
        $name = $item.Current.AutomationId
        $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 900
        Check-Page $name
    }
}
finally {
    if (-not $process.HasExited) { $process.CloseMainWindow() | Out-Null; Start-Sleep 2; if (-not $process.HasExited) { $process.Kill() } }
}

$unique = $problems | Sort-Object -Unique
$unique
if ($unique.Count -gt 0) { throw "$($unique.Count) accessibility problem(s)." }
"Accessibility check passed ($data)"
