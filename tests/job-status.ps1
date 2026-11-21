$ErrorActionPreference='Stop'
$path=Join-Path (Split-Path -Parent $PSScriptRoot) 'src\payload\Freshkit.Status.ps1'
if(-not(Test-Path $path)){throw 'Missing stage status implementation'}
. $path
$temporary=Join-Path $env:TEMP ('Freshkit-stage-test-'+[guid]::NewGuid().ToString('N')+'.json')
$oldPath=$env:FRESHKIT_STAGE_PATH
try{
    $env:FRESHKIT_STAGE_PATH=$temporary
    @{Steps=@(@{Id='Tweaks';Status='Pending';Detail=''},@{Id='Apps';Status='Pending';Detail=''})}|ConvertTo-Json -Depth 5|Set-Content $temporary
    $caught=$false
    try{Invoke-FreshkitStep 'Tweaks' {
        & (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -NonInteractive -Command 'exit 23'
        Assert-FreshkitExit $LASTEXITCODE 'test command'
    }}catch{$caught=$true}
    if(-not $caught){throw 'Native command failure was swallowed'}
    $state=Get-Content $temporary -Raw|ConvertFrom-Json
    if($state.Steps[0].Status -ne 'Failed' -or $state.Steps[1].Status -ne 'Blocked'){throw 'Failure did not block later work'}
    Invoke-FreshkitStep 'Tweaks' {Assert-FreshkitExit 0 'test command'}
    $state=Get-Content $temporary -Raw|ConvertFrom-Json
    if($state.Steps[0].Status -ne 'Done'){throw 'Successful retry did not finish the step'}
    Assert-FreshkitExit 3010 'driver' -AllowReboot
    Write-Host 'PASS native failures, blocked stages, successful retries and reboot exit code'

    $jobDir=$temporary+'.work'
    [IO.Directory]::CreateDirectory($jobDir)|Out-Null
    $jobScript=Join-Path $jobDir 'Job.ps1'
    $jobFile=Join-Path $jobDir 'job.json'
    $logFile=Join-Path $jobDir 'job.log'
    $runnerFile=Join-Path $jobDir 'runner.ps1'
    Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\scripts\Job.ps1'),$path -Destination $jobDir
    Set-Content -LiteralPath $runnerFile "Write-Host 'Synthetic runner started'"
    @{
        ExpectedUserSid='S-1-0-0'; StagePath=$temporary; LogDir=$jobDir; LogFile=$logFile
        Action='Install'; RunnerPath=$runnerFile
    } | ConvertTo-Json | Set-Content -LiteralPath $jobFile
    $output=& (Join-Path $PSHOME 'powershell.exe') -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $jobScript -JobFile $jobFile | Out-String
    if($LASTEXITCODE -ne 1 -or $output -match 'Synthetic runner started' -or $output -notmatch 'same Windows account'){
        throw ('A job for a different Windows account did not stop with account guidance: '+$output)
    }
    Write-Host 'PASS the elevated worker rejects a different Windows account before running setup'
}finally{
    $env:FRESHKIT_STAGE_PATH=$oldPath
    if($jobDir){
        Remove-Item -LiteralPath $jobScript,$jobFile,$logFile,$runnerFile,(Join-Path $jobDir 'Freshkit.Status.ps1') -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $jobDir -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
}
