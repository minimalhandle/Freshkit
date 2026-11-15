# Refresh the bundled app logos from Chocolatey's package metadata.
# Requires Node.js and sharp; normal builds use the checked-in PNGs only.
param([Parameter(Mandatory=$true)][string]$Node, [Parameter(Mandatory=$true)][string]$NodeModules)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'src/icons'
$scratch = Join-Path $root 'obj/icon-downloads'
New-Item -ItemType Directory -Force $target, $scratch | Out-Null
$sources = @()
$overrides = @{
    'ab-download-manager' = 'https://raw.githubusercontent.com/Aurocosh/chocolatey-packages/master/automatic/ab-download-manager/img/icon.png'
    'innosetup' = 'https://jrsoftware.org/favicon.ico'
    'dotnet-desktopruntime' = 'https://raw.githubusercontent.com/dotnetcore-chocolatey/dotnetcore-chocolateypackages/5a90ffc61572e8403cb88c325f88cf6989de8c79/icons/dotnetcore.png'
    'dotnet-7.0-desktopruntime' = 'https://raw.githubusercontent.com/dotnetcore-chocolatey/dotnetcore-chocolateypackages/5a90ffc61572e8403cb88c325f88cf6989de8c79/icons/dotnetcore.png'
    'dotnet-8.0-desktopruntime' = 'https://raw.githubusercontent.com/dotnetcore-chocolatey/dotnetcore-chocolateypackages/5a90ffc61572e8403cb88c325f88cf6989de8c79/icons/dotnetcore.png'
    'playnite' = 'https://raw.githubusercontent.com/TheCakeIsNaOH/chocolatey-packages/36cdec772744c373abeaf3e791fca7a60742d69d/Icons/playnite.png'
    'rpi-imager' = 'https://raw.githubusercontent.com/eugbaranov/chocolatey-packages/master/rpi-imager/RPi-Logo-SCREEN.png'
}
foreach ($line in Get-Content "$root/src/Freshkit.lists.txt") {
    if ($line -notmatch '^::PACKAGE ([^|]+)\|') { continue }
    $packageId = $Matches[1]
    $id = $packageId.ToLowerInvariant()
    $metadata = 'https://community.chocolatey.org/api/v2/Packages()?$filter=' + [Uri]::EscapeDataString("Id eq '$packageId' and IsLatestVersion")
    try {
        if ($overrides.ContainsKey($id)) { $url = $overrides[$id] }
        else {
            [xml]$feed = (Invoke-WebRequest -Uri $metadata -TimeoutSec 30).Content
            $url = $feed.SelectSingleNode('//*[local-name()="IconUrl"]').InnerText
        }
        if (-not $url.StartsWith('https://')) { throw 'Missing HTTPS icon URL' }
        $raw = Join-Path $scratch "$id.image"
        Invoke-WebRequest -Uri $url -OutFile $raw -TimeoutSec 30
        if ($url.EndsWith('.ico')) {
            Add-Type -AssemblyName System.Drawing
            $image = [System.Drawing.Image]::FromFile($raw)
            try { $image.Save("$raw.png", [System.Drawing.Imaging.ImageFormat]::Png) } finally { $image.Dispose() }
            $raw = "$raw.png"
        }
        & $Node -e 'const sharp=require(require.resolve("sharp",{paths:[process.argv[1]]})); sharp(process.argv[2]).resize(96,96,{fit:"contain",background:{r:0,g:0,b:0,alpha:0}}).png().toFile(process.argv[3]).catch(e=>{console.error(e.message);process.exit(1)});' $NodeModules $raw (Join-Path $target "$id.png")
        if ($LASTEXITCODE) { throw 'Image conversion failed' }
        $sources += [pscustomobject]@{ package = $id; url = $url }
        Write-Output "Saved $id"
    } catch { Write-Warning "$id`: $_" }
}
$sources | ConvertTo-Json | Set-Content "$target/sources.json" -Encoding UTF8
