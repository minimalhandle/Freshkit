# Detection must remain read-only: all registry/Appx state below is injected.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$detector = Join-Path $root 'src/scripts/TweakState.ps1'
if (-not (Test-Path $detector)) { throw 'FAIL: read-only tweak detection is not implemented.' }
. $detector -DebloatRoot (Join-Path $root 'vendor/Win11Debloat')
function Assert-State($Expected, $Actual, $Because) {
    if ($Expected -cne $Actual) { throw "FAIL $Because : expected '$Expected', got '$Actual'" }
}
function New-Operation($Type='SetValue', $ValueType='DWord', $ValueData=1) {
    [pscustomobject]@{OperationType=$Type;KeyPath='HKEY_CURRENT_USER\Software\Synthetic';ValueName='test';ValueType=$ValueType;ValueData=$ValueData}
}
$present = { param($op) [pscustomobject]@{KeyExists=$true;ValueExists=$true;Kind='DWord';Value=1} }
$missingKey = { param($op) [pscustomobject]@{KeyExists=$false;ValueExists=$false;Kind=$null;Value=$null} }
$missingValue = { param($op) [pscustomobject]@{KeyExists=$true;ValueExists=$false;Kind=$null;Value=$null} }
$op = New-Operation
Assert-State 'Applied' (Get-FreshkitRegistryState @($op) $present) 'matching value'
Assert-State 'Not applied' (Get-FreshkitRegistryState @($op) $missingKey) 'missing set key'
Assert-State 'Not applied' (Get-FreshkitRegistryState @($op) $missingValue) 'missing set value'
Assert-State 'Not applied' (Get-FreshkitRegistryState @($op) { [pscustomobject]@{KeyExists=$true;ValueExists=$true;Kind='String';Value='1'} }) 'kind mismatch'
Assert-State 'Not applied' (Get-FreshkitRegistryState @($op) { [pscustomobject]@{KeyExists=$true;ValueExists=$true;Kind='DWord';Value=0} }) 'data mismatch'
foreach ($type in @('DeleteKey','DeleteValue')) {
    $delete = New-Operation $type
    Assert-State 'Applied' (Get-FreshkitRegistryState @($delete) $missingKey) 'already missing deletion'
    Assert-State 'Not applied' (Get-FreshkitRegistryState @($delete) $present) 'existing deletion target'
}
Assert-State 'Applied' (Get-FreshkitRegistryState @((New-Operation 'DeleteValue')) $missingValue) 'already missing value'
Assert-State 'Unknown' (Get-FreshkitRegistryState @($op) { throw 'access denied' }) 'failed registry query'
Assert-State 'Unknown' (Get-FreshkitRegistryState @((New-Operation 'SetValue' 'Hexb' @(1,2))) $present) 'unsupported registry kind'
Assert-State 'Unknown' (Get-FreshkitRegistryState @()) 'empty operation set'
Assert-State 'Unknown' (Get-FreshkitRegistryState @((New-Operation 'Unsupported'))) 'unsupported operation'
Assert-State 'Unknown' (Get-FreshkitRegistryState @($op) { $null }) 'null snapshot'
Assert-State 'Unknown' (Get-FreshkitRegistryState @($op,(New-Operation 'SetValue' 'Hexb' 0)) $missingKey) 'unknown takes precedence over a known mismatch'
Assert-State 'Applied' (Get-FreshkitRegistryState @((New-Operation 'SetValue' 'DWord' ([uint32]::MaxValue))) { [pscustomobject]@{KeyExists=$true;ValueExists=$true;Kind='DWord';Value=[uint32]::MaxValue} }) 'unsigned DWORD bits'
foreach ($data in @(@{Items=@()}, @{Items=@('only')})) {
    $arrayOp = New-Operation 'SetValue' 'Hex7' $data.Items
    Assert-State 'Applied' (Get-FreshkitRegistryState @($arrayOp) { [pscustomobject]@{KeyExists=$true;ValueExists=$true;Kind='MultiString';Value=$data.Items} }) 'empty or single element array'
}
foreach ($case in @(@{Type='Binary';Kind='Binary';Data=@(1,2)}, @{Type='Hex7';Kind='MultiString';Data=@('a','b')})) {
    $sequence = New-Operation 'SetValue' $case.Type $case.Data
    $snapshot = [pscustomobject]@{KeyExists=$true;ValueExists=$true;Kind=$case.Kind;Value=$case.Data}
    Assert-State 'Applied' (Get-FreshkitRegistryState @($sequence) { $snapshot }) 'matching ordered array'
    $snapshot.Value = @($case.Data[1],$case.Data[0])
    Assert-State 'Not applied' (Get-FreshkitRegistryState @($sequence) { $snapshot }) 'reversed array'
}
$raw = New-Operation 'SetValue' 'Hex2' '%TEMP%'
Assert-State 'Applied' (Get-FreshkitRegistryState @($raw) { [pscustomobject]@{KeyExists=$true;ValueExists=$true;Kind='ExpandString';Value='%TEMP%'} }) 'raw environment string'
Assert-State 'Not applied' (Get-FreshkitRegistryState @((New-Operation 'SetValue' 'String' 'Case')) { [pscustomobject]@{KeyExists=$true;ValueExists=$true;Kind='String';Value='case'} }) 'case sensitive data'
$fixture = Join-Path $root ('tests/.tweak-state-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path "$fixture/Config","$fixture/Regfiles","$fixture/Scripts/Helpers" -Force | Out-Null
    Copy-Item (Join-Path $root 'vendor/Win11Debloat/Scripts/Helpers/Get-RegFileOperations.ps1') "$fixture/Scripts/Helpers/"
    '{"Features":[{"FeatureId":"Known","RegistryKey":"good.reg"},{"FeatureId":"Unsupported","RegistryKey":"bad.reg"},{"FeatureId":"EnableWindowsSandbox"},{"FeatureId":"DisableWidgets"},{"FeatureId":"DisableTelemetry","RegistryKey":"good.reg"},{"FeatureId":"DisableBing","RegistryKey":"good.reg"},{"FeatureId":"DisableCopilot","RegistryKey":"good.reg"}]}' | Set-Content "$fixture/Config/Features.json"
    "Windows Registry Editor Version 5.00`n[HKEY_CURRENT_USER\Software\Synthetic]`n`"test`"=dword:00000001" | Set-Content "$fixture/Regfiles/good.reg"
    "Windows Registry Editor Version 5.00`n[HKEY_CURRENT_USER\Software\Synthetic]`n`"test`"=dword:00000001`n`"other`"=unsupported" | Set-Content "$fixture/Regfiles/bad.reg"
    $result = Invoke-FreshkitTweakState $fixture -RegistryReader $present -AppxReader { [pscustomobject]@{Name='Example.App'} }
    Assert-State 'Applied' $result.Features.Known 'vendor parser integration'
    Assert-State 'Unknown' $result.Features.Unsupported 'partial unsupported files are not falsely applied'
    Assert-State 'Unknown' $result.Features.EnableWindowsSandbox 'optional feature unsupported'
    Assert-State 'Unknown' $result.Features.DisableWidgets 'widgets unsupported'
    # Matching registry data cannot confirm scheduled-task/package side effects.
    foreach ($id in @('DisableTelemetry','DisableBing','DisableCopilot')) {
        Assert-State 'Unknown' $result.Features[$id] ($id + ' requires non-registry verification despite matching registry')
    }
    if (-not $result.AppxComplete -or $result.Appx.Count -ne 1 -or $result.Appx[0] -cne 'Example.App') { throw 'FAIL Appx inventory' }
    $failed = Invoke-FreshkitTweakState $fixture -RegistryReader {throw 'access denied'} -AppxReader { throw 'query failure' }
    Assert-State 'Unknown' $failed.Features.Known 'query failure stays unknown'
    if ($failed.AppxComplete -or $failed.Appx.Count -ne 0 -or $failed.Errors.Count -eq 0) { throw 'FAIL Appx failed inventory must be incomplete' }
    $json = $failed | ConvertTo-Json -Depth 6 | ConvertFrom-Json
    if ($null -eq $json.Features -or $json.AppxComplete) { throw 'FAIL JSON contract' }
} finally {
    if ((Split-Path -Parent $fixture) -ne (Join-Path $root 'tests')) { throw 'Unsafe fixture cleanup path' }
    Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host 'PASS read-only tweak state: registry types/data/order, deletions, unknowns, parsing and Appx failure'
