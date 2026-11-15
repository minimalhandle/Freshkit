#requires -Version 5.1
Set-StrictMode -Version 2.0
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Freshkit.Status.ps1')
function Get-InstallResumeRoot {Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'FreshWindowsSetupResume'}
function Get-InstallRunDirectory {
    param([ValidatePattern('^[a-f0-9]{32}$')][string]$RunId)
    Join-Path (Get-InstallResumeRoot) $RunId
}
function Get-InstallUserSid {[Security.Principal.WindowsIdentity]::GetCurrent().User.Value}
function Get-InstallBootId {
    $os=Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop
    $os.LastBootUpTime.ToUniversalTime().Ticks.ToString([Globalization.CultureInfo]::InvariantCulture)
}
function Test-InstallPendingReboot {
    foreach($path in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending','HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')){
        if(Test-Path -LiteralPath $path){return $true}
    }
    # PendingFileRenameOperations is shared with unrelated background software.
    # Its presence alone cannot establish that this installation needs a restart.
    # Package exit codes 3010/1641 still enforce installer-requested reboots.
    return $false
}
function Get-InstallRunSecurity {
    param([string]$UserSid,[switch]$File)
    if($File){$acl=[Security.AccessControl.FileSecurity]::new()}else{$acl=[Security.AccessControl.DirectorySecurity]::new()}
    $acl.SetAccessRuleProtection($true,$false)
    $acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    foreach($sid in @('S-1-5-18','S-1-5-32-544',$UserSid)){
        $rights=if($sid -eq $UserSid){[Security.AccessControl.FileSystemRights]::ReadAndExecute}else{[Security.AccessControl.FileSystemRights]::FullControl}
        $inherit=if($File){[Security.AccessControl.InheritanceFlags]::None}else{[Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit}
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($sid),$rights,$inherit,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow))
    }
    return $acl
}
function Assert-InstallRunTrust {
    param([string]$Path)
    $trusted=@('S-1-5-18','S-1-5-32-544','S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')
    $resumeRoot=[IO.Path]::GetFullPath((Get-InstallResumeRoot)).TrimEnd('\')
    $node=[IO.Path]::GetFullPath($Path)
    while($node){
        if(Test-Path -LiteralPath $node){
            $item=Get-Item -LiteralPath $node -Force
            if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse points are not allowed in persisted installation state.'}
            $acl=Get-Acl -LiteralPath $node
            if($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $trusted){throw 'Resume inputs/ancestors must be administrator or SYSTEM owned.'}
            $raw=[Security.AccessControl.RawSecurityDescriptor]::new($acl.GetSecurityDescriptorBinaryForm(),0)
            if($null -eq $raw.DiscretionaryAcl){throw 'Resume path has an unprotected null DACL.'}
            $rights=[Security.AccessControl.FileSystemRights]::Delete -bor [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor [Security.AccessControl.FileSystemRights]::ChangePermissions -bor [Security.AccessControl.FileSystemRights]::TakeOwnership
            if($node -eq $resumeRoot -or $node.StartsWith($resumeRoot+'\',[StringComparison]::OrdinalIgnoreCase)){$rights=$rights -bor [Security.AccessControl.FileSystemRights]::Write}
            foreach($rule in $acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])){
                if($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly){continue}
                if($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Value -notin $trusted -and ($rule.FileSystemRights -band $rights)){throw 'Unprivileged write/delete access to resume inputs or ancestors is not allowed.'}
            }
        }
        $parent=[IO.Path]::GetDirectoryName($node);if($parent -eq $node){break};$node=$parent
    }
}
function New-InstallRunDirectory {
    param([string]$RunId,[string]$UserSid)
    $base=Get-InstallResumeRoot
    Assert-InstallRunTrust (Split-Path -Parent $base)
    if(Test-Path -LiteralPath $base){Assert-InstallRunTrust $base}else{[IO.Directory]::CreateDirectory($base,(Get-InstallRunSecurity $UserSid))|Out-Null}
    $directory=Get-InstallRunDirectory $RunId
    if(Test-Path -LiteralPath $directory){throw 'Resume run directory must be new.'}
    [IO.Directory]::CreateDirectory($directory,(Get-InstallRunSecurity $UserSid))|Out-Null
    Assert-InstallRunTrust $directory
    return $directory
}
function Write-InstallProtectedBytes {
    param([string]$Path,[byte[]]$Bytes,[string]$UserSid)
    $stream=[IO.FileStream]::new($Path,[IO.FileMode]::CreateNew,[Security.AccessControl.FileSystemRights]::Write,[IO.FileShare]::None,4096,[IO.FileOptions]::None,(Get-InstallRunSecurity $UserSid -File))
    try{$stream.Write($Bytes,0,$Bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
}
function Save-InstallRunState {
    param($State)
    $directory=Get-InstallRunDirectory $State.RunId
    Assert-InstallRunTrust $directory
    $State.UpdatedUtc=[datetime]::UtcNow.ToString('o')
    $json=$State|ConvertTo-Json -Depth 18
    $temporary=Join-Path $directory ('state-'+[guid]::NewGuid().ToString('N')+'.partial')
    try{
        Write-InstallProtectedBytes $temporary ([Text.Encoding]::UTF8.GetBytes($json)) $State.UserSid
        Move-Item -LiteralPath $temporary -Destination (Join-Path $directory 'state.json') -Force
    }finally{if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary -Force}}
}
function Read-InstallRunState {
    param([ValidatePattern('^[a-f0-9]{32}$')][string]$RunId)
    $path=Join-Path (Get-InstallRunDirectory $RunId) 'state.json'
    Assert-InstallRunTrust $path
    $state=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
    if($state.SchemaVersion -ne 1 -or $state.RunId -cne $RunId -or $state.TaskName -cne ('FreshWindowsSetup-'+$RunId) -or $state.UserSid -ne (Get-InstallUserSid)){throw 'Resume state schema, run/task identity or Windows account does not match.'}
    if($state.Status -notin @('Ready','Running','AwaitingReboot','NeedsReview','Completed','Cancelled')){throw 'Invalid resume state status.'}
    return $state
}
function Get-InstallResumeTaskDefinition {
    param($State)
    $runner=Join-Path (Get-InstallRunDirectory $State.RunId) 'Resume-Run.ps1'
    $hostPath=Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell/v1.0/powershell.exe'
    $action=New-ScheduledTaskAction -Execute (Join-Path ([Environment]::SystemDirectory) 'cmd.exe') -Argument ('/d /k ""'+$hostPath+'" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+$runner+'" -RunId '+$State.RunId+' -Automatic"') -WorkingDirectory ([Environment]::SystemDirectory)
    $trigger=New-ScheduledTaskTrigger -AtLogOn -User $State.UserSid
    $principal=New-ScheduledTaskPrincipal -UserId $State.UserSid -LogonType Interactive -RunLevel Highest
    $settings=New-ScheduledTaskSettingsSet -StartWhenAvailable -RunOnlyIfNetworkAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit ([timespan]::Zero)
    New-ScheduledTask -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Continue the user-approved Windows app installation after reboot. Removed on completion.'
}
function Register-InstallResumeTask {
    param($State)
    $definition=Get-InstallResumeTaskDefinition $State
    Register-ScheduledTask -TaskName $State.TaskName -InputObject $definition -ErrorAction Stop|Out-Null
    $service=$null;$folder=$null;$task=$null
    try{
        $service=New-Object -ComObject Schedule.Service;$service.Connect();$folder=$service.GetFolder('\');$task=$folder.GetTask($State.TaskName)
        # TASK_DONT_ADD_PRINCIPAL_ACE: retain explicit read/execute-only user access.
        $task.SetSecurityDescriptor(('O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;GRGX;;;'+$State.UserSid+')'),16)
    }catch{Unregister-ScheduledTask -TaskName $State.TaskName -Confirm:$false -ErrorAction SilentlyContinue;throw}
    finally{foreach($item in @($task,$folder,$service)){if($null -ne $item -and [Runtime.InteropServices.Marshal]::IsComObject($item)){[Runtime.InteropServices.Marshal]::ReleaseComObject($item)|Out-Null}}}
}
function Remove-InstallResumeTask {
    param($State)
    if(Get-ScheduledTask -TaskName $State.TaskName -ErrorAction SilentlyContinue){Unregister-ScheduledTask -TaskName $State.TaskName -Confirm:$false -ErrorAction Stop}
}
function Get-InstallExecutionPlan {
    param([object[]]$Plan)
    $all=@($Plan)
    if(@($all|Where-Object packageId -eq 'vcredist-all').Count){$all=@($all|Where-Object packageId -ne 'vcredist140')}
    $all=@($all|Sort-Object @{e={switch -Regex ($_.packageId){'^vcredist' {0;break} '^dotnet-7\.' {1;break} '^dotnet-8\.' {2;break} '^dotnet' {3;break} default {10}}}},name,packageId)
    $selected=@{};foreach($app in $all){$selected[$app.packageId]=$true}
    $done=@{}
    while($done.Count -lt $all.Count){
        $next=$null
        foreach($app in $all){
            if($done.ContainsKey($app.packageId)){continue}
            $dependency=switch($app.packageId){'openrgb.install' {'pawnio'} 'streamlink-twitch-gui' {'streamlink'} default {''}}
            if($dependency -and $selected.ContainsKey($dependency) -and -not $done.ContainsKey($dependency)){continue}
            $next=$app;break
        }
        if($null -eq $next){throw 'Execution plan contains duplicate packages or a dependency cycle.'}
        $done[$next.packageId]=$true;$next
    }
}
function New-InstallRun {
    [CmdletBinding(SupportsShouldProcess=$true)]
    param([object[]]$AppPlan,[object[]]$DriverCandidates,[string[]]$ApprovedDriverIds,[string]$Executable,[string]$ChocolateyVersion,[string]$SourceDirectory)
    if(-not(Test-SetupAdministrator)){throw 'Creating a resumable installation requires Administrator rights.'}
    if(-not $PSCmdlet.ShouldProcess('Current account','Persist installation plan and register temporary post-reboot continuation')){return}
    $id=[guid]::NewGuid().ToString('N');$sid=Get-InstallUserSid
    $directory=New-InstallRunDirectory $id $sid
    $snapshots=@()
    foreach($name in @('Start-Setup.ps1','Install-ChocolateyApps.ps1','Install-Chocolatey.Core.psm1','Install-Chocolatey.apps.json','Settings.ps1','Settings.Core.psm1','Settings.Profiles.json','Drivers.Core.psm1','Install-Run.Core.psm1','Resume-Run.ps1','Freshkit.Status.ps1')){
        $source=Join-Path $SourceDirectory $name
        $target=Join-Path $directory $name
        Write-InstallProtectedBytes $target ([IO.File]::ReadAllBytes($source)) $sid
        $snapshots+=@([pscustomobject]@{Name=$name;SHA256=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash})
    }
    foreach($approved in $ApprovedDriverIds){if(@($DriverCandidates|Where-Object {$_.Id -eq $approved -and $_.CanOffer}).Count -ne 1){throw 'Driver approval is not bound to one applicable candidate.'}}
    $driverQueue=@($DriverCandidates|Where-Object {$_.Id -in $ApprovedDriverIds})
    $apps=@(Get-InstallExecutionPlan $AppPlan)
    $state=[pscustomobject]@{SchemaVersion=1;RunId=$id;UserSid=$sid;Machine=$env:COMPUTERNAME;Executable=$Executable;ChocolateyVersion=$ChocolateyVersion;TaskName=('FreshWindowsSetup-'+$id);Status='Ready';CreatedUtc=[datetime]::UtcNow.ToString('o');UpdatedUtc='';DriverQueue=$driverQueue;AppQueue=$apps;DriverResults=@();AppResults=@();InFlight=$null;Attempts=[pscustomobject]@{};Progress=0;RebootCount=0;RebootProgress=0;NoProgressReboots=0;WaitingBootId='';LastRebootReason='';Failure='';CleanupError='';Snapshots=$snapshots}
    $state|Add-Member -NotePropertyName FreshkitStagePath -NotePropertyValue ([string]$env:FRESHKIT_STAGE_PATH)
    Add-InstallRunScratch $state $SourceDirectory
    foreach($candidate in $DriverCandidates){
        if($candidate.Id -in $ApprovedDriverIds){continue}
        $status=if($candidate.Status -eq 'Candidate'){'NotSelected'}elseif($candidate.Status -eq 'HardwareUnknown'){'Failed'}else{$candidate.Status}
        $version=if($candidate.PSObject.Properties['InstalledVersion']){$candidate.InstalledVersion}else{''}
        $state.DriverResults+=@([pscustomobject]@{Id=$candidate.Id;Name=$candidate.Name;PackageId=$candidate.Id;Status=$status;Version=$version;ExitCode=$null;Detail=$candidate.Detail})
    }
    Set-FreshkitStage '' '' '' -RunId $state.RunId
    Save-InstallRunState $state
    try{Register-InstallResumeTask $state}catch{Set-InstallRunReview $state ('Resume registration failed; no installers were launched. '+$_.Exception.Message);throw}
    return $state
}
function Assert-InstallSnapshot {
    param($State)
    $directory=Get-InstallRunDirectory $State.RunId
    foreach($file in $State.Snapshots){
        if($file.Name -notmatch '\A[A-Za-z0-9.-]+\.(ps1|psm1|json)\z' -or $file.SHA256 -notmatch '\A[A-Fa-f0-9]{64}\z'){throw 'Invalid resume snapshot manifest.'}
        $path=Join-Path $directory $file.Name;Assert-InstallRunTrust $path
        if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.SHA256){throw 'Persisted installer code/catalogue changed. Resume was refused.'}
    }
}
function Test-RunPackageRegistered {
    param($State,[string]$PackageId,$Item=$null)
    if($null -ne $Item -and $Item.PSObject.Properties['githubRepo'] -and $Item.githubRepo){return [bool](Test-SetupGitHubInstalled $Item.name).Installed}
    if($null -ne $Item -and $Item.PSObject.Properties['wingetId'] -and $Item.wingetId){return [bool](Test-SetupWingetInstalled $Item.wingetId).Installed}
    $output=@(& $State.Executable list --exact $PackageId --limit-output)
    if($LASTEXITCODE -ne 0){throw 'Unable to reconcile interrupted Chocolatey package state.'}
    return (@($output|Where-Object {$_ -match ('\A'+[regex]::Escape($PackageId)+'\|[^|]+\z')}).Count -eq 1)
}
function Add-InstallRunResult {
    param($State,[string]$Phase,$Result)
    if($Phase -eq 'Driver'){$State.DriverResults=@($State.DriverResults)+@($Result);$State.DriverQueue=@($State.DriverQueue|Select-Object -Skip 1)}
    else{$State.AppResults=@($State.AppResults)+@($Result);$State.AppQueue=@($State.AppQueue|Select-Object -Skip 1)}
    $State.InFlight=$null;$State.Progress=[int]$State.Progress+1
    Save-InstallRunState $State
}
function Set-InstallRunReview {
    param($State,[string]$Reason)
    $State.Status='NeedsReview';$State.Failure=$Reason;Save-InstallRunState $State
    $stage=if($State.DriverQueue.Count){'Drivers'}elseif($State.AppQueue.Count){'Apps'}else{'Bootstrap'}
    Set-FreshkitStage $stage 'Failed' $Reason
    Show-InstallRunReport $State
    try{Remove-InstallResumeTask $State;Remove-InstallRunFiles $State}catch{$State.CleanupError=$_.Exception.Message;Save-InstallRunState $State}
}
function Set-InstallRunAwaitingReboot {
    param($State,[string]$Reason)
    if($State.RebootCount -ge 8 -or $State.NoProgressReboots -ge 2){Set-InstallRunReview $State 'Repeated reboot requests without installation progress. Review Windows Update/pending-reboot state manually.';return}
    $State.Status='AwaitingReboot';$State.WaitingBootId=Get-InstallBootId;$State.LastRebootReason=$Reason;$State.RebootCount=[int]$State.RebootCount+1;$State.RebootProgress=$State.Progress
    Save-InstallRunState $State
}
function Remove-InstallRunFiles {
    param($State)
    Remove-InstallScratch $State
    $directory=Get-InstallRunDirectory $State.RunId
    Assert-InstallRunTrust $directory
    $names=@('Start-Setup.ps1','Install-ChocolateyApps.ps1','Install-Chocolatey.Core.psm1','Install-Chocolatey.apps.json','Settings.ps1','Settings.Core.psm1','Settings.Profiles.json','Drivers.Core.psm1','Install-Run.Core.psm1','Resume-Run.ps1','Freshkit.Status.ps1','state.json','Report.txt','Report.csv')
    foreach($name in $names){
        $path=Join-Path $directory $name
        if(Test-Path -LiteralPath $path){Assert-InstallRunTrust $path;Remove-Item -LiteralPath $path -Force}
    }
    foreach($file in @(Get-ChildItem -LiteralPath $directory -File -Force|Where-Object Name -match '^(state|report)-[a-f0-9]{32}\.partial$')){
        Assert-InstallRunTrust $file.FullName;Remove-Item -LiteralPath $file.FullName -Force
    }
    # Never recursively delete: unexpected contents must be reviewed, not erased.
    if(@(Get-ChildItem -LiteralPath $directory -Force).Count){throw 'Unexpected files remain in the continuation directory; automatic cleanup stopped.'}
    Remove-Item -LiteralPath $directory -Force -ErrorAction Stop
    $base=Get-InstallResumeRoot
    if((Test-Path -LiteralPath $base) -and -not @(Get-ChildItem -LiteralPath $base -Force).Count){Remove-Item -LiteralPath $base -Force -ErrorAction SilentlyContinue}
}
function Add-InstallRunScratch {
    param($State,[string]$SourceDirectory)
    $base=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    $root=[IO.Path]::GetFullPath($SourceDirectory).TrimEnd('\')
    if([IO.Path]::GetDirectoryName($root) -ne $base -or [IO.Path]::GetFileName($root) -notmatch '^FreshWindowsSetup-[a-f0-9]{32}$'){return}
    if(-not $State.PSObject.Properties['SourceScratchDirectories']){$State|Add-Member -NotePropertyName SourceScratchDirectories -NotePropertyValue @()}
    if($root -notin @($State.SourceScratchDirectories|ForEach-Object {$_.Root})){
        $State.SourceScratchDirectories+=@([pscustomobject]@{Root=$root;Base=$base})
    }
}
function Remove-InstallScratch {
    param($State)
    if(-not $State.PSObject.Properties['SourceScratchDirectories']){return}
    foreach($scratch in $State.SourceScratchDirectories){
        # Installation is finished. Loaded scripts can be removed while the summary stays visible.
        $root=[IO.Path]::GetFullPath($scratch.Root).TrimEnd('\')
        $base=[IO.Path]::GetFullPath($scratch.Base).TrimEnd('\')
        if([IO.Path]::GetDirectoryName($root) -ne $base -or [IO.Path]::GetFileName($root) -notmatch '^FreshWindowsSetup-[a-f0-9]{32}$'){throw 'Unexpected extraction cleanup path.'}
        if(-not(Test-Path -LiteralPath $root)){continue}
        $ancestor=$root
        while($ancestor){
            if((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reparse point in extraction cleanup path.'}
            $ancestor=[IO.Path]::GetDirectoryName($ancestor)
        }
        foreach($name in @('Start-Setup.ps1','Install-ChocolateyApps.ps1','Install-Chocolatey.Core.psm1','Install-Chocolatey.apps.json','Settings.ps1','Settings.Core.psm1','Settings.Profiles.json','Drivers.Core.psm1','Install-Run.Core.psm1','Resume-Run.ps1','Freshkit.Status.ps1')){
            $path=Join-Path $root $name
            if(Test-Path -LiteralPath $path){
                $item=Get-Item -LiteralPath $path -Force
                if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Unexpected extraction file type; cleanup stopped.'}
                Remove-Item -LiteralPath $path -Force
            }
        }
        if(@(Get-ChildItem -LiteralPath $root -Force).Count){throw 'Unexpected extraction contents; cleanup stopped.'}
        Remove-Item -LiteralPath $root -Force
    }
}
function Complete-InstallRun {
    param($State)
    Update-FreshkitEngineStages $State -Finished
    $State.Status='Completed';$State.InFlight=$null;Save-InstallRunState $State
    try{
        Remove-InstallResumeTask $State
        Remove-InstallRunFiles $State
    }catch{$State.CleanupError=$_.Exception.Message;Save-InstallRunState $State}
}
function Invoke-InstallRunWorker {
    param($State)
    if(-not(Test-SetupAdministrator)){throw 'Resume must run in the approved Administrator account.'}
    if($State.UserSid -ne (Get-InstallUserSid)){throw 'Resume belongs to a different Windows account.'}
    if($State.Status -in @('Completed','Cancelled','NeedsReview')){return $State}
    Assert-InstallSnapshot $State
    $null=Assert-ChocolateyReady $State.Executable $State.ChocolateyVersion
    if($State.Status -eq 'AwaitingReboot'){
        if((Get-InstallBootId) -eq $State.WaitingBootId){return $State}
        if($State.Progress -eq $State.RebootProgress -and (Test-InstallPendingReboot)){$State.NoProgressReboots=[int]$State.NoProgressReboots+1}else{$State.NoProgressReboots=0}
    }
    $State.Status='Running';Save-InstallRunState $State
    if(-not $State.DriverQueue.Count -and -not $State.AppQueue.Count){Complete-InstallRun $State;return $State}
    if(Test-InstallPendingReboot){Set-InstallRunAwaitingReboot $State 'Windows reports a pending reboot before the next installer.';return $State}
    while($State.DriverQueue.Count -or $State.AppQueue.Count){
        $phase=if($State.DriverQueue.Count){'Driver'}else{'App'}
        Update-FreshkitEngineStages $State
        $item=if($phase -eq 'Driver'){$State.DriverQueue[0]}else{$State.AppQueue[0]}
        $package=if($phase -eq 'Driver'){$item.Id}else{$item.packageId}
        $attemptKey=$phase+'-'+$package
        if($null -ne $State.InFlight){
            if($State.InFlight.PackageId -ne $package -or $State.InFlight.Phase -ne $phase){throw 'Interrupted item does not match the remaining queue.'}
            $registered=Test-RunPackageRegistered $State $package $item
            $verifiedDriver=($phase -ne 'Driver' -or @(Get-DriverRegistryEvidence $package).Count)
            if($phase -eq 'Driver' -and $package -eq 'nvidia-display-driver'){$verifiedDriver=$verifiedDriver -and @(Get-DriverRegistryEvidence 'nvidia-app').Count -gt 0}
            if($registered -and $verifiedDriver){
                $result=[pscustomobject]@{Id=$package;Name=$item.name;PackageId=$package;Status='RecoveredInstalled';ExitCode=$null;Version='';Detail='Interrupted item reconciled with local package registration after restart.'}
                Add-InstallRunResult $State $phase $result;continue
            }
        }
        $attempts=0;if($State.Attempts.PSObject.Properties[$attemptKey]){$attempts=[int]$State.Attempts.$attemptKey}
        if($attempts -ge 2){
            $result=[pscustomobject]@{Id=$package;Name=$item.Name;PackageId=$package;Status='Failed';ExitCode=$null;Version='';Detail='Interrupted installer retry limit reached.'}
            Add-InstallRunResult $State $phase $result;continue
        }
        $State.Attempts|Add-Member -NotePropertyName $attemptKey -NotePropertyValue ($attempts+1) -Force
        $State.InFlight=[pscustomobject]@{Phase=$phase;PackageId=$package;StartedUtc=[datetime]::UtcNow.ToString('o')}
        Save-InstallRunState $State
        try{
            if($phase -eq 'Driver'){
                $inventory=Get-DriverDeviceInventory
                $fresh=@(Get-DriverCandidates $inventory|Where-Object Id -eq $package)[0]
                if(-not $fresh.CanOffer){
                    if($fresh.Status -eq 'HardwareUnknown'){throw 'Hardware cannot be revalidated for the approved driver.'}
                    $result=[pscustomobject]@{Id=$package;Name=$item.Name;PackageId=$package;Status='NotApplicable';ExitCode=$null;Version='';Detail=$fresh.Detail}
                }else{
                    $fresh.PackageParameters=$item.PackageParameters
                    $stage=Invoke-DriverStage @($fresh) $State.Executable -ApprovedIds @($package) -Confirm:$false
                    $result=$stage.Results[0]
                }
            }else{$result=@(Invoke-ChocolateyAppPlan @($item) $State.Executable -Confirm:$false)[0]}
        }catch{$result=[pscustomobject]@{Id=$package;Name=$item.Name;PackageId=$package;Status='Failed';ExitCode=$null;Version='';Detail=$_.Exception.Message}}
        if($result.Status -eq 'DeferredForReboot'){
            # No installation occurred: retain this item for the next boot.
            $State.InFlight=$null
            Set-InstallRunAwaitingReboot $State ($package+' deferred until Windows restarts.');return $State
        }
        Add-InstallRunResult $State $phase $result
        if($result.Status -eq 'RebootRequired' -or $result.ExitCode -in @(3010,1641) -or (Test-InstallPendingReboot)){
            Set-InstallRunAwaitingReboot $State ($package+' requires a restart before continuing.');return $State
        }
    }
    Complete-InstallRun $State
    return $State
}
function Update-FreshkitEngineStages {
    param($State,[switch]$Finished)
    foreach($phase in @('Driver','App')){
        $id=if($phase -eq 'Driver'){'Drivers'}else{'Apps'}
        $results=@(if($phase -eq 'Driver'){$State.DriverResults|Where-Object Status -ne 'NotSelected'}else{$State.AppResults})
        $queue=@(if($phase -eq 'Driver'){$State.DriverQueue}else{$State.AppQueue})
        if($queue.Count){
            if($phase -eq 'Driver' -or -not $State.DriverQueue.Count){Set-FreshkitStage $id 'Running' ($queue.Count.ToString()+' remaining')}
        }elseif($results.Count){
            $failed=@($results|Where-Object {$_.Status -in @('Failed','UnverifiedDriverInstall')})
            Set-FreshkitStage $id $(if($failed.Count){'Failed'}else{'Done'}) ($results.Count.ToString()+' processed; '+$failed.Count+' failed')
        }elseif($Finished -or $phase -eq 'Driver'){Set-FreshkitStage $id 'Skipped' 'No matching work'}
    }
}
function Invoke-InstallRun {
    param($State)
    $mutex=[Threading.Mutex]::new($false,('Global\FreshWindowsSetup-'+$State.RunId))
    $acquired=$false
    try{
        try{$acquired=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$acquired=$true}
        if(-not $acquired){Write-Host 'This installation is already running in another window.';return $State}
        Invoke-InstallRunWorker $State
    }finally{if($acquired){$mutex.ReleaseMutex()};$mutex.Dispose()}
}
function Request-InstallRunReboot {
    param($State)
    if($State.Status -ne 'AwaitingReboot'){return}
    Write-Host $State.LastRebootReason
    if((Get-InstallBootId) -ne $State.WaitingBootId){throw 'Reboot checkpoint does not match the current boot.'}
    Write-Host 'Progress is saved. Restarting Windows automatically. Sign back into this account to continue; internet access is required.'
    Restart-Computer -Force -Confirm:$false
}
function Cancel-InstallRun {
    param($State)
    $mutex=[Threading.Mutex]::new($false,('Global\FreshWindowsSetup-'+$State.RunId));$acquired=$false
    try{
        try{$acquired=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$acquired=$true}
        if(-not $acquired){throw 'This installation is actively running. Cancel only after its current operation has finished.'}
        $State.Status='Cancelled';$State.InFlight=$null;Save-InstallRunState $State
        Remove-InstallResumeTask $State
        Remove-InstallRunFiles $State
        return $State
    }finally{if($acquired){$mutex.ReleaseMutex()};$mutex.Dispose()}
}
function Get-InstallReportRows {
    param($State)
    # Last outcome wins after a reviewed retry; queued items are never counted as installed.
    $rows=[Collections.Generic.List[object]]::new()
    foreach($phase in @('Driver','App')){
        $results=if($phase -eq 'Driver'){@($State.DriverResults)}else{@($State.AppResults)}
        $queue=if($phase -eq 'Driver'){@($State.DriverQueue)}else{@($State.AppQueue)}
        $latest=[ordered]@{}
        foreach($result in $results){$latest[$result.PackageId]=$result}
        foreach($item in $queue){
            $id=if($phase -eq 'Driver'){$item.Id}else{$item.packageId}
            if($latest.Contains($id) -and $latest[$id].Status -in @('Failed','UnverifiedDriverInstall')){continue}
            $status=if($State.Status -eq 'Cancelled'){'Cancelled'}elseif($State.Status -eq 'NeedsReview' -and $State.InFlight -and $State.InFlight.PackageId -eq $id){'Failed'}else{'Pending'}
            $detail=if($State.InFlight -and $State.InFlight.PackageId -eq $id){'Interrupted or unresolved installation; review before retrying.'}else{'Not yet installed by this run.'}
            if($status -eq 'Failed'){$detail=$State.Failure}
            $latest[$id]=[pscustomobject]@{PackageId=$id;Name=$item.Name;Status=$status;Version='';ExitCode=$null;Detail=$detail}
        }
        foreach($result in $latest.Values){
            $category=switch($result.Status){
                {$_ -in @('PackageInstalled','RebootRequired','RecoveredInstalled')} {'Installed';break}
                {$_ -in @('Failed','UnverifiedDriverInstall')} {'Failed';break}
                'Pending' {'Pending';break}
                'Cancelled' {'Cancelled';break}
                default {'Skipped'}
            }
            $version=if($result.Version){$result.Version}elseif($category -eq 'Installed'){'Not recorded'}else{''}
            $group=if($phase -eq 'Driver'){'Driver'}elseif($result.PackageId -match '^(vcredist|dotnet)'){'Runtime'}else{'App'}
            $rows.Add([pscustomobject]@{Type=$group;Name=$result.Name;Package=$result.PackageId;Outcome=$category;Status=$result.Status;Version=$version;ExitCode=$result.ExitCode;Detail=$result.Detail})
        }
    }
    return $rows.ToArray()
}
function Show-InstallRunReport {
    param($State)
    $rows=@(Get-InstallReportRows $State)
    Write-Host ("`nInstallation status: "+$State.Status)
    $summary=@(foreach($outcome in @('Installed','Skipped','Failed','Pending','Cancelled')){'{0}: {1}' -f $outcome,@($rows|Where-Object Outcome -eq $outcome).Count})
    Write-Host ($summary -join ' | ')
    $rows|Select-Object Type,Name,Outcome,Version,ExitCode|Format-Table -Wrap -AutoSize|Out-Host
    foreach($row in @($rows|Where-Object Outcome -eq 'Failed')){Write-Host ('FAILED: '+$row.Name+' ('+$row.Package+'): '+$row.Detail)}
    # Freshkit: keep a user-readable copy of the outcome for its progress/summary view.
    try{if($env:LOCALAPPDATA){$reportDir=Join-Path $env:LOCALAPPDATA 'Freshkit\runs';[IO.Directory]::CreateDirectory($reportDir)|Out-Null;[pscustomobject]@{RunId=$State.RunId;Status=$State.Status;Failure=$State.Failure;CleanupError=$State.CleanupError;FinishedUtc=[datetime]::UtcNow.ToString('o');Rows=$rows}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $reportDir ($State.RunId+'.json')) -Encoding UTF8}}catch{}
    Write-Host ('Chocolatey log: '+(Join-Path (Split-Path -Parent (Split-Path -Parent $State.Executable)) 'logs\chocolatey.log'))
}
function Get-PendingInstallRuns {
    $base=Get-InstallResumeRoot
    if(-not(Test-Path -LiteralPath $base)){return}
    foreach($directory in @(Get-ChildItem -LiteralPath $base -Directory|Where-Object Name -match '^[a-f0-9]{32}$')){
        try{$state=Read-InstallRunState $directory.Name;if($state.Status -in @('Ready','Running','AwaitingReboot','NeedsReview')){$state}}catch{Write-Warning $_.Exception.Message}
    }
}
Export-ModuleMember -Function Get-InstallExecutionPlan,New-InstallRun,Invoke-InstallRun,Save-InstallRunState,Read-InstallRunState,Get-InstallRunDirectory,Get-PendingInstallRuns,Get-InstallResumeTaskDefinition,Request-InstallRunReboot,Assert-InstallRunTrust,Assert-InstallSnapshot,Set-InstallRunReview,Register-InstallResumeTask,Remove-InstallResumeTask,Get-InstallUserSid,Cancel-InstallRun,Get-InstallReportRows,Show-InstallRunReport,Add-InstallRunScratch
