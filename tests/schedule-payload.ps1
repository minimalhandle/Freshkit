$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$text=[IO.File]::ReadAllText((Join-Path $root 'src/scripts/Schedule.ps1'))
# Evaluate only the string-building section. Never execute task registration or updates.
$start=$text.IndexOf("        `$body=@'")
$end=$text.IndexOf('        $action=New-ScheduledTaskAction',$start)
if($start -lt 0 -or $end -lt 0){throw 'Schedule payload boundaries missing'}
$builder=$text.Substring($start,$end-$start).Replace("Join-Path `$PSScriptRoot 'Install-Chocolatey.Core.psm1'",("'"+(Join-Path $root 'src/payload/Install-Chocolatey.Core.psm1').Replace("'","''")+"'"))
$Store='9NKSQGP7F2NH';$GitHub='minimalhandle/Afterclip|Afterclip;minimalhandle/SleekMeeter|Sleekmeeter'
. ([scriptblock]::Create($builder))
if($encoded.Length -gt 31000){throw 'Scheduled updater exceeds its command-line budget'}
$decoded=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($encoded))
if($decoded -match "FromBase64String\('(?<payload>[A-Za-z0-9+/=]+)'\)"){
    $input=[IO.MemoryStream]::new([Convert]::FromBase64String($Matches.payload))
    $gzip=[IO.Compression.GZipStream]::new($input,[IO.Compression.CompressionMode]::Decompress)
    $reader=[IO.StreamReader]::new($gzip,[Text.Encoding]::UTF8)
    try{$decoded=$reader.ReadToEnd()}finally{$reader.Dispose();$input.Dispose()}
}
if(-not $decoded.Contains('minimalhandle/Afterclip') -or -not $decoded.Contains('Save-SetupGitHubIdentity') -or -not $decoded.Contains('-Repo $p[0]')){throw 'GitHub identity checks or apps were dropped from the scheduled updater'}
$errors=$null;[void][Management.Automation.Language.Parser]::ParseInput($decoded,[ref]$null,[ref]$errors)
if($errors.Count){throw $errors[0]}
Write-Host ('PASS self-contained weekly updater retains both GitHub apps and identity checks ('+$encoded.Length+' encoded characters)')
