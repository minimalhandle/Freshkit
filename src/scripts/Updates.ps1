#requires -Version 5.1
# Freshkit: update installed apps (elevated). -All updates everything Chocolatey and winget know about.
param([string]$LogFile='',[string]$Choco='',[string]$Winget='',[switch]$All)
$ErrorActionPreference='Continue'
if($LogFile){try{[IO.Directory]::CreateDirectory((Split-Path -Parent $LogFile))|Out-Null;Start-Transcript -LiteralPath $LogFile -Append|Out-Null}catch{}}
$code=0
$root=[Environment]::GetEnvironmentVariable('ChocolateyInstall','Machine');if(-not $root){$root=Join-Path $env:ProgramData 'chocolatey'}
$chocoExe=Join-Path $root 'bin\choco.exe'
$wingetExe=$null;$cmd=Get-Command winget.exe -ErrorAction SilentlyContinue|Select-Object -First 1
if($cmd){$wingetExe=$cmd.Source}elseif($env:LOCALAPPDATA -and (Test-Path (Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\winget.exe'))){$wingetExe=Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\winget.exe'}
$wingetCommon=@('--silent','--source','winget','--accept-package-agreements','--accept-source-agreements','--disable-interactivity')
function Show($lines){foreach($l in $lines){$t=([string]$l).TrimEnd();if($t.Trim() -and $t -notmatch '^[\s\-\\|/]+$' -and $t -notmatch '[▀-▟]'){Write-Host $t}}}

$chocoIds=@($Choco -split ','|ForEach-Object {$_.Trim()}|Where-Object {$_})
$wingetIds=@($Winget -split ','|ForEach-Object {$_.Trim()}|Where-Object {$_})
if($All -or $chocoIds.Count){
    if(Test-Path -LiteralPath $chocoExe){
        Write-Host '';Write-Host '==> Updating Chocolatey apps'
        $target=if($All){@('all')}else{$chocoIds}
        Show (& $chocoExe upgrade @target -y --no-progress 2>&1)
        if($LASTEXITCODE -notin @(0,3010,1641)){$code=1;Write-Host ('choco exit code: '+$LASTEXITCODE)}
    }else{Write-Host 'Chocolatey is not installed; skipping.'}
}
if($All -or $wingetIds.Count){
    if($wingetExe){
        Write-Host '';Write-Host '==> Updating winget apps'
        if($All){Show (& $wingetExe upgrade --all @wingetCommon 2>&1);if($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne -1978335189){$code=1;Write-Host ('winget exit code: 0x{0:X8}' -f $LASTEXITCODE)}}
        else{
            foreach($id in $wingetIds){
                Write-Host ('-- '+$id)
                Show (& $wingetExe upgrade --id $id --exact @wingetCommon 2>&1)
                if($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne -1978335189){$code=1;Write-Host ('winget exit code: 0x{0:X8}' -f $LASTEXITCODE)}
            }
        }
    }else{Write-Host 'winget is not available; skipping.'}
}
Write-Host '';Write-Host ('==> Finished (exit code '+$code+')')
try{Stop-Transcript|Out-Null}catch{}
exit $code
