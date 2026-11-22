$ErrorActionPreference='Stop'
# Windows PowerShell 5.1 reads a script without a BOM in the system ANSI codepage, so any non-ASCII
# character in a BOM-less .ps1/.psm1 is garbled on a normal Windows install (a '[block-chars]' regex
# once turned into a reversed range and failed every winget install after it had succeeded).
$root=Split-Path -Parent $PSScriptRoot
$bad=@()
foreach($file in Get-ChildItem -Path (Join-Path $root 'src') -Recurse -Include *.ps1,*.psm1,*.psd1){
    $bytes=[IO.File]::ReadAllBytes($file.FullName)
    $bom=($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    if(-not $bom -and @($bytes | Where-Object { $_ -gt 0x7F }).Count -gt 0){$bad+=$file.FullName.Substring($root.Length+1)}
}
if($bad.Count){throw ('Non-ASCII text in BOM-less PowerShell files (use \uXXXX escapes or save with a BOM): '+($bad -join ', '))}
# The progress-bar filter must parse and behave under the ANSI reading.
Import-Module (Join-Path $root 'src/payload/Install-Chocolatey.Core.psm1') -Force -DisableNameChecking
$bar=([string][char]0x2588)*10+'  1.2 MB / 3.4 MB'
if($bar -notmatch '[\u2580-\u259F]'){throw 'Progress-bar filter no longer matches block characters'}
if('Successfully installed' -match '[\u2580-\u259F]'){throw 'Progress-bar filter matches ordinary output'}
Write-Host 'PASS PowerShell sources are ANSI-safe and the winget progress filter parses'
