<#
.SYNOPSIS
  Shows the newest complaints and suggestions sent from the app (the "feedback" table in the cloud).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/Read-Feedback.ps1 -Count 20
#>
param([int]$Count = 20)

$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $ErrorActionPreference = "Continue"
    & npx supabase db query --linked "select id, created_at, kind, status, persona_name, contact, app_version, message from public.feedback order by created_at desc limit $Count" 2>$null
}
finally {
    Pop-Location
}
