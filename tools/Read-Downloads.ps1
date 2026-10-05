<#
.SYNOPSIS
  How many people clicked "Download" on the website (the "site_downloads" table in the cloud), and how many times
  GitHub actually served the installer and the zip.

.DESCRIPTION
  A "person" is one visitor on one day: the server keeps a daily-changing hash of the IP instead of the address,
  so the same person on two different days counts twice. GitHub's numbers include downloads from any link, not only
  the website; the app's own updates download other files and are not counted there.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/Read-Downloads.ps1 -Days 14
#>
param([int]$Days = 14)

# The Supabase CLI writes UTF-8; Windows PowerShell would decode it with the OEM code page.
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$queries = [ordered]@{
    "Website: totals" = @"
select count(*) as clicks,
       count(distinct (created_at::date, visitor)) as people,
       count(distinct (created_at::date, visitor)) filter (where created_at >= current_date) as people_today,
       count(distinct (created_at::date, visitor)) filter (where created_at >= current_date - 6) as people_7_days,
       min(created_at) as first_click
from public.site_downloads
"@
    "Website: by day" = @"
select created_at::date as day, count(*) as clicks, count(distinct visitor) as people,
       count(*) filter (where language = 'ru') as ru, count(*) filter (where language = 'en') as en
from public.site_downloads where created_at >= current_date - ($Days - 1)
group by 1 order by 1 desc
"@
    "Website: which button" = @"
select asset, place, count(*) as clicks from public.site_downloads group by 1, 2 order by 3 desc
"@
    "Website: where people came from" = @"
select coalesce(referrer, '(typed the address or a bookmark)') as came_from, count(distinct (created_at::date, visitor)) as people
from public.site_downloads group by 1 order by 2 desc limit 15
"@
}

$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $ErrorActionPreference = "Continue"
    foreach ($title in $queries.Keys) {
        Write-Host "`n== $title" -ForegroundColor Cyan
        & npx supabase db query --linked ($queries[$title] -replace "\r?\n", " ") 2>$null
    }
}
finally {
    Pop-Location
}

Write-Host "`n== GitHub: files downloaded, per release" -ForegroundColor Cyan
try {
    $releases = Invoke-RestMethod "https://api.github.com/repos/zegolka-dev/AimOdometer/releases?per_page=100"
    $rows = foreach ($release in $releases) {
        $setup = $release.assets | Where-Object name -eq "AimOdometerApp-win-Setup.exe"
        $zip = $release.assets | Where-Object name -eq "AimOdometerApp-win-Portable.zip"
        [pscustomobject]@{
            Version  = $release.tag_name
            Setup    = [int]($setup.download_count | Measure-Object -Sum).Sum
            Portable = [int]($zip.download_count | Measure-Object -Sum).Sum
        }
    }
    $rows | Format-Table -AutoSize
    "Total: Setup.exe {0}, zip {1}" -f ($rows | Measure-Object Setup -Sum).Sum, ($rows | Measure-Object Portable -Sum).Sum
}
catch {
    Write-Warning "GitHub did not answer: $($_.Exception.Message)"
}
