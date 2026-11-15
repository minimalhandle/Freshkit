#requires -Version 5.1
# Freshkit: register or remove the weekly automatic-update task (elevated).
# The task is self-contained: it doesn't need Freshkit.exe, so it keeps working if the USB stick is gone.
param([string]$LogFile='',[switch]$Disable,[ValidateSet('Monday','Tuesday','Wednesday','Thursday','Friday','Saturday','Sunday')][string]$Day='Sunday',[ValidatePattern('^\d{2}:\d{2}$')][string]$Time='03:00')
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
Stop-Transcript|Out-Null
# Keep the log only when something failed; leave no folder behind otherwise.
if($ok){
    Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue
    foreach($d in @($log,(Join-Path $data 'runs'),$data)){if((Test-Path -LiteralPath $d) -and -not @(Get-ChildItem -LiteralPath $d -Force).Count){Remove-Item -LiteralPath $d -Force -ErrorAction SilentlyContinue}}
}
'@
        $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($body))
        $action=New-ScheduledTaskAction -Execute (Join-Path $PSHOME 'powershell.exe') -Argument ('-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand '+$encoded)
        $at=[datetime]::ParseExact($Time,'HH:mm',[Globalization.CultureInfo]::InvariantCulture)
        $trigger=New-ScheduledTaskTrigger -Weekly -DaysOfWeek $Day -At $at
        $principal=New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Highest
        $settings=New-ScheduledTaskSettingsSet -StartWhenAvailable -RunOnlyIfNetworkAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 4)
        Register-ScheduledTask -TaskName $taskName -TaskPath $taskPath -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Freshkit: update Chocolatey and winget apps every week.' -Force|Out-Null
        Write-Host ("Automatic updates scheduled: every $Day at $Time (runs when you're signed in; catches up if the PC was off).")
    }
}catch{Write-Host ('ERROR: '+$_.Exception.Message);$code=1}
try{Stop-Transcript|Out-Null}catch{}
exit $code
