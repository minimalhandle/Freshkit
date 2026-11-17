param([switch]$Render)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $fw 'WPF'
$refs = @("/r:$wpf\PresentationFramework.dll", "/r:$wpf\PresentationCore.dll", "/r:$wpf\WindowsBase.dll", "/r:$fw\System.Xaml.dll", "/r:$fw\System.Web.Extensions.dll", "/r:$fw\System.IO.Compression.dll", '/r:System.dll', '/r:System.Xml.dll', '/r:System.Core.dll', '/r:System.Windows.Forms.dll')
$out = Join-Path $root 'obj\UiTests.exe'
$refs += @(& "$root\tools\icon-build.ps1")
$sources = @(Get-ChildItem "$root\src\app\*.cs" | ForEach-Object FullName) + (Join-Path $PSScriptRoot 'UiTests.cs')
$sources += Join-Path $PSScriptRoot 'IconTests.cs'
$sources += Join-Path $PSScriptRoot 'TweakTests.cs'
$refs += @(Get-ChildItem "$root\src\icons\*.png" | ForEach-Object { "/resource:$($_.FullName),icons/$($_.Name)" })
$refs += "/resource:$root\src\freshkit-logo.png,freshkit-logo.png"
& "$fw\csc.exe" /nologo /target:exe /main:Freshkit.UiTests /out:$out $refs "/resource:$root\src\MainWindow.xaml,MainWindow.xaml" "/resource:$root\src\Freshkit.lists.txt,Freshkit.lists.txt" "/resource:$root\src\payload\Settings.Profiles.json,payload/Settings.Profiles.json" "/resource:$root\obj\win11debloat.zip,win11debloat.zip" $sources
if ($LASTEXITCODE) { throw 'UI test compilation failed' }
if ($Render) { & $out --render } else { & $out }
if ($LASTEXITCODE) { throw 'UI tests failed' }
