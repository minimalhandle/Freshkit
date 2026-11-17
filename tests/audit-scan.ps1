$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../src/scripts/AuditScan.ps1')
$result=Invoke-FreshkitAuditInventory -Windows -Devices -TweakReader { [pscustomobject]@{Features=@{TestFeature='Applied'};Appx=@('Test.Package');AppxComplete=$true;Errors=@()} } -DeviceReader {
    @([pscustomobject]@{Name='Missing driver';PNPDeviceID='PCI fixture';Present=$true;ConfigManagerErrorCode=28},[pscustomobject]@{Name='Disabled device';PNPDeviceID='USB fixture';Present=$true;ConfigManagerErrorCode=22},[pscustomobject]@{Name='Disconnected';PNPDeviceID='OLD fixture';Present=$false;ConfigManagerErrorCode=45},[pscustomobject]@{Name='Healthy';PNPDeviceID='OK fixture';Present=$true;ConfigManagerErrorCode=0})
}
if(-not $result.DevicesComplete -or $result.Devices.Count -ne 2 -or $result.Devices[0].Code -ne 28 -or $result.Features.TestFeature -ne 'Applied'){throw 'Read-only inventory failed to preserve present problem devices and feature states'}
$failed=Invoke-FreshkitAuditInventory -Windows -Devices -TweakReader {throw 'fixture failure'} -DeviceReader {throw 'fixture failure'}
if($failed.AppxComplete -or $failed.DevicesComplete -or $failed.Errors.Count -ne 2){throw 'Failures must remain unknown'}
$unused=Invoke-FreshkitAuditInventory -TweakReader {throw 'must not inspect Windows'} -DeviceReader {throw 'must not inspect devices'}
if($unused.Errors.Count -ne 0 -or $unused.DevicesComplete -or $unused.AppxComplete){throw 'Unrequested inventories must not run'}
$unknown=Invoke-FreshkitAuditInventory -Devices -DeviceReader { @([pscustomobject]@{Name='Unknown presence';Present=$null;ConfigManagerErrorCode=0}) }
if($unknown.DevicesComplete){throw 'Unknown device presence cannot establish a healthy present-device inventory'}
Write-Host 'PASS synthetic audit inventory, absent devices, failures and selection isolation (no host scan)'
