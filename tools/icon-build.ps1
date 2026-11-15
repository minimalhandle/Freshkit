# Compiler arguments shared by the app and test builds. No package restore needed.
$iconRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'vendor/icons'
"/r:$iconRoot/Svg.dll"
'/r:System.Drawing.dll'
'/r:System.Web.dll'
Get-ChildItem $iconRoot -File | ForEach-Object { "/resource:$($_.FullName),icon-libs/$($_.Name)" }
