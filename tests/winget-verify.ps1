$ErrorActionPreference='Stop'
# winget install verification: slow-registering installers and readable error codes. No winget calls.
$root=Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $root 'src\payload\Install-Chocolatey.Core.psm1') -Force -DisableNameChecking
$module=Get-Module 'Install-Chocolatey.Core'

$calls=[ref]0
$late={param($id) $calls.Value++;[pscustomobject]@{Installed=($calls.Value -ge 3);Version='1.0'}}.GetNewClosure()
$result=& $module {param($p) Wait-SetupWingetInstalled 'Discord.Discord' -TimeoutSeconds 30 -IntervalSeconds 0 -Probe $p} $late
if(-not $result.Installed -or $calls.Value -ne 3){throw 'An app that registers a little after winget returns was not picked up'}

$calls.Value=0
$never={param($id) $calls.Value++;[pscustomobject]@{Installed=$false;Version=''}}.GetNewClosure()
$result=& $module {param($p) Wait-SetupWingetInstalled 'Missing.App' -TimeoutSeconds 0 -IntervalSeconds 0 -Probe $p} $never
if($result.Installed -or $calls.Value -ne 1){throw 'An app that never registers must still be reported as not installed'}

$text=& $module {Get-SetupWingetErrorText 'install' -1978335215}
if($text -notlike 'winget install returned 0x8A150011: *checksum*'){throw "Hash mismatch text is unclear: $text"}
$text=& $module {Get-SetupWingetErrorText 'upgrade' -1978335000}
if($text -ne 'winget upgrade returned 0x8A1500E8.'){throw "Unknown codes must keep the raw code only: $text"}

# Discord's first-run System Helper: registered by setup so Discord doesn't raise a UAC prompt. Fake app folder only.
$dir=Join-Path ([IO.Path]::GetTempPath()) ('freshkit-discord-'+[guid]::NewGuid().ToString('N'))
try{
    $state=@{Service=$false;Ran=@()}
    $exists={$state.Service}.GetNewClosure()
    $run={param($p) $state.Ran+=$p;$state.Service=$true;0}.GetNewClosure()
    $trusted={param($p) $true}
    $note=& $module {param($d,$e,$t,$r) Install-SetupDiscordHelper -AppDir $d -TimeoutSeconds 0 -ServiceExists $e -Trusted $t -Run $r} $dir $exists $trusted $run
    if($state.Ran.Count -or $note -notmatch 'may ask'){throw 'With no helper downloaded yet, nothing runs and the result says Discord may still ask'}
    $helperDir=Join-Path $dir 'app-1.0.9270\modules\discord_utils-1\discord_utils';[IO.Directory]::CreateDirectory($helperDir)|Out-Null
    $helper=Join-Path $helperDir 'DiscordSystemHelper.exe';[IO.File]::WriteAllText($helper,'fixture')
    $note=& $module {param($d,$e,$t,$r) Install-SetupDiscordHelper -AppDir $d -TimeoutSeconds 0 -ServiceExists $e -Trusted {param($p) $false} -Run $r} $dir $exists $trusted $run
    if($state.Ran.Count){throw 'A helper without a valid Discord signature must never be run elevated'}
    $note=& $module {param($d,$e,$t,$r) Install-SetupDiscordHelper -AppDir $d -TimeoutSeconds 0 -ServiceExists $e -Trusted $t -Run $r} $dir $exists $trusted $run
    if($state.Ran.Count -ne 1 -or $state.Ran[0] -ne $helper -or $note -notmatch 'Registered'){throw "The downloaded helper is registered as a service: $note"}
    $note=& $module {param($d,$e,$t,$r) Install-SetupDiscordHelper -AppDir $d -TimeoutSeconds 0 -ServiceExists $e -Trusted $t -Run $r} $dir $exists $trusted $run
    if($state.Ran.Count -ne 1 -or $note){throw 'An already registered helper is left alone'}
}finally{Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue}
Write-Host 'PASS winget verification waits for slow registration, explains common error codes and pre-registers Discord''s helper'
