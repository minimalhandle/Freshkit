$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$fw=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$wpf=Join-Path $fw 'WPF'
$refs=@("/r:$wpf\PresentationFramework.dll","/r:$wpf\PresentationCore.dll","/r:$wpf\WindowsBase.dll","/r:$fw\System.Xaml.dll","/r:$fw\System.Web.Extensions.dll","/r:$fw\System.IO.Compression.dll",'/r:System.dll','/r:System.Xml.dll','/r:System.Core.dll','/r:System.Windows.Forms.dll')
$out=Join-Path $root 'obj\CoreTests.exe'
$refs += @(& "$root\tools\icon-build.ps1")
$sources=@(Get-ChildItem "$root\src\app\*.cs"|ForEach-Object FullName)+(Join-Path $PSScriptRoot 'CoreTests.cs')+(Join-Path $PSScriptRoot 'WorkflowTests.cs')+(Join-Path $PSScriptRoot 'SystemFactsTests.cs')+(Join-Path $PSScriptRoot 'SignalsTests.cs')+(Join-Path $PSScriptRoot 'ReinstallTests.cs')+(Join-Path $PSScriptRoot 'ExtrasTests.cs')
& "$fw\csc.exe" /nologo /target:exe /main:Freshkit.CoreTests /out:$out $refs "/resource:$root\src\Freshkit.lists.txt,Freshkit.lists.txt" "/resource:$root\src\payload\Settings.Profiles.json,payload/Settings.Profiles.json" $sources
if($LASTEXITCODE){throw 'Test compilation failed'}
& $out
if($LASTEXITCODE){throw 'Core tests failed'}
