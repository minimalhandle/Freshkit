#requires -Version 5.1
# Freshkit runner: the PowerShell section of the original InstallApps.bat, driven by parameters.
# Freshkit.exe extracts the engine to -EngineDir (a FreshWindowsSetup-<guid> temp folder).
param(
    [string]$Action='Install',
    [string]$ListPath='',
    [string]$Apps='',              # comma-separated selection: choco IDs and winget:<Id>
    [string]$ArchivePath='',       # settings archive for Backup/Restore/Inspect
    [string]$Profiles='',          # ALL or comma-separated settings profile IDs
    [string]$RunId='',             # Resume/CancelResume target
    [string]$DriverIds='',         # specific failed driver packages to retry
    [string]$SelfPath='',          # path of InstallApps.exe (default backup folder)
    [switch]$IncludeDisabled,      # treat ::DISABLED lines as selectable packages
    [switch]$Upgrade,              # use 'choco upgrade' so installed apps move to latest
    [switch]$AllowVersionMismatch,
    [switch]$Capture,              # GUI mode: render output as plain text lines
    [string]$EngineDir='',         # folder holding the engine payload
    [switch]$Ui,                   # Freshkit in-app install (no extra console windows)
    [string]$LogDir='',            # transcript folder for Freshkit's live log
    [switch]$SkipDrivers,          # install apps without the driver stage
    [string]$Time='',              # ScheduleBackup: daily time (HH:mm)
    [int]$Keep=0                   # ScheduleBackup: automatic backups to keep
)
$ErrorActionPreference='Stop'
if($Capture){try{[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)}catch{}}
$valid=@('Home','Menu','Plan','List','Install','Bootstrap','Drivers','DriverPlan','Resume','CancelResume','Backup','Restore','Inspect','SettingsList','BackupPlan','RestorePlan','ScheduleBackup','UnscheduleBackup')
$match=@($valid|Where-Object {$_ -ieq $Action})
if(-not $match.Count){throw ('Unknown action "'+$Action+'". Use one of: '+($valid -join ', ')+'.')}
$action=$match[0]
$runRoot=if($EngineDir){$EngineDir}else{$PSScriptRoot}
if($SelfPath){$env:APP_INSTALLER_SELF=$SelfPath}
if(-not $ListPath){throw 'No package list supplied.'}
$exitCode=1
try {
    $source=[IO.File]::ReadAllText($ListPath)
    # ---- Packages (same rules as the BAT) ----
    $sections=[regex]::Matches($source,'(?ms)^::BEGIN_PACKAGES\r?\n(?<body>.*?)^::END_PACKAGES\r?$')
    if($sections.Count -ne 1){throw 'Expected one editable package section.'}
    $catalogPath=Join-Path $runRoot 'Install-Chocolatey.apps.json'
    $catalog=Get-Content -LiteralPath $catalogPath -Raw|ConvertFrom-Json
    $byPackage=@{}
    foreach($app in $catalog.apps){if($app.packageId){$byPackage[$app.packageId]=$app}}
    $seenPackages=@{}
    $linePattern=if($IncludeDisabled){'^::(?:PACKAGE|DISABLED)\s+(?<entry>.+)$'}else{'^::PACKAGE\s+(?<entry>.+)$'}
    $configured=@(foreach($line in ($sections[0].Groups['body'].Value -split '\r?\n')){
        if($line -notmatch $linePattern){continue}
        $parts=$Matches.entry -split '\|'
        if($parts.Count -gt 3){throw 'Use package-id|Display name|Optional parameters on each PACKAGE line.'}
        $id=$parts[0].Trim()
        if($id -notmatch '\A[a-zA-Z0-9]+(?:[.-][a-zA-Z0-9]+)*\z'){throw "Invalid Chocolatey package ID: $id"}
        if($seenPackages.ContainsKey($id)){throw "Duplicate Chocolatey package ID: $id"}
        $seenPackages[$id]=$true
        $name=$id
        if($parts.Count -gt 1 -and $parts[1].Trim()){$name=$parts[1].Trim()}
        $parameters=''
        if($parts.Count -gt 2){$parameters=$parts[2].Trim()}
        $appId=$id.ToLowerInvariant()
        if($byPackage.ContainsKey($id)){$appId=$byPackage[$id].id}
        $entry=[pscustomobject]@{id=$appId;name=$name;packageId=$id;status='Chocolatey';packageParameters=$parameters;phase=2;note='Selected in the editable standalone package list.'}
        if($Upgrade){$entry|Add-Member -NotePropertyName upgrade -NotePropertyValue $true}
        $entry
    })
    # ---- winget packages (Freshkit) ----
    $wingetSections=[regex]::Matches($source,'(?ms)^::BEGIN_WINGET\r?\n(?<body>.*?)^::END_WINGET\r?$')
    if($wingetSections.Count -gt 1){throw 'Expected at most one winget section.'}
    if($wingetSections.Count -eq 1){
        $wingetPattern=if($IncludeDisabled){'^::(?:WINGET|DISABLED)\s+(?<entry>.+)$'}else{'^::WINGET\s+(?<entry>.+)$'}
        $configured+=@(foreach($line in ($wingetSections[0].Groups['body'].Value -split '\r?\n')){
            if($line -notmatch $wingetPattern){continue}
            $parts=$Matches.entry -split '\|'
            if($parts.Count -gt 3){throw 'Use winget-id|Display name|Optional override arguments on each WINGET line.'}
            $id=$parts[0].Trim()
            if($id -notmatch '\A[A-Za-z0-9][A-Za-z0-9.+_-]*\z'){throw "Invalid winget package ID: $id"}
            $key='winget-'+(($id.ToLowerInvariant() -replace '[^a-z0-9]+','-').Trim('-'))
            if($seenPackages.ContainsKey($key)){throw "Duplicate winget package ID: $id"}
            $seenPackages[$key]=$true
            $name=$id;if($parts.Count -gt 1 -and $parts[1].Trim()){$name=$parts[1].Trim()}
            $parameters='';if($parts.Count -gt 2){$parameters=$parts[2].Trim()}
            $entry=[pscustomobject]@{id=$key;name=$name;packageId=$key;wingetId=$id;status='Chocolatey';packageParameters=$parameters;phase=2;note='winget package selected in Freshkit.'}
            if($Upgrade){$entry|Add-Member -NotePropertyName upgrade -NotePropertyValue $true}
            $entry
        })
    }
    # ---- GitHub release packages (Freshkit) ----
    $githubSections=[regex]::Matches($source,'(?ms)^::BEGIN_GITHUB\r?\n(?<body>.*?)^::END_GITHUB\r?$')
    if($githubSections.Count -gt 1){throw 'Expected at most one GitHub section.'}
    if($githubSections.Count -eq 1){
        $githubPattern=if($IncludeDisabled){'^::(?:GITHUB|DISABLED)\s+(?<entry>.+)$'}else{'^::GITHUB\s+(?<entry>.+)$'}
        $configured+=@(foreach($line in ($githubSections[0].Groups['body'].Value -split '\r?\n')){
            if($line -notmatch $githubPattern){continue}
            $parts=$Matches.entry -split '\|'
            if($parts.Count -gt 3){throw 'Use owner/repo|Display name|Optional silent installer arguments on each GITHUB line.'}
            $repo=$parts[0].Trim()
            if($repo -notmatch '\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}\z' -or $repo -match '/\.\.?\z'){throw "Invalid GitHub repository: $repo"}
            $key='github-'+(($repo.ToLowerInvariant() -replace '[^a-z0-9]+','-').Trim('-'))
            if($seenPackages.ContainsKey($key)){throw "Duplicate GitHub repository: $repo"}
            $seenPackages[$key]=$true
            $name=$repo;if($parts.Count -gt 1 -and $parts[1].Trim()){$name=$parts[1].Trim()}
            $parameters='';if($parts.Count -gt 2){$parameters=$parts[2].Trim()}
            $entry=[pscustomobject]@{id=$key;name=$name;packageId=$key;githubRepo=$repo;status='Chocolatey';packageParameters=$parameters;phase=2;note='GitHub release selected in Freshkit.'}
            if($Upgrade){$entry|Add-Member -NotePropertyName upgrade -NotePropertyValue $true}
            $entry
        })
    }
    if(-not $configured.Count){throw 'No enabled packages. Add at least one ::PACKAGE line.'}
    $catalog.apps=$configured
    $catalog|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $catalogPath -Encoding UTF8
    # Map selected package IDs to catalogue app IDs.
    $selectedIds=@()
    foreach($pkg in @($Apps -split ','|ForEach-Object {$_.Trim()}|Where-Object {$_})){
        if($pkg -like 'winget:*'){$wid=$pkg.Substring(7);$hit=@($configured|Where-Object {$_.PSObject.Properties['wingetId'] -and $_.wingetId -ieq $wid})}
        elseif($pkg -like 'github:*'){$gid=$pkg.Substring(7);$hit=@($configured|Where-Object {$_.PSObject.Properties['githubRepo'] -and $_.githubRepo -ieq $gid})}
        else{$hit=@($configured|Where-Object {-not $_.PSObject.Properties['wingetId'] -and -not $_.PSObject.Properties['githubRepo'] -and $_.packageId -ieq $pkg})}
        if($hit.Count -ne 1){throw "Selected package is not in the list: $pkg"}
        $selectedIds+=$hit[0].id
    }
    # ---- Settings rules (same rules as the BAT) ----
    $configSections=[regex]::Matches($source,'(?ms)^::BEGIN_CONFIG\r?\n(?<body>.*?)^::END_CONFIG\r?$')
    if($configSections.Count -ne 1){throw 'Expected one editable settings section.'}
    $profilePath=Join-Path $runRoot 'Settings.Profiles.json'
    $profileData=Get-Content -LiteralPath $profilePath -Raw|ConvertFrom-Json
    $profileDefaults=@{};foreach($profile in $profileData.profiles){$profileDefaults[$profile.id]=$profile}
    $configuredProfiles=@{};$profileOrder=[Collections.Generic.List[string]]::new()
    # The shipped catalog is independent of the app list. Only explicit rules override it.
    foreach($profile in $profileData.profiles){$configuredProfiles[$profile.id]=$profile;$profileOrder.Add($profile.id)}
    $overridden=@{}
    foreach($line in ($configSections[0].Groups['body'].Value -split '\r?\n')){
        if($line -match '^::NOCONFIG\s+([^|]+)\|'){$configuredProfiles.Remove($Matches[1].Trim());continue}
        if($line -notmatch '^::CONFIG\s+(?<entry>.+)$'){continue}
        $parts=$Matches.entry -split '\|'
        if($parts.Count -notin @(4,5)){throw 'Use profile-id|Display name|Root|Relative path|optional processes for CONFIG rules.'}
        $id=$parts[0].Trim();$name=$parts[1].Trim();$rootName=$parts[2].Trim().ToUpperInvariant();$relative=$parts[3].Trim()
        if($id -notmatch '\A[a-z0-9]+(?:[.-][a-z0-9]+)*\z' -or -not $name){throw 'Invalid settings profile ID/name.'}
        if(-not $overridden.ContainsKey($id)){
            if($profileDefaults.ContainsKey($id)){$profile=$profileDefaults[$id];$profile.rules=@()}
            else{$profile=[pscustomobject]@{id=$id;name=$name;packageId=$id;versionPattern=('^'+[regex]::Escape($name)+'(?:\s|$)');processes=@();rules=@();exclude=@('*.log','*.tmp','cache','GPUCache','Cookies*','Login Data*','hosts.yml');evidence='User-configured settings rule.'}}
            $profile.name=$name;$configuredProfiles[$id]=$profile;if(-not $profileOrder.Contains($id)){$profileOrder.Add($id)};$overridden[$id]=$true
        }
        $configuredProfiles[$id].rules+=@([pscustomobject]@{root=$rootName;path=$relative})
        # Optional 5th field: processes that must be closed (comma-separated, no .exe), e.g. lghub,lghub_agent
        if($parts.Count -eq 5){foreach($proc in @($parts[4] -split ','|ForEach-Object {$_.Trim() -replace '\.exe$',''}|Where-Object {$_})){if($proc -notmatch '\A[A-Za-z0-9._-]+\z'){throw "Invalid process name in CONFIG rule: $proc"};if($proc -notin @($configuredProfiles[$id].processes)){$configuredProfiles[$id].processes=@($configuredProfiles[$id].processes)+$proc}}}
    }
    $profileData.profiles=@($profileOrder|Where-Object {$configuredProfiles.ContainsKey($_)}|ForEach-Object {$configuredProfiles[$_]})
    $disabled=@(foreach($line in ($configSections[0].Groups['body'].Value -split '\r?\n')){if($line -match '^::NOCONFIG\s+([^|]+)\|'){$Matches[1].Trim()}})
    $profileData.profiles=@($profileData.profiles|Where-Object {$_.id -notin $disabled})
    $profileData|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $profilePath -Encoding UTF8
    Import-Module (Join-Path $runRoot 'Settings.Core.psm1') -Force -DisableNameChecking
    $null=@(Get-SettingsProfiles $profilePath)

    function Invoke-EngineAction([string]$Selected){
        $settings=($Selected -in @('Backup','Restore','Inspect','SettingsList','BackupPlan','RestorePlan','ScheduleBackup','UnscheduleBackup'))
        $scriptName='Start-Setup.ps1';$childAction=$Selected
        if($settings){$scriptName='Settings.ps1';$childAction=$Selected -replace 'Plan$','';if($Selected -eq 'SettingsList'){$childAction='List'};if($Selected -eq 'ScheduleBackup'){$childAction='Schedule'};if($Selected -eq 'UnscheduleBackup'){$childAction='Unschedule'}}
        $params=@{Action=$childAction}
        if($Selected -in @('Install','Menu','Plan','List')){if($selectedIds.Count){$params.Apps=$selectedIds}elseif($Selected -eq 'Install'){$params.All=$true}}
        if($Selected -in @('Resume','CancelResume') -and $RunId){$params.RunId=$RunId}
        if(-not $settings){if($Ui){$params.Ui=$true};if($LogDir){$params.LogDir=$LogDir};if($SkipDrivers -and $Selected -in @('Install','Menu')){$params.SkipDrivers=$true};if($DriverIds){$params.DriverIds=$DriverIds}}
        if($settings){
            if($ArchivePath){$params.ArchivePath=$ArchivePath}
            if($Profiles -eq 'ALL'){$params.All=$true}elseif($Profiles){$params.Apps=@($Profiles -split ','|ForEach-Object {$_.Trim()}|Where-Object {$_})}
            if($Selected -in @('BackupPlan','RestorePlan')){$params.WhatIf=$true}
            if($AllowVersionMismatch -and $childAction -eq 'Restore'){$params.AllowVersionMismatch=$true}
            if($childAction -eq 'Schedule'){if($Time){$params.Time=$Time};if($Keep -gt 0){$params.Keep=$Keep}}
        }
        $global:LASTEXITCODE=0
        $script=Join-Path $runRoot $scriptName
        if($Capture){& $script @params *>&1|Out-String -Stream -Width 1000|ForEach-Object {$_.TrimEnd()}}
        else{& $script @params}
        $script:engineExit=[int]$global:LASTEXITCODE
    }

    if($action -eq 'Home'){
        do {
            Write-Host '';Write-Host 'Fresh Windows setup - installation and settings'
            Write-Host 'I = Install apps   B = Back up settings   R = Restore settings'
            Write-Host 'D = Drivers only   H = Preview detected hardware/drivers'
            Write-Host 'C = Continue a saved installation (normally automatic after reboot)'
            Write-Host 'X = Cancel a saved installation and remove its continuation task'
            Write-Host 'S = Show settings presets   P = Preview app list   Q = Quit'
            $choice=(Read-Host 'Choose an action').Trim().ToUpperInvariant()
            $selected=switch($choice){'I' {'Menu'} 'D' {'Drivers'} 'H' {'DriverPlan'} 'C' {'Resume'} 'X' {'CancelResume'} 'B' {'Backup'} 'R' {'Restore'} 'S' {'SettingsList'} 'P' {'Plan'} 'Q' {'Quit'} default {'Invalid'}}
            if($selected -eq 'Quit'){$exitCode=0;break};if($selected -eq 'Invalid'){continue}
            try{Invoke-EngineAction $selected;$exitCode=$script:engineExit}catch{Write-Host ('ERROR: '+$_.Exception.Message);$exitCode=1}
            if($selected -in @('Menu','Drivers','Resume')){break}
            Write-Host "Action exit code: $exitCode. After installing apps, choose R to restore their saved settings."
        } while($true)
    } else {
        Invoke-EngineAction $action;$exitCode=$script:engineExit
    }
} catch {
    Write-Host ('ERROR: '+$_.Exception.Message)
    $exitCode=1
}
exit $exitCode
