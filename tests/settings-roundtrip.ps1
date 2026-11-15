$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$scratch=Join-Path $root ('obj\settings-test-'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch)|Out-Null
try{
    $engine=Join-Path $scratch 'engine';Copy-Item -LiteralPath (Join-Path $root 'src\payload') -Destination $engine -Recurse
    $list=Join-Path $scratch 'list.txt'
    @'
::BEGIN_PACKAGES
::PACKAGE steam|Steam|
::END_PACKAGES
::BEGIN_CONFIG
::CONFIG mpv|mpv|APPDATA|mpv
::END_CONFIG
'@|Set-Content -LiteralPath $list
    $hostPath=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $output=& $hostPath -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $root 'src\Runner.ps1') -Action SettingsList -EngineDir $engine -ListPath $list -Capture 2>&1
    if($LASTEXITCODE){throw ($output -join "`n")}
    $profiles=(Get-Content (Join-Path $engine 'Settings.Profiles.json') -Raw|ConvertFrom-Json).profiles
    foreach($id in @('lghub','afterclip','sleekmeeter','mpv','silage')){if($id -notin $profiles.id){throw "Legacy list hid built-in profile: $id"}}
    Import-Module (Join-Path $engine 'Settings.Core.psm1') -Force -DisableNameChecking
    $profile=@($profiles|Where-Object id -eq 'mpv')
    $source=Join-Path $scratch 'source';$dest=Join-Path $scratch 'restored'
    [IO.Directory]::CreateDirectory((Join-Path $source 'mpv\cache'))|Out-Null
    [IO.Directory]::CreateDirectory($dest)|Out-Null
    [IO.File]::WriteAllText((Join-Path $source 'mpv\mpv.conf'),'test-setting=yes')
    [IO.File]::WriteAllText((Join-Path $source 'mpv\cache\ignored.bin'),'ignored')
    $zip=Join-Path $scratch 'backup.zip'
    $installed=@{mpv=[pscustomobject]@{Installed=$true;Version='1.0'}}
    $backup=New-SettingsBackup $profile $zip -Roots @{APPDATA=$source} -InstalledState $installed -Confirm:$false
    if($backup.FileCount -ne 1){throw 'Backup did not apply mpv exclusions'}
    $restore=Restore-SettingsBackup $profile $zip -Roots @{APPDATA=$dest} -InstalledState $installed -Confirm:$false
    if($restore.Results[0].Status -ne 'Restored'){throw 'Restore failed'}
    if([IO.File]::ReadAllText((Join-Path $dest 'mpv\mpv.conf')) -ne 'test-setting=yes'){throw 'Settings round trip changed contents'}
    if(Test-Path (Join-Path $dest 'mpv\cache')){throw 'Excluded cache restored'}
    Write-Host 'PASS legacy-list catalog merge, backup exclusions, checksum validation and settings round trip'
}finally{
    # Test-generated directory, resolved under workspace obj before recursive cleanup.
    $resolved=[IO.Path]::GetFullPath($scratch)
    $allowed=[IO.Path]::GetFullPath((Join-Path $root 'obj'))+'\'
    if(-not $resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected test cleanup path'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
