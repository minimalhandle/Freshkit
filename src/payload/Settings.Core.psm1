#requires -Version 5.1
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression
function Get-SettingsRoots {
    @{APPDATA=[Environment]::GetFolderPath('ApplicationData');LOCALAPPDATA=[Environment]::GetFolderPath('LocalApplicationData');USERPROFILE=[Environment]::GetFolderPath('UserProfile');DOCUMENTS=[Environment]::GetFolderPath('MyDocuments')}
}
function Assert-SettingsPlainPath {
    param([string]$Path)
    $node=[IO.Path]::GetFullPath($Path)
    while($node){
        if(Test-Path -LiteralPath $node){if((Get-Item -LiteralPath $node -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Reparse point is not allowed: $node"}}
        $parent=[IO.Path]::GetDirectoryName($node);if($parent -eq $node){break};$node=$parent
    }
}
function Join-SettingsPath {
    param([string]$Root,[string]$Relative)
    if(-not $Root -or -not $Relative -or [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':') -or $Relative -match '(^|[\\/])\.\.?([\\/]|$)' -or $Relative -match '[\x00-\x1f]'){throw 'Invalid relative settings path.'}
    $base=[IO.Path]::GetFullPath($Root).TrimEnd('\','/')+'\'
    $path=[IO.Path]::GetFullPath((Join-Path $base $Relative))
    if(-not $path.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)){throw 'Settings path escapes its root.'}
    Assert-SettingsPlainPath $path
    return $path
}
function Assert-SettingsRegistryPath {
    param([string]$Path,[switch]$Relative)
    if($Path -match '[\x00-\x1f/:]' -or $Path -match '(^|\\)\.\.?($|\\)' -or $Path.StartsWith('\')){throw 'Invalid settings registry path.'}
    if(-not $Relative -and ($Path -notmatch '^Software\\[^\\]+' -or $Path -match '^Software\\(?:Classes|Policies)(?:\\|$)|^Software\\Microsoft\\Windows(?:\\|$)')){throw 'Registry rules must target app-specific HKCU Software keys.'}
}
function Get-SettingsProfiles {
    param([string]$Path,[string[]]$Ids=@())
    $data=Get-Content -LiteralPath $Path -Raw|ConvertFrom-Json
    if($data.schemaVersion -ne 1){throw 'Unsupported settings profile schema.'}
    $seen=@{}
    foreach($profile in $data.profiles){
        if($profile.id -notmatch '\A[a-z0-9]+(?:[.-][a-z0-9]+)*\z' -or $seen.ContainsKey($profile.id)){throw 'Invalid/duplicate settings profile ID.'}
        $seen[$profile.id]=$true
        $rules=@{}
        foreach($rule in $profile.rules){
            if($rule.root -notin @('APPDATA','LOCALAPPDATA','USERPROFILE','DOCUMENTS','HKCU')){throw 'Unsupported settings root.'}
            if($rule.root -eq 'HKCU'){Assert-SettingsRegistryPath $rule.path}else{$null=Join-SettingsPath 'C:\SettingsRuleValidation' $rule.path}
            $key=$rule.root+'|'+$rule.path
            if($rules.ContainsKey($key)){throw 'Duplicate settings rule.'};$rules[$key]=$true
        }
        if(-not $profile.rules.Count){throw 'Settings profile has no rules.'}
    }
    foreach($id in $Ids){if(-not $seen.ContainsKey($id)){throw "No settings profile: $id"}}
    foreach($profile in $data.profiles){if(-not $Ids.Count -or $profile.id -in $Ids){$profile}}
}
function Test-SettingsExcluded {
    param($Profile,[string]$Relative)
    $name=[IO.Path]::GetFileName($Relative)
    foreach($pattern in @($Profile.exclude)){
        if($name -like $pattern -or $Relative.Replace('\','/') -like $pattern){return $true}
    }
    return $false
}
function Get-SettingsRunningProfiles {
    param([object[]]$Profiles)
    foreach($profile in $Profiles){foreach($name in @($profile.processes)){
        if($name -and @(Get-Process -Name $name -ErrorAction SilentlyContinue).Count){$profile;break}
    }}
}
function Assert-SettingsAppsClosed {
    param([object[]]$Profiles)
    foreach($profile in $Profiles){foreach($name in @($profile.processes)){
        if($name -and @(Get-Process -Name $name -ErrorAction SilentlyContinue).Count){throw "Close $($profile.name) before backing up or restoring its settings."}
    }}
}
function Get-SettingsInstalledState {
    param([object[]]$Profiles)
    $state=@{}
    $entries=@(foreach($key in @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*')){Get-ItemProperty -Path $key -ErrorAction SilentlyContinue})
    $chocoRoot=[Environment]::GetEnvironmentVariable('ChocolateyInstall','Machine')
    if(-not $chocoRoot){$chocoRoot=Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'chocolatey'}
    foreach($profile in $Profiles){
        $record=[pscustomobject]@{Installed=$false;Version=''}
        foreach($entry in $entries){
            if($entry.PSObject.Properties['DisplayName'] -and [string]$entry.DisplayName -match $profile.versionPattern){
                $record.Installed=$true
                if($entry.PSObject.Properties['DisplayVersion']){$record.Version=[string]$entry.DisplayVersion}
                break
            }
        }
        if(-not $record.Installed -and $profile.packageId -match '\A[a-z0-9]+(?:[.-][a-z0-9]+)*\z'){
            $nuspec=Join-Path $chocoRoot ('lib/'+$profile.packageId+'/'+$profile.packageId+'.nuspec')
            if(Test-Path -LiteralPath $nuspec -PathType Leaf){
                $record.Installed=$true
                try {$xml=[xml]::new();$xml.XmlResolver=$null;$xml.LoadXml([IO.File]::ReadAllText($nuspec));$record.Version=$xml.SelectSingleNode('//*[local-name()="metadata"]/*[local-name()="version"]').InnerText}catch{}
            }
        }
        $state[$profile.id]=$record
    }
    return $state
}
function Get-SettingsRegistrySnapshot {
    param([string]$Path)
    Assert-SettingsRegistryPath $Path
    $pending=[Collections.Generic.Queue[string]]::new();$pending.Enqueue('')
    while($pending.Count){
        $relative=$pending.Dequeue();$full=$Path;if($relative){$full+='\'+$relative}
        $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($full)
        if($null -eq $key){continue}
        try {
            foreach($name in $key.GetValueNames()){
                $kind=$key.GetValueKind($name).ToString()
                $value=$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                if($kind -in @('Binary','None')){$value=[Convert]::ToBase64String([byte[]]$value)}
                elseif($kind -eq 'QWord'){$value=([long]$value).ToString([Globalization.CultureInfo]::InvariantCulture)}
                [pscustomobject]@{Key=$relative;Name=$name;Kind=$kind;Value=$value}
            }
            foreach($child in $key.GetSubKeyNames()){$next=$child;if($relative){$next=$relative+'\'+$child};$pending.Enqueue($next)}
        } finally {$key.Dispose()}
    }
}
function Get-SettingsHash {
    param([IO.Stream]$Stream,[long]$ExpectedLength=-1)
    $sha=[Security.Cryptography.SHA256]::Create()
    try{
        $buffer=New-Object byte[] 65536;$total=[long]0
        while(($count=$Stream.Read($buffer,0,$buffer.Length)) -gt 0){
            $total+=$count;if($total -gt 268435456 -or ($ExpectedLength -ge 0 -and $total -gt $ExpectedLength)){throw 'Expanded settings file exceeds its size limit or declared length.'}
            $null=$sha.TransformBlock($buffer,0,$count,$buffer,0)
        }
        $null=$sha.TransformFinalBlock($buffer,0,0)
        if($ExpectedLength -ge 0 -and $total -ne $ExpectedLength){throw 'Settings file length does not match its manifest.'}
        return [BitConverter]::ToString($sha.Hash).Replace('-','')
    }finally{$sha.Dispose()}
}
function New-SettingsBackup {
    [CmdletBinding(SupportsShouldProcess=$true)]
    param([object[]]$Profiles,[string]$ArchivePath,[hashtable]$Roots=(Get-SettingsRoots),[hashtable]$InstalledState=$null,[switch]$AllowEmpty)
    $path=[IO.Path]::GetFullPath($ArchivePath)
    if([IO.Path]::GetExtension($path) -ne '.zip'){throw 'Settings backup must be a .zip file.'}
    Assert-SettingsPlainPath $path
    if(Test-Path -LiteralPath $path){throw 'Backup destination already exists; choose a new filename.'}
    if(-not(Test-Path -LiteralPath (Split-Path -Parent $path) -PathType Container)){throw 'Backup destination folder does not exist.'}
    # Freshkit: skip apps that are still running instead of failing the whole backup.
    $running=@(Get-SettingsRunningProfiles $Profiles)
    if($running.Count){
        Write-Warning ('Skipped because still running (close them, then back up again): '+(($running|ForEach-Object {$_.name}) -join ', '))
        $Profiles=@($Profiles|Where-Object {$_.id -notin @($running.id)})
        if(-not $Profiles.Count){throw 'Every selected app is still running. Close them and try again.'}
    }
    if($null -eq $InstalledState){$InstalledState=Get-SettingsInstalledState $Profiles}
    if(-not $PSCmdlet.ShouldProcess($path,'Back up selected application settings')){return}
    $fileRows=[Collections.Generic.List[object]]::new();$registryRows=[Collections.Generic.List[object]]::new();$apps=[Collections.Generic.List[object]]::new()
    $total=[long]0;$created=$false
    try {
        $output=[IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$created=$true
        try {
            $zip=[IO.Compression.ZipArchive]::new($output,[IO.Compression.ZipArchiveMode]::Create,$true)
            try {
                foreach($profile in $Profiles){
                    $countBefore=$fileRows.Count+$registryRows.Count
                    foreach($rule in $profile.rules){
                        if($rule.root -eq 'HKCU'){
                            foreach($value in @(Get-SettingsRegistrySnapshot $rule.path)){
                                if($value.Name -match '(?i)password|token|secret|credential'){continue}
                                $registryRows.Add([pscustomobject]@{AppId=$profile.id;Root='HKCU';RulePath=$rule.path;Key=$value.Key;Name=$value.Name;Kind=$value.Kind;Value=$value.Value})
                            }
                            continue
                        }
                        if(-not $Roots.ContainsKey($rule.root)){throw 'Missing settings root mapping.'}
                        $source=Join-SettingsPath $Roots[$rule.root] $rule.path
                        if(-not(Test-Path -LiteralPath $source)){continue}
                        $item=Get-Item -LiteralPath $source -Force
                        $directory=$item.PSIsContainer
                        $files=[Collections.Generic.List[object]]::new()
                        if($directory){
                            $pending=[Collections.Generic.Stack[string]]::new();$pending.Push($source)
                            while($pending.Count){foreach($child in @(Get-ChildItem -LiteralPath $pending.Pop() -Force)){
                                Assert-SettingsPlainPath $child.FullName
                                $relative=$child.FullName.Substring($source.TrimEnd('\').Length+1)
                                if(Test-SettingsExcluded $profile $relative){continue}
                                if($child.PSIsContainer){$pending.Push($child.FullName)}else{$files.Add($child)}
                            }}
                        }else{$files.Add($item)}
                        foreach($file in $files){
                            $relative='';if($directory){$relative=$file.FullName.Substring($source.TrimEnd('\').Length+1)}
                            if(Test-SettingsExcluded $profile $file.Name){continue}
                            if($fileRows.Count -ge 49999){throw 'Too many settings files.'}
                            $inputStream=[IO.File]::Open($file.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
                            try {
                                $total+=$inputStream.Length
                                if($inputStream.Length -gt 268435456 -or $total -gt 2147483648){throw 'Settings backup exceeds configured size limits.'}
                                $hash=Get-SettingsHash $inputStream $inputStream.Length;$inputStream.Position=0
                                $zipPath='files/'+$fileRows.Count.ToString('D6')+'.bin'
                                $entry=$zip.CreateEntry($zipPath,[IO.Compression.CompressionLevel]::Optimal)
                                $stream=$entry.Open();try{$inputStream.CopyTo($stream)}finally{$stream.Dispose()}
                                $fileRows.Add([pscustomobject]@{AppId=$profile.id;Root=$rule.root;RulePath=$rule.path;Directory=$directory;Relative=$relative;Entry=$zipPath;Length=$inputStream.Length;SHA256=$hash})
                            }finally{$inputStream.Dispose()}
                        }
                    }
                    $version='';if($InstalledState.ContainsKey($profile.id)){$version=$InstalledState[$profile.id].Version}
                    $apps.Add([pscustomobject]@{Id=$profile.id;Name=$profile.name;Version=$version;Items=($fileRows.Count+$registryRows.Count-$countBefore)})
                }
                if(-not $AllowEmpty -and -not($fileRows.Count+$registryRows.Count)){throw 'No matching settings were found. No backup was kept.'}
                $manifest=[pscustomobject]@{SchemaVersion=1;CreatedUtc=[datetime]::UtcNow.ToString('o');Apps=@($apps.ToArray());Files=@($fileRows.ToArray());Registry=@($registryRows.ToArray())}
                $json=$manifest|ConvertTo-Json -Depth 12
                if([Text.Encoding]::UTF8.GetByteCount($json) -gt 10485760){throw 'Settings manifest exceeds its size limit.'}
                $entry=$zip.CreateEntry('manifest.json');$writer=[IO.StreamWriter]::new($entry.Open(),[Text.UTF8Encoding]::new($false))
                try{$writer.Write($json)}finally{$writer.Dispose()}
            }finally{$zip.Dispose()}
        }finally{$output.Dispose()}
        return [pscustomobject]@{Archive=$path;FileCount=$fileRows.Count;RegistryValues=$registryRows.Count;Apps=@($apps.ToArray())}
    }catch{if($created -and (Test-Path -LiteralPath $path)){Remove-Item -LiteralPath $path -Force};throw}
}
function Read-SettingsArchive {
    param([string]$ArchivePath,[object[]]$Profiles)
    Assert-SettingsPlainPath $ArchivePath
    $inputStream=[IO.File]::OpenRead([IO.Path]::GetFullPath($ArchivePath))
    try {
        $zip=[IO.Compression.ZipArchive]::new($inputStream,[IO.Compression.ZipArchiveMode]::Read,$true)
        try {
            if($zip.Entries.Count -gt 50000){throw 'Too many archive entries.'}
            $entries=@{};$total=[long]0
            foreach($entry in $zip.Entries){
                if($entries.ContainsKey($entry.FullName) -or $entry.FullName -notmatch '\A(?:manifest\.json|files/[0-9]{6}\.bin)\z'){throw 'Unexpected or duplicate archive entry.'}
                $total+=$entry.Length
                if($entry.Length -gt 268435456 -or $total -gt 2147483648){throw 'Archive exceeds configured size limits.'}
                $entries[$entry.FullName]=$entry
            }
            if(-not $entries.ContainsKey('manifest.json') -or $entries['manifest.json'].Length -gt 10485760){throw 'Missing/oversized settings manifest.'}
            $reader=[IO.StreamReader]::new($entries['manifest.json'].Open())
            try{
                $builder=[Text.StringBuilder]::new();$buffer=New-Object char[] 8192
                while(($count=$reader.Read($buffer,0,$buffer.Length)) -gt 0){if($builder.Length+$count -gt 10485760){throw 'Expanded manifest exceeds its limit.'};[void]$builder.Append($buffer,0,$count)}
                $manifest=$builder.ToString()|ConvertFrom-Json
            }finally{$reader.Dispose()}
            if($manifest.SchemaVersion -ne 1){throw 'Unsupported settings archive schema.'}
            $profilesById=@{};foreach($profile in $Profiles){$profilesById[$profile.id]=$profile}
            $apps=@{}
            foreach($app in $manifest.Apps){if($apps.ContainsKey($app.Id)){throw 'Duplicate app in settings manifest.'};$apps[$app.Id]=$app}
            $seen=@{};$targets=@{}
            foreach($file in $manifest.Files){
                if(-not $apps.ContainsKey($file.AppId) -or -not $profilesById.ContainsKey($file.AppId)){throw 'Archive references an unknown settings profile.'}
                $rules=@($profilesById[$file.AppId].rules|Where-Object {$_.root -eq $file.Root -and $_.path -eq $file.RulePath -and $_.root -ne 'HKCU'})
                if($rules.Count -ne 1 -or $file.Directory -isnot [bool]){throw 'Archive targets a settings rule that is not allowed.'}
                if($file.Directory){$null=Join-SettingsPath 'C:\SettingsArchiveValidation' $file.Relative}elseif($file.Relative){throw 'File rule has an unexpected relative path.'}
                $target=$file.AppId+'|'+$file.Root+'|'+$file.RulePath+'|'+$file.Relative
                if($targets.ContainsKey($target)){throw 'Duplicate settings destination.'};$targets[$target]=$true
                if(-not $entries.ContainsKey($file.Entry) -or $seen.ContainsKey($file.Entry) -or $file.SHA256 -notmatch '\A[A-Fa-f0-9]{64}\z' -or $file.Length -ne $entries[$file.Entry].Length){throw 'Invalid settings file manifest.'}
                $relative=$file.Relative;if(-not $relative){$relative=[IO.Path]::GetFileName($file.RulePath)}
                if(Test-SettingsExcluded $profilesById[$file.AppId] $relative){throw 'Archive contains an excluded settings file.'}
                $stream=$entries[$file.Entry].Open();try{$hash=Get-SettingsHash $stream $file.Length}finally{$stream.Dispose()}
                if($hash -ne $file.SHA256){throw 'Settings archive checksum mismatch.'};$seen[$file.Entry]=$true
            }
            if($entries.Count -ne $seen.Count+1){throw 'Unexpected unlisted archive files.'}
            $registrySeen=@{}
            foreach($value in $manifest.Registry){
                if(-not $apps.ContainsKey($value.AppId) -or -not $profilesById.ContainsKey($value.AppId)){throw 'Unknown registry settings profile.'}
                $rules=@($profilesById[$value.AppId].rules|Where-Object {$_.root -eq 'HKCU' -and $_.path -eq $value.RulePath})
                if($rules.Count -ne 1 -or $value.Root -ne 'HKCU' -or $value.Kind -notin @('String','ExpandString','MultiString','DWord','QWord','Binary','None')){throw 'Registry settings rule/type is not allowed.'}
                Assert-SettingsRegistryPath $value.Key -Relative
                if($value.Name -match '(?i)password|token|secret|credential'){throw 'Archive contains an excluded credential registry value.'}
                $key=$value.AppId+'|'+$value.RulePath+'|'+$value.Key+'|'+$value.Name
                if($registrySeen.ContainsKey($key)){throw 'Duplicate registry destination.'};$registrySeen[$key]=$true
                $null=Convert-SettingsRegistryValue $value
            }
            foreach($id in $apps.Keys){if(-not $profilesById.ContainsKey($id)){throw 'Archive includes an unknown app profile.'}}
            return $manifest
        }finally{$zip.Dispose()}
    }finally{$inputStream.Dispose()}
}
function Convert-SettingsRegistryValue {
    param($Record)
    switch($Record.Kind){
        'Binary' {return ,([Convert]::FromBase64String([string]$Record.Value))}
        'None' {return ,([Convert]::FromBase64String([string]$Record.Value))}
        'DWord' {return [int]$Record.Value}
        'QWord' {return [long]::Parse([string]$Record.Value,[Globalization.CultureInfo]::InvariantCulture)}
        'MultiString' {return ,([string[]]@($Record.Value))}
        default {return [string]$Record.Value}
    }
}
function Restore-SettingsBackup {
    [CmdletBinding(SupportsShouldProcess=$true)]
    param([object[]]$Profiles,[string]$ArchivePath,[string]$SafetyArchivePath,[hashtable]$Roots=(Get-SettingsRoots),[hashtable]$InstalledState=$null,[switch]$AllowVersionMismatch)
    # Validate every archive item, including unselected apps, before any writes.
    $allProfiles=@(Get-SettingsProfiles (Join-Path $PSScriptRoot 'Settings.Profiles.json'))
    foreach($profile in $Profiles){if($profile.id -notin @($allProfiles.id)){$allProfiles+=@($profile)}}
    $manifest=Read-SettingsArchive $ArchivePath $allProfiles
    if($null -eq $InstalledState){$InstalledState=Get-SettingsInstalledState $Profiles}
    $ready=[Collections.Generic.List[object]]::new();$rows=[Collections.Generic.List[object]]::new();$destinations=@{}
    foreach($profile in $Profiles){
        $row=[pscustomobject]@{Id=$profile.id;Status='';Files=0;RegistryValues=0;Detail=''}
        $source=@($manifest.Apps|Where-Object Id -eq $profile.id)
        if(-not $source.Count -or $source[0].Items -eq 0){$row.Status='NoSavedSettings';$rows.Add($row);continue}
        if(-not $InstalledState.ContainsKey($profile.id) -or -not $InstalledState[$profile.id].Installed){$row.Status='SkippedNotInstalled';$row.Detail='Install this app before restoring its settings.';$rows.Add($row);continue}
        $first=[version]'0.0';$second=[version]'0.0'
        if([version]::TryParse([string]$source[0].Version,[ref]$first) -and [version]::TryParse([string]$InstalledState[$profile.id].Version,[ref]$second) -and $first.Major -ne $second.Major -and -not $AllowVersionMismatch){$row.Status='VersionMismatch';$row.Detail='Different major app versions; review compatibility and explicitly allow it to proceed.';$rows.Add($row);continue}
        foreach($file in @($manifest.Files|Where-Object AppId -eq $profile.id)){
            $target=Join-SettingsPath $Roots[$file.Root] $file.RulePath
            if($file.Directory){$target=Join-SettingsPath $target $file.Relative}
            if((Test-Path -LiteralPath $target) -and -not(Test-Path -LiteralPath $target -PathType Leaf)){throw 'Settings file destination is a directory.'}
            if($destinations.ContainsKey($target)){throw 'Overlapping settings rules target the same file.'};$destinations[$target]=$true
        }
        $row.Status='Ready';$row.Detail='Paths in file contents and hardware-dependent settings may require manual adjustment.'
        if(-not $source[0].Version -or -not $InstalledState[$profile.id].Version){$row.Detail+=' App versions could not be fully compared.'}
        $rows.Add($row);$ready.Add($profile)
    }
    # Freshkit: an app that's still running is skipped (its files may be in use), the rest are restored.
    foreach($profile in @(Get-SettingsRunningProfiles @($ready.ToArray()))){
        [void]$ready.Remove($profile)
        foreach($row in $rows){if($row.Id -eq $profile.id){$row.Status='AppRunning';$row.Detail=('Close '+$profile.name+' completely (including its tray icon), then restore again.')}}
    }
    if(-not $ready.Count){return [pscustomobject]@{SafetyArchive='';Results=@($rows.ToArray())}}
    Assert-SettingsAppsClosed @($ready.ToArray())
    if(-not $PSCmdlet.ShouldProcess(($ready.name -join ', '),'Save existing settings to a safety ZIP, then restore selected settings')){
        foreach($row in $rows){if($row.Status -eq 'Ready'){$row.Status='Preview'}}
        return [pscustomobject]@{SafetyArchive='';Results=@($rows.ToArray())}
    }
    if(-not $SafetyArchivePath){$SafetyArchivePath=Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($ArchivePath))) ('pre-restore-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N')+'.zip')}
    $safety=New-SettingsBackup @($ready.ToArray()) $SafetyArchivePath -Roots $Roots -InstalledState $InstalledState -AllowEmpty -Confirm:$false
    # Reopen under FileShare.Read to prevent mutation between verification and use.
    $inputStream=[IO.File]::Open([IO.Path]::GetFullPath($ArchivePath),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try {
        $zip=[IO.Compression.ZipArchive]::new($inputStream,[IO.Compression.ZipArchiveMode]::Read,$true)
        try {
            foreach($profile in $ready){
                $row=@($rows|Where-Object Id -eq $profile.id)[0]
                try {
                    foreach($file in @($manifest.Files|Where-Object AppId -eq $profile.id)){
                        $target=Join-SettingsPath $Roots[$file.Root] $file.RulePath;if($file.Directory){$target=Join-SettingsPath $target $file.Relative}
                        $entry=$zip.GetEntry($file.Entry);if($null -eq $entry -or $entry.Length -ne $file.Length){throw 'Archive changed after validation.'}
                        $stream=$entry.Open();try{$hash=Get-SettingsHash $stream $file.Length}finally{$stream.Dispose()}
                        if($hash -ne $file.SHA256){throw 'Archive changed after validation.'}
                        [IO.Directory]::CreateDirectory((Split-Path -Parent $target))|Out-Null
                        $temporary=Join-Path (Split-Path -Parent $target) ('.settings-'+[guid]::NewGuid().ToString('N')+'.partial')
                        try {
                            $stream=$entry.Open();$output=[IO.File]::Open($temporary,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
                            try{$stream.CopyTo($output)}finally{$stream.Dispose();$output.Dispose()}
                            Assert-SettingsPlainPath $target
                            Move-Item -LiteralPath $temporary -Destination $target -Force
                        }finally{if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary -Force}}
                        $row.Files++
                    }
                    foreach($value in @($manifest.Registry|Where-Object AppId -eq $profile.id)){
                        $keyPath=$value.RulePath;if($value.Key){$keyPath+='\'+$value.Key}
                        $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($keyPath)
                        try{$key.SetValue([string]$value.Name,(Convert-SettingsRegistryValue $value),([Microsoft.Win32.RegistryValueKind]([string]$value.Kind)))}finally{$key.Dispose()}
                        $row.RegistryValues++
                    }
                    $row.Status='Restored'
                }catch{$row.Status='Failed';$row.Detail=$_.Exception.Message+' Existing settings are saved in '+$safety.Archive}
            }
        }finally{$zip.Dispose()}
    }finally{$inputStream.Dispose()}
    return [pscustomobject]@{SafetyArchive=$safety.Archive;Results=@($rows.ToArray())}
}
Export-ModuleMember -Function Get-SettingsRoots,Get-SettingsProfiles,Get-SettingsInstalledState,New-SettingsBackup,Read-SettingsArchive,Restore-SettingsBackup
