<#
.SYNOPSIS
  Runs the pgTAP tests in supabase/tests/database against the linked Supabase project without Docker.

.DESCRIPTION
  `supabase test db` needs Docker for pg_prove. This runner sends the test file through `supabase db query --linked`
  (Management API) instead. Every assertion's output is collected in a temporary table and the script ends by raising
  an error that carries the results: the error aborts the transaction, so nothing the tests inserted can ever be
  committed to the real database. CI runs the same files with `supabase test db` on a local stack.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/Test-CloudRls.ps1
#>
param([string]$Path = (Join-Path (Split-Path $PSScriptRoot -Parent) "supabase\tests\database"))

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$failed = 0
foreach ($file in Get-ChildItem $Path -Filter *.test.sql) {
    $sql = [IO.File]::ReadAllText($file.FullName)
    $statements = [regex]::Split($sql, ';\s*\r?\n') | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    $out = New-Object Collections.Generic.List[string]
    foreach ($statement in $statements) {
        $body = ($statement -split "`n" | Where-Object { $_ -notmatch '^\s*--' }) -join "`n"
        $body = $body.Trim()
        if (-not $body) { continue }
        if ($body -match '^(begin|rollback)$') { continue }
        if ($body -match '^select\s+(plan|throws_ok|results_eq|is|isnt|ok|lives_ok)\(' -or $body -match '^select \* from finish\(\)') {
            $out.Add("insert into pg_temp.tap_out (line) $body;")
        }
        else {
            $out.Add("$body;")
        }
    }

    $script = @"
begin;
create temporary table tap_out (n serial, line text);
-- The tests switch to the client roles; they must still be able to record results.
grant insert, select on pg_temp.tap_out to public;
grant usage on sequence pg_temp.tap_out_n_seq to public;
$($out -join "`n")
do `$`$ begin raise exception 'TAP-RESULTS%', (select string_agg(line, chr(10) order by n) from pg_temp.tap_out); end `$`$;
"@
    $temp = Join-Path $env:TEMP ("rls-" + [Guid]::NewGuid().ToString("N") + ".sql")
    [IO.File]::WriteAllText($temp, $script, (New-Object Text.UTF8Encoding $false))
    try {
        Push-Location $root
        $ErrorActionPreference = "Continue"  # the CLI writes progress to stderr
        $result = (& npx supabase db query --linked -f $temp 2>&1 | Out-String)
        $ErrorActionPreference = "Stop"
    }
    finally {
        Pop-Location
        Remove-Item $temp -ErrorAction SilentlyContinue
    }

    $start = $result.IndexOf("TAP-RESULTS")
    if ($start -lt 0) { $result; throw "No test results from $($file.Name) (the script failed before the end)." }
    $tap = $result.Substring($start + "TAP-RESULTS".Length) -replace '\\n', "`n"
    $lines = $tap -split "`n" | ForEach-Object { $_.Trim().Trim('"', '}', ' ', [char]92) } | Where-Object { $_ -match '^(ok|not ok|1\.\.|#)' }
    $lines | ForEach-Object { $_ }
    $bad = @($lines | Where-Object { $_ -match '^not ok' -or $_ -match '^# Looks like' })
    $failed += $bad.Count
    "{0}: {1}" -f $file.Name, $(if ($bad.Count) { "FAILED" } else { "passed" })
}

if ($failed -gt 0) { throw "$failed failing pgTAP line(s)." }
