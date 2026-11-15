# Catalog health check: run before a release (needs internet; changes nothing).
# Checks every entry in src\Freshkit.lists.txt against its source and its bundled logo:
#   Chocolatey  - the package still has a current (latest, listed) version; warns when it hasn't been updated in -StaleDays.
#   winget      - the package ID exists (needs winget on this PC; skipped otherwise).
#   Store       - the product exists in the Microsoft Store catalog for -Market.
#   GitHub      - the latest release still has a Windows installer Freshkit can use.
#   Logos       - src\icons has a readable PNG for it, recorded in sources.json.
# Exit code 0 = no errors (warnings allowed), 1 = at least one error.
param([int]$StaleDays = 730, [string]$Market = 'US', [switch]$SkipWinget)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$icons = Join-Path $root 'src\icons'
Import-Module (Join-Path $root 'src\payload\Install-Chocolatey.Core.psm1') -Force -DisableNameChecking

# ---- read the catalog (same sections and rules as the app)
$text = [IO.File]::ReadAllText((Join-Path $root 'src\Freshkit.lists.txt'))
$entries = New-Object Collections.ArrayList
foreach ($section in @(@('PACKAGES', 'PACKAGE', 'choco'), @('WINGET', 'WINGET', 'winget'), @('GITHUB', 'GITHUB', 'github'))) {
    $m = [regex]::Match($text, '(?ms)^::BEGIN_' + $section[0] + '\r?\n(?<body>.*?)^::END_' + $section[0] + '\r?$')
    if (-not $m.Success) { continue }
    foreach ($line in ($m.Groups['body'].Value -split '\r?\n')) {
        if ($line -notmatch ('^::(?<kind>' + $section[1] + '|DISABLED)\s+(?<id>[^|]+)\|(?<name>[^|]*)')) { continue }
        $id = $Matches.id.Trim(); $name = $Matches.name.Trim(); $ticked = ($Matches.kind -ne 'DISABLED'); $source = $section[2]
        if ($source -eq 'winget' -and $id -cmatch '\A(?:9[A-Z0-9]{11}|XP[A-Z0-9]{12})\z') { $source = 'store' }
        $icon = switch ($source) { 'choco' { $id.ToLowerInvariant() } 'github' { 'github-' + (($id.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-')) } default { 'winget-' + $id.ToLowerInvariant() } }
        [void]$entries.Add([pscustomobject]@{ Source = $source; Id = $id; Name = $name; Ticked = $ticked; Icon = $icon })
    }
}
$results = New-Object Collections.ArrayList
function Add-Result($entry, [string]$level, [string]$message) { [void]$results.Add([pscustomobject]@{ Level = $level; Source = $entry.Source; Id = $entry.Id; Message = $message }) }

# ---- Chocolatey: the latest listed version of each package (one query each: the feed rejects combined filters)
$choco = @($entries | Where-Object Source -eq 'choco'); $n = 0
foreach ($entry in $choco) {
    $n++; Write-Progress -Activity 'Checking Chocolatey packages' -Status $entry.Id -PercentComplete (100 * $n / [Math]::Max(1, $choco.Count))
    $filter = "tolower(Id) eq '" + $entry.Id.ToLowerInvariant().Replace("'", "''") + "' and IsLatestVersion"
    try { [xml]$feed = (Invoke-WebRequest -Uri ('https://community.chocolatey.org/api/v2/Packages()?$filter=' + [Uri]::EscapeDataString($filter)) -UseBasicParsing -TimeoutSec 60).Content }
    catch { Add-Result $entry 'ERROR' ('Chocolatey feed error: ' + $_.Exception.Message); continue }
    $e = @($feed.feed.entry) | Where-Object { $_ } | Select-Object -First 1
    if (-not $e) { Add-Result $entry 'ERROR' 'No current Chocolatey version (package removed, unlisted or renamed).'; continue }
    $published = [datetime]$e.properties.Published.'#text'   # when this (latest) version was published
    if ($published -lt (Get-Date).AddDays(-$StaleDays)) { Add-Result $entry 'WARN' ('Chocolatey package not updated since ' + $published.ToString('yyyy-MM-dd') + ' (v' + $e.properties.Version + '); check for a fresher source.') }
}
Write-Progress -Activity 'Checking Chocolatey packages' -Completed

# ---- Microsoft Store: products must exist in the display catalog
$store = @($entries | Where-Object Source -eq 'store')
if ($store.Count) {
    $catalog = Invoke-RestMethod -Uri ('https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds=' + (($store.Id) -join ',') + '&market=' + $Market + '&languages=en-US,neutral') -UseBasicParsing -TimeoutSec 120
    $known = @{}; foreach ($p in @($catalog.Products)) { $known[[string]$p.ProductId] = $true }
    foreach ($entry in $store) { if (-not $known[$entry.Id]) { Add-Result $entry 'ERROR' ('Not found in the Microsoft Store catalog for market ' + $Market + '.') } }
}

# ---- winget: package IDs must exist
$wingetExe = (Get-Command winget.exe -ErrorAction SilentlyContinue | Select-Object -First 1).Source
foreach ($entry in @($entries | Where-Object Source -eq 'winget')) {
    if ($SkipWinget -or -not $wingetExe) { Add-Result $entry 'SKIP' 'winget not available; ID not checked.'; continue }
    $out = @(& $wingetExe show --id $entry.Id --exact --source winget --accept-source-agreements --disable-interactivity 2>&1)
    if ($LASTEXITCODE -ne 0) { Add-Result $entry 'ERROR' ('winget has no package with this ID (exit 0x{0:X8}).' -f $LASTEXITCODE) }
}

# ---- GitHub: the latest release must have a usable installer
foreach ($entry in @($entries | Where-Object Source -eq 'github')) {
    try {
        $release = Get-SetupGitHubRelease $entry.Id
        $asset = Select-SetupGitHubAsset $release
        if (-not $asset) { Add-Result $entry 'ERROR' ('Latest release ' + $release.tag_name + ' has no x64 .msi or setup .exe.') }
        elseif (-not $asset.digest) { Add-Result $entry 'WARN' ($asset.name + ' has no published SHA-256 checksum.') }
    } catch { Add-Result $entry 'ERROR' $_.Exception.Message }
}

# ---- logos: bundled, readable and recorded
$recorded = @{}
foreach ($s in (Get-Content (Join-Path $icons 'sources.json') -Raw | ConvertFrom-Json)) { foreach ($x in @($s)) { $recorded[[string]$x.package] = $true } }
foreach ($entry in $entries) {
    $file = Join-Path $icons ($entry.Icon + '.png')
    if (-not (Test-Path -LiteralPath $file)) { Add-Result $entry 'WARN' ('No bundled logo (' + $entry.Icon + '.png); run tools\add-missing-icons.ps1.'); continue }
    try { $img = [Drawing.Image]::FromFile($file); try { if ($img.Width -lt 32) { Add-Result $entry 'WARN' 'Bundled logo is smaller than 32px.' } } finally { $img.Dispose() } }
    catch { Add-Result $entry 'ERROR' 'Bundled logo is not a readable PNG.' }
    if (-not $recorded[$entry.Icon]) { Add-Result $entry 'WARN' 'Logo source is not recorded in src\icons\sources.json.' }
}

# ---- report
$counts = $entries | Group-Object Source | ForEach-Object { $_.Name + ' ' + $_.Count }
Write-Host ('Checked ' + $entries.Count + ' catalog apps (' + ($counts -join ', ') + ').')
if ($results.Count) { $results | Sort-Object @{ e = { @{ ERROR = 0; WARN = 1; SKIP = 2 }[$_.Level] } }, Source, Id | Format-Table -AutoSize -Wrap | Out-String -Width 220 | Write-Host }
$errors = @($results | Where-Object Level -eq 'ERROR').Count
Write-Host ('{0} error(s), {1} warning(s).' -f $errors, @($results | Where-Object Level -eq 'WARN').Count)
exit [int]($errors -gt 0)
