#requires -Version 5.1
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
$script:CommunitySource='https://community.chocolatey.org/api/v2/'
function Test-SetupAdministrator {
    $principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
function Get-ChocolateyText {
    param([string]$Url)
    $uri=[uri]$Url
    if($uri.Scheme -ne 'https' -or $uri.DnsSafeHost -ne 'community.chocolatey.org' -or -not $uri.IsDefaultPort -or $uri.UserInfo){throw 'Unapproved Chocolatey metadata/bootstrap URL.'}
    [Net.ServicePointManager]::SecurityProtocol=[Net.ServicePointManager]::SecurityProtocol -bor 3072
    $request=[Net.HttpWebRequest]::Create($uri)
    $request.AllowAutoRedirect=$false
    $request.Timeout=30000; $request.ReadWriteTimeout=30000
    $request.UserAgent='FreshWindowsSetup/1.0'
    $response=$request.GetResponse()
    try {
        if([int]$response.StatusCode -ne 200){throw 'Unexpected metadata/bootstrap HTTP response.'}
        $reader=[IO.StreamReader]::new($response.GetResponseStream())
        try {
            $buffer=New-Object char[] 8192
            $text=[Text.StringBuilder]::new()
            $deadline=[datetime]::UtcNow.AddSeconds(60)
            while(($count=$reader.Read($buffer,0,$buffer.Length)) -gt 0){
                if($text.Length+$count -gt 2097152 -or [datetime]::UtcNow -gt $deadline){throw 'Chocolatey metadata/bootstrap response exceeds limits.'}
                [void]$text.Append($buffer,0,$count)
            }
            return $text.ToString()
        } finally {$reader.Dispose()}
    } finally {$response.Dispose()}
}
function Convert-ChocolateyLatestVersion {
    param([string]$Text)
    $xml=[xml]::new(); $xml.XmlResolver=$null; $xml.LoadXml($Text)
    $entries=@($xml.SelectNodes('//*[local-name()="entry"]'))
    if($entries.Count -ne 1){throw 'Expected exactly one latest stable Chocolatey package.'}
    $properties=$entries[0].SelectSingleNode('.//*[local-name()="properties"]')
    if($null -eq $properties){throw 'Missing Chocolatey package metadata.'}
    $fields=@{}
    foreach($name in @('IsLatestVersion','IsPrerelease','IsApproved','Version')){
        $node=$properties.SelectSingleNode('./*[local-name()="'+$name+'"]')
        if($null -eq $node){throw "Missing Chocolatey package field: $name"}
        $fields[$name]=$node.InnerText
    }
    if($fields.IsLatestVersion -ne 'true' -or $fields.IsPrerelease -ne 'false' -or $fields.IsApproved -ne 'true' -or $fields.Version -notmatch '\A[0-9]+(?:\.[0-9]+){1,3}\z') {throw 'Invalid/unapproved latest stable Chocolatey metadata.'}
    $identity=$entries[0].SelectSingleNode('./*[local-name()="id"]').InnerText
    if($identity -notmatch "Packages\(Id='chocolatey',Version='[0-9.]+'\)\z"){throw 'Unexpected Chocolatey package identity.'}
    return $fields.Version
}
function Get-ChocolateyLatestVersion {
    $url=$script:CommunitySource+'Packages()?$filter='+[uri]::EscapeDataString("tolower(Id) eq 'chocolatey' and IsLatestVersion")
    Convert-ChocolateyLatestVersion (Get-ChocolateyText $url)
}
function Get-SetupChocolateyPath {
    $root=[Environment]::GetEnvironmentVariable('ChocolateyInstall','Machine')
    if(-not $root){$root=Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'chocolatey'}
    return (Join-Path $root 'bin/choco.exe')
}
function Invoke-SetupChoco {
    param([string]$Executable,[string[]]$Arguments)
    $output=@(& $Executable @Arguments)
    $code=$LASTEXITCODE
    return [pscustomobject]@{ExitCode=$code;Output=$output}
}
function Assert-ChocolateyReady {
    param([string]$Executable,[string]$ExpectedVersion='')
    if(-not(Test-Path -LiteralPath $Executable -PathType Leaf)){throw 'Chocolatey executable is missing. Run the main launcher first.'}
    $result=Invoke-SetupChoco $Executable @('--version')
    $version=($result.Output -join '').Trim()
    if($result.ExitCode -ne 0 -or $version -notmatch '\A[0-9]+(?:\.[0-9]+){1,3}\z'){throw 'Chocolatey --version verification failed.'}
    if($ExpectedVersion -and [version]$version -ne [version]$ExpectedVersion){throw "Chocolatey version verification failed: expected $ExpectedVersion, found $version."}
    $local=Invoke-SetupChoco $Executable @('list','--exact','chocolatey','--limit-output')
    if($local.ExitCode -ne 0 -or @($local.Output|Where-Object {$_ -match ('\Achocolatey\|'+[regex]::Escape($version)+'\z')}).Count -ne 1){throw 'Chocolatey is not correctly registered in its local package database.'}
    return $version
}
function Invoke-OfficialChocolateyBootstrap {
    param([string]$Version,[string]$Executable)
    $root=Split-Path -Parent (Split-Path -Parent $Executable)
    if(Test-Path -LiteralPath $root){throw "An incomplete Chocolatey directory already exists: $root. Review it before retrying; nothing was deleted."}
    $env:ChocolateyInstall=$root
    $scriptText=Get-ChocolateyText 'https://community.chocolatey.org/install.ps1'
    # Explicit official URL overrides any inherited download/version settings.
    & ([scriptblock]::Create($scriptText)) -ChocolateyDownloadUrl ($script:CommunitySource+'package/chocolatey/'+$Version) -UseNativeUnzip
}
function Initialize-SetupChocolatey {
    if(-not(Test-SetupAdministrator)){throw 'Chocolatey setup requires Administrator rights.'}
    $latest=Get-ChocolateyLatestVersion
    $executable=Get-SetupChocolateyPath
    if(Test-Path -LiteralPath $executable){
        $current=Assert-ChocolateyReady $executable
        if([version]$current -gt [version]$latest){throw "Installed Chocolatey $current is newer than repository stable $latest. No downgrade was attempted."}
        if([version]$current -lt [version]$latest){
            $upgrade=Invoke-SetupChoco $executable @('upgrade','chocolatey','-y',('--version='+$latest),('--source='+$script:CommunitySource),'--use-package-exit-codes','--ignore-detected-reboot','--no-progress')
            $upgrade.Output|ForEach-Object {Write-Host $_}
            if($upgrade.ExitCode -ne 0){throw "Chocolatey upgrade returned $($upgrade.ExitCode). Resolve any failure/reboot before app installation."}
        }
    } else {Invoke-OfficialChocolateyBootstrap $latest $executable | ForEach-Object {Write-Host $_}}
    $verified=Assert-ChocolateyReady $executable $latest
    # Verify community repository access using the installed CLI, not just HTTP.
    $remote=Invoke-SetupChoco $executable @('search','chocolatey','--exact','--limit-output',('--source='+$script:CommunitySource))
    if($remote.ExitCode -ne 0 -or @($remote.Output|Where-Object {$_ -match ('\Achocolatey\|'+[regex]::Escape($latest)+'\z')}).Count -ne 1){throw 'Chocolatey community repository verification failed. No app window will open.'}
    return [pscustomobject]@{Executable=$executable;Version=$verified}
}
function Get-ChocolateyAppPlan {
    param([string]$Path,[string[]]$Ids=@(),[switch]$All)
    $data=Get-Content -LiteralPath $Path -Raw|ConvertFrom-Json
    if($data.schemaVersion -ne 1){throw 'Unsupported Chocolatey app catalogue schema.'}
    if($All -and $Ids.Count){throw 'Choose -All or -Apps, not both.'}
    $seen=@{}
    foreach($app in $data.apps){
        if($app.id -notmatch '\A[a-z0-9]+(?:[.-][a-z0-9]+)*\z' -or $seen.ContainsKey($app.id)){throw 'Invalid/duplicate app ID.'}
        $seen[$app.id]=$true
        if($app.status -notin @('Chocolatey','DecisionRequired','HardwareReview')){throw 'Invalid Chocolatey routing status.'}
        if($app.packageId -and $app.packageId -notmatch '\A[a-zA-Z0-9]+(?:[.-][a-zA-Z0-9]+)*\z'){throw 'Invalid package ID.'}
        if($app.status -eq 'Chocolatey' -and -not $app.packageId){throw 'Chocolatey app is missing a package ID.'}
    }
    foreach($id in $Ids){if(-not $seen.ContainsKey($id)){throw "Unknown app ID: $id"}}
    # Catalogue stores runtimes first, then apps. Chocolatey also resolves package dependencies.
    foreach($app in $data.apps){if($All -or -not $Ids.Count -or $app.id -in $Ids){$app}}
}
function Get-ChocolateyInstallArguments {
    param($App)
    if($App.status -ne 'Chocolatey'){throw "App requires a decision/review: $($App.id)"}
    $verb='install';if($App.PSObject.Properties['upgrade'] -and $App.upgrade){$verb='upgrade'}
    $arguments=@($verb,$App.packageId,'-y',('--source='+$script:CommunitySource),'--use-package-exit-codes','--ignore-detected-reboot','--no-progress')
    if($App.packageParameters){$arguments+=('--package-parameters='+$App.packageParameters)}
    if($App.PSObject.Properties['installArguments'] -and $App.installArguments){$arguments+=('--install-arguments='+$App.installArguments)}
    if($App.PSObject.Properties['force'] -and $App.force){$arguments+='--force'}
    return $arguments
}
# Installers that create no Start menu entry when run silently. The shortcut name and the app's EXE relative to
# Program Files (64-bit first, then 32-bit).
$script:MissingShortcutApps=@{
    'streamlink-twitch-gui'=@{Name='Streamlink Twitch GUI';Exe='Streamlink Twitch GUI\streamlink-twitch-gui.exe'}
}
function Add-SetupStartMenuShortcut {
    param(
        [string]$PackageId,
        [string]$StartMenu=(Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'),
        [string[]]$ProgramDirs=@($env:ProgramFiles,${env:ProgramFiles(x86)}),
        [scriptblock]$Create={param($Link,$Target) $shell=New-Object -ComObject WScript.Shell;$s=$shell.CreateShortcut($Link);$s.TargetPath=$Target;$s.WorkingDirectory=(Split-Path -Parent $Target);$s.IconLocation=$Target+',0';$s.Save()}
    )
    if(-not $script:MissingShortcutApps.ContainsKey($PackageId)){return ''}
    $entry=$script:MissingShortcutApps[$PackageId]
    try{
        $target=@(foreach($dir in $ProgramDirs){if($dir){$path=Join-Path $dir $entry.Exe;if(Test-Path -LiteralPath $path -PathType Leaf){$path}}})|Select-Object -First 1
        if(-not $target){return ' Its program file was not found, so no Start menu shortcut was added.'}
        $link=Join-Path $StartMenu ($entry.Name+'.lnk')
        if(Test-Path -LiteralPath $link){return ''}
        & $Create $link $target
        return ' Added a Start menu shortcut (the silent installer does not create one).'
    }catch{return (' A Start menu shortcut could not be added: '+$_.Exception.Message)}
}
function Invoke-ChocolateyAppPlan {
    [CmdletBinding(SupportsShouldProcess=$true)]
    param([object[]]$Plan,[string]$Executable)
    $stopForReboot=$false
    foreach($app in $Plan){
        $row=[pscustomobject]@{Id=$app.id;Name=$app.name;PackageId=$app.packageId;Status='';ExitCode=$null;Detail=$app.note;Version=''}
        if($app.status -ne 'Chocolatey'){$row.Status=$app.status; $row; continue}
        if($stopForReboot){$row.Status='DeferredForReboot';$row;continue}
        if(-not $PSCmdlet.ShouldProcess($app.name,('Install Chocolatey package '+$app.packageId))){$row.Status='Preview';$row;continue}
        try {
            if($app.PSObject.Properties['githubRepo'] -and $app.githubRepo){
                # Freshkit: GitHub release installer, through the same queue/reboot handling.
                $g=Invoke-SetupGitHubApp $app
                $row.ExitCode=$g.ExitCode;$row.Status=$g.Status;$row.Detail=$g.Detail;$row.Version=$g.Version
                if($row.Status -eq 'RebootRequired'){$stopForReboot=$true}
                $row;continue
            }
            if($app.PSObject.Properties['wingetId'] -and $app.wingetId){
                # Freshkit: winget-sourced app, installed through the same queue/reboot handling.
                $w=Invoke-SetupWingetApp $app
                $row.ExitCode=$w.ExitCode;$row.Status=$w.Status;$row.Detail=$w.Detail;$row.Version=$w.Version
                if($row.Status -eq 'RebootRequired'){$stopForReboot=$true}
                $row;continue
            }
            $result=Invoke-SetupChoco $Executable (Get-ChocolateyInstallArguments $app)
            $result.Output|ForEach-Object {Write-Host $_}
            $row.ExitCode=$result.ExitCode
            if($result.ExitCode -in @(350,1604)){$row.Status='DeferredForReboot';$stopForReboot=$true;$row;continue}
            if($result.ExitCode -notin @(0,3010,1641)){throw "Chocolatey install returned $($result.ExitCode)."}
            $local=Invoke-SetupChoco $Executable @('list','--exact',$app.packageId,'--limit-output')
            $matches=@($local.Output|Where-Object {$_ -match ('\A'+[regex]::Escape($app.packageId)+'\|[^|]+\z')})
            if($local.ExitCode -ne 0 -or $matches.Count -ne 1){throw 'Chocolatey did not register the requested package after installation.'}
            $row.Version=($matches[0] -split '\|')[1]
            $row.Status='PackageInstalled'
            $row.Detail='Chocolatey returned success and registered the package; application function has not been tested.'+(Add-SetupStartMenuShortcut $app.packageId)
            if($result.ExitCode -in @(3010,1641)){$row.Status='RebootRequired';$stopForReboot=$true}
        } catch {$row.Status='Failed';$row.Detail=$_.Exception.Message}
        $row
    }
}
function Get-SetupWingetPath {
    foreach($attempt in 1..2){
        $candidates=@()
        $cmd=Get-Command winget.exe -CommandType Application -ErrorAction SilentlyContinue|Select-Object -First 1
        if($cmd){$candidates+=$cmd.Source}
        if($env:LOCALAPPDATA){$candidates+=(Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\winget.exe')}
        foreach($candidate in $candidates){if($candidate -and (Test-Path -LiteralPath $candidate)){return $candidate}}
        # Fresh installs can take a while to register App Installer for the account.
        if($attempt -eq 1){try{Add-AppxPackage -RegisterByFamilyName -MainPackage 'Microsoft.DesktopAppInstaller_8wekyb3d8bbwe' -ErrorAction Stop;Start-Sleep -Seconds 5}catch{}}
    }
    throw 'winget (App Installer) is not available on this PC yet. Update "App Installer" from the Microsoft Store, then retry.'
}
# Microsoft Store product IDs (e.g. 9NT1R1C2HH7J) use the msstore source; everything else uses winget.
function Get-SetupWingetSource {param([string]$Id) if($Id -cmatch '\A(?:9[A-Z0-9]{11}|XP[A-Z0-9]{12})\z'){'msstore'}else{'winget'}}
function Test-SetupWingetInstalled {
    param([string]$Id)
    $winget=Get-SetupWingetPath
    $output=@(& $winget list --id $Id --exact --source (Get-SetupWingetSource $Id) --accept-source-agreements --disable-interactivity 2>&1)
    $code=$LASTEXITCODE;$version=''
    if($code -eq 0){
        foreach($line in $output){
            $text=[string]$line;$index=$text.IndexOf($Id,[StringComparison]::OrdinalIgnoreCase)
            if($index -ge 0){$rest=@($text.Substring($index+$Id.Length).Trim() -split '\s+');if($rest.Count -and $rest[0]){$version=$rest[0]};break}
        }
    }
    return [pscustomobject]@{Installed=($code -eq 0);Version=$version}
}
function Wait-SetupWingetInstalled {
    # Some installers (Discord and other Squirrel-based apps) return before they register with Windows, so
    # winget's installed list can lag a successful install. Keep checking for a while before calling it failed.
    param([string]$Id,[int]$TimeoutSeconds=120,[int]$IntervalSeconds=5,[scriptblock]$Probe={param($i) Test-SetupWingetInstalled $i})
    $deadline=[datetime]::UtcNow.AddSeconds($TimeoutSeconds)
    while($true){
        $result=& $Probe $Id
        if($result.Installed -or [datetime]::UtcNow -ge $deadline){return $result}
        Start-Sleep -Seconds $IntervalSeconds
    }
}
function Get-SetupWingetErrorText {
    param([string]$Verb,[int]$Code)
    $hex='0x{0:X8}' -f $Code
    $reason=switch($hex){
        '0x8A150008' {'the download failed'}
        '0x8A150010' {'winget has no installer that suits this PC'}
        '0x8A150011' {"the download doesn't match the checksum in winget's catalog. The publisher has probably replaced the file; winget's catalog usually catches up within a few days"}
        '0x8A150101' {'the app is running. Close it and retry'}
        '0x8A150102' {'another installation was already running. Retry when it has finished'}
        '0x8A150105' {'the disk is full'}
        '0x8A150107' {'there was no network connection'}
        default {''}
    }
    $text='winget '+$Verb+' returned '+$hex
    if($reason){$text+=': '+$reason}
    return $text+'.'
}
function Install-SetupDiscordHelper {
    # Discord opens itself after installing and, on that first run, registers a 'Discord System Helper' service,
    # asking for administrator rights with a UAC prompt to do it. Setup is already elevated, so it registers the
    # service itself as soon as Discord has downloaded the helper; Discord then finds the service and doesn't ask.
    param(
        [string]$AppDir=(Join-Path $env:LOCALAPPDATA 'Discord'),[int]$TimeoutSeconds=90,
        [scriptblock]$ServiceExists={[bool](Get-Service -Name 'DiscordSystemHelper' -ErrorAction SilentlyContinue)},
        [scriptblock]$Trusted={param($Path) $s=Get-AuthenticodeSignature -LiteralPath $Path;$s.Status -eq 'Valid' -and $s.SignerCertificate.Subject -match '(?:^|,\s*)O=Discord Inc\.(?:,|$)'},
        [scriptblock]$Run={param($Path) $p=Start-Process -FilePath $Path -ArgumentList 'install' -WindowStyle Hidden -PassThru;if(-not $p.WaitForExit(60000)){return -1};$p.ExitCode}
    )
    try{
        if(& $ServiceExists){return ''}
        $deadline=[datetime]::UtcNow.AddSeconds($TimeoutSeconds)
        while($true){
            $helper=@(Get-ChildItem -Path (Join-Path $AppDir 'app-*\modules\discord_utils-*\discord_utils\DiscordSystemHelper.exe') -ErrorAction SilentlyContinue|Sort-Object LastWriteTime -Descending)
            # A helper still being written fails the signature check; it is retried until the deadline.
            if($helper.Count -and (& $Trusted $helper[0].FullName)){
                $code=& $Run $helper[0].FullName
                if(& $ServiceExists){return ' Registered the Discord System Helper service, so Discord does not ask for administrator rights.'}
                return (' Registering the Discord System Helper returned '+$code+'; Discord may ask for administrator rights the first time it opens.')
            }
            if([datetime]::UtcNow -ge $deadline){return ' Discord had not downloaded its System Helper yet; it may ask for administrator rights the first time it opens.'}
            Start-Sleep -Milliseconds 250
        }
    }catch{return (' The Discord System Helper could not be registered ('+$_.Exception.Message+'); Discord may ask for administrator rights the first time it opens.')}
}
function Invoke-SetupWingetApp {
    param($App)
    $winget=Get-SetupWingetPath;$id=[string]$App.wingetId
    if($id -notmatch '\A[A-Za-z0-9][A-Za-z0-9.+_-]*\z'){throw "Invalid winget package ID: $id"}
    $upgrade=($App.PSObject.Properties['upgrade'] -and $App.upgrade)
    $before=Test-SetupWingetInstalled $id
    if($before.Installed -and -not $upgrade){return [pscustomobject]@{Status='PackageInstalled';ExitCode=0;Version=$before.Version;Detail='Already installed (winget); not reinstalled.'}}
    $verb=if($before.Installed){'upgrade'}else{'install'}
    $arguments=@($verb,'--id',$id,'--exact','--source',(Get-SetupWingetSource $id),'--silent','--accept-package-agreements','--accept-source-agreements','--disable-interactivity')
    if($App.packageParameters){$arguments+=@('--override',[string]$App.packageParameters)}
    Write-Host ('winget '+($arguments -join ' '))
    $output=@(& $winget @arguments 2>&1);$code=$LASTEXITCODE
    foreach($line in $output){$text=([string]$line).TrimEnd();if($text.Trim() -and $text -notmatch '^[\s\-\\|/]+$' -and $text -notmatch '[\u2580-\u259F]'){Write-Host $text}}   # block-element progress bars; keep this file ASCII (PS 5.1 reads BOM-less files as ANSI)
    $notNeeded=@(-1978335189,-1978335135)   # 0x8A15002B no applicable update, 0x8A150061 already installed
    $reboot=@(3010,1641,-1978334967,-1978334966)
    if($code -ne 0 -and $code -notin $notNeeded -and $code -notin $reboot){throw (Get-SetupWingetErrorText $verb $code)}
    $extra='';if($id -eq 'Discord.Discord'){$extra=Install-SetupDiscordHelper;if($extra){Write-Host $extra.Trim()}}
    $after=Wait-SetupWingetInstalled $id
    if(-not $after.Installed){throw "winget reported success, but $id still wasn't in the installed list after 2 minutes."}
    $status=if($code -in $reboot){'RebootRequired'}else{'PackageInstalled'}
    return [pscustomobject]@{Status=$status;ExitCode=$code;Version=$after.Version;Detail=('Installed with winget ('+$id+'); application function has not been tested.'+$extra)}
}
# ---- GitHub releases (Freshkit): latest release's Windows installer, run silently ----
function Test-SetupGitHubRepo {param([string]$Repo) return ($Repo -match '\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}\z' -and $Repo -notmatch '/\.\.?\z')}
function ConvertTo-SetupNameKey {
    param([string]$Name)
    $value=($Name -replace '\s+v?\d+(\.\d+)+.*$','').ToLowerInvariant() -replace '\([^)]*\)',' '
    return (($value -replace '\b(x64|x86|64-bit|32-bit|install|portable)\b',' ') -replace '[^a-z0-9]','')
}
function Test-SetupGitHubInstalled {
    param([string]$Name,[string]$Repo,[object[]]$Records=$null,$Links=$null,[switch]$AllowUnlinked)
    if(-not (Test-SetupGitHubRepo $Repo)){return [pscustomobject]@{Installed=$false;Version='';Possible=$false}}
    if($null -eq $Records){
        $Records=@(foreach($hive in @([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryHive]::CurrentUser)){
            foreach($view in @([Microsoft.Win32.RegistryView]::Registry64,[Microsoft.Win32.RegistryView]::Registry32)){
                $base=$null;$keys=$null
                try{
                    $base=[Microsoft.Win32.RegistryKey]::OpenBaseKey($hive,$view)
                    $keys=$base.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')
                    if(-not $keys){continue}
                    foreach($sub in $keys.GetSubKeyNames()){
                        $key=$keys.OpenSubKey($sub)
                        if(-not $key){continue}
                        try{
                            $display=[string]$key.GetValue('DisplayName');if(-not $display){continue}
                            $repository=''
                            foreach($field in @('URLInfoAbout','HelpLink','URLUpdateInfo')){
                                $uri=$null
                                if([Uri]::TryCreate([string]$key.GetValue($field),[UriKind]::Absolute,[ref]$uri) -and $uri.Scheme -eq 'https' -and $uri.Host -eq 'github.com' -and -not $uri.UserInfo){
                                    $parts=$uri.AbsolutePath.Trim('/').Split('/')
                                    if($parts.Count -ge 2){$candidate=($parts[0]+'/'+$parts[1]) -replace '\.git$','';if(Test-SetupGitHubRepo $candidate){$repository=$candidate;break}}
                                }
                            }
                            $fields=@(([string]$hive+'|'+[string]$view+'|'+$sub),$display,[string]$key.GetValue('Publisher'),[string]$key.GetValue('InstallLocation'),[string]$key.GetValue('UninstallString'))
                            $hash=[Security.Cryptography.SHA256]::Create()
                            try{$fingerprint=[BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes(($fields -join "`n")))).Replace('-','')}finally{$hash.Dispose()}
                            [pscustomobject]@{RegistryPath=$fields[0];Name=$display;Version=[string]$key.GetValue('DisplayVersion');Repository=$repository;Fingerprint=$fingerprint}
                        }finally{$key.Dispose()}
                    }
                }catch{}finally{if($keys){$keys.Dispose()};if($base){$base.Dispose()}}
            }
        })
    }
    if($null -eq $Links){
        $Links=@{};$path=Join-Path $env:LOCALAPPDATA 'Freshkit\installed-identities.json'
        if(Test-Path -LiteralPath $path){try{$json=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json;foreach($p in $json.PSObject.Properties){$Links[$p.Name]=[string]$p.Value}}catch{}}
    }
    $wanted=ConvertTo-SetupNameKey $Name
    $confirmed=@();$possible=@();$seen=@{}
    foreach($r in $Records){
        $identity=$r.RegistryPath;if($identity -like 'CurrentUser|*'){$identity=$identity.Replace('|Registry32|','|Registry64|')}
        if($seen.ContainsKey($identity)){continue};$seen[$identity]=$true
        $repository=$r.Repository;if(-not $repository -and $Links.ContainsKey($r.Fingerprint)){$repository=$Links[$r.Fingerprint]}
        if($repository -eq $Repo){$confirmed+=,$r}
        elseif(-not $repository -and $wanted.Length -ge 3 -and (ConvertTo-SetupNameKey ($r.Name -replace '\s+v?\d+(\.\d+)+.*$','')) -eq $wanted){$possible+=,$r}
    }
    $pick=$null
    if($confirmed.Count -eq 1){$pick=$confirmed[0]}
    elseif($AllowUnlinked -and $confirmed.Count -eq 0 -and $possible.Count -eq 1){$pick=$possible[0]}
    if($pick){return [pscustomobject]@{Installed=$true;Version=$pick.Version;Fingerprint=$pick.Fingerprint;Possible=$false}}
    return [pscustomobject]@{Installed=$false;Version='';Possible=($possible.Count -gt 0 -or $confirmed.Count -gt 1)}
}
function Save-SetupGitHubIdentity {
    param([string]$Fingerprint,[string]$Repo)
    if(-not $Fingerprint -or -not (Test-SetupGitHubRepo $Repo)){return}
    $path=Join-Path $env:LOCALAPPDATA 'Freshkit\installed-identities.json';$links=@{}
    if(Test-Path -LiteralPath $path){$old=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json;foreach($p in $old.PSObject.Properties){$links[$p.Name]=[string]$p.Value}}
    $links[$Fingerprint]=$Repo
    [IO.Directory]::CreateDirectory((Split-Path -Parent $path))|Out-Null
    $temporary=$path+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
    try{[IO.File]::WriteAllText($temporary,($links|ConvertTo-Json),[Text.UTF8Encoding]::new($false));if(Test-Path -LiteralPath $path){[IO.File]::Replace($temporary,$path,[System.Management.Automation.Language.NullString]::Value)}else{[IO.File]::Move($temporary,$path)}}finally{if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary -Force}}
}
function Select-SetupGitHubAsset {
    # Prefer an x64/neutral MSI, then an installer-looking EXE. Portable, ARM and 32-bit builds are skipped.
    param($Release,[string]$Pattern='')
    $assets=@($Release.assets|Where-Object {
        $_.name -match '(?i)\.(msi|exe)\z' -and $_.name -notmatch '(?i)(arm64|aarch64|i[36]86|ia32|win32|x86(?![-_]?64)|portable|debug|symbols|uninstall)'
    })
    if($Pattern){$assets=@($assets|Where-Object {$_.name -like $Pattern})}
    $ranked=@($assets|Sort-Object @{e={if($_.name -match '(?i)\.msi\z'){0}elseif($_.name -match '(?i)(setup|install)'){1}else{2}}},@{e={if($_.name -match '(?i)(x64|amd64|win64|x86_64)'){0}else{1}}})
    if(-not $ranked.Count){return $null}
    return $ranked[0]
}
function Get-SetupGitHubWebRelease {
    # github.com's own pages: /releases/latest redirects to the tag, and expanded_assets lists each file with its
    # SHA-256. Unlike api.github.com these aren't limited to 60 requests an hour per network.
    param([string]$Repo)
    $request=[Net.HttpWebRequest]::Create('https://github.com/'+$Repo+'/releases/latest')
    $request.AllowAutoRedirect=$false;$request.UserAgent='Freshkit';$request.Timeout=30000
    try{$response=$request.GetResponse()}catch{$ex=$_.Exception;while($ex -and -not ($ex -is [Net.WebException])){$ex=$ex.InnerException};$response=if($ex){$ex.Response}else{$null}}
    # github.com closes the connection (rather than answering 404) for repositories without releases.
    if(-not $response){throw ('github.com/'+$Repo+' has no published release, or github.com could not be reached.')}
    try{$location=[string]$response.Headers['Location'];$status=[int]$response.StatusCode}finally{$response.Close()}
    if($status -lt 300 -or $status -ge 400 -or $location -notmatch '/releases/tag/(?<tag>[^/?#]+)\z'){throw ('github.com/'+$Repo+' has no published release.')}
    $tag=[Uri]::UnescapeDataString($Matches.tag)
    $html=(Invoke-WebRequest -Uri ('https://github.com/'+$Repo+'/releases/expanded_assets/'+[Uri]::EscapeDataString($tag)) -UseBasicParsing -TimeoutSec 60 -Headers @{'User-Agent'='Freshkit'}).Content
    $prefix='/'+$Repo+'/releases/download/'
    $links=[regex]::Matches($html,'href="(?<href>/[^"]+/releases/download/[^"]+)"')
    $assets=@()
    for($i=0;$i -lt $links.Count;$i++){
        $href=[Net.WebUtility]::HtmlDecode($links[$i].Groups['href'].Value)
        if(-not $href.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){continue}
        # The checksum shown for an asset sits between its link and the next asset's link.
        $end=if($i+1 -lt $links.Count){$links[$i+1].Index}else{$html.Length}
        $digest=[regex]::Match($html.Substring($links[$i].Index,$end-$links[$i].Index),'sha256:[0-9a-f]{64}')
        $assets+=[pscustomobject]@{name=[Uri]::UnescapeDataString(($href -split '/')[-1]);browser_download_url=('https://github.com'+$href);size=$null;digest=$(if($digest.Success){$digest.Value}else{$null})}
    }
    return [pscustomobject]@{tag_name=$tag;assets=$assets}
}
function Get-SetupGitHubRelease {
    param([string]$Repo)
    if(-not (Test-SetupGitHubRepo $Repo)){throw "Invalid GitHub repository: $Repo"}
    [Net.ServicePointManager]::SecurityProtocol=[Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
    try{return Get-SetupGitHubWebRelease $Repo}
    catch{
        $web=$_.Exception.Message
        try{return Invoke-RestMethod -Uri ('https://api.github.com/repos/'+$Repo+'/releases/latest') -Headers @{'User-Agent'='Freshkit';'Accept'='application/vnd.github+json'} -UseBasicParsing -TimeoutSec 60}
        catch{throw ('Could not read the latest GitHub release of '+$Repo+': '+$web+' / API: '+$_.Exception.Message)}
    }
}
function Get-SetupInstallerKind {
    param([string]$Path)
    if($Path -match '(?i)\.msi\z'){return 'msi'}
    $bytes=[IO.File]::ReadAllBytes($Path);$text=[Text.Encoding]::ASCII.GetString($bytes)
    if($text.Contains('Nullsoft')){return 'nsis'}
    if($text.Contains('Inno Setup')){return 'inno'}
    return 'unknown'
}
function Invoke-SetupGitHubApp {
    param($App)
    $repo=[string]$App.githubRepo;$upgrade=($App.PSObject.Properties['upgrade'] -and $App.upgrade)
    $before=Test-SetupGitHubInstalled -Name $App.name -Repo $repo
    # Upgrade mode refuses only an unconfirmed lookalike (a name-only match); an app with no match at all is installed.
    if($upgrade -and -not $before.Installed -and $before.Possible){throw ('Repository identity is not confirmed for '+$App.name+'; link the installed app in Freshkit before updating.')}
    if($before.Installed -and -not $upgrade){return [pscustomobject]@{Status='PackageInstalled';ExitCode=0;Version=$before.Version;Detail='Already installed; not reinstalled.'}}
    $release=Get-SetupGitHubRelease $repo
    $tag=([string]$release.tag_name) -replace '^[vV]',''
    if($before.Installed -and $before.Version -and $tag -and ($before.Version -replace '^[vV]','') -eq $tag){return [pscustomobject]@{Status='PackageInstalled';ExitCode=0;Version=$before.Version;Detail='Already the latest GitHub release.'}}
    $asset=Select-SetupGitHubAsset $release
    if(-not $asset){throw ('The latest release of '+$repo+' has no Windows installer (.msi or setup .exe).')}
    if(([string]$asset.browser_download_url) -notmatch '\Ahttps://github\.com/'){throw 'Unexpected GitHub download location.'}
    $folder=Join-Path ([IO.Path]::GetTempPath()) ('Freshkit-github-'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $folder -Force|Out-Null
    try{
        $file=Join-Path $folder ([IO.Path]::GetFileName([string]$asset.name))
        Write-Host ('Downloading '+$asset.name+' from github.com/'+$repo)
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $file -UseBasicParsing -TimeoutSec 600 -Headers @{'User-Agent'='Freshkit'}
        if($asset.size -and (Get-Item -LiteralPath $file).Length -ne [long]$asset.size){throw 'The download is incomplete (size mismatch).'}
        $check='no published checksum'
        if($asset.PSObject.Properties['digest'] -and ([string]$asset.digest) -match '\Asha256:([0-9a-fA-F]{64})\z'){
            if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $Matches[1].ToUpperInvariant()){throw 'The download does not match GitHub''s SHA-256 checksum.'}
            $check='SHA-256 verified'
        }
        $signature=(Get-AuthenticodeSignature -LiteralPath $file).Status
        $kind=Get-SetupInstallerKind $file
        if($kind -eq 'msi'){$exe=Join-Path ([Environment]::SystemDirectory) 'msiexec.exe';$arguments='/i "'+$file+'" /qn /norestart';if($App.packageParameters){$arguments+=' '+[string]$App.packageParameters}}
        else{
            $exe=$file
            if($App.packageParameters){$arguments=[string]$App.packageParameters}
            elseif($kind -eq 'nsis'){$arguments='/S'}
            elseif($kind -eq 'inno'){$arguments='/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'}
            else{throw ('Freshkit could not identify the silent switches for '+$asset.name+'. Add them as the third field of its ::GITHUB line.')}
        }
        Write-Host ('Installing '+$asset.name+' ('+$kind+'; '+$check+'; signature '+$signature+')')
        $process=Start-Process -FilePath $exe -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
        $code=$process.ExitCode
        if($code -notin @(0,3010,1641)){throw ('The '+$asset.name+' installer returned '+$code+'.')}
        $after=Test-SetupGitHubInstalled -Name $App.name -Repo $repo -AllowUnlinked
        if(-not $after.Installed){throw ('The installer finished but "'+$App.name+'" is not listed in Installed apps.')}
        Save-SetupGitHubIdentity $after.Fingerprint $repo
        $status=if($code -in @(3010,1641)){'RebootRequired'}else{'PackageInstalled'}
        return [pscustomobject]@{Status=$status;ExitCode=$code;Version=$after.Version;Detail=('Installed '+$asset.name+' from github.com/'+$repo+' ('+$check+'); application function has not been tested.')}
    } finally {Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue}
}
function ConvertTo-SetupLiteral {param([string]$Value); return ("'"+$Value.Replace("'","''")+"'")}
function Get-SetupEncodedCommand {
    param([string]$Script,[string]$Action,[string[]]$Apps=@(),[switch]$All,[string]$ExpectedUserSid='',[string]$ExpectedVersion='',[string]$RunId='',[switch]$Ui,[string]$LogDir='',[switch]$SkipDrivers)
    $text='& '+(ConvertTo-SetupLiteral $Script)+' -Action '+(ConvertTo-SetupLiteral $Action)
    if($Apps.Count){$text+=' -Apps '+(ConvertTo-SetupLiteral ($Apps -join ','))}
    if($All){$text+=' -All'}
    if($ExpectedUserSid){$text+=' -ExpectedUserSid '+(ConvertTo-SetupLiteral $ExpectedUserSid)}
    if($ExpectedVersion){$text+=' -ExpectedVersion '+(ConvertTo-SetupLiteral $ExpectedVersion)}
    if($RunId){$text+=' -RunId '+(ConvertTo-SetupLiteral $RunId)}
    if($Ui){$text+=' -Ui'}
    if($LogDir){$text+=' -LogDir '+(ConvertTo-SetupLiteral $LogDir)}
    if($SkipDrivers){$text+=' -SkipDrivers'}
    return [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($text))
}
function Open-ChocolateyAppWindow {
    param([string]$Script,[string]$Action,[string[]]$Apps=@(),[switch]$All,[string]$ExpectedUserSid,[string]$ExpectedVersion,[switch]$SkipDrivers)
    if(-not(Test-SetupAdministrator)){throw 'The new command window must inherit an Administrator token.'}
    $encoded=Get-SetupEncodedCommand @PSBoundParameters
    $hostPath=Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell/v1.0/powershell.exe'
    $arguments='/d /k ""'+$hostPath+'" -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand '+$encoded+'"'
    # Wait until the app window closes so a standalone wrapper can safely remove
    # its temporary scripts only after every elevated consumer has finished.
    Start-Process -FilePath (Join-Path ([Environment]::SystemDirectory) 'cmd.exe') -ArgumentList $arguments -WorkingDirectory ([Environment]::SystemDirectory) -Wait | Out-Null
}
Export-ModuleMember -Function Test-SetupAdministrator,Get-ChocolateyLatestVersion,Convert-ChocolateyLatestVersion,Get-SetupChocolateyPath,Assert-ChocolateyReady,Initialize-SetupChocolatey,Get-ChocolateyAppPlan,Get-ChocolateyInstallArguments,Invoke-ChocolateyAppPlan,Get-SetupEncodedCommand,Open-ChocolateyAppWindow,Get-SetupWingetPath,Test-SetupWingetInstalled,Invoke-SetupWingetApp,Test-SetupGitHubRepo,Test-SetupGitHubInstalled,Invoke-SetupGitHubApp,Select-SetupGitHubAsset,Get-SetupGitHubRelease
