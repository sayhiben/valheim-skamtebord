[CmdletBinding()]
param([switch]$NoBuild)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-SkamtebordRoot
& (Join-Path $PSScriptRoot 'qa.ps1') -StageOnly -NoBuild:$NoBuild
$runtime = Join-Path $root '.local/runtime'
$peerRoot = Join-Path $root ('.local/network-qa/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $peerRoot -Force | Out-Null
$children = @()
try {
    foreach ($role in @('host','client')) {
        $log = Join-Path $peerRoot ($role + '.log')
        $argsForPeer = @('-skamtebord-smoke','-skamtebord-peer',$role,'-skamtebord-peer-root',('"'+$peerRoot+'"'),'-logFile',('"'+$log+'"'),'-force-d3d11','-screen-fullscreen','0','-screen-width','960','-screen-height','540')
        $children += Start-Process -FilePath (Join-Path $runtime 'valheim.exe') -ArgumentList $argsForPeer -WorkingDirectory $runtime -WindowStyle Hidden -PassThru
        Write-Host "Started QA $role PID $($children[-1].Id); $log"
        if ($role -eq 'host') {
            $until = (Get-Date).AddSeconds(90)
            while (!(Test-Path -LiteralPath (Join-Path $peerRoot 'port.txt')) -and (Get-Date) -lt $until -and !$children[0].HasExited) { Start-Sleep -Milliseconds 200; $children[0].Refresh() }
            if (!(Test-Path -LiteralPath (Join-Path $peerRoot 'port.txt'))) { throw 'QA loopback listener did not start.' }
        }
    }
    $until = (Get-Date).AddSeconds(300)
    while (@($children | Where-Object { !$_.HasExited }).Count -gt 0 -and (Get-Date) -lt $until) {
        Start-Sleep -Milliseconds 300
        foreach ($child in $children) { $child.Refresh() }
        foreach ($child in $children) { if ($child.HasExited -and $child.ExitCode -ne 0) { throw "QA peer failed. See $peerRoot" } }
    }
    foreach ($child in $children) { if (!$child.HasExited) { throw 'Multiplayer QA timeout.' }; $child.WaitForExit(); if ($child.ExitCode -ne 0) { throw "QA peer exited with $($child.ExitCode). See $peerRoot" } }
    foreach ($role in @('host','client')) {
        $saveRoot = (Get-Content -LiteralPath (Join-Path $peerRoot ($role+'-save-root.txt')) -Raw).Trim()
        Get-Content -LiteralPath (Join-Path $saveRoot 'smoke-result.txt')
    }
    Write-Host "Multiplayer QA passed. Evidence: $peerRoot"
} finally {
    foreach ($child in $children) {
        if (!$child.HasExited) { Stop-Process -Id $child.Id -Force }
        $null = $child.WaitForExit(15000)
    }
}
