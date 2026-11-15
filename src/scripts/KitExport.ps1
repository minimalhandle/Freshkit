#requires -Version 5.1
# Freshkit: build the offline kit next to Freshkit.exe (elevated).
# Saves this PC's drivers (network only, or all third-party drivers) and optionally Wi-Fi networks,
# so a freshly installed Windows can get online before anything is downloaded.
param([Parameter(Mandatory=$true)][string]$KitDir,[string]$LogFile='',[ValidateSet('Network','All')][string]$Mode='Network',[switch]$Wifi)
$ErrorActionPreference='Stop'
if($LogFile){try{[IO.Directory]::CreateDirectory((Split-Path -Parent $LogFile))|Out-Null;Start-Transcript -LiteralPath $LogFile -Append|Out-Null}catch{}}
$code=0
try{
    $stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
    $driverDir=Join-Path $KitDir ('Drivers\'+$env:COMPUTERNAME+'-'+$stamp)
    [IO.Directory]::CreateDirectory($driverDir)|Out-Null
    Write-Host '==> Exporting drivers'
    if($Mode -eq 'All'){
        Export-WindowsDriver -Online -Destination $driverDir|Select-Object ClassName,ProviderName,Version,OriginalFileName|Format-Table -AutoSize|Out-String -Width 220|Write-Host
    }else{
        $net=@(Get-WindowsDriver -Online|Where-Object {$_.ClassName -eq 'Net'})
        if(-not $net.Count){Write-Host 'No third-party network drivers found (Windows inbox drivers are already on every install).'}
        foreach($d in $net){
            Write-Host ('{0}  {1}  {2}' -f $d.ProviderName,$d.Version,$d.OriginalFileName)
            $target=Join-Path $driverDir ([IO.Path]::GetFileNameWithoutExtension($d.Driver))
            [IO.Directory]::CreateDirectory($target)|Out-Null
            & (Join-Path $env:SystemRoot 'System32\pnputil.exe') /export-driver $d.Driver $target|Out-Null
            if($LASTEXITCODE -ne 0){throw "Driver export failed (exit code $LASTEXITCODE)."}
        }
    }
    if($Wifi){
        Write-Host '';Write-Host '==> Saving Wi-Fi networks (passwords in plain text)'
        $wifiDir=Join-Path $KitDir 'WiFi';[IO.Directory]::CreateDirectory($wifiDir)|Out-Null
        & netsh.exe wlan export profile ('folder="'+$wifiDir+'"') key=clear 2>&1|ForEach-Object {Write-Host $_}
        if($LASTEXITCODE -ne 0){throw "Wi-Fi export failed (exit code $LASTEXITCODE)."}
    }
    $readme=@"
Freshkit offline kit
====================
Created $(Get-Date -Format 'yyyy-MM-dd HH:mm') on $env:COMPUTERNAME.

Drivers\   Driver packages exported from that PC ($Mode drivers). Freshkit installs them first
           on a fresh Windows install (Drivers page, or the 'offline kit' step of an install).
WiFi\      Saved Wi-Fi networks, if you chose to include them. These files contain the
           Wi-Fi passwords in plain text: keep this USB stick private.
"@
    Set-Content -LiteralPath (Join-Path $KitDir 'README.txt') -Value $readme -Encoding UTF8
    $size=(Get-ChildItem -LiteralPath $KitDir -Recurse -File|Measure-Object Length -Sum).Sum
    Write-Host '';Write-Host ('==> Kit saved to '+$KitDir+' ('+[math]::Round($size/1MB,1)+' MB)')
}catch{Write-Host ('ERROR: '+$_.Exception.Message);$code=1}
try{Stop-Transcript|Out-Null}catch{}
exit $code
