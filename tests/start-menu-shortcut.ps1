$ErrorActionPreference='Stop'
# Start menu shortcuts for installers that create none when silent. Temp folders and a fake shortcut writer only.
$root=Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $root 'src\payload\Install-Chocolatey.Core.psm1') -Force -DisableNameChecking
$module=Get-Module 'Install-Chocolatey.Core'
$dir=Join-Path ([IO.Path]::GetTempPath()) ('freshkit-shortcut-'+[guid]::NewGuid().ToString('N'))
try{
    $menu=Join-Path $dir 'menu';$pf=Join-Path $dir 'pf';$pf86=Join-Path $dir 'pf86'
    foreach($d in $menu,$pf,$pf86){[IO.Directory]::CreateDirectory($d)|Out-Null}
    $made=[Collections.Generic.List[object]]::new()
    $create={param($Link,$Target) $made.Add(@($Link,$Target));[IO.File]::WriteAllText($Link,'fixture')}.GetNewClosure()
    function Shortcut([string]$Id){& $module {param($i,$m,$p,$c) Add-SetupStartMenuShortcut $i -StartMenu $m -ProgramDirs $p -Create $c} $Id $menu @($pf,$pf86) $create}

    if((Shortcut 'steam') -or $made.Count){throw 'Packages whose installers make their own shortcuts are left alone'}
    $note=Shortcut 'streamlink-twitch-gui'
    if($made.Count -or $note -notmatch 'not found'){throw "No shortcut is made when the app's EXE is missing: $note"}
    $exe=Join-Path $pf86 'Streamlink Twitch GUI\streamlink-twitch-gui.exe'
    [IO.Directory]::CreateDirectory((Split-Path -Parent $exe))|Out-Null;[IO.File]::WriteAllText($exe,'fixture')
    $note=Shortcut 'streamlink-twitch-gui'
    if($made.Count -ne 1 -or $made[0][0] -ne (Join-Path $menu 'Streamlink Twitch GUI.lnk') -or $made[0][1] -ne $exe -or $note -notmatch 'Added'){throw "Streamlink Twitch GUI gets a Start menu shortcut to its EXE: $note"}
    if((Shortcut 'streamlink-twitch-gui') -or $made.Count -ne 1){throw 'An existing shortcut is not replaced'}
}finally{Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue}
Write-Host 'PASS Start menu shortcuts are added for silent installers that skip them, only when the EXE exists and none is there'
