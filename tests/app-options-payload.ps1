# Exercise the real installer argument composition with all network/process/identity effects replaced.
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $root 'src/payload/Install-Chocolatey.Core.psm1') -Force -DisableNameChecking
& (Get-Module 'Install-Chocolatey.Core') {
    $script:optionTestReads=0
    $script:optionTestArguments=''
    function Test-SetupGitHubInstalled { param($Name,$Repo,[switch]$AllowUnlinked) $script:optionTestReads++;return [pscustomobject]@{Installed=($script:optionTestReads -gt 1);Version='';Fingerprint='test'} }
    function Get-SetupGitHubRelease {param($Repo) return [pscustomobject]@{tag_name='v1.0'}}
    function Select-SetupGitHubAsset {param($Release) return [pscustomobject]@{name='fixture.msi';browser_download_url='https://github.com/test/fixture/releases/download/v1/fixture.msi';size=0}}
    function Invoke-WebRequest {param($Uri,$OutFile,[switch]$UseBasicParsing,$TimeoutSec,$Headers) [IO.File]::WriteAllText($OutFile,'fixture')}
    function Get-AuthenticodeSignature {param($FilePath) return [pscustomobject]@{Status='Valid'}}
    function Get-SetupInstallerKind {param($File) return 'msi'}
    function Save-SetupGitHubIdentity {param($Fingerprint,$Repo)}
    function Start-Process {param($FilePath,$ArgumentList,[switch]$Wait,[switch]$PassThru,$WindowStyle) $script:optionTestArguments=$ArgumentList;return [pscustomobject]@{ExitCode=0}}
    $result=Invoke-SetupGitHubApp ([pscustomobject]@{name='Fixture';githubRepo='test/fixture';packageParameters='ADDLOCAL=Core';upgrade=$false})
    if($result.Status -ne 'PackageInstalled' -or $script:optionTestArguments -notmatch '/qn /norestart ADDLOCAL=Core$'){throw 'GitHub MSI arguments were ignored or silent defaults were lost.'}
}
Write-Host 'PASS GitHub MSI options preserve fixed installer path and silent defaults (no installs or network)'

