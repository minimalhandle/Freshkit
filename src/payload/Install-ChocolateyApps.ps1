#requires -Version 5.1
[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [ValidateSet('Menu','Install','List','Plan','Drivers')][string]$Action='Install',
    [string[]]$Apps=@(),[switch]$All,
    [ValidatePattern('^S-1-[0-9-]+$')][string]$ExpectedUserSid='',
    [ValidatePattern('^[0-9]+(?:\.[0-9]+){1,3}$')][string]$ExpectedVersion='',
    [switch]$Ui,[string]$LogDir='',[switch]$SkipDrivers,[string]$DriverIds=''
)
$ErrorActionPreference='Stop'
foreach($name in @('Install-Chocolatey.Core.psm1','Drivers.Core.psm1','Install-Run.Core.psm1')){Import-Module (Join-Path $PSScriptRoot $name) -Force -DisableNameChecking}
$ids=@($Apps|ForEach-Object {$_ -split ','}|ForEach-Object {$_.Trim()}|Where-Object {$_})
$catalogPath=Join-Path $PSScriptRoot 'Install-Chocolatey.apps.json'
$plan=@(Get-ChocolateyAppPlan $catalogPath $ids -All:$All)
if($Action -eq 'Drivers'){$plan=@()}
if($Action -in @('List','Plan') -or $WhatIfPreference){
    Get-InstallExecutionPlan @(Get-NonDriverAppPlan $plan)|Select-Object id,name,packageId,status|Format-Table -AutoSize
    Write-Host 'Read-only execution preview: matching hardware drivers, required restart(s), runtimes, apps. No task is registered.'
    exit 0
}
if(-not(Test-SetupAdministrator)){throw 'Use the main launcher to create the verified Administrator window.'}
if($ExpectedUserSid -and $ExpectedUserSid -ne (Get-InstallUserSid)){throw 'The Windows account changed; use the original account.'}
if(-not $ExpectedVersion){throw 'Use the main launcher to install and verify Chocolatey first.'}
$executable=Get-SetupChocolateyPath;$null=Assert-ChocolateyReady $executable $ExpectedVersion
if(@(Get-PendingInstallRuns).Count){throw 'An unfinished installation exists. Choose Resume in the BAT before starting another.'}
if($Action -eq 'Menu'){$Action='Install'}
if($Action -eq 'Install' -and -not $ids.Count){$All=$true;$plan=@(Get-ChocolateyAppPlan $catalogPath -All)}
if($Action -ne 'Drivers' -and -not $All -and -not $ids.Count){throw 'Specify -All or -Apps.'}
$inventory=Get-DriverDeviceInventory
$candidates=@(Get-DriverCandidates $inventory -AppPlan $plan)
if($SkipDrivers){Write-Host 'Driver installation skipped (chosen in Freshkit).';$approved=@()}elseif($DriverIds){
    $requested=@($DriverIds -split ',')
    foreach($id in $requested){if($id -notin @($candidates.Id)){throw "Unknown driver retry ID: $id"}}
    $approved=@($candidates|Where-Object {$_.Id -in $requested -and $_.CanOffer}|ForEach-Object Id)
}else{$approved=@(Get-DriverInstallSelection $candidates $executable)}
$regular=@(Get-InstallExecutionPlan @(Get-NonDriverAppPlan $plan))
Write-Host 'Order: matching chipset/display drivers; required restart(s); Visual C++/.NET runtimes; apps. A temporary task resumes this exact selection after sign-in.'
$state=New-InstallRun -AppPlan $regular -DriverCandidates $candidates -ApprovedDriverIds $approved -Executable $executable -ChocolateyVersion $ExpectedVersion -SourceDirectory $PSScriptRoot -Confirm:$false
try{$state=Invoke-InstallRun $state}catch{Set-InstallRunReview $state $_.Exception.Message;throw}
    if($state.Status -in @('Completed','NeedsReview','Cancelled')){Show-InstallRunReport $state}
if($state.Status -eq 'AwaitingReboot'){Request-InstallRunReboot $state;exit 3010}
if($state.Status -eq 'NeedsReview'){Write-Warning $state.Failure;if($state.CleanupError){Write-Warning $state.CleanupError};Write-Host 'Installation stopped. Resolve the issue, then run the BAT again. Temporary continuation is removed when cleanup succeeds.';exit 1}
if($state.CleanupError){Write-Warning ('Cleanup needs attention: '+$state.CleanupError)}
if($state.Status -eq 'Completed'){Write-Host 'Done. Temporary continuation files and task cleaned up.'}

if($state.CleanupError -or @(Get-InstallReportRows $state|Where-Object Outcome -eq 'Failed').Count){exit 1}
