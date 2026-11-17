$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $root 'src/payload/Install-Chocolatey.Core.psm1') -Force -DisableNameChecking
$normalised=& (Get-Module 'Install-Chocolatey.Core') { ConvertTo-SetupNameKey 'Foo (Install) x64 1.2.3' }
if($normalised -ne 'foo'){throw 'PowerShell identity normalization differs from the C# snapshot'}
$record=[pscustomobject]@{RegistryPath='LocalMachine|Registry64|Afterclip';Name='Afterclip';Version='1.4.0';Publisher='Afterclip';InstallLocation='C:\Apps\Afterclip';UninstallString='uninstall.exe';Repository='minimalhandle/Afterclip';Fingerprint='ABC'}
$result=Test-SetupGitHubInstalled -Name 'Afterclip' -Repo 'minimalhandle/Afterclip' -Records @($record) -Links @{}
if(-not $result.Installed -or $result.Version -ne '1.4.0'){throw 'Repository evidence failed for an older release'}
if((Test-SetupGitHubInstalled -Name 'Afterclip' -Repo 'panzergen/AfterClip' -Records @($record) -Links @{}).Installed){throw 'Unrelated repository was attributed by name'}
$record.Repository=''
if((Test-SetupGitHubInstalled -Name 'Afterclip' -Repo 'minimalhandle/Afterclip' -Records @($record) -Links @{}).Installed){throw 'A name alone authorised an update'}
if(-not (Test-SetupGitHubInstalled -Name 'Afterclip' -Repo 'minimalhandle/Afterclip' -Records @($record) -Links @{ABC='minimalhandle/Afterclip'}).Installed){throw 'An explicit association was ignored'}
$record.Repository='other/Afterclip'
if((Test-SetupGitHubInstalled -Name 'Afterclip' -Repo 'minimalhandle/Afterclip' -Records @($record) -Links @{ABC='minimalhandle/Afterclip'}).Installed){throw 'A stale association overrode repository metadata'}
Write-Host 'PASS PowerShell repository identity, old versions, namesakes and explicit associations'
