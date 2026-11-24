#requires -Version 5.1
# Daily-backup pruning, the new backup profiles and Store-app detection fields. Temp folders only; no scheduled tasks.
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $root 'src\payload\Settings.Core.psm1') -Force -DisableNameChecking
function Assert([bool]$Condition,[string]$Name){if(-not $Condition){throw "FAIL: $Name"}}

$profiles=@(Get-SettingsProfiles (Join-Path $root 'src\payload\Settings.Profiles.json'))
foreach($id in 'firefox','streamdeck','voicemeeter','playnite','elgato-studio','windows-terminal'){Assert ($id -in @($profiles.id)) "built-in profile $id"}
$firefox=@($profiles|Where-Object id -eq 'firefox')[0]
foreach($path in 'Profiles\a.default\logins.json','Profiles\a.default\key4.db','Profiles\a.default\cookies.sqlite','Profiles\a.default\cache2','Profiles\a.default\storage\temporary'){
    Assert (& (Get-Module Settings.Core) {param($p,$r) Test-SettingsExcluded $p $r} $firefox $path) "Firefox excludes $path"
}
foreach($path in 'profiles.ini','Profiles\a.default\places.sqlite','Profiles\a.default\prefs.js','Profiles\a.default\extensions'){
    Assert (-not (& (Get-Module Settings.Core) {param($p,$r) Test-SettingsExcluded $p $r} $firefox $path)) "Firefox keeps $path"
}
Assert (@($profiles|Where-Object {$_.PSObject.Properties['appxName']}).Count -eq 2) 'Store-installed profiles declare their package name'

# A bad Store package name is rejected like any other invalid rule.
$work=Join-Path ([IO.Path]::GetTempPath()) ('freshkit-reinstall-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work|Out-Null
try{
    $bad=@{schemaVersion=1;profiles=@(@{id='x';name='x';packageId='x';versionPattern='^x';appxName='bad name/..';processes=@();rules=@(@{root='APPDATA';path='x'});exclude=@()})}|ConvertTo-Json -Depth 6
    $badPath=Join-Path $work 'bad.json';[IO.File]::WriteAllText($badPath,$bad)
    $rejected=$false;try{$null=Get-SettingsProfiles $badPath}catch{$rejected=$true}
    Assert $rejected 'invalid appxName rejected'

    # Pruning keeps the newest automatic backups and never touches other ZIPs.
    $names='settings-backup-auto-20261101-030000.zip','settings-backup-auto-20261102-030000.zip','settings-backup-auto-20261103-030000.zip','settings-backup-auto-20261104-030000.zip','settings-backup-20261101-120000.zip','settings-backup-auto-notes.zip','pre-restore-x.zip'
    foreach($n in $names){[IO.File]::WriteAllText((Join-Path $work $n),'x')}
    $removed=@(Remove-SettingsOldAutoBackups $work 2)
    $left=@(Get-ChildItem $work -Filter *.zip|ForEach-Object Name)
    Assert ($removed.Count -eq 2 -and 'settings-backup-auto-20261101-030000.zip' -in $removed -and 'settings-backup-auto-20261102-030000.zip' -in $removed) 'oldest automatic backups pruned'
    Assert ('settings-backup-auto-20261104-030000.zip' -in $left -and 'settings-backup-auto-20261103-030000.zip' -in $left) 'newest automatic backups kept'
    Assert ('settings-backup-20261101-120000.zip' -in $left -and 'settings-backup-auto-notes.zip' -in $left -and 'pre-restore-x.zip' -in $left) 'manual and unrelated ZIPs untouched'
    $zeroKeep=$false;try{$null=Remove-SettingsOldAutoBackups $work 0}catch{$zeroKeep=$true}
    Assert $zeroKeep 'keeping zero backups is refused'
}finally{Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue}
Write-Host 'PASS new backup profiles (Firefox exclusions, Store detection fields) and daily-backup pruning (temp folder only)'
