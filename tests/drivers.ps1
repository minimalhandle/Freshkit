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
Assert-Driver (((Offered $hybrid)|Sort-Object) -join ',' -eq 'amd-software-adrenalin-edition,geforce-game-ready-driver,intel-arc-graphics-driver') 'Hybrid systems offer each GPU package once'
# Desktop Ryzen built-in graphics (shown as Microsoft Basic Display Adapter before its driver) next to an NVIDIA
# card: Windows Update doesn't supply its driver, so it needs AMD Software as well as the card's own package.
$igpu=Device 'Microsoft Basic Display Adapter' 'Display' 'PCI\VEN_1002&DEV_13C0&SUBSYS_7E611462'
$rtx=Device 'NVIDIA GeForce RTX 5070' 'Display' 'PCI\VEN_10DE&DEV_2F04&SUBSYS_8A231043'
Assert-Driver ((((Offered (Inventory @($igpu,$rtx)))|Sort-Object) -join ',') -eq 'amd-software-adrenalin-edition,geforce-game-ready-driver') 'Built-in Ryzen graphics next to an NVIDIA card gets AMD Software as well as the NVIDIA package'
$unrelated=Inventory @((Device 'Intel chipset' 'System' 'PCI\VEN_8086&DEV_56A0'),(Device 'AMD audio' 'MEDIA' 'PCI\VEN_1002&DEV_747E'),(Device 'USB graphics' 'Display' 'USB\VEN_8086&DEV_56A0'),(Device 'Malformed PCI' 'Display' 'PCI\VEN_8086&DEV_56A00'))
Assert-Driver ((Offered $unrelated).Count -eq 0) 'Vendor IDs in unrelated classes or malformed identities must not trigger GPU drivers'
$unknown=Inventory @();$unknown.Complete=$false
Assert-Driver (@(Get-DriverCandidates $unknown|Where-Object CanOffer).Count -eq 0) 'Unavailable inventory must never offer drivers'
$plan=@(@{packageId='intel-arc-graphics-driver'},@{packageId='intel-graphics-driver'},@{packageId='amd-software-adrenalin-edition'},@{packageId='geforce-game-ready-driver'},@{packageId='amd-ryzen-chipset'},@{packageId='steam'})
Assert-Driver ((@(Get-NonDriverAppPlan $plan)|ForEach-Object packageId) -join ',' -eq 'steam') 'Driver identities including the older Intel package must not bypass hardware gating through Apps'

