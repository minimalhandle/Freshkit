# Bundles 96x96 PNG logos for catalog packages that don't have one yet (src/icons/<id>.png),
# using each Chocolatey package's IconUrl. Needs only Windows PowerShell 5.1; SVGs render with vendor/icons/Svg.dll.
# Store/winget entries use src/icons/winget-<id>.png and take their logo from -WingetSources (id=url).
param([string]$Scratch = (Join-Path ([IO.Path]::GetTempPath()) 'freshkit-icons'), [hashtable]$WingetSources = @{}, [hashtable]$Homepages = @{})
$ErrorActionPreference = 'Stop'
# Keep TLS 1.3 available: forcing Tls12 alone breaks CDNs such as cdn.statically.io.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'src/icons'
New-Item -ItemType Directory -Force $Scratch | Out-Null
Add-Type -AssemblyName System.Drawing
$svgLoaded = $false
function Load-Svg {
    if ($script:svgLoaded) { return }
    $dir = Join-Path $root 'vendor/icons'
    # LoadFrom resolves Svg.dll's dependencies from the same folder.
    foreach ($dll in Get-ChildItem $dir -Filter *.dll) { [void][Reflection.Assembly]::LoadFrom($dll.FullName) }
    $script:svgLoaded = $true
}
# Largest declared site icon (apple-touch-icon first), else /favicon.ico.
function Get-SiteIcons([string]$site) {
    $list = @()
    try {
        $html = (Invoke-WebRequest -Uri $site -UseBasicParsing -TimeoutSec 30 -UserAgent 'Mozilla/5.0').Content
        foreach ($m in [regex]::Matches($html, '<link[^>]*>', 'IgnoreCase')) {
            $rel = [regex]::Match($m.Value, 'rels*=s*["'']([^"'']+)', 'IgnoreCase').Groups[1].Value
            $href = [regex]::Match($m.Value, 'hrefs*=s*["'']([^"'']+)', 'IgnoreCase').Groups[1].Value
            if ($rel -notmatch 'icon' -or -not $href) { continue }
            $abs = (New-Object Uri((New-Object Uri $site), [Net.WebUtility]::HtmlDecode($href))).AbsoluteUri
            if ($rel -match 'apple') { $list = @($abs) + $list } else { $list += $abs }
        }
    } catch { }
    $list += (New-Object Uri $site).GetLeftPart('Authority') + '/favicon.ico'
    return $list
}
function Save-Icon([string]$raw, [string]$out) {
    $head = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($raw), 0, [Math]::Min(512, (Get-Item $raw).Length))
    if ($head -match '<svg|<\?xml') {
        Load-Svg
        [Svg.SvgDocument]::ResolveExternalImages = 'None'; [Svg.SvgDocument]::ResolveExternalElements = 'None'
        $doc = [Svg.SvgDocument]::Open($raw); $src = $doc.Draw(96, 96)
    } elseif ($head.StartsWith([string][char]0 + [char]0 + [char]1)) {
        $icon = New-Object Drawing.Icon($raw, 256, 256); $src = $icon.ToBitmap(); $icon.Dispose()
    } else { $src = [Drawing.Image]::FromFile($raw) }
    try {
        $bmp = New-Object Drawing.Bitmap 96, 96
        $g = [Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'HighQuality'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([Drawing.Color]::Transparent)
        $scale = [Math]::Min(96 / $src.Width, 96 / $src.Height); $w = $src.Width * $scale; $h = $src.Height * $scale
        $g.DrawImage($src, (96 - $w) / 2, (96 - $h) / 2, $w, $h); $g.Dispose()
        $bmp.Save($out, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    } finally { $src.Dispose() }
}
$sourcesPath = Join-Path $target 'sources.json'
# PowerShell 5.1 returns a JSON array as one object; enumerate it so entries stay flat.
$sources = New-Object Collections.ArrayList
foreach ($entry in (Get-Content $sourcesPath -Raw | ConvertFrom-Json)) { foreach ($e in @($entry)) { [void]$sources.Add($e) } }
$jobs = @()
foreach ($line in Get-Content (Join-Path $root 'src/Freshkit.lists.txt')) {
    if ($line -match '^::(?:PACKAGE|DISABLED) ([^|]+)\|') { $jobs += [pscustomobject]@{ Id = $Matches[1]; File = $Matches[1].ToLowerInvariant() + '.png'; Url = $null } }
}
foreach ($k in $WingetSources.Keys) { $jobs += [pscustomobject]@{ Id = $k; File = 'winget-' + $k.ToLowerInvariant() + '.png'; Url = $WingetSources[$k] } }
# Older feed entries for these point at http sites that redirect to a generic WordPress logo; their bundled logos come from the current package icons.
$skip = @('nvda', 'spybot')
foreach ($job in $jobs) {
    if ($skip -contains $job.Id) { continue }
    $out = Join-Path $target $job.File
    if (Test-Path $out) { continue }
    try {
        $url = $job.Url; $homepage = $Homepages[$job.Id]
        if (-not $url -and -not $homepage) {
            $q = 'https://community.chocolatey.org/api/v2/FindPackagesById()?id=' + [Uri]::EscapeDataString("'" + $job.Id + "'")
            [xml]$feed = (Invoke-WebRequest -Uri $q -UseBasicParsing -TimeoutSec 60).Content
            $urls = @($feed.SelectNodes('//*[local-name()="IconUrl"]') | ForEach-Object { $_.InnerText } | Where-Object { $_ })
            if (-not $urls.Count) { throw 'No IconUrl' }
            $url = $urls[-1]
            $project = @($feed.SelectNodes('//*[local-name()="ProjectUrl"]') | ForEach-Object { $_.InnerText } | Where-Object { $_ })
            if ($project.Count) { $homepage = $project[-1] }
        }
        if (-not $url) { $url = 'about:none' }
        # Retired CDNs mirror GitHub; plain-HTTP icon URLs are tried over HTTPS first.
        $url = $url -replace '^https?://(rawcdn\.githack\.com|cdn\.rawgit\.com|rawgit\.com)/', 'https://raw.githubusercontent.com/'
        $candidates = @($url); if ($url.StartsWith('http://')) { $candidates = @($url -replace '^http://', 'https://'), $url }
        $raw = Join-Path $Scratch ($job.File + '.download'); $ok = $false
        $last = 'No icon found'
        foreach ($c in $candidates) { if ($c -notmatch '^https?://') { continue }; try { Invoke-WebRequest -Uri $c -OutFile $raw -UseBasicParsing -TimeoutSec 60; Save-Icon $raw $out; $url = $c; $ok = $true; break } catch { $last = $_ } }
        if (-not $ok -and $homepage) {
            foreach ($c in (Get-SiteIcons ($homepage -replace '^http://', 'https://'))) { try { Invoke-WebRequest -Uri $c -OutFile $raw -UseBasicParsing -TimeoutSec 30 -UserAgent 'Mozilla/5.0'; Save-Icon $raw $out; $url = $c; $ok = $true; break } catch { $last = $_ } }
        }
        if (-not $ok) { throw $last }
        [void]$sources.Add([pscustomobject]@{ package = ($job.File -replace '\.png$', ''); url = $url })
        Write-Output "Saved $($job.File)"
    } catch { Write-Warning ("{0}: {1}" -f $job.Id, ([string]$_).Split([char]10)[0]) }
}
# Package families share one logo (runtimes, Java builds).
foreach ($family in @(@('dotnet-desktopruntime', '^(dotnet-|netfx-)'), @('vcredist-all', '^(vcredist|vstor)'), @('temurin21', '^temurin'), @('corretto17jdk', '^corretto'))) {
    $from = Join-Path $target ($family[0] + '.png')
    if (-not (Test-Path $from)) { $any = Get-ChildItem $target -Filter *.png | Where-Object { $_.BaseName -match $family[1] } | Select-Object -First 1; if ($any) { $from = $any.FullName } else { continue } }
    foreach ($job in $jobs) {
        $out = Join-Path $target $job.File
        if (-not (Test-Path $out) -and $job.Id.ToLowerInvariant() -match $family[1]) { Copy-Item $from $out; Write-Output "Shared $($job.File)" }
    }
}
ConvertTo-Json -InputObject @($sources | Sort-Object package) -Depth 3 | Set-Content $sourcesPath -Encoding UTF8
