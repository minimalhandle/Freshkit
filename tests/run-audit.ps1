$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$fw=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$wpf=Join-Path $fw 'WPF'
$refs=@("/r:$wpf/PresentationFramework.dll","/r:$wpf/PresentationCore.dll","/r:$wpf/WindowsBase.dll","/r:$fw/System.Xaml.dll","/r:$fw/System.Web.Extensions.dll","/r:$fw/System.IO.Compression.dll",'/r:System.dll','/r:System.Xml.dll','/r:System.Core.dll','/r:System.Windows.Forms.dll')
$refs+=@(& "$root/tools/icon-build.ps1")
$refs+="/resource:$root/obj/win11debloat.zip,win11debloat.zip"
$refs+="/resource:$root/src/payload/Settings.Profiles.json,payload/Settings.Profiles.json"
$out=Join-Path $root 'obj/AuditTests.exe'
$sources=@(Get-ChildItem "$root/src/app/*.cs"|ForEach-Object FullName)+(Join-Path $PSScriptRoot 'AuditTests.cs')
& "$fw/csc.exe" /nologo /target:exe /main:Freshkit.AuditTests /out:$out $refs $sources
if($LASTEXITCODE){throw 'Audit test compilation failed'}
& $out
if($LASTEXITCODE){throw 'Audit tests failed'}
