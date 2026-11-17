$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $root 'src/payload/Drivers.Core.psm1') -Force -DisableNameChecking
$module=Get-Module Drivers.Core
$script:checks=0
function Assert-Driver($Condition,[string]$Message){if(-not $Condition){throw $Message};$script:checks++}
function Device([string]$Name,[string]$Class,[string]$Id){[pscustomobject]@{Name=$Name;Class=$Class;HardwareIds=@($Id)}}
function Inventory([object[]]$Devices,[int]$Build=26100){[pscustomobject]@{Complete=$true;Devices=$Devices;Processors=@('AMD Ryzen 7 9700X');OsBuild=$Build;Error=''}}
function Offered($Inventory){@(Get-DriverCandidates $Inventory|Where-Object CanOffer|ForEach-Object Id)}

# Missing Intel recognition must fail here before any external dependency is replaced.
$intel=Inventory @((Device 'Intel Arc A770' 'Display' 'PCI\VEN_8086&DEV_56A0&SUBSYS_10208086'))
Assert-Driver ((Offered $intel) -contains 'intel-arc-graphics-driver') 'A reviewed Intel Arc PCI device must offer the Intel driver'
$amd=Inventory @((Device 'AMD Radeon RX 7800 XT' 'Display' 'PCI\VEN_1002&DEV_747E'))
Assert-Driver (((Offered $amd) -join ',') -eq 'amd-software-adrenalin-edition') 'AMD graphics must select only Adrenalin'
Assert-Driver ((Offered (Inventory $amd.Devices 19045)).Count -eq 0) 'The reviewed Windows 11 AMD package must not run on Windows 10'
foreach($id in @('5693','56A1','56A2','56A5','56A6','E20B','E20C','7D55','7D51','64A0')){
    Assert-Driver ((Offered (Inventory @((Device 'Microsoft Basic Display Adapter' 'Display' ('PCI\VEN_8086&DEV_'+$id))))) -contains 'intel-arc-graphics-driver') ('Reviewed Intel device must match by PCI identity: '+$id)
}
foreach($id in @('5916','3E92','9A49','4680','FFFF','56B1','56AF')){
    $candidate=@(Get-DriverCandidates (Inventory @((Device 'Intel Graphics' 'Display' ('PCI\VEN_8086&DEV_'+$id))))|Where-Object Id -eq 'intel-arc-graphics-driver')[0]
    Assert-Driver (-not $candidate.CanOffer -and $candidate.Status -eq 'NotApplicable' -and $candidate.Detail -match 'OEM|manufacturer') ('Older/unreviewed/Pro Intel device must provide guidance without an incompatible install: '+$id)
    Assert-Driver ($candidate.Hardware -contains 'Intel Graphics') 'Unsupported Intel graphics must remain visible in detection results'
}
Assert-Driver ((Offered (Inventory $intel.Devices 19044)).Count -eq 0) 'Intel current branch requires Windows 10 22H2 or Windows 11'
Assert-Driver ((Offered (Inventory $intel.Devices 19045)) -contains 'intel-arc-graphics-driver') 'Intel supports Windows 10 22H2'
$hybrid=Inventory @($amd.Devices+$intel.Devices+(Device 'NVIDIA GeForce' 'Display' 'PCI\VEN_10DE&DEV_2803')+$intel.Devices)
Assert-Driver (((Offered $hybrid)|Sort-Object) -join ',' -eq 'amd-software-adrenalin-edition,intel-arc-graphics-driver,nvidia-display-driver') 'Hybrid systems offer each GPU package once'
$unrelated=Inventory @((Device 'Intel chipset' 'System' 'PCI\VEN_8086&DEV_56A0'),(Device 'AMD audio' 'MEDIA' 'PCI\VEN_1002&DEV_747E'),(Device 'USB graphics' 'Display' 'USB\VEN_8086&DEV_56A0'),(Device 'Malformed PCI' 'Display' 'PCI\VEN_8086&DEV_56A00'))
Assert-Driver ((Offered $unrelated).Count -eq 0) 'Vendor IDs in unrelated classes or malformed identities must not trigger GPU drivers'
$unknown=Inventory @();$unknown.Complete=$false
Assert-Driver (@(Get-DriverCandidates $unknown|Where-Object CanOffer).Count -eq 0) 'Unavailable inventory must never offer drivers'
$plan=@(@{packageId='intel-arc-graphics-driver'},@{packageId='intel-graphics-driver'},@{packageId='amd-software-adrenalin-edition'},@{packageId='nvidia-display-driver'},@{packageId='amd-ryzen-chipset'},@{packageId='steam'})
Assert-Driver ((@(Get-NonDriverAppPlan $plan)|ForEach-Object packageId) -join ',' -eq 'steam') 'Driver identities including the older Intel package must not bypass hardware gating through Apps'

