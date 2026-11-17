$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$scriptPath=Join-Path $root 'src/scripts/DriverPreview.ps1'
if(-not (Test-Path $scriptPath)){throw 'Read-only driver preview backend must exist'}
. $scriptPath
Import-Module (Join-Path $root 'src/payload/Drivers.Core.psm1') -Force -DisableNameChecking
$script:checks=0
function Assert-Preview($Condition,[string]$Reason){if(-not $Condition){throw $Reason};$script:checks++}
function D($Name,$Class,$Id){[pscustomobject]@{Name=$Name;Class=$Class;HardwareIds=@($Id)}}
$intelId='PCI\VEN_8086&DEV_56A0&SUBSYS_10208086\ONE'
$amdId='PCI\VEN_1002&DEV_747E&SUBSYS_00011002\TWO'
$script:inventory=[pscustomobject]@{Complete=$true;Devices=@((D 'Intel Arc A770' Display $intelId),(D 'AMD Radeon RX7800XT' Display $amdId),(D 'AMD chipset' System 'PCI\VEN_1022&DEV_1480\CHIP'));Processors=@('AMD Ryzen 7 9700X');OsBuild=26100;Error=''}
$script:signed=@([pscustomobject]@{DeviceID=$intelId;DriverVersion='32.0.101.9175'},[pscustomobject]@{DeviceID=$amdId;DriverVersion='32.0.21037.1004'},[pscustomobject]@{DeviceID='PCI\VEN_1022&DEV_1480\CHIP';DriverVersion='10.0.26100.1'})
$inventoryReader={$script:inventory}
$signedReader={$script:signed}
$metadataReader={param($id) [pscustomobject]@{Version='26.10.2';Status='Available';Detail='Fixture primary feed'}}
function Scan {Invoke-FreshkitDriverPreview -InventoryReader $inventoryReader -SignedDriverReader $signedReader -MetadataReader $metadataReader}
function Invoke-DriverStage {throw 'A preview must never execute an installer'}
function Invoke-ChocolateyAppPlan {throw 'A preview must never execute package installation'}
function Set-ItemProperty {throw 'A preview must never write registry values'}
$result=Scan
Assert-Preview ($result.SchemaVersion -eq 1 -and $result.InventoryComplete -and $result.Rows.Count -eq 3) 'Preview must show GPU and chipset rows on hybrid PCs'
$intel=@($result.Rows|Where-Object PackageId -eq 'intel-arc-graphics-driver')[0]
$amd=@($result.Rows|Where-Object PackageId -eq 'amd-software-adrenalin-edition')[0]
Assert-Preview ($intel.InstalledDriverVersion -eq '32.0.101.9175' -and $amd.InstalledDriverVersion -eq '32.0.21037.1004') 'Installed versions must match each exact PnP instance, not a vendor or package version'
Assert-Preview ($amd.AvailablePackageVersion -eq '26.10.2' -and $amd.InstalledDriverVersion -ne $amd.AvailablePackageVersion) 'Available package version must remain separate from installed Windows driver version'
Assert-Preview ($intel.Compatibility -eq 'Candidate' -and $intel.Reason -and $intel.SupportUrl -match '^https://') 'Preview must expose compatibility reasons and direct support links'
$secondIntel='PCI\VEN_8086&DEV_56A0&SUBSYS_10208086\OTHER-SLOT'
$savedDevices=$script:inventory.Devices
$script:inventory.Devices=@((D 'Identical Intel GPU' Display $intelId),(D 'Identical Intel GPU' Display $secondIntel))
$script:signed=@([pscustomobject]@{DeviceID=$intelId;DriverVersion='32.0.101.9175'},[pscustomobject]@{DeviceID=$secondIntel;DriverVersion='32.0.101.8992'})
$script:metadataCalls=0
$result=Invoke-FreshkitDriverPreview -InventoryReader $inventoryReader -SignedDriverReader $signedReader -MetadataReader {param($id) $script:metadataCalls++;[pscustomobject]@{Version='32.0.101.9175';Status='Available';Detail='Fixture metadata'}}
Assert-Preview ($result.Rows.Count -eq 2 -and $result.Rows[0].InstalledDriverVersion -eq '32.0.101.9175' -and $result.Rows[1].InstalledDriverVersion -eq '32.0.101.8992') 'Identical GPU names must preserve separate PnP instance driver versions'
Assert-Preview ($script:metadataCalls -eq 1) 'Multiple cards must share one current metadata request for their package'
$script:inventory.Devices=$savedDevices
$script:signed=@([pscustomobject]@{DeviceID='PCI\VEN_8086&DEV_56A0\DIFFERENT-INSTANCE';DriverVersion='99.0'})
$result=Scan
Assert-Preview (@($result.Rows|Where-Object PackageId -eq 'intel-arc-graphics-driver')[0].InstalledDriverVersion -eq 'Unknown') 'Same GPU model in a different slot must not supply installed version'
$script:signed=@([pscustomobject]@{DeviceID=$intelId;DriverVersion='32.1'},[pscustomobject]@{DeviceID=$intelId;DriverVersion='32.2'})
Assert-Preview (@((Scan).Rows|Where-Object PackageId -eq 'intel-arc-graphics-driver')[0].InstalledDriverVersion -eq 'Unknown') 'Conflicting signed driver versions must remain Unknown'
$script:inventory.Devices=@((D 'Intel UHD 630' Display 'PCI\VEN_8086&DEV_3E92&SUBSYS_00008086\OLD'),(D 'Unrecognised graphics' Display 'ROOT\DISPLAY\0001'))
$result=Scan
Assert-Preview ($result.Rows.Count -eq 2 -and @($result.Rows|Where-Object Compatibility -eq 'Candidate').Count -eq 0) 'Unsupported graphics remain visible without changing installation rules'
Assert-Preview (@($result.Rows|Where-Object Hardware -eq 'Intel UHD 630')[0].SupportUrl -match '^https://www.intel.com/') 'Unsupported Intel cards must have a direct supported driver selector'
$script:unsupportedMetadataCalls=0
$result=Invoke-FreshkitDriverPreview -InventoryReader $inventoryReader -SignedDriverReader $signedReader -MetadataReader {param($id) $script:unsupportedMetadataCalls++;throw 'Unsupported devices must not request package metadata'}
$unsupported=@($result.Rows|Where-Object Hardware -eq 'Intel UHD 630')[0]
Assert-Preview ($unsupported.PackageId -eq '' -and $unsupported.PackageName -eq 'No reviewed automatic package') 'An incompatible Intel branch must never be presented as a proposed automatic package'
Assert-Preview ($script:unsupportedMetadataCalls -eq 0 -and $unsupported.Compatibility -eq 'NotApplicable' -and $unsupported.Reason -match 'OEM' -and $unsupported.SupportUrl -match '^https://www.intel.com/') 'Unsupported Intel preview must preserve compatibility guidance and support link without a metadata lookup'
$script:inventory|Add-Member Manufacturer 'Dell Inc.' -Force
$result=Scan
Assert-Preview ($result.Rows[0].SupportUrl -eq 'https://www.dell.com/support/home/') 'Known OEM machines must link directly to manufacturer support'
$script:inventory.PSObject.Properties.Remove('Manufacturer')
$result=Invoke-FreshkitDriverPreview -InventoryReader {throw 'CIM denied'} -SignedDriverReader {throw 'No driver inventory'} -MetadataReader {throw 'No network'}
Assert-Preview (-not $result.InventoryComplete -and $result.Error -and $result.Rows.Count -eq 0) 'Inventory failure must be explicit and never imply no updates are needed'
$script:inventory.Devices=@((D 'Intel Arc A770' Display $intelId))
$result=Invoke-FreshkitDriverPreview -InventoryReader $inventoryReader -SignedDriverReader {throw 'CIM denied'} -MetadataReader {throw 'Offline'}
Assert-Preview ($result.Rows[0].InstalledDriverVersion -eq 'Unknown' -and $result.Rows[0].AvailablePackageVersion -eq 'Unknown' -and $result.Rows[0].MetadataStatus -eq 'Unknown' -and $result.Error) 'Unavailable driver inventory and metadata must be Unknown'
$feed=@'
<feed xmlns="http://www.w3.org/2005/Atom" xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices" xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata"><entry><content><m:properties><d:Id>intel-arc-graphics-driver</d:Id><d:Version>32.0.101.9175</d:Version><d:IsLatestVersion>true</d:IsLatestVersion><d:IsPrerelease>false</d:IsPrerelease></m:properties></content></entry></feed>
'@
$script:feed=$feed
$package=Get-FreshkitDriverPackageMetadata intel-arc-graphics-driver -Transport {param($uri,$timeout) if($uri -notmatch '^https://community.chocolatey.org/api/v2/' -or $timeout -gt 5000){throw 'Invalid metadata boundary'};$script:feed}
Assert-Preview ($package.Status -eq 'Available' -and $package.Version -eq '32.0.101.9175') 'Primary Chocolatey Atom metadata must supply an exact package version'
$package=Get-FreshkitDriverPackageMetadata amd-software-adrenalin-edition -Transport {param($uri,$timeout) $script:feed}
Assert-Preview ($package.Status -eq 'Unknown' -and $package.Version -eq 'Unknown') 'Unrelated package metadata must not be accepted'
$package=Get-FreshkitDriverPackageMetadata intel-arc-graphics-driver -Transport {param($uri,$timeout) '<invalid'}
Assert-Preview ($package.Status -eq 'Unknown') 'Malformed feed must remain Unknown'
$script:feed=$feed.Replace('<d:IsLatestVersion>true','<d:IsLatestVersion>false')
$package=Get-FreshkitDriverPackageMetadata intel-arc-graphics-driver -Transport {param($uri,$timeout) $script:feed}
Assert-Preview ($package.Status -eq 'Unknown') 'An old package entry must not be presented as current availability'
Write-Host ('PASS '+$script:checks+' read-only driver preview checks (synthetic CIM and metadata; no installs)')
