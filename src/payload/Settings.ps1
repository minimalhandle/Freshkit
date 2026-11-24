#requires -Version 5.1
[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [ValidateSet('Backup','Restore','Inspect','List','Auto','Schedule','Unschedule')][string]$Action='List',
    [string]$ArchivePath='',[string[]]$Apps=@(),[switch]$All,[switch]$AllowVersionMismatch,
    [string]$Time='03:00',[int]$Keep=7,[string]$TaskName='Freshkit daily settings backup'
)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'Settings.Core.psm1') -Force -DisableNameChecking
$profiles=@(Get-SettingsProfiles (Join-Path $PSScriptRoot 'Settings.Profiles.json'))
# Daily backups: a scheduled task runs a copy of this engine (and the session's backup profiles) as the signed-in user,
# so it keeps working without Freshkit.exe or its USB stick.
$scheduledCopy=Join-Path $env:LOCALAPPDATA 'Freshkit\ScheduledBackup'
function Get-ScheduledSelection {
    $ids=@($Apps|ForEach-Object {$_ -split ','}|ForEach-Object {$_.Trim()}|Where-Object {$_})
    if($All -or -not $ids.Count){return @()}
    foreach($id in $ids){if($id -notmatch '\A[a-z0-9]+(?:[.-][a-z0-9]+)*\z' -or $id -notin @($profiles.id)){throw "No settings profile: $id"}}
    return $ids
}
if($Action -eq 'Unschedule'){
    Unregister-ScheduledTask -TaskName $TaskName -TaskPath '\' -Confirm:$false -ErrorAction SilentlyContinue
    if(Test-Path -LiteralPath $scheduledCopy){Remove-Item -LiteralPath $scheduledCopy -Recurse -Force}
    Write-Host 'Daily settings backups turned off. Existing backups were kept.'
    exit 0
}
if($Action -eq 'Schedule'){
    if(-not $ArchivePath){throw 'Choose a folder for the daily backups.'}
    $folder=[IO.Path]::GetFullPath($ArchivePath)
    if($folder.Contains('"') -or -not (Test-Path -LiteralPath $folder -PathType Container)){throw 'The backup folder does not exist.'}
    Assert-SettingsPlainPath $folder
    if($Time -notmatch '\A(?:[01]\d|2[0-3]):[0-5]\d\z'){throw 'Use HH:mm for the backup time.'}
    if($Keep -lt 1 -or $Keep -gt 365){throw 'Keep between 1 and 365 backups.'}
    $ids=@(Get-ScheduledSelection)
    [IO.Directory]::CreateDirectory($scheduledCopy)|Out-Null
    foreach($name in @('Settings.ps1','Settings.Core.psm1','Settings.Profiles.json')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $scheduledCopy $name) -Force}
    $selection=if($ids.Count){' -Apps "'+($ids -join ',')+'"'}else{' -All'}
    $arguments='-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+(Join-Path $scheduledCopy 'Settings.ps1')+'" -Action Auto -ArchivePath "'+$folder+'" -Keep '+$Keep+$selection
    $taskAction=New-ScheduledTaskAction -Execute (Join-Path $PSHOME 'powershell.exe') -Argument $arguments
    $trigger=New-ScheduledTaskTrigger -Daily -At ([datetime]::ParseExact($Time,'HH:mm',[Globalization.CultureInfo]::InvariantCulture))
    $principal=New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
    $taskSettings=New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 1)
    Register-ScheduledTask -TaskName $TaskName -TaskPath '\' -Action $taskAction -Trigger $trigger -Principal $principal -Settings $taskSettings -Description 'Freshkit: back up app settings every day. Running apps are skipped; older automatic backups are pruned.' -Force|Out-Null
    Write-Host ("Daily settings backups scheduled at $Time to $folder, keeping the newest $Keep. Apps that are open at that time are skipped.")
    exit 0
}
if($Action -eq 'Auto'){
    $logDir=Join-Path $env:LOCALAPPDATA 'Freshkit\logs';[IO.Directory]::CreateDirectory($logDir)|Out-Null
    $log=Join-Path $logDir ('auto-backup-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')
    try{Start-Transcript -LiteralPath $log|Out-Null}catch{}
    $code=0
    try{
        $folder=[IO.Path]::GetFullPath($ArchivePath)
        if(-not (Test-Path -LiteralPath $folder -PathType Container)){throw "Backup folder is unavailable: $folder"}
        $ids=@(Get-ScheduledSelection)
        $selected=if($ids.Count){@($profiles|Where-Object {$_.id -in $ids})}else{$profiles}
        $zip=Join-Path $folder ('settings-backup-auto-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.zip')
        $result=New-SettingsBackup $selected $zip -Confirm:$false
        Write-Host "Backup saved: $($result.Archive) ($($result.FileCount) files, $($result.RegistryValues) registry values)."
        # Prune only automatic backups, and only after a new one was written.
        foreach($name in @(Remove-SettingsOldAutoBackups $folder $Keep)){Write-Host "Removed older automatic backup: $name"}
    }catch{Write-Host ('ERROR: '+$_.Exception.Message);$code=1}
    try{Stop-Transcript|Out-Null}catch{}
    if(-not $code){Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue}
    exit $code
}
if($Action -eq 'List'){
    $profiles|Select-Object id,name,@{n='Locations';e={($_.rules|ForEach-Object {$_.root+':'+$_.path}) -join '; '}}|Format-Table -Wrap -AutoSize
    $routes=(Get-Content (Join-Path $PSScriptRoot 'Install-Chocolatey.apps.json') -Raw|ConvertFrom-Json).apps
    Write-Host 'Packages without a settings preset (use their own export/sync or add CONFIG rules):'
    $routes|Where-Object {$_.status -eq 'Chocolatey' -and $_.packageId -notin @($profiles.packageId)}|Select-Object name,packageId|Format-Table -AutoSize
    exit 0
}
$base=Get-Location
if($env:APP_INSTALLER_SELF){$base=Split-Path -Parent $env:APP_INSTALLER_SELF}
if(-not $ArchivePath){
    if($Action -eq 'Backup'){
        $default=Join-Path $base ('settings-backup-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.zip')
        $answer=Read-Host "Backup ZIP destination [$default]"
        $ArchivePath=if($answer.Trim()){$answer.Trim().Trim('"')}else{$default}
    }else{$ArchivePath=(Read-Host 'Full path to the settings backup ZIP').Trim().Trim('"')}
}
$ArchivePath=[IO.Path]::GetFullPath($ArchivePath)
if($Action -in @('Restore','Inspect')){
    $manifest=Read-SettingsArchive $ArchivePath $profiles
    $manifest.Apps|Select-Object Id,Name,Version,Items|Format-Table -AutoSize
    Write-Host 'Archive manifest, paths and payload checksums verified.'
    if($Action -eq 'Inspect'){exit 0}
    $profiles=@($profiles|Where-Object {$_.id -in @($manifest.Apps|Where-Object Items -gt 0|ForEach-Object {$_.Id})})
}
$ids=@($Apps|ForEach-Object {$_ -split ','}|ForEach-Object {$_.Trim()}|Where-Object {$_})
if(-not $All -and -not $ids.Count){
    $profiles|Select-Object id,name|Format-Table -AutoSize
    $answer=Read-Host 'Choose settings profile IDs separated by commas, ALL, or Q'
    if($answer.Trim() -eq 'Q'){exit 0}
    if($answer.Trim() -eq 'ALL'){$All=$true}else{$ids=@($answer -split ','|ForEach-Object {$_.Trim()}|Where-Object {$_})}
    if(-not $All -and -not $ids.Count){throw 'No settings profiles selected.'}
}
foreach($id in $ids){if($id -notin @($profiles.id)){throw "No available settings profile in this selection/archive: $id"}}
if(-not $All){$profiles=@($profiles|Where-Object id -in $ids)}
if(-not $profiles.Count){throw 'No settings profiles available.'}
Write-Host 'Close the selected apps first. No processes will be force-closed.'
if($Action -eq 'Backup'){
    Write-Host 'The backup ZIP is not encrypted and may contain private settings/URLs. Keep it on a drive you will not wipe.'
    $result=New-SettingsBackup $profiles $ArchivePath -WhatIf:$WhatIfPreference -Confirm:$false
    if($null -ne $result){
        $result.Apps|Select-Object Id,Name,Version,Items|Format-Table -AutoSize
        Write-Host "Backup saved: $($result.Archive) ($($result.FileCount) files, $($result.RegistryValues) registry values)."
        if(@($result.Apps|Where-Object Items -eq 0).Count){Write-Host 'Profiles showing zero items had no settings at the configured locations.'}
    }
    exit 0
}
$state=Get-SettingsInstalledState $profiles
$mismatches=@(foreach($profile in $profiles){
    $source=@($manifest.Apps|Where-Object Id -eq $profile.id)[0]
    $a=[version]'0.0';$b=[version]'0.0'
    if($state[$profile.id].Installed -and [version]::TryParse([string]$source.Version,[ref]$a) -and [version]::TryParse([string]$state[$profile.id].Version,[ref]$b) -and $a.Major -ne $b.Major){$profile.name}
})
if($mismatches.Count -and -not $AllowVersionMismatch -and -not $WhatIfPreference){
    Write-Host ('Different major app versions: '+($mismatches -join ', '))
    try{$AllowVersionMismatch=(Read-Host 'Allow these version changes after reviewing compatibility? Y/N [N]') -eq 'Y'}catch{Write-Host 'Non-interactive run: profiles with a different major version are skipped.';$AllowVersionMismatch=$false}
}
$result=Restore-SettingsBackup $profiles $ArchivePath -InstalledState $state -AllowVersionMismatch:$AllowVersionMismatch -WhatIf:$WhatIfPreference -Confirm:$false
$result.Results|Format-Table Id,Status,Files,RegistryValues,Detail -Wrap -AutoSize
if($result.SafetyArchive){Write-Host "Existing settings saved before overwrite: $($result.SafetyArchive)"}
if(-not $WhatIfPreference){
    $report=Join-Path $env:LOCALAPPDATA 'Freshkit\logs\settings-result.json'
    [IO.Directory]::CreateDirectory((Split-Path -Parent $report))|Out-Null
    $result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $report -Encoding UTF8
    Write-Host "Restore report: $report"
}
if(@($result.Results|Where-Object Status -eq 'Failed').Count){exit 1}
if(@($result.Results|Where-Object Status -in @('SkippedNotInstalled','VersionMismatch','NoSavedSettings','AppRunning')).Count){exit 2}
exit 0
