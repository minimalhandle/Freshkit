param([Parameter(Mandatory)][string]$DebloatRoot)

# This file only inspects current-user Appx and registry state. It never loads the
# vendor entry point, writes registry keys, or requests elevation.
function Read-FreshkitRegistryOperation {
    param($Operation)
    if ($Operation.KeyPath -notmatch '^(HKEY_[^\\]+)\\(.+)$') { throw 'Unsupported registry path' }
    $subKey = $Matches[2]
    $rootKey = switch ($Matches[1]) {
        'HKEY_CURRENT_USER' { [Microsoft.Win32.Registry]::CurrentUser }
        'HKEY_LOCAL_MACHINE' { [Microsoft.Win32.Registry]::LocalMachine }
        'HKEY_CLASSES_ROOT' { [Microsoft.Win32.Registry]::ClassesRoot }
        'HKEY_USERS' { [Microsoft.Win32.Registry]::Users }
        'HKEY_CURRENT_CONFIG' { [Microsoft.Win32.Registry]::CurrentConfig }
        default { throw 'Unsupported registry hive' }
    }
    $key = $null
    try {
        $key = $rootKey.OpenSubKey($subKey, $false)
        if ($null -eq $key) { return [pscustomobject]@{KeyExists=$false;ValueExists=$false;Kind=$null;Value=$null} }
        if ($Operation.OperationType -eq 'DeleteKey') { return [pscustomobject]@{KeyExists=$true;ValueExists=$false;Kind=$null;Value=$null} }
        $exists = @($key.GetValueNames()) -icontains $Operation.ValueName
        $kind = $null
        $raw = $null
        if ($exists -and $Operation.OperationType -eq 'SetValue') {
            $kind = $key.GetValueKind($Operation.ValueName)
            $raw = $key.GetValue($Operation.ValueName, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            if ($kind -eq [Microsoft.Win32.RegistryValueKind]::DWord) {
                $raw = [BitConverter]::ToUInt32([BitConverter]::GetBytes([int32]$raw), 0)
            } elseif ($kind -eq [Microsoft.Win32.RegistryValueKind]::QWord) {
                $raw = [BitConverter]::ToUInt64([BitConverter]::GetBytes([int64]$raw), 0)
            }
        }
        return [pscustomobject]@{KeyExists=$true;ValueExists=$exists;Kind=$kind;Value=$raw}
    } finally {
        if ($null -ne $key) { $key.Close() }
    }
}

function Get-FreshkitRegistryState {
    param([AllowEmptyCollection()][object[]]$Operations, [scriptblock]$RegistryReader = {param($op) Read-FreshkitRegistryOperation $op})
    if ($null -eq $Operations -or $Operations.Count -eq 0) { return 'Unknown' }
    $mismatch = $false
    $unknown = $false
    foreach ($op in $Operations) {
        try {
            $expectedKind = switch ([string]$op.ValueType) {
                'DWord' { 'DWord' }
                'QWord' { 'QWord' }
                'String' { 'String' }
                'Binary' { 'Binary' }
                'Hex2' { 'ExpandString' }
                'Hex7' { 'MultiString' }
                default { $null }
            }
            if ($op.OperationType -notin @('SetValue','DeleteValue','DeleteKey')) { $unknown = $true; continue }
            if ($op.OperationType -eq 'SetValue' -and $null -eq $expectedKind) { $unknown = $true; continue }
            $state = & $RegistryReader $op
            # Null or incomplete injected snapshots must not become absent values.
            if ($null -eq $state -or $null -eq $state.KeyExists -or $null -eq $state.ValueExists) { $unknown = $true; continue }
            switch ($op.OperationType) {
                'DeleteKey' { if ($state.KeyExists) { $mismatch = $true } }
                'DeleteValue' { if ($state.KeyExists -and $state.ValueExists) { $mismatch = $true } }
                'SetValue' {
                    if (-not $state.KeyExists -or -not $state.ValueExists) { $mismatch = $true; continue }
                    if ($null -eq $state.Kind) { $unknown = $true; continue }
                    if ([string]$state.Kind -ne $expectedKind) { $mismatch = $true; continue }
                    if ($expectedKind -in @('Binary','MultiString')) {
                        $actual = @(if ($null -ne $state.Value) { $state.Value })
                        $expected = @(if ($null -ne $op.ValueData) { $op.ValueData })
                        if ($actual.Count -ne $expected.Count) { $mismatch = $true; continue }
                        for ($i=0; $i -lt $actual.Count; $i++) {
                            if ($actual[$i] -cne $expected[$i]) { $mismatch = $true }
                        }
                    } elseif ($state.Value -cne $op.ValueData) {
                        $mismatch = $true
                    }
                }
            }
        } catch { $unknown = $true }
    }
    if ($unknown) { return 'Unknown' }
    if ($mismatch) { return 'Not applied' }
    return 'Applied'
}

function Get-FreshkitRegFileOperations {
    param([string]$Path)
    # The vendor parser skips unsupported lines. Reject them before parsing so
    # a partially understood apply file cannot appear fully applied.
    $content = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop
    $content = $content -replace '\\\r?\n\s*', ''
    $hasKey = $false
    foreach ($rawLine in ($content -split '\r?\n')) {
        $line = $rawLine.Trim()
        if (-not $line -or $line.StartsWith(';') -or $line -eq 'Windows Registry Editor Version 5.00') { continue }
        if ($line -match '^\[-?HKEY_[^\]]+\]$') { $hasKey = $true; continue }
        if (-not $hasKey -or $line -notmatch '^(?:@|"[^"]+")=(.*)$') { throw 'Unsupported registry content' }
        if ($null -eq (Convert-RegValueData -valueData $Matches[1].Trim())) { throw 'Unsupported registry value' }
    }
    $operations = @(Get-RegFileOperations -regFilePath $Path -WarningAction Stop -ErrorAction Stop)
    if ($operations.Count -eq 0) { throw 'No detectable registry operations' }
    return $operations
}

function Invoke-FreshkitTweakState {
    param(
        [Parameter(Mandatory)][string]$Root,
        [scriptblock]$RegistryReader = {param($op) Read-FreshkitRegistryOperation $op},
        [scriptblock]$AppxReader = { Get-AppxPackage -ErrorAction Stop }
    )
    $features = [ordered]@{}
    $errors = New-Object 'System.Collections.Generic.List[string]'
    $appx = @()
    $appxComplete = $false
    try {
        $packages = @(& $AppxReader)
        $appx = @($packages | ForEach-Object { if (-not [string]::IsNullOrWhiteSpace([string]$_.Name)) { [string]$_.Name } } | Sort-Object -Unique)
        $appxComplete = $true
    } catch { $errors.Add('Current-user app inventory could not be read.') }
    try {
        . (Join-Path $Root 'Scripts/Helpers/Get-RegFileOperations.ps1')
        $config = Get-Content -LiteralPath (Join-Path $Root 'Config/Features.json') -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        foreach ($feature in $config.Features) {
            $id = [string]$feature.FeatureId
            if ([string]::IsNullOrWhiteSpace($id)) { continue }
            $features[$id] = 'Unknown'
            # These vendor actions have additional non-registry effects that a
            # .reg file alone cannot prove. Keep their state explicitly unknown.
            if ($id -in @('DisableWidgets','DisableStoreSearchSuggestions','EnableWindowsSandbox','EnableWindowsSubsystemForLinux','DisableTelemetry','DisableBing','DisableCopilot')) { continue }
            if ([string]::IsNullOrWhiteSpace([string]$feature.RegistryKey)) { continue }
            try {
                $regRoot = [IO.Path]::GetFullPath((Join-Path $Root 'Regfiles')) + [IO.Path]::DirectorySeparatorChar
                $regPath = [IO.Path]::GetFullPath((Join-Path $regRoot $feature.RegistryKey))
                if (-not $regPath.StartsWith($regRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid registry definition path' }
                $operations = @(Get-FreshkitRegFileOperations $regPath)
                $features[$id] = Get-FreshkitRegistryState $operations $RegistryReader
            } catch { $errors.Add(('Registry definition could not be inspected: ' + $id)) }
        }
    } catch { $errors.Add('Tweak definitions could not be read.') }
    [pscustomobject]@{Features=$features;Appx=@($appx);AppxComplete=$appxComplete;Errors=@($errors.ToArray())}
}

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-FreshkitTweakState -Root $DebloatRoot | ConvertTo-Json -Depth 6 -Compress
}
