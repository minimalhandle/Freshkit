#requires -Version 5.1
param([string]$DriversModule = '')

# Read-only inventory and feed requests. This script never enters a package
# installation, registry-write, elevation or resume-task path.
function Get-FreshkitDriverPackageMetadata {
    param([ValidatePattern('^[a-z0-9][a-z0-9.-]+$')][string]$PackageId,
        [scriptblock]$Transport = {
            param($Uri,$TimeoutMilliseconds)
            $request=[Net.HttpWebRequest]::Create($Uri)
            $request.Timeout=$TimeoutMilliseconds;$request.ReadWriteTimeout=$TimeoutMilliseconds
            $request.AllowAutoRedirect=$false;$request.UserAgent='Freshkit driver preview'
            $response=$null;$reader=$null;$pending=$null
            try {
                $pending=$request.BeginGetResponse($null,$null)
                if(-not $pending.AsyncWaitHandle.WaitOne($TimeoutMilliseconds)){throw 'Package metadata request timed out.'}
                $response=$request.EndGetResponse($pending)
                $reader=[IO.StreamReader]::new($response.GetResponseStream())
                $reader.ReadToEnd()
            } finally {
                $request.Abort()
                if($reader){$reader.Dispose()};if($response){$response.Dispose()}
                if($pending){$pending.AsyncWaitHandle.Close()}
            }
        })
    $unknown=[pscustomobject]@{Version='Unknown';Status='Unknown';Detail='Current Chocolatey package metadata is unavailable.'}
    try {
        [Net.ServicePointManager]::SecurityProtocol=[Net.ServicePointManager]::SecurityProtocol -bor 3072
        $filter=[Uri]::EscapeDataString("Id eq '$PackageId' and IsLatestVersion eq true and IsPrerelease eq false")
        $uri='https://community.chocolatey.org/api/v2/Packages()?$filter='+$filter+'&$select=Id,Version,IsLatestVersion,IsPrerelease&$top=1'
        $text=[string](& $Transport $uri 4000)
        if($text.Length -gt 1048576){return $unknown}
        $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null
        $reader=[Xml.XmlReader]::Create([IO.StringReader]::new($text),$settings)
        try {$xml=[Xml.XmlDocument]::new();$xml.XmlResolver=$null;$xml.Load($reader)}finally{$reader.Dispose()}
        $found=@(foreach($props in $xml.SelectNodes("//*[local-name()='entry']//*[local-name()='properties']")){
            $values=@{};foreach($node in $props.ChildNodes){$values[$node.LocalName]=$node.InnerText}
            if($values['Id'] -ieq $PackageId -and $values['IsLatestVersion'] -eq 'true' -and $values['IsPrerelease'] -eq 'false' -and $values['Version'] -match '^\d+(?:\.\d+){1,3}(?:-[0-9A-Za-z.-]+)?$'){$values['Version']}
        })
        if($found.Count -eq 1){return [pscustomobject]@{Version=[string]$found[0];Status='Available';Detail='Latest stable package reported by community.chocolatey.org. Package and Windows driver versions use different numbering.'}}
    } catch { }
    return $unknown
}

function Get-FreshkitDriverSupportUrl {
    param([string]$Manufacturer,[string]$DeviceId)
    switch -Regex ($Manufacturer) {
        'Dell' {return 'https://www.dell.com/support/home/'}
        'Lenovo' {return 'https://pcsupport.lenovo.com/'}
        'Hewlett|^HP\b' {return 'https://support.hp.com/drivers'}
        'ASUS|ASUSTeK' {return 'https://www.asus.com/support/download-center/'}
        'Acer' {return 'https://www.acer.com/us-en/support/drivers-and-manuals'}
        'Micro-Star|^MSI' {return 'https://www.msi.com/support/download'}
        'Microsoft' {return 'https://support.microsoft.com/surface'}
    }
    if($DeviceId -match '^PCI\\VEN_8086&'){return 'https://www.intel.com/content/www/us/en/support/articles/000090440/graphics.html'}
    if($DeviceId -match '^PCI\\VEN_1002&|^PCI\\VEN_1022&'){return 'https://www.amd.com/en/support/download/drivers.html'}
    if($DeviceId -match '^PCI\\VEN_10DE&'){return 'https://www.nvidia.com/Download/index.aspx'}
    return 'https://support.microsoft.com/windows/update-drivers-manually-in-windows-ec62f46c-ff14-c91d-eead-d7126dc1f7b6'
}

function Read-FreshkitDriverPreviewInventory {
    $inventory=Get-DriverDeviceInventory
    $manufacturer=''
    try {$manufacturer=[string](Get-CimInstance -ClassName Win32_ComputerSystem -ErrorAction Stop).Manufacturer}catch{}
    $inventory|Add-Member -NotePropertyName Manufacturer -NotePropertyValue $manufacturer -Force
    return $inventory
}

