#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidatePattern('^[a-f0-9]{32}$')][string]$RunId,[switch]$Automatic)
$ErrorActionPreference='Stop'
if($env:LOCALAPPDATA -and (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'Freshkit'))){try{$fkLogs=Join-Path $env:LOCALAPPDATA 'Freshkit\logs';[IO.Directory]::CreateDirectory($fkLogs)|Out-Null;Start-Transcript -LiteralPath (Join-Path $fkLogs ('resume-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')) -Append|Out-Null}catch{}}
$expected=Join-Path (Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'FreshWindowsSetupResume') $RunId
if([IO.Path]::GetFullPath($PSScriptRoot) -ne [IO.Path]::GetFullPath($expected)){throw 'Resume must run from its protected machine-local snapshot.'}
foreach($name in @('Install-Chocolatey.Core.psm1','Drivers.Core.psm1','Install-Run.Core.psm1','Freshkit.Status.ps1')){
    $path=Join-Path $PSScriptRoot $name;$item=Get-Item -LiteralPath $path -Force
    if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse point in resume code.'}
    $acl=Get-Acl -LiteralPath $path
    if($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin @('S-1-5-18','S-1-5-32-544')){throw 'Resume code ownership is not trusted.'}
    foreach($rule in $acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])){
        $write=[Security.AccessControl.FileSystemRights]::Write -bor [Security.AccessControl.FileSystemRights]::Delete -bor [Security.AccessControl.FileSystemRights]::ChangePermissions -bor [Security.AccessControl.FileSystemRights]::TakeOwnership
        if($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Value -notin @('S-1-5-18','S-1-5-32-544') -and ($rule.FileSystemRights -band $write)){throw 'Resume code is writable by an unprivileged principal.'}
    }
}
foreach($name in @('Install-Chocolatey.Core.psm1','Drivers.Core.psm1','Install-Run.Core.psm1')){Import-Module (Join-Path $PSScriptRoot $name) -Force -DisableNameChecking}
$state=Read-InstallRunState $RunId
$env:FRESHKIT_STAGE_PATH=if($state.PSObject.Properties['FreshkitStagePath']){[string]$state.FreshkitStagePath}else{''}
try{
    Assert-InstallSnapshot $state
    if($state.Status -eq 'NeedsReview'){Show-InstallRunReport $state;Write-Warning 'Previous run stopped. Resolve the error and rerun the BAT; no automatic retry was attempted.';exit 1}
    $state=Invoke-InstallRun $state
    if($state.Status -in @('Completed','NeedsReview','Cancelled')){Show-InstallRunReport $state}
    if($state.Status -eq 'AwaitingReboot'){Request-InstallRunReboot $state;exit 3010}
    if($state.Status -eq 'NeedsReview'){Write-Warning $state.Failure;if($state.CleanupError){Write-Warning $state.CleanupError};Write-Host 'Installation stopped. Resolve the issue, then run the BAT again. Temporary continuation is removed when cleanup succeeds.';exit 1}
    if($state.Status -eq 'Completed'){Write-Host 'Done. Temporary continuation files and task cleaned up.'}
    if($state.CleanupError){Write-Warning ('Cleanup needs attention: '+$state.CleanupError)}
    if($state.CleanupError -or @(Get-InstallReportRows $state|Where-Object Outcome -eq 'Failed').Count){exit 1}
}catch{if($state.Status -ne 'Completed'){Set-InstallRunReview $state $_.Exception.Message};Write-Error $_ -ErrorAction Continue;exit 1}
