#requires -Version 5.1
# Freshkit: register or remove the weekly automatic-update task (elevated).
# The task is self-contained: it doesn't need Freshkit.exe, so it keeps working if the USB stick is gone.
# -Store: Microsoft Store product IDs to upgrade each week. -GitHub: 'owner/repo|Display name;...' GitHub-release apps
# (updated only when already installed). Their updater functions are copied from the engine into the task itself.
param([string]$LogFile='',[switch]$Disable,[ValidateSet('Monday','Tuesday','Wednesday','Thursday','Friday','Saturday','Sunday')][string]$Day='Sunday',[ValidatePattern('^\d{2}:\d{2}$')][string]$Time='03:00',[string]$Store='',[string]$GitHub='')
$ErrorActionPreference='Stop'
if($LogFile){try{[IO.Directory]::CreateDirectory((Split-Path -Parent $LogFile))|Out-Null;Start-Transcript -LiteralPath $LogFile -Append|Out-Null}catch{}}
$taskPath='\Freshkit\';$taskName='Weekly updates'
$code=0
try{
    if($Disable){
        Unregister-ScheduledTask -TaskName $taskName -TaskPath $taskPath -Confirm:$false -ErrorAction SilentlyContinue
        Write-Host 'Automatic weekly updates turned off.'
    }else{
        $body=@'
$data=Join-Path $env:LOCALAPPDATA 'Freshkit';$log=Join-Path $data 'logs';[IO.Directory]::CreateDirectory($log)|Out-Null
$file=Join-Path $log ('auto-update-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')
Start-Transcript -LiteralPath $file|Out-Null
$ok=$true
$root=[Environment]::GetEnvironmentVariable('ChocolateyInstall','Machine');if(-not $root){$root=Join-Path $env:ProgramData 'chocolatey'}
$choco=Join-Path $root 'bin\choco.exe'
if(Test-Path -LiteralPath $choco){Write-Host '==> Chocolatey';& $choco upgrade all -y --no-progress;if($LASTEXITCODE -notin @(0,3010,1641)){$ok=$false}}
$w=(Get-Command winget.exe -ErrorAction SilentlyContinue|Select-Object -First 1).Source
if(-not $w){$w=Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\winget.exe'}
if(Test-Path -LiteralPath $w){Write-Host '==> winget';& $w upgrade --all --silent --source winget --accept-package-agreements --accept-source-agreements --disable-interactivity;if($LASTEXITCODE -notin @(0,-1978335189)){$ok=$false}}
#FRESHKIT-STORE-GITHUB#
Stop-Transcript|Out-Null
# Keep the log only when something failed; leave no folder behind otherwise.
if($ok){
    Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue
    foreach($d in @($log,(Join-Path $data 'runs'),$data)){if((Test-Path -LiteralPath $d) -and -not @(Get-ChildItem -LiteralPath $d -Force).Count){Remove-Item -LiteralPath $d -Force -ErrorAction SilentlyContinue}}
}
'@
        # Store and GitHub apps: data is passed as single-quoted literals; GitHub updates reuse the engine's functions.
        $extra=''
        $storeIds=@($Store -split ','|ForEach-Object {$_.Trim()}|Where-Object {$_ -cmatch '\A(?:9[A-Z0-9]{11}|XP[A-Z0-9]{12})\z'})
        if($storeIds.Count){
            $extra+="if(Test-Path -LiteralPath `$w){Write-Host '==> Microsoft Store';foreach(`$id in @('"+($storeIds -join "','")+"')){& `$w upgrade --id `$id --exact --silent --source msstore --accept-package-agreements --accept-source-agreements --disable-interactivity;if(`$LASTEXITCODE -notin @(0,-1978335189,-1978335212)){`$ok=`$false}}}`n"
        }
        $pairs=@($GitHub -split ';'|ForEach-Object {$p=$_ -split '\|',2;if($p.Count -eq 2 -and $p[0].Trim() -match '\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}\z' -and $p[1].Trim()){$p[0].Trim()+'|'+$p[1].Trim()}})
        $githubBlock=''
        if($pairs.Count){
            $module=Join-Path $PSScriptRoot 'Install-Chocolatey.Core.psm1'
            $names=@('Test-SetupGitHubRepo','ConvertTo-SetupNameKey','Test-SetupGitHubInstalled','Save-SetupGitHubIdentity','Select-SetupGitHubAsset','Get-SetupGitHubWebRelease','Get-SetupGitHubRelease','Get-SetupInstallerKind','Invoke-SetupGitHubApp')
            $ast=[Management.Automation.Language.Parser]::ParseFile($module,[ref]$null,[ref]$null)
            $functions=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $names -contains $n.Name},$true)|ForEach-Object {(($_.Extent.Text -split "`n")|Where-Object {$_ -notmatch '^\s*#'}) -join "`n"})
            if($functions.Count -ne $names.Count){throw 'The GitHub updater functions could not be read from the engine.'}
            $list=($pairs|ForEach-Object {"'"+$_.Replace("'","''")+"'"}) -join ','
            $githubBlock=($functions -join "`n")+"`nWrite-Host '==> GitHub apps'`nforeach(`$pair in @($list)){`$p=`$pair -split '\|',2;try{if((Test-SetupGitHubInstalled -Name `$p[1] -Repo `$p[0]).Installed){`$r=Invoke-SetupGitHubApp ([pscustomobject]@{githubRepo=`$p[0];name=`$p[1];packageParameters='';upgrade=`$true});Write-Host (`$p[1]+': '+`$r.Status)}}catch{Write-Host (`$p[1]+': '+`$_.Exception.Message);`$ok=`$false}}`n"
        }
        $full=$body.Replace('#FRESHKIT-STORE-GITHUB#',$extra+$githubBlock)
        # Compress a large self-contained payload instead of silently omitting GitHub apps.
        $body=$full
        $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($body))
        if($encoded.Length -gt 31000){
            $buffer=[IO.MemoryStream]::new()
            $gzip=[IO.Compression.GZipStream]::new($buffer,[IO.Compression.CompressionMode]::Compress,$true)
            try{$bytes=[Text.Encoding]::UTF8.GetBytes($body);$gzip.Write($bytes,0,$bytes.Length)}finally{$gzip.Dispose()}
            $payload=[Convert]::ToBase64String($buffer.ToArray());$buffer.Dispose()
            $body="`$b=[IO.MemoryStream]::new([Convert]::FromBase64String('$payload'));`$g=[IO.Compression.GZipStream]::new(`$b,[IO.Compression.CompressionMode]::Decompress);`$r=[IO.StreamReader]::new(`$g,[Text.Encoding]::UTF8);try{& ([scriptblock]::Create(`$r.ReadToEnd()))}finally{`$r.Dispose();`$b.Dispose()}"
            $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($body))
        }
        if($encoded.Length -gt 31000){throw 'The weekly updater is too large to schedule. Reduce its app list.'}
        $action=New-ScheduledTaskAction -Execute (Join-Path $PSHOME 'powershell.exe') -Argument ('-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand '+$encoded)
        $at=[datetime]::ParseExact($Time,'HH:mm',[Globalization.CultureInfo]::InvariantCulture)
        $trigger=New-ScheduledTaskTrigger -Weekly -DaysOfWeek $Day -At $at
        $principal=New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Highest
        $settings=New-ScheduledTaskSettingsSet -StartWhenAvailable -RunOnlyIfNetworkAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 4)
        Register-ScheduledTask -TaskName $taskName -TaskPath $taskPath -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Freshkit: update Chocolatey, winget, Microsoft Store and GitHub apps every week.' -Force|Out-Null
        Write-Host ("Automatic updates scheduled: every $Day at $Time (runs when you're signed in; catches up if the PC was off).")
    }
}catch{Write-Host ('ERROR: '+$_.Exception.Message);$code=1}
try{Stop-Transcript|Out-Null}catch{}
exit $code
