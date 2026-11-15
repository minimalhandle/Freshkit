#requires -Version 5.1
# Freshkit: update installed apps (elevated). -All updates everything Chocolatey and winget know about.
# -Store: Microsoft Store product IDs (upgraded through winget's msstore source; never installs new apps).
# -GitHub: 'owner/repo|Display name;...' for GitHub-release apps; only apps already installed are updated.
param([string]$LogFile='',[string]$Choco='',[string]$Winget='',[string]$Store='',[string]$GitHub='',[switch]$All)
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
$storeIds=@($Store -split ','|ForEach-Object {$_.Trim()}|Where-Object {$_ -cmatch '\A(?:9[A-Z0-9]{11}|XP[A-Z0-9]{12})\z'})
if($storeIds.Count){
    if($wingetExe){
        Write-Host '';Write-Host '==> Updating Microsoft Store apps'
        foreach($id in $storeIds){
            Write-Host ('-- '+$id)
            Show (& $wingetExe upgrade --id $id --exact --silent --source msstore --accept-package-agreements --accept-source-agreements --disable-interactivity 2>&1)
            # 0x8A15002B: no applicable update; 0x8A150014: not installed (nothing to update).
            if($LASTEXITCODE -notin @(0,-1978335189,-1978335212)){$code=1;Write-Host ('winget exit code: 0x{0:X8}' -f $LASTEXITCODE)}
        }
    }else{Write-Host 'winget is not available; skipping Microsoft Store apps.'}
}
$githubApps=@($GitHub -split ';'|ForEach-Object {$p=$_ -split '\|',2;if($p.Count -eq 2 -and $p[0].Trim() -and $p[1].Trim()){[pscustomobject]@{githubRepo=$p[0].Trim();name=$p[1].Trim();packageParameters='';upgrade=$true}}})
if($githubApps.Count){
    Write-Host '';Write-Host '==> Updating GitHub apps'
    Import-Module (Join-Path $PSScriptRoot 'Install-Chocolatey.Core.psm1') -Force -DisableNameChecking
    foreach($app in $githubApps){
        Write-Host ('-- '+$app.name+' (github.com/'+$app.githubRepo+')')
        try{
            if(-not (Test-SetupGitHubRepo $app.githubRepo)){throw 'Invalid GitHub repository.'}
            if(-not (Test-SetupGitHubInstalled $app.name).Installed){Write-Host 'Not installed; skipped.';continue}
            $r=Invoke-SetupGitHubApp $app;Write-Host ($r.Status+': '+$r.Detail)
        }catch{$code=1;Write-Host ('ERROR: '+$_.Exception.Message)}
    }
}
Write-Host '';Write-Host ('==> Finished (exit code '+$code+')')
try{Stop-Transcript|Out-Null}catch{}
exit $code
