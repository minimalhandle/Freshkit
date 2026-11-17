# Builds dist\Freshkit.exe: one self-contained file (engine, Win11Debloat, lists, UI and icon embedded).
# Needs only the .NET Framework 4.x that ships with Windows 10/11.
param([string]$OutDir = '', [switch]$RefreshIconCache)
$ErrorActionPreference = 'Stop'
$src  = Join-Path $PSScriptRoot 'src'
$out  = if ($OutDir) { $OutDir } else { Join-Path $PSScriptRoot 'dist' }
$obj  = Join-Path $PSScriptRoot 'obj'
New-Item -ItemType Directory -Force $out, $obj | Out-Null
if (-not (Test-Path "$src\app.ico")) { & (Join-Path $PSScriptRoot 'tools\make-icon.ps1') | Out-Null }

# Win11Debloat (vendored) -> obj\win11debloat.zip with forward-slash entry names
$vendor = Join-Path $PSScriptRoot 'vendor\Win11Debloat'
$zipPath = Join-Path $obj 'win11debloat.zip'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
$zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    $base = (Resolve-Path $vendor).Path.TrimEnd('\') + '\'
    Get-ChildItem $vendor -Recurse -File | ForEach-Object {
        $name = $_.FullName.Substring($base.Length).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $name, 'Optimal')
    }
} finally { $zip.Dispose() }

$fw  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $fw 'WPF'
$res = @(
    "/resource:$src\Runner.ps1,Runner.ps1",
    "/resource:$src\Freshkit.lists.txt,Freshkit.lists.txt",
    "/resource:$src\MainWindow.xaml,MainWindow.xaml",
    "/resource:$src\app.ico,app.ico",
    "/resource:$src\freshkit-logo.png,freshkit-logo.png",
    "/resource:$zipPath,win11debloat.zip"
)
Get-ChildItem "$src\payload" -File | ForEach-Object { $res += "/resource:$($_.FullName),payload/$($_.Name)" }
Get-ChildItem "$src\scripts" -File | ForEach-Object { $res += "/resource:$($_.FullName),scripts/$($_.Name)" }
Get-ChildItem "$src\icons\*.png" -File | ForEach-Object { $res += "/resource:$($_.FullName),icons/$($_.Name)" }
$refs = @(
    "/r:$wpf\PresentationFramework.dll", "/r:$wpf\PresentationCore.dll", "/r:$wpf\WindowsBase.dll", "/r:$fw\System.Xaml.dll",
    "/r:$fw\System.Web.Extensions.dll", "/r:$fw\System.IO.Compression.dll", '/r:System.dll', '/r:System.Xml.dll', '/r:System.Core.dll', '/r:System.Windows.Forms.dll'
)
$sources = @(Get-ChildItem "$src\app\*.cs" | ForEach-Object { $_.FullName })
$refs += @(& "$PSScriptRoot\tools\icon-build.ps1")
& "$fw\csc.exe" /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 /out:"$out\Freshkit.exe" /win32manifest:"$src\app.manifest" /win32icon:"$src\app.ico" $refs $res $sources
if ($LASTEXITCODE) { throw "csc failed ($LASTEXITCODE)" }
# Tell Explorer that this file's icon may have changed when rebuilding in place.
# The explicit cache refresh is useful when an older icon is already cached.
try {
    if (-not ('FreshkitBuildShell' -as [type])) {
        Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FreshkitBuildShell {
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)]
    public static extern void SHChangeNotify(int eventId, uint flags, [MarshalAs(UnmanagedType.LPWStr)] string item, IntPtr unused);
}
'@
    }
    $exePath = [IO.Path]::GetFullPath((Join-Path $out 'Freshkit.exe'))
    if ($RefreshIconCache) { [FreshkitBuildShell]::SHChangeNotify(0x08000000, 0x1000, $null, [IntPtr]::Zero) }
    [FreshkitBuildShell]::SHChangeNotify(0x00002000, 0x1005, $exePath, [IntPtr]::Zero)
    [FreshkitBuildShell]::SHChangeNotify(0x00001000, 0x1005, [IO.Path]::GetDirectoryName($exePath), [IntPtr]::Zero)
} catch { Write-Warning ('Build succeeded; Explorer notification was unavailable: ' + $_.Exception.Message) }
Get-Item "$out\Freshkit.exe" | Select-Object Name, @{n = 'KB'; e = { [int]($_.Length / 1KB) } }
