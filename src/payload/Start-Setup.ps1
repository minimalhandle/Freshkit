#requires -Version 5.1
[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [ValidateSet('Menu','Install','List','Plan','Bootstrap','Drivers','DriverPlan','Resume','CancelResume')][string]$Action='Install',
    [string[]]$Apps=@(),
    [switch]$All,
    [ValidatePattern('^S-1-[0-9-]+$')][string]$ExpectedUserSid='',
    [ValidatePattern('^[a-f0-9]{32}$')][string]$RunId='',
    [switch]$Ui,
    [string]$LogDir='',
    [switch]$SkipDrivers,[string]$DriverIds=''
)
$ErrorActionPreference='Stop'
# Freshkit's elevated job keeps the transcript (-LogDir is accepted for compatibility).
Import-Module (Join-Path $PSScriptRoot 'Install-Chocolatey.Core.psm1') -Force -DisableNameChecking
. (Join-Path $PSScriptRoot 'Freshkit.Status.ps1')
$ids=@($Apps|ForEach-Object {$_ -split ','}|ForEach-Object {$_.Trim()}|Where-Object {$_})
$catalogPath=Join-Path $PSScriptRoot 'Install-Chocolatey.apps.json'
$plan=@(Get-ChocolateyAppPlan $catalogPath $ids -All:$All)
if($Action -eq 'DriverPlan' -or ($Action -eq 'Drivers' -and $WhatIfPreference)){
    Import-Module (Join-Path $PSScriptRoot 'Drivers.Core.psm1') -Force -DisableNameChecking
    $inventory=Get-DriverDeviceInventory
    Get-DriverCandidates $inventory|Select-Object Name,Status,@{n='Hardware';e={$_.Hardware -join '; '}},Detail|Format-Table -Wrap -AutoSize
    Write-Host 'Hardware preview only. No downloads, Chocolatey calls, elevation or driver installation.'
    exit 0
}
if($Action -in @('List','Plan') -or $WhatIfPreference){
    Import-Module (Join-Path $PSScriptRoot 'Install-Run.Core.psm1') -Force -DisableNameChecking
    Write-Host 'Fresh Windows 11: verify Chocolatey; matching hardware drivers; required reboot(s); runtimes; apps; final on-screen summary.'
    Get-InstallExecutionPlan $plan|Select-Object name,packageId,@{n='source';e={if($_.PSObject.Properties['githubRepo'] -and $_.githubRepo){'GitHub'}elseif($_.PSObject.Properties['wingetId'] -and $_.wingetId){'winget'}elseif($_.status -eq 'Chocolatey'){'Chocolatey'}else{$_.status}}}|Format-Table -AutoSize
    Write-Host 'The editable list stays alphabetical; execution puts runtimes and selected prerequisites first. Reboots resume automatically after same-account sign-in.'
    Write-Host 'Preview only. No downloads, elevation, installation or new windows.'
    exit 0
}
if($Action -eq 'Menu'){$Action='Install'}
if($Action -eq 'Install' -and -not $ids.Count){$All=$true}
if(-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess -or [Environment]::OSVersion.Version.Build -lt 22000){throw 'This installer targets a fresh Windows 11 x64 installation using 64-bit Windows PowerShell.'}
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
if($ExpectedUserSid -and $ExpectedUserSid -ne $sid){throw 'Use UAC consent from the same Windows account. Different administrator credentials would install user apps into another profile.'}
if(-not(Test-SetupAdministrator)){
    $encoded=Get-SetupEncodedCommand -Script $PSCommandPath -Action $Action -Apps $ids -All:$All -ExpectedUserSid $sid -RunId $RunId -Ui:$Ui -LogDir $LogDir -SkipDrivers:$SkipDrivers
    $hostPath=Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell/v1.0/powershell.exe'
    $process=Start-Process -FilePath $hostPath -Verb RunAs -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -EncodedCommand '+$encoded) -WorkingDirectory ([Environment]::SystemDirectory) -Wait -PassThru
    exit $process.ExitCode
}
try {
    if($Action -in @('Resume','CancelResume')){
        Import-Module (Join-Path $PSScriptRoot 'Install-Run.Core.psm1') -Force -DisableNameChecking
        if($RunId){$pending=@(Read-InstallRunState $RunId)}else{$pending=@(Get-PendingInstallRuns)}
        if(-not $pending.Count){Write-Host 'No unfinished installation for this Windows account.';exit 0}
        if($pending.Count -ne 1){throw 'Multiple unfinished installations found. Specify a RunId explicitly; no automatic guess was made.'}
        $state=$pending[0]
        if($state.Status -in @('Completed','Cancelled')){Write-Host "Run is $($state.Status).";exit 0}
        if($Action -eq 'CancelResume'){$null=Cancel-InstallRun $state;Write-Host 'Remaining installation cancelled; temporary continuation files and task removed. Completed apps were kept.';exit 0}
        Add-InstallRunScratch $state $PSScriptRoot;Save-InstallRunState $state
        Assert-InstallSnapshot $state
        $runner=Join-Path (Get-InstallRunDirectory $state.RunId) 'Resume-Run.ps1'
        $hostPath=Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell/v1.0/powershell.exe'
        $process=Start-Process -FilePath (Join-Path ([Environment]::SystemDirectory) 'cmd.exe') -ArgumentList ('/d /k ""'+$hostPath+'" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+$runner+'" -RunId '+$state.RunId+'"') -WorkingDirectory ([Environment]::SystemDirectory) -Wait -PassThru
        exit $process.ExitCode
    }
    Set-FreshkitStage 'Bootstrap' 'Running' 'Verifying Chocolatey'
    try {$ready=Initialize-SetupChocolatey;Set-FreshkitStage 'Bootstrap' 'Done' ('Chocolatey '+$ready.Version+' verified')}
    catch {Set-FreshkitStage 'Bootstrap' 'Failed' $_.Exception.Message;throw}
    Write-Host "Chocolatey $($ready.Version) verified: executable, local registration and community repository."
    if($Action -ne 'Bootstrap'){
        if($Ui){
            # Freshkit: run the app stage here, at script scope, in this already elevated and logged process.
            # (Calling it from inside a module function breaks the module when the script re-imports it.)
            $appParams=@{Action=$Action;ExpectedUserSid=$sid;ExpectedVersion=$ready.Version}
            if($ids.Count){$appParams.Apps=$ids}
            if($All){$appParams.All=$true}
            if($SkipDrivers){$appParams.SkipDrivers=$true}
            if($DriverIds){$appParams.DriverIds=$DriverIds}
            $global:LASTEXITCODE=0
            & (Join-Path $PSScriptRoot 'Install-ChocolateyApps.ps1') @appParams
            exit ([int]$global:LASTEXITCODE)
        }
        Open-ChocolateyAppWindow -Script (Join-Path $PSScriptRoot 'Install-ChocolateyApps.ps1') -Action $Action -Apps $ids -All:$All -ExpectedUserSid $sid -ExpectedVersion $ready.Version
    }
} catch {Write-Error $_ -ErrorAction Continue;exit 1}
