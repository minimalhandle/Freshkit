$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$fw=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$wpf=Join-Path $fw 'WPF'
$refs=@("/r:$wpf/PresentationFramework.dll","/r:$wpf/PresentationCore.dll","/r:$wpf/WindowsBase.dll","/r:$fw/System.Xaml.dll","/r:$fw/System.Web.Extensions.dll","/r:$fw/System.IO.Compression.dll",'/r:System.dll','/r:System.Xml.dll','/r:System.Core.dll','/r:System.Windows.Forms.dll')
$refs+=@(& "$root/tools/icon-build.ps1")
$out=Join-Path $root 'obj/PreflightTests.exe'
$sources=@(Get-ChildItem "$root/src/app/*.cs"|ForEach-Object FullName)+(Join-Path $PSScriptRoot 'PreflightTests.cs')
& "$fw/csc.exe" /nologo /target:exe /main:Freshkit.PreflightTests /out:$out $refs $sources
if($LASTEXITCODE){throw 'Preflight test compilation failed'}
& $out
if($LASTEXITCODE){throw 'Preflight tests failed'}
