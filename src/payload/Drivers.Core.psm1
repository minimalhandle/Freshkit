#requires -Version 5.1
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
function Get-DriverDeviceInventory {
    try {
        $devices=[Collections.Generic.List[object]]::new()
        foreach($device in @(Get-CimInstance -ClassName Win32_PnPEntity -Filter "PNPClass='Display' OR PNPClass='System'" -ErrorAction Stop)){
            if($device.PSObject.Properties['Present'] -and $device.Present -eq $false){continue}
            if($device.PSObject.Properties['ConfigManagerErrorCode'] -and $device.ConfigManagerErrorCode -eq 45){continue}
            $ids=@($device.HardwareID|Where-Object {$_})
            if($device.PNPDeviceID){$ids+=@([string]$device.PNPDeviceID)}
            $devices.Add([pscustomobject]@{Name=[string]$device.Name;Class=[string]$device.PNPClass;HardwareIds=@($ids|Select-Object -Unique)})
        }
        $processors=@(Get-CimInstance -ClassName Win32_Processor -ErrorAction Stop|ForEach-Object {$_.Name})
        return [pscustomobject]@{Complete=($devices.Count -gt 0);Devices=@($devices.ToArray());Processors=$processors;OsBuild=[Environment]::OSVersion.Version.Build;Error=''}
    }catch{return [pscustomobject]@{Complete=$false;Devices=@();Processors=@();OsBuild=[Environment]::OSVersion.Version.Build;Error=$_.Exception.Message}}
}
function Get-DriverCandidates {
    param($Inventory,[object[]]$AppPlan=@())
    $definitions=@(
        @{Id='amd-ryzen-chipset';Name='AMD Ryzen Chipset Drivers';Class='System';Vendor='1022'},
        @{Id='nvidia-display-driver';Name='NVIDIA Display Driver + NVIDIA App (without PhysX)';Class='Display';Vendor='10DE'},
        @{Id='amd-software-adrenalin-edition';Name='AMD Software: Adrenalin Edition';Class='Display';Vendor='1002'},
        # Reviewed consumer Arc A/B and Core Ultra IDs. Keep this explicit: the
        # current Arc download no longer supports every Iris Xe/UHD generation.
        # Evidence and update guidance: docs/driver-support.md.
        @{Id='intel-arc-graphics-driver';Name='Intel Arc / Core Ultra Graphics Driver';Class='Display';Vendor='8086';DeviceIds=@(
            '5690','5691','5692','5693','5694','5696','5697',
            '56A0','56A1','56A2','56A5','56A6',
            'E20B','E20C',
            '7D40','7D55','7DD5','7D45','64A0','6420','64B0','7D67','7D51','7DD1','7D41'
        )}
    )
    foreach($definition in $definitions){
        $parameters='';foreach($app in $AppPlan){if($app.packageId -eq $definition.Id){$parameters=$app.packageParameters}}
        if($definition.Id -eq 'nvidia-display-driver' -and $parameters -notmatch '(?i)(?:^|\s)/NVApp(?:\s|$)'){$parameters=($parameters+' /NVApp').Trim()}
        $candidate=[pscustomobject]@{Id=$definition.Id;Name=$definition.Name;Status='HardwareUnknown';Hardware=@();Detail='Hardware inventory is unavailable; no hardware driver will be installed.';CanOffer=$false;PackageParameters=$parameters}
        if($Inventory.Complete){
            $unsupported=[Collections.Generic.List[string]]::new()
            $matchesFound=@(@(foreach($device in $Inventory.Devices){
                if($device.Class -ne $definition.Class){continue}
                $vendorIds=@($device.HardwareIds|Where-Object {$_ -match ('\APCI\\VEN_'+$definition.Vendor+'&DEV_[0-9A-F]{4}(?:&|\z)')})
                if(-not $vendorIds.Count){continue}
                if($definition.ContainsKey('DeviceIds')){
                    $supported=@($vendorIds|Where-Object {
                        $_ -match '\APCI\\VEN_8086&DEV_([0-9A-F]{4})(?:&|\z)' -and $Matches[1] -in $definition.DeviceIds
                    })
                    if(-not $supported.Count){$unsupported.Add([string]$device.Name);continue}
                }
                $device.Name
            })|Select-Object -Unique)
            if($matchesFound.Count){
                $candidate.Status='Candidate';$candidate.CanOffer=$true;$candidate.Hardware=$matchesFound
                $candidate.Detail='Matching vendor/class detected. This is a candidate, not full model/OS/INF compatibility verification; confirm the package supports your device.'
                if($definition.Id -eq 'amd-ryzen-chipset' -and -not @($Inventory.Processors|Where-Object {$_ -match '\bRyzen\b'}).Count){
                    $candidate.Status='NotApplicable';$candidate.CanOffer=$false
                    $candidate.Detail='The Chocolatey chipset package requires a Ryzen processor; inventory did not identify one.'
                }
                elseif($definition.Id -eq 'amd-ryzen-chipset'){$candidate.Detail+=' CPU: '+($Inventory.Processors -join '; ')}
                # The reviewed current Adrenalin package downloads a Win11 build.
                if($definition.Id -eq 'amd-software-adrenalin-edition' -and $Inventory.OsBuild -lt 22000){
                    $candidate.Status='NotApplicable';$candidate.CanOffer=$false
                    $candidate.Detail='The reviewed Adrenalin package targets Windows 11. Select a suitable Windows 10/OEM package separately.'
                }
                if($definition.Id -eq 'intel-arc-graphics-driver'){
                    $candidate.Detail='Reviewed Intel Arc/Core Ultra PCI device detected. The generic Intel driver can replace OEM customizations; model/INF operation is not verified.'
                    if($Inventory.OsBuild -lt 19045){
                        $candidate.Status='NotApplicable';$candidate.CanOffer=$false
                        $candidate.Detail='The reviewed Intel driver requires Windows 10 22H2 (build 19045) or Windows 11. Use a suitable OEM driver on older Windows releases.'
                    }
                    if($unsupported.Count){$candidate.Detail+=' Other Intel adapters are outside the reviewed current branch: '+(($unsupported.ToArray()|Select-Object -Unique) -join '; ')+'. Use the computer manufacturer/OEM or Intel driver selector for those adapters.'}
                }
            }else{
                $candidate.Status='NotApplicable';$candidate.Detail='No matching present PCI device was found in the required device class.'
                if($unsupported.Count){
                    $candidate.Hardware=@($unsupported.ToArray()|Select-Object -Unique)
                    $candidate.Detail='Intel display hardware detected, but its PCI device is outside the reviewed current Arc/Core Ultra driver branch. Use the computer manufacturer/OEM driver or https://www.intel.com/content/www/us/en/support/articles/000090440/graphics.html to select the correct older, Pro or unreviewed device driver.'
                }
            }
        }
        $candidate
    }
}
function Get-DriverPackageState {
    param([string]$Executable,[string]$PackageId)
    $output=@(& $Executable list --exact $PackageId --limit-output)
    if($LASTEXITCODE -ne 0){throw "Unable to read Chocolatey's installed state for $PackageId."}
    $rows=@($output|Where-Object {$_ -match ('\A'+[regex]::Escape($PackageId)+'\|[^|]+\z')})
    if($rows.Count -gt 1){throw 'Ambiguous installed driver package state.'}
    $version='';if($rows.Count){$version=($rows[0] -split '\|')[1]}
    return [pscustomobject]@{Installed=($rows.Count -eq 1);Version=$version}
}
function Invoke-DriverPackageInstall {
    param($App,[string]$Executable)
    $rows=@(Invoke-ChocolateyAppPlan @($App) $Executable -Confirm:$false)
    if($rows.Count -ne 1){throw 'Expected exactly one driver package outcome.'}
    return $rows[0]
}
function Get-DriverRegistryEvidence {
    param([string]$PackageId)
    $patterns=@{'nvidia-display-driver'='^NVIDIA Graphics Driver';'nvidia-app'='^NVIDIA App(?:\s|$)';'amd-ryzen-chipset'='^AMD Chipset (Software|Drivers)';'amd-software-adrenalin-edition'='^AMD Software';'intel-arc-graphics-driver'='^Intel(?:\(R\)|\u00ae)? (?:Arc(?:\(TM\)|\u2122)? (?:& Iris Xe )?)?Graphics Driver(?:\s|$)'}
    if(-not $patterns.ContainsKey($PackageId)){throw 'Unknown driver/component evidence identity.'}
    foreach($root in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*')){
        foreach($item in @(Get-ItemProperty -Path $root -ErrorAction SilentlyContinue)){
            if($item.PSObject.Properties['DisplayName'] -and [string]$item.DisplayName -match $patterns[$PackageId]){
                $version='';if($item.PSObject.Properties['DisplayVersion']){$version=[string]$item.DisplayVersion}
                [pscustomobject]@{Name=$item.DisplayName;Version=$version;Key=$item.PSPath}
            }
        }
    }
}
function Get-NonDriverAppPlan {
    param([object[]]$Plan)
    $driverIds=@('nvidia-display-driver','amd-ryzen-chipset','amd-software-adrenalin-edition','intel-arc-graphics-driver','intel-graphics-driver')
    foreach($app in $Plan){if($app.packageId -notin $driverIds){$app}}
}
function Invoke-DriverStage {
    [CmdletBinding(SupportsShouldProcess=$true)]
    param([object[]]$Candidates,[string]$Executable,[string[]]$ApprovedIds=@())
    $known=@('nvidia-display-driver','amd-ryzen-chipset','amd-software-adrenalin-edition','intel-arc-graphics-driver')
    foreach($id in $ApprovedIds){if($id -notin @($Candidates.Id)){throw "Approval does not identify a driver candidate: $id"}}
    $results=[Collections.Generic.List[object]]::new();$blocked=$false;$reboot=$false;$failed=$false
    foreach($candidate in $Candidates){
        if($candidate.Id -notin $known){throw 'Unreviewed driver package identity.'}
        $row=[pscustomobject]@{Id=$candidate.Id;Name=$candidate.Name;PackageId=$candidate.Id;Status='';ExitCode=$null;Version='';Detail=$candidate.Detail;Hardware=@($candidate.Hardware);RegistryEvidence=@()}
        try {
            if(-not $candidate.CanOffer){$row.Status=$candidate.Status;$results.Add($row);continue}
            if($blocked){$row.Status=if($reboot){'DeferredForReboot'}else{'DeferredAfterDriverFailure'};$results.Add($row);continue}
            if($WhatIfPreference){$state=[pscustomobject]@{Installed=$false;Version=''}}else{$state=Get-DriverPackageState $Executable $candidate.Id}
            if($state.Installed){$row.Status='AlreadyInstalled';$row.Version=$state.Version;$row.Detail='Chocolatey already tracks this driver package; it was not reinstalled.';$results.Add($row);continue}
            if($candidate.Id -notin $ApprovedIds){$row.Status='Declined';$results.Add($row);continue}
            if(-not $PSCmdlet.ShouldProcess($candidate.Name,'Install the explicitly selected Chocolatey driver package')){$row.Status='Preview';$results.Add($row);continue}
            $parameters='';if($candidate.PSObject.Properties['PackageParameters']){$parameters=$candidate.PackageParameters}
            $app=[pscustomobject]@{id=$candidate.Id;name=$candidate.Name;packageId=$candidate.Id;status='Chocolatey';packageParameters=$parameters;note=$candidate.Detail}
            if($candidate.Id -eq 'nvidia-display-driver'){
                $app.packageParameters=($parameters -replace '(?i)(?:^|\s)/NVApp(?=\s|$)','').Trim()+' /NVApp'
                $app|Add-Member -NotePropertyName installArguments -NotePropertyValue '-deselect:Display.PhysX'
            }
            $outcome=Invoke-DriverPackageInstall $app $Executable
            $row.Status=$outcome.Status;$row.ExitCode=$outcome.ExitCode;$row.Version=$outcome.Version;$row.Detail=$outcome.Detail
            if($row.Status -in @('PackageInstalled','RebootRequired')){
                $row.RegistryEvidence=@(Get-DriverRegistryEvidence $candidate.Id)
                if(-not $row.RegistryEvidence.Count){$row.Status='UnverifiedDriverInstall';$row.Detail='Chocolatey registered the package, but matching machine uninstall evidence was not found. Review the driver installation before continuing.'}
                else{$row.Detail+=' Matching machine registration found; hardware operation is not verified.'}
                if($candidate.Id -eq 'nvidia-display-driver'){
                    $appEvidence=@(Get-DriverRegistryEvidence 'nvidia-app')
                    if(-not $appEvidence.Count){$row.Status='UnverifiedDriverInstall';$row.Detail='NVIDIA App machine registration was not found after installing the driver with /NVApp.'}
                    else{$row.RegistryEvidence+=@($appEvidence);$row.Detail+=' NVIDIA App registration found. PhysX was deselected using NVIDIA installer arguments.'}
                }
            }
            if($outcome.Status -in @('RebootRequired','DeferredForReboot') -or $outcome.ExitCode -in @(3010,1641,350,1604)){$reboot=$true;$blocked=$true}
            if($row.Status -in @('Failed','UnverifiedDriverInstall')){$failed=$true;$blocked=$true}
        }catch{$row.Status='Failed';$row.Detail=$_.Exception.Message;$failed=$true;$blocked=$true}
        $results.Add($row)
    }
    return [pscustomobject]@{CanContinue=(-not $blocked);RebootRequired=$reboot;Failed=$failed;Results=@($results.ToArray())}
}
function Get-DriverInstallSelection {
    param([object[]]$Candidates,[string]$Executable)
    Write-Host "`nDetecting hardware and selecting matching chipset/display driver packages automatically."
    foreach($candidate in $Candidates){
        if(-not $candidate.CanOffer){Write-Host "[$($candidate.Name)] $($candidate.Status): $($candidate.Detail)";continue}
        $state=Get-DriverPackageState $Executable $candidate.Id
        if($state.Installed){
            $candidate.Status='AlreadyInstalled';$candidate.Detail='Already tracked by Chocolatey; not reinstalled.'
            $candidate|Add-Member -NotePropertyName InstalledVersion -NotePropertyValue $state.Version -Force
            Write-Host "[$($candidate.Name)] Already installed ($($state.Version)).";continue
        }
        Write-Host ('Installing '+$candidate.Name+' for: '+($candidate.Hardware -join '; '))
        $candidate.Id
    }
}
Export-ModuleMember -Function Get-DriverDeviceInventory,Get-DriverCandidates,Get-DriverPackageState,Get-DriverRegistryEvidence,Get-NonDriverAppPlan,Invoke-DriverStage,Get-DriverInstallSelection
