# Builds dist\Freshkit.exe, then runs every test runner (run-*.ps1) and every standalone payload test script.
# Each test runs in its own Windows PowerShell 5.1 process so one script's exit or state cannot affect the rest.
# Exit code 0 only when the build and all tests pass. Use -SkipBuild to reuse the existing obj\ and dist\ output.
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$results = New-Object System.Collections.Generic.List[object]
$total = [Diagnostics.Stopwatch]::StartNew()

function Invoke-Step([string]$name, [string]$script) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    # In Windows PowerShell 5.1, 'Stop' would turn a child's stderr output into a terminating error and abort the run.
    $ErrorActionPreference = 'Continue'
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script 2>&1 | ForEach-Object { "$_" } | Out-String
    $ok = $LASTEXITCODE -eq 0
    $ErrorActionPreference = 'Stop'
    $lines = @($output.Trim() -split "`r?`n" | Where-Object { $_.Trim() })
    $last = if ($lines.Count) { $lines[-1].Trim() } else { '' }
    $results.Add([pscustomobject]@{ Test = $name; Result = $(if ($ok) { 'PASS' } else { 'FAIL' }); Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1); Summary = $last })
    if (-not $ok) { Write-Host "---- $name output ----" -ForegroundColor Red; Write-Host $output.TrimEnd() }
}

if (-not $SkipBuild) { Invoke-Step 'build.ps1' (Join-Path $root 'build.ps1') }
if ($SkipBuild -or $results[0].Result -eq 'PASS') {
    Get-ChildItem (Join-Path $PSScriptRoot 'run-*.ps1') | Where-Object Name -ne 'run-all.ps1' | Sort-Object Name |
        ForEach-Object { Invoke-Step $_.Name $_.FullName }
    Get-ChildItem (Join-Path $PSScriptRoot '*.ps1') | Where-Object Name -notlike 'run-*' | Sort-Object Name |
        ForEach-Object { Invoke-Step $_.Name $_.FullName }
}

$results | Format-Table -AutoSize -Wrap | Out-String -Width 200 | Write-Host
$failed = @($results | Where-Object Result -eq 'FAIL').Count
$color = if ($failed) { 'Red' } else { 'Green' }
Write-Host ("{0} of {1} passed in {2:n0}s" -f ($results.Count - $failed), $results.Count, $total.Elapsed.TotalSeconds) -ForegroundColor $color
exit $failed
