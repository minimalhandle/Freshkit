#requires -Version 5.1
[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [ValidateSet('Backup','Restore','Inspect','List')][string]$Action='List',
    [string]$ArchivePath='',[string[]]$Apps=@(),[switch]$All,[switch]$AllowVersionMismatch
)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'Settings.Core.psm1') -Force -DisableNameChecking
$profiles=@(Get-SettingsProfiles (Join-Path $PSScriptRoot 'Settings.Profiles.json'))
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
