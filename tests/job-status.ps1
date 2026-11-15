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
}finally{
    $env:FRESHKIT_STAGE_PATH=$oldPath
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
}