# Only external package execution/registry access is replaced: selection, validation,
# result/evidence handling and driver-specific payload generation remain real.
& $module {
    $script:RegistryRows=@([pscustomobject]@{DisplayName='Intel(R) Graphics Driver';DisplayVersion='32.0.101.9175';PSPath='Test:Intel'})
    function script:Get-ItemProperty {param($Path,$ErrorAction) $script:RegistryRows}
    function script:Get-DriverPackageState {param($Executable,$PackageId) [pscustomobject]@{Installed=$false;Version=''}}
    function script:Invoke-DriverPackageInstall {
        param($App,$Executable)
        $script:InstalledApp=$App
        [pscustomobject]@{Status='PackageInstalled';ExitCode=0;Version='32.0.101.9175';Detail='Fixture installer outcome.'}
    }
}
$candidates=@(Get-DriverCandidates $intel)
$selected=@(Get-DriverInstallSelection $candidates 'fixture-only')
Assert-Driver (($selected -join ',') -eq 'intel-arc-graphics-driver') 'Automatic driver selection must include supported Intel hardware'
$stage=Invoke-DriverStage $candidates 'fixture-only' -ApprovedIds $selected -Confirm:$false
$installed=@($stage.Results|Where-Object Id -eq 'intel-arc-graphics-driver')[0]
Assert-Driver ($stage.CanContinue -and $installed.Status -eq 'PackageInstalled' -and $installed.RegistryEvidence.Count -gt 0) 'Intel installation must use real stage validation and machine driver registration'
Assert-Driver ((& $module {$script:InstalledApp.packageId}) -eq 'intel-arc-graphics-driver') 'The install payload must preserve the approved Intel package identity'
& $module {$script:RegistryRows=@([pscustomobject]@{DisplayName='Intel Graphics Software';DisplayVersion='26.1';PSPath='Test:ControlPanel'})}
$stage=Invoke-DriverStage $candidates 'fixture-only' -ApprovedIds $selected -Confirm:$false
Assert-Driver ($stage.Failed -and @($stage.Results|Where-Object Id -eq 'intel-arc-graphics-driver')[0].Status -eq 'UnverifiedDriverInstall') 'An Intel control application alone must not verify a graphics driver'
& $module {$script:RegistryRows=@([pscustomobject]@{DisplayName='AMD Software';DisplayVersion='26.10.2';PSPath='Test:AMD'})}
$stage=Invoke-DriverStage @(Get-DriverCandidates $amd) 'fixture-only' -ApprovedIds @('amd-software-adrenalin-edition') -Confirm:$false
Assert-Driver ($stage.CanContinue -and @($stage.Results|Where-Object Id -eq 'amd-software-adrenalin-edition')[0].Status -eq 'PackageInstalled') 'AMD graphics must flow through the install stage with machine evidence'

# Exercise the real resumable worker with only account, disk, task and package
# boundaries replaced. Nothing writes resume state or registers a task in this test.
Import-Module (Join-Path $root 'src/payload/Install-Run.Core.psm1') -Force -DisableNameChecking
$runModule=Get-Module Install-Run.Core
& $runModule {
    function script:Test-SetupAdministrator {$true}
    function script:Get-InstallUserSid {'S-1-5-21-123'}
    function script:Assert-InstallSnapshot {param($State)}
    function script:Assert-ChocolateyReady {param($Executable,$Version)}
    function script:Save-InstallRunState {param($State)}
    function script:Test-InstallPendingReboot {$false}
    function script:Update-FreshkitEngineStages {param($State,[switch]$Finished)}
    function script:Remove-InstallResumeTask {param($State)}
    function script:Remove-InstallRunFiles {param($State)}
    function script:Get-DriverDeviceInventory {$script:InventoryFixture}
    function script:Test-RunPackageRegistered {param($State,$PackageId,$Item) $true}
}
function DriverRun($Candidate){
    [pscustomobject]@{RunId='fixture';UserSid='S-1-5-21-123';Status='Ready';Executable='fixture-only';ChocolateyVersion='2.7.5';DriverQueue=@($Candidate);AppQueue=@();DriverResults=@();AppResults=@();InFlight=$null;Attempts=[pscustomobject]@{};Progress=0;CleanupError=''}
}
$intelCandidate=@(Get-DriverCandidates $intel|Where-Object Id -eq 'intel-arc-graphics-driver')[0]
& $module {$script:RegistryRows=@([pscustomobject]@{DisplayName='Intel(R) Graphics Driver';DisplayVersion='32.0.101.9175';PSPath='Test:Intel'})}
& $runModule {param($Inventory) $script:InventoryFixture=$Inventory} $intel
$state=DriverRun $intelCandidate
$state=& $runModule {param($State) Invoke-InstallRunWorker $State} $state
Assert-Driver ($state.Status -eq 'Completed' -and $state.DriverQueue.Count -eq 0 -and $state.DriverResults[0].Status -eq 'PackageInstalled') 'Intel must complete the actual install worker and its result queue'
$state=DriverRun $intelCandidate
$state.InFlight=[pscustomobject]@{PackageId='intel-arc-graphics-driver';Phase='Driver'}
$state=& $runModule {param($State) Invoke-InstallRunWorker $State} $state
Assert-Driver ($state.DriverResults[0].Status -eq 'RecoveredInstalled') 'An interrupted Intel install with package and machine evidence must reconcile without reinstalling'
& $runModule {param($Inventory) $script:InventoryFixture=$Inventory} $unrelated
$state=DriverRun $intelCandidate
$state=& $runModule {param($State) Invoke-InstallRunWorker $State} $state
Assert-Driver ($state.DriverResults[0].Status -eq 'NotApplicable') 'Continuation must revalidate Intel hardware instead of installing a stale saved candidate'
& $runModule {param($Inventory) $script:InventoryFixture=$Inventory} $unknown
$state=DriverRun $intelCandidate
$state=& $runModule {param($State) Invoke-InstallRunWorker $State} $state
Assert-Driver ($state.DriverResults[0].Status -eq 'Failed') 'Continuation must fail closed when Intel hardware cannot be revalidated'
Remove-Module Install-Run.Core -Force
Remove-Module Drivers.Core -Force
Write-Host ('PASS '+$script:checks+' hardware, hybrid, package selection, install evidence and continuation checks (no real installs)')