function Invoke-FreshkitDriverPreview {
    param([scriptblock]$InventoryReader={Read-FreshkitDriverPreviewInventory},
        [scriptblock]$SignedDriverReader={Get-CimInstance -ClassName Win32_PnPSignedDriver -ErrorAction Stop},
        [scriptblock]$MetadataReader={param($id) Get-FreshkitDriverPackageMetadata $id})
    $errors=[Collections.Generic.List[string]]::new();$rows=[Collections.Generic.List[object]]::new();$signed=@();$inventory=$null
    try {$inventory=& $InventoryReader;if($null -eq $inventory -or -not $inventory.Complete){$errors.Add('Hardware inventory is unavailable or incomplete.')}}catch{$errors.Add('Hardware inventory could not be read.')}
    try {$signed=@(& $SignedDriverReader)}catch{$errors.Add('Installed Windows driver versions could not be read.')}
    $metadata=@{}
    if($null -ne $inventory){
        $manufacturer='';if($inventory.PSObject.Properties['Manufacturer']){$manufacturer=[string]$inventory.Manufacturer}
        foreach($device in @($inventory.Devices)){
            if($device.Class -ne 'Display' -and -not ($device.Class -eq 'System' -and @($device.HardwareIds|Where-Object {$_ -match '^PCI\\VEN_1022&'}).Count)){continue}
            $ids=@($device.HardwareIds|Where-Object {$_})
            $instance=@($ids|Where-Object {($_ -split '\\').Count -ge 3}|Select-Object -First 1)
            $id=if($instance.Count){[string]$instance[0]}elseif($ids.Count){[string]$ids[0]}else{''}
            $versions=@($signed|Where-Object {$_.DeviceID -and $ids -icontains [string]$_.DeviceID}|ForEach-Object {if($_.DriverVersion){[string]$_.DriverVersion}}|Select-Object -Unique)
            $installed=if($versions.Count -eq 1){$versions[0]}else{'Unknown'}
            $one=[pscustomobject]@{Complete=[bool]$inventory.Complete;Devices=@($device);Processors=@($inventory.Processors);OsBuild=$inventory.OsBuild;Error=''}
            $candidate=@(Get-DriverCandidates $one|Where-Object {$_.Hardware.Count -gt 0}|Select-Object -First 1)
            $package='';$name='No reviewed automatic package';$status=if($inventory.Complete){'NotApplicable'}else{'HardwareUnknown'}
            $reason='No reviewed automatic package matches this device. Select its driver through the computer manufacturer support page.'
            if($candidate.Count){
                $status=$candidate[0].Status;$reason=$candidate[0].Detail
                if($candidate[0].CanOffer){$package=$candidate[0].Id;$name=$candidate[0].Name}
            }
            $available='Unknown';$metadataStatus='Unknown';$metadataDetail='No compatible automatic package is proposed.'
            if($candidate.Count -and $candidate[0].CanOffer){
                if(-not $metadata.ContainsKey($package)){
                    try {$entry=& $MetadataReader $package;if($null -eq $entry){throw 'No metadata'};$metadata[$package]=$entry}catch{$metadata[$package]=[pscustomobject]@{Version='Unknown';Status='Unknown';Detail='Current package metadata is unavailable.'}}
                }
                $entry=$metadata[$package]
                if($entry.Status -eq 'Available' -and $entry.Version -and $entry.Version -ne 'Unknown'){$available=[string]$entry.Version;$metadataStatus='Available'}
                $metadataDetail=[string]$entry.Detail
            }
            $rows.Add([pscustomobject]@{Hardware=[string]$device.Name;DeviceId=$id;Kind=$(if($device.Class -eq 'Display'){'Graphics'}else{'Chipset'});PackageId=$package;PackageName=$name;Compatibility=$status;Reason=$reason;InstalledDriverVersion=$installed;AvailablePackageVersion=$available;MetadataStatus=$metadataStatus;MetadataDetail=$metadataDetail;SupportUrl=(Get-FreshkitDriverSupportUrl $manufacturer $id)})
        }
    }
    [pscustomobject]@{SchemaVersion=1;InventoryComplete=($null -ne $inventory -and [bool]$inventory.Complete);Error=($errors.ToArray() -join ' ');ScannedUtc=[datetime]::UtcNow.ToString('o');Rows=@($rows.ToArray())}
}

if($MyInvocation.InvocationName -ne '.'){
    $ErrorActionPreference='Stop'
    [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
    if(-not $DriversModule){$DriversModule=Join-Path $PSScriptRoot 'Drivers.Core.psm1'}
    Import-Module -Name $DriversModule -Force -DisableNameChecking
    Invoke-FreshkitDriverPreview|ConvertTo-Json -Depth 7 -Compress
}
