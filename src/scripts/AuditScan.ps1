#requires -Version 5.1
param([string]$DebloatRoot='', [switch]$Windows, [switch]$Devices)

# Read-only inventories only. Never load Win11Debloat's mutation entry point.
function Invoke-FreshkitAuditInventory {
    param([switch]$Windows,[switch]$Devices,
        [scriptblock]$TweakReader = {
            . (Join-Path $PSScriptRoot 'TweakState.ps1') -DebloatRoot $DebloatRoot
            Invoke-FreshkitTweakState -Root $DebloatRoot
        },
        [scriptblock]$DeviceReader = { Get-CimInstance -ClassName Win32_PnPEntity -ErrorAction Stop })
    $features=@{};$appx=@();$appxComplete=$false;$devicesComplete=$false
    $problems=New-Object 'System.Collections.Generic.List[object]'
    $errors=New-Object 'System.Collections.Generic.List[string]'
    if($Windows){
        try {
            $state=& $TweakReader
            if($null -eq $state.Features){throw 'Missing feature inventory'}
            $features=$state.Features;$appx=@($state.Appx);$appxComplete=[bool]$state.AppxComplete
            foreach($message in @($state.Errors)){if($message){$errors.Add([string]$message)}}
        } catch {$errors.Add('Windows settings or current-user apps could not be inspected.')}
    }
    if($Devices){
        try {
            $inventory=@(& $DeviceReader)
            if(-not $inventory.Count){throw 'No device inventory returned'}
            $devicesComplete=$true
            foreach($device in $inventory){
                if($device.Present -eq $false){continue}
                if($null -eq $device.Present){$devicesComplete=$false;continue}
                if($null -eq $device.ConfigManagerErrorCode){$devicesComplete=$false;continue}
                if([int]$device.ConfigManagerErrorCode -gt 0){$problems.Add([pscustomobject]@{Name=[string]$device.Name;Id=[string]$device.PNPDeviceID;Code=[int]$device.ConfigManagerErrorCode})}
            }
        } catch {$devicesComplete=$false;$errors.Add('Present-device problem codes could not be inspected.')}
    }
    [pscustomobject]@{SchemaVersion=1;Features=$features;Appx=$appx;AppxComplete=$appxComplete;Devices=@($problems.ToArray());DevicesComplete=$devicesComplete;Errors=@($errors.ToArray())}
}

if($MyInvocation.InvocationName -ne '.'){
    $ErrorActionPreference='Stop'
    [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
    Invoke-FreshkitAuditInventory -Windows:$Windows -Devices:$Devices|ConvertTo-Json -Depth 7 -Compress
}
