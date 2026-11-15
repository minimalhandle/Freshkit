# Shared structured status for the UI and the protected reboot snapshot.
function Set-FreshkitStage {
    param([string]$Id,[string]$Status,[string]$Detail='',[string]$RunId='')
    $path=$env:FRESHKIT_STAGE_PATH
    if(-not $path){return} # CLI runs must never update an unrelated UI job.
    if(-not(Test-Path -LiteralPath $path)){return}
    $record=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
    if($RunId){$record|Add-Member -NotePropertyName EngineRunId -NotePropertyValue $RunId -Force}
    foreach($step in $record.Steps){if($step.Id -eq $Id){$step.Status=$Status;$step.Detail=$Detail}}
    if($Status -eq 'Failed'){
        foreach($step in $record.Steps){if($step.Status -eq 'Pending'){$step.Status='Blocked';$step.Detail='Waiting for the failed step to be retried'}}
    }
    $temporary=$path+'.partial'
    [IO.File]::WriteAllText($temporary,($record|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))
    [IO.File]::Replace($temporary,$path,[System.Management.Automation.Language.NullString]::Value)
    if($Id){Write-Host ('==> '+$Id+': '+$Status+$(if($Detail){' - '+$Detail}))}
}
function Assert-FreshkitExit {
    param([int]$Code,[string]$Command,[switch]$AllowReboot)
    if($Code -eq 0 -or ($AllowReboot -and $Code -in @(3010,1641))){return}
    throw "$Command failed (exit code $Code). See the activity log for details."
}
function Invoke-FreshkitStep {
    param([string]$Id,[scriptblock]$Work)
    Set-FreshkitStage $Id 'Running' 'In progress'
    try {& $Work;Set-FreshkitStage $Id 'Done' 'Completed successfully'}
    catch {Set-FreshkitStage $Id 'Failed' $_.Exception.Message;throw}
}
