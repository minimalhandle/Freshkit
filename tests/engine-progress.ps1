$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$temporary=Join-Path $root 'obj\engine-stage-test.json'
$old=$env:FRESHKIT_STAGE_PATH
try{
    $env:FRESHKIT_STAGE_PATH=$temporary
    @{Steps=@(@{Id='Drivers';Status='Pending';Detail=''},@{Id='Apps';Status='Pending';Detail=''},@{Id='Settings';Status='Pending';Detail=''})}|ConvertTo-Json -Depth 5|Set-Content $temporary
    Import-Module (Join-Path $root 'src\payload\Install-Run.Core.psm1') -Force -DisableNameChecking
    $module=Get-Module 'Install-Run.Core'
    $state=[pscustomobject]@{DriverQueue=@(@{Id='nvidia-display-driver'});AppQueue=@(@{packageId='steam'});DriverResults=@();AppResults=@()}
    & $module {param($s) Update-FreshkitEngineStages $s;Set-FreshkitStage '' '' '' -RunId 'test-run'} $state
    $record=Get-Content $temporary -Raw|ConvertFrom-Json
    if($record.EngineRunId -ne 'test-run' -or $record.Steps[0].Status -ne 'Running'){throw 'Driver stage or report identity missing'}
    $state.DriverQueue=@();$state.DriverResults=@(@{Status='Failed'})
    & $module {param($s) Update-FreshkitEngineStages $s} $state
    $record=Get-Content $temporary -Raw|ConvertFrom-Json
    if($record.Steps[0].Status -ne 'Failed' -or $record.Steps[1].Status -ne 'Running'){throw 'Driver failure was hidden or independent app work lost'}
    $state.AppQueue=@();$state.AppResults=@(@{Status='PackageInstalled'})
    & $module {param($s) Update-FreshkitEngineStages $s -Finished} $state
    $record=Get-Content $temporary -Raw|ConvertFrom-Json
    if($record.Steps[0].Status -ne 'Failed' -or $record.Steps[1].Status -ne 'Done'){throw 'Final stages do not reflect real outcomes'}
    Write-Host 'PASS engine stage transitions, persistent run identity, driver failures and independent app completion'
}finally{
    $env:FRESHKIT_STAGE_PATH=$old
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
}
