#requires -Version 5.1
# Freshkit elevated job: one UAC prompt for the whole setup.
# Order: offline-kit drivers -> Wi-Fi networks -> Windows tweaks (Win11Debloat) -> engine (Chocolatey, drivers, apps).
# Settings restore runs afterwards from Freshkit itself (no elevation needed).
param([Parameter(Mandatory=$true)][string]$JobFile)
$ErrorActionPreference='Stop'
$job=Get-Content -LiteralPath $JobFile -Raw|ConvertFrom-Json
. (Join-Path $PSScriptRoot 'Freshkit.Status.ps1')
$env:FRESHKIT_STAGE_PATH=$job.StagePath
[IO.Directory]::CreateDirectory($job.LogDir)|Out-Null
try{Start-Transcript -LiteralPath $job.LogFile -Append|Out-Null}catch{}
function Step([string]$Name){Write-Host '';Write-Host ('==> '+$Name)}
function Has([string]$Name){return ($job.PSObject.Properties[$Name] -and $job.$Name)}
$code=0
try{
    $isAdmin=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if(-not $isAdmin){throw 'The Freshkit job must run as administrator.'}

    if(Has 'DriverKit'){
        Step 'Installing drivers from the offline kit'
        Invoke-FreshkitStep 'OfflineDrivers' {
            if(-not @(Get-ChildItem -LiteralPath $job.DriverKit -Filter *.inf -Recurse -File).Count){throw 'No driver INF files found in the selected kit.'}
            & (Join-Path $env:SystemRoot 'System32\pnputil.exe') /add-driver (Join-Path $job.DriverKit '*.inf') /subdirs /install 2>&1|ForEach-Object {Write-Host $_}
            Assert-FreshkitExit $LASTEXITCODE 'pnputil' -AllowReboot
        }
    }
    if(Has 'WifiKit'){
        Step 'Adding saved Wi-Fi networks from the offline kit'
        Invoke-FreshkitStep 'Wifi' {
            $files=@(Get-ChildItem -LiteralPath $job.WifiKit -Filter *.xml -File)
            if(-not $files.Count){throw 'No Wi-Fi profiles found in the selected kit.'}
            foreach($file in $files){
                & netsh.exe wlan add profile ('filename="'+$file.FullName+'"') user=current 2>&1|ForEach-Object {Write-Host $_}
                Assert-FreshkitExit $LASTEXITCODE 'netsh Wi-Fi import'
            }
        }
    }
    if(Has 'DebloatRoot'){
        Step 'Applying Windows tweaks (Win11Debloat)'
        # No undo backups, and no %LOCALAPPDATA%\Win11Debloat left behind (unless it was already there).
        $w11dData=Join-Path $env:LOCALAPPDATA 'Win11Debloat';$w11dExisted=Test-Path -LiteralPath $w11dData
        $arguments=@('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $job.DebloatRoot 'Win11Debloat.ps1'),'-Silent','-SkipRegistryBackup','-LogPath',$job.LogDir)+@($job.DebloatArgs)
        Write-Host ('Win11Debloat '+((@($job.DebloatArgs)) -join ' '))
        try {
            Invoke-FreshkitStep 'Tweaks' {
                & (Join-Path $PSHOME 'powershell.exe') @arguments 2>&1|ForEach-Object {$t=([string]$_).TrimEnd();if($t){Write-Host $t}}
                Assert-FreshkitExit $LASTEXITCODE 'Win11Debloat'
            }
        } finally {
            if(-not $w11dExisted -and (Test-Path -LiteralPath $w11dData)){Remove-Item -LiteralPath $w11dData -Recurse -Force -ErrorAction SilentlyContinue}
        }
    }
    if(Has 'Action'){
        $label=switch($job.Action){'Install' {'Installing Chocolatey, drivers and apps'} 'Drivers' {'Installing matching drivers'} 'Bootstrap' {'Installing / updating Chocolatey'} 'Resume' {'Continuing the saved installation'} default {$job.Action}}
        Step $label
        $params=@{Action=$job.Action;EngineDir=$job.EngineDir;ListPath=$job.ListPath;LogDir=$job.LogDir;IncludeDisabled=$true;Ui=$true}
        if(Has 'Apps'){$params.Apps=$job.Apps}
        if(Has 'Upgrade'){$params.Upgrade=$true}
        if(Has 'SkipDrivers'){$params.SkipDrivers=$true}
        if(Has 'RunId'){$params.RunId=$job.RunId}
        if(Has 'DriverIds'){$params.DriverIds=$job.DriverIds}
        if(Has 'SelfPath'){$params.SelfPath=$job.SelfPath}
        $global:LASTEXITCODE=0
        & $job.RunnerPath @params
        $code=[int]$global:LASTEXITCODE
    }
    Step ('Finished (exit code '+$code+')')
}catch{
    Write-Host ('ERROR: '+$_.Exception.Message)
    $code=1
}finally{
    try{Stop-Transcript|Out-Null}catch{}
}
exit $code