# Only external package execution/registry access is replaced: selection, validation,
# result/evidence handling and driver-specific payload generation remain real.
& $module {
    $script:RegistryRows=@([pscustomobject]@{DisplayName='Intel(R) Graphics Driver';DisplayVersion='32.0.101.9175';PSPath='Test:Intel'})
    $script:AfterInstallRows=$null
    $script:RemovedRecords=@()
    function script:Remove-DriverPackageRecord {param($Executable,$PackageId) $script:RemovedRecords+=$PackageId}
    function script:Get-ItemProperty {param($Path,$ErrorAction) $script:RegistryRows}
    function script:Get-DriverPackageState {param($Executable,$PackageId) [pscustomobject]@{Installed=$false;Version=''}}
    function script:Invoke-DriverPackageInstall {
        param($App,$Executable)
        $script:InstalledApp=$App
        $version=switch($App.packageId){'amd-software-adrenalin-edition' {'26.10.2'} 'geforce-game-ready-driver' {'621.24.0'} default {'32.0.101.9175'}}
        if($script:AfterInstallRows){$script:RegistryRows=$script:AfterInstallRows}
        [pscustomobject]@{Status='PackageInstalled';ExitCode=0;Version=$version;Detail='Fixture installer outcome.'}
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
& $module {$script:RegistryRows=@([pscustomobject]@{DisplayName='AMD Software';DisplayVersion='26.9.1';PSPath='Test:AMD'})}
$stage=Invoke-DriverStage @(Get-DriverCandidates $amd) 'fixture-only' -ApprovedIds @('amd-software-adrenalin-edition') -Confirm:$false
$row=@($stage.Results|Where-Object Id -eq 'amd-software-adrenalin-edition')[0]
Assert-Driver ($row.Status -eq 'UnverifiedDriverInstall' -and $row.Detail -match '26\.9\.1') 'A reported success is unverified when Windows still shows an older AMD Software version'

# A crash during the NVIDIA installer left Chocolatey's record (621.24) but Windows still runs 610.88.
$nvidia=Inventory @($rtx)
& $module {
    $script:InstalledApp=$null
    $script:RegistryRows=@([pscustomobject]@{DisplayName='NVIDIA Graphics Driver 610.88';DisplayVersion='610.88';PSPath='Test:NV'})
    $script:AfterInstallRows=@([pscustomobject]@{DisplayName='NVIDIA Graphics Driver 621.24';DisplayVersion='621.24';PSPath='Test:NV'},[pscustomobject]@{DisplayName='NVIDIA App 11.0';DisplayVersion='11.0';PSPath='Test:App'})
    function script:Get-DriverPackageState {param($Executable,$PackageId) [pscustomobject]@{Installed=$true;Version='621.24.0'}}
}
Assert-Driver (-not (Get-DriverInstallCheck 'geforce-game-ready-driver' '621.24.0').Verified) 'A stale Chocolatey record is not proof the NVIDIA driver installed'
Assert-Driver (((Get-DriverInstallSelection @(Get-DriverCandidates $nvidia) 'fixture-only') -join ',') -eq 'geforce-game-ready-driver') 'An unfinished driver install is selected again instead of being skipped as already installed'
$stage=Invoke-DriverStage @(Get-DriverCandidates $nvidia) 'fixture-only' -ApprovedIds @('geforce-game-ready-driver') -Confirm:$false
$row=@($stage.Results|Where-Object Id -eq 'geforce-game-ready-driver')[0]
Assert-Driver ($row.Status -eq 'PackageInstalled' -and $row.Detail -match 'did not finish' -and (& $module {[bool]$script:InstalledApp.force})) 'An unfinished NVIDIA install is forced to reinstall and then verified'
$nvPayload=& $module {$script:InstalledApp}
Assert-Driver ($nvPayload.packageParameters -notmatch 'NVApp' -and $nvPayload.installArguments -eq '-deselect:Display.PhysX') 'NVIDIA uses the full installer (no stripped /NVApp option) and still leaves out PhysX'
Assert-Driver ((& $module {$script:RemovedRecords}) -contains 'nvidia-display-driver' -and $row.Detail -match 'older nvidia-display-driver') 'After the full NVIDIA package installs, the stripped package record from earlier versions is dropped'
Import-Module (Join-Path $root 'src/payload/Install-Chocolatey.Core.psm1') -DisableNameChecking
Assert-Driver ((Get-ChocolateyInstallArguments ([pscustomobject]@{status='Chocolatey';packageId='geforce-game-ready-driver';packageParameters='';force=$true})) -contains '--force') 'A forced reinstall passes --force to Chocolatey'
& $module {$script:InstalledApp=$null;$script:AfterInstallRows=$null;$script:RegistryRows=@([pscustomobject]@{DisplayName='NVIDIA Graphics Driver 621.24';DisplayVersion='621.24';PSPath='Test:NV'},[pscustomobject]@{DisplayName='NVIDIA App 11.0';DisplayVersion='11.0';PSPath='Test:App'})}
$stage=Invoke-DriverStage @(Get-DriverCandidates $nvidia) 'fixture-only' -ApprovedIds @('geforce-game-ready-driver') -Confirm:$false
Assert-Driver (@($stage.Results|Where-Object Id -eq 'geforce-game-ready-driver')[0].Status -eq 'AlreadyInstalled' -and -not (& $module {$script:InstalledApp})) 'A driver Windows shows at the recorded version is not reinstalled'
Assert-Driver ((Get-DriverInstallCheck 'geforce-game-ready-driver' '621.24.20261120').Verified) 'Chocolatey package fix versions still match the installed driver'
& $module {function script:Get-DriverPackageState {param($Executable,$PackageId) [pscustomobject]@{Installed=$false;Version=''}}}

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
    function script:Get-InstallBootId {'boot-1'}
    function script:Get-DriverPackageState {param($Executable,$PackageId) $script:PackageStateFixture}
    function script:Invoke-ChocolateyAppPlan {param($Plan,$Executable,[switch]$Confirm) [pscustomobject]@{Id=$Plan[0].packageId;Name=$Plan[0].name;PackageId=$Plan[0].packageId;Status='PackageInstalled';ExitCode=0;Version='1.0';Detail='Fixture app.'}}
    $script:PackageStateFixture=[pscustomobject]@{Installed=$true;Version='32.0.101.9175'}
}
function DriverRun($Candidate,[object[]]$Apps=@()){
    [pscustomobject]@{RunId='fixture';UserSid='S-1-5-21-123';Status='Ready';Executable='fixture-only';ChocolateyVersion='2.7.5';DriverQueue=@($Candidate);AppQueue=$Apps;DriverResults=@();AppResults=@();InFlight=$null;Attempts=[pscustomobject]@{};Progress=0;CleanupError='';RebootCount=0;RebootProgress=0;NoProgressReboots=0;WaitingBootId='';LastRebootReason='';Failure=''}
}
$steam=[pscustomobject]@{packageId='steam';name='Steam';status='Chocolatey'}
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
$state=DriverRun $intelCandidate @($steam)
$state=& $runModule {param($State) Invoke-InstallRunWorker $State} $state
Assert-Driver ($state.Status -eq 'AwaitingReboot' -and $state.AppQueue.Count -eq 1 -and $state.LastRebootReason -match 'Drivers were installed') 'Installing drivers restarts Windows once before any app is installed'
$state=DriverRun $intelCandidate @($steam)
$state.InFlight=[pscustomobject]@{PackageId='intel-arc-graphics-driver';Phase='Driver'}
$state=& $runModule {param($State) Invoke-InstallRunWorker $State} $state
Assert-Driver ($state.Status -eq 'Completed' -and $state.AppResults[0].Status -eq 'PackageInstalled') 'A driver already confirmed after a restart does not trigger another restart before apps'
$nvidiaCandidate=@(Get-DriverCandidates $nvidia|Where-Object Id -eq 'geforce-game-ready-driver')[0]
& $module {$script:RegistryRows=@([pscustomobject]@{DisplayName='NVIDIA Graphics Driver 610.88';DisplayVersion='610.88';PSPath='Test:NV'},[pscustomobject]@{DisplayName='NVIDIA App 11.0';DisplayVersion='11.0';PSPath='Test:App'})}
& $runModule {param($Inventory) $script:InventoryFixture=$Inventory;$script:PackageStateFixture=[pscustomobject]@{Installed=$true;Version='621.24.0'}} $nvidia
$state=DriverRun $nvidiaCandidate
$state.InFlight=[pscustomobject]@{PackageId='geforce-game-ready-driver';Phase='Driver'}
$state=& $runModule {param($State) Invoke-InstallRunWorker $State} $state
Assert-Driver ($state.DriverResults[0].Status -ne 'RecoveredInstalled' -and $state.DriverResults[0].Status -ne 'AlreadyInstalled') 'An NVIDIA install interrupted by a crash is not reported as installed when Windows shows the older driver'
& $module {$script:RegistryRows=@([pscustomobject]@{DisplayName='Intel(R) Graphics Driver';DisplayVersion='32.0.101.9175';PSPath='Test:Intel'})}
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
