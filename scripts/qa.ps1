[CmdletBinding()]
param(
    [ValidateSet('Play', 'Keyboard', 'Physics', 'Ramps', 'Radio', 'Flow', 'Surfaces', 'Carving')][string] $Mode = 'Play',
    [string] $ValheimPath,
    [string] $RadioDirectory,
    [switch] $NoBuild,
    [switch] $StageOnly,
    [switch] $FreshProgression,
    [switch] $FullWorld,
    [switch] $Record,
    [switch] $RenderDiagnostics,
    [switch] $Ramps
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-SkamtebordRoot
if (!$RadioDirectory) { $RadioDirectory = Join-Path $root 'radio-mp3s' }
$RadioDirectory = [IO.Path]::GetFullPath($RadioDirectory)
$game = Resolve-ValheimPath -ValheimPath $ValheimPath
if (@(Get-Process -Name valheim, valheim_server -ErrorAction SilentlyContinue).Count) {
    throw 'A Valheim process is already running. Close it before starting another QA session.'
}
if ($Record -and $Mode -notin @('Keyboard', 'Ramps')) { throw '-Record requires -Mode Keyboard or Ramps.' }
if ($Ramps -and $Mode -ne 'Play') { throw '-Ramps adds the interactive course to Play mode; use -Mode Ramps for automated tests.' }
if ($RenderDiagnostics -and $Mode -ne 'Play') { throw '-RenderDiagnostics captures the ordinary camera in Play mode.' }
$runtime = Assert-SkamtebordChildPath -Path (Join-Path $root '.local/runtime') -Parent $root
if ((Test-Path -LiteralPath $runtime) -and (Get-Item -LiteralPath $runtime).LinkType) { throw 'The QA runtime root must be a real directory.' }
New-Item -ItemType Directory -Path $runtime -Force | Out-Null
$packages = Get-SkamtebordPackages -Download
if (!$NoBuild) {
    foreach ($project in @('src/Skamtebord', 'tests/Skamtebord.RuntimeSmoke')) {
        $buildLog = Join-Path $root ('.local/qa-build-' + (Split-Path $project -Leaf) + '.log')
        Write-Host "Building $project..."
        & dotnet build (Join-Path $root $project) -c Release --nologo -v:q "-p:ValheimPath=$game" *> $buildLog
        if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $buildLog -Tail 30; throw "Build failed: $project. See $buildLog." }
    }
}

# Only these read-only game-data junctions are permitted. Never recursively clean
# the staged runtime: its data directories point into the user's Steam install.
foreach ($name in @('valheim_Data', 'MonoBleedingEdge')) {
    $link = Join-Path $runtime $name
    $target = Join-Path $game $name
    if (!(Test-Path -LiteralPath $link)) {
        New-Item -ItemType Junction -Path $link -Target $target | Out-Null
    } else {
        $item = Get-Item -LiteralPath $link
        if ($item.LinkType -ne 'Junction' -or [IO.Path]::GetFullPath([string]$item.Target) -ne [IO.Path]::GetFullPath($target)) {
            throw "Unexpected QA game-data path: $link. Resolve it manually; the launcher never deletes it."
        }
    }
}
foreach ($name in @('valheim.exe', 'UnityPlayer.dll')) {
    Copy-SkamtebordFile -Source (Join-Path $game $name) -Destination (Join-Path $runtime $name)
}
$loader = Join-Path $packages.BepInEx 'BepInExPack_Valheim'
foreach ($file in Get-ChildItem -LiteralPath $loader -Recurse -File) {
    $relative = $file.FullName.Substring($loader.Length).TrimStart('\', '/')
    $target = Assert-SkamtebordChildPath -Path (Join-Path $runtime $relative) -Parent $runtime
    if ($file.Extension -in @('.ini', '.cfg') -and (Test-Path -LiteralPath $target)) { continue }
    Copy-SkamtebordFile -Source $file.FullName -Destination $target
}
'892970' | Set-Content -LiteralPath (Join-Path $runtime 'steam_appid.txt') -Encoding ascii
Copy-SkamtebordFile -Source (Join-Path $packages.Jotunn 'plugins/Jotunn.dll') -Destination (Join-Path $runtime 'BepInEx/plugins/Jotunn/Jotunn.dll')
Copy-SkamtebordFile -Source (Join-Path $root 'src/Skamtebord/bin/Release/netstandard2.1/Skamtebord.dll') -Destination (Join-Path $runtime 'BepInEx/plugins/Skamtebord/Skamtebord.dll')
Copy-SkamtebordFile -Source (Join-Path $root 'tests/Skamtebord.RuntimeSmoke/bin/Release/netstandard2.1/Skamtebord.RuntimeSmoke.dll') -Destination (Join-Path $runtime 'BepInEx/plugins/RuntimeSmoke/Skamtebord.RuntimeSmoke.dll')
if ($StageOnly) { Write-Host "QA staged at $runtime"; return }
$log = Join-Path $root ('.local/qa-' + $Mode.ToLowerInvariant() + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
$arguments = @('-skamtebord-smoke', '-console', '-logFile', ('"' + $log + '"'))
$arguments += @('-skamtebord-radio-directory', ('"' + $RadioDirectory + '"'))
if ($Mode -in @('Physics','Surfaces','Carving') -or ($Mode -eq 'Ramps' -and !$Record)) { $arguments += @('-batchmode', '-nographics') }
else { $arguments += @('-force-d3d11', '-screen-fullscreen', '0', '-screen-width', '1920', '-screen-height', '1080') }
if ($Mode -eq 'Play') { $arguments += '-skamtebord-qa' }
if ($Mode -eq 'Keyboard') { $arguments += '-skamtebord-keyboard-test' }
if ($Mode -eq 'Radio') { $arguments += '-skamtebord-radio-test' }
if ($Mode -eq 'Flow') { $arguments += '-skamtebord-flow-test' }
if ($Mode -eq 'Surfaces') { $arguments += '-skamtebord-surface-test' }
if ($Mode -eq 'Carving') { $arguments += '-skamtebord-carving-test' }
if ($Mode -eq 'Ramps' -or $Ramps) { $arguments += '-skamtebord-ramps' }
if ($FreshProgression) { $arguments += '-skamtebord-fresh-progression' }
if ($FullWorld) { $arguments += '-skamtebord-full-world' }
if ($Record) { $arguments += $(if ($Mode -eq 'Ramps') { '-skamtebord-ramp-capture' } else { '-skamtebord-keyboard-capture' }) }
if ($RenderDiagnostics) { $arguments += '-skamtebord-render-diagnostics' }
$launch = @{ FilePath = (Join-Path $runtime 'valheim.exe'); ArgumentList = $arguments; WorkingDirectory = $runtime; PassThru = $true }
if ($Mode -ne 'Play') { $launch.WindowStyle = 'Hidden' }
$watch = [Diagnostics.Stopwatch]::StartNew()
$process = Start-Process @launch
Write-Host "QA $Mode started (PID $($process.Id)). Log: $log"
$sessionIndex = Join-Path $runtime 'latest-qa-session.txt'
$ready = $null
while (!$process.HasExited) {
    if (!$ready -and (Test-Path -LiteralPath $sessionIndex)) {
        $session = (Get-Content -LiteralPath $sessionIndex -Raw).Trim()
        $null = Assert-SkamtebordChildPath -Path $session -Parent (Join-Path $runtime 'smoke-saves')
        $readyFile = Join-Path $session 'qa-ready.json'
        if (Test-Path -LiteralPath $readyFile) {
            try { $candidate = Get-Content -LiteralPath $readyFile -Raw | ConvertFrom-Json } catch { $candidate = $null }
            if ($candidate -and $candidate.processId -eq $process.Id) {
                $ready = $candidate
                Write-Host ("Ready in {0:N1}s from launch; Skamtebord {1}; board in slot 1. Evidence: {2}" -f $watch.Elapsed.TotalSeconds, $ready.level, $session)
                if ($Mode -eq 'Play') { $ready; return }
            }
        }
    }
    if ($watch.Elapsed.TotalSeconds -gt 450) {
        Stop-Process -Id $process.Id -Force # Only the isolated child launched by this command.
        throw "QA process exceeded its timeout. Inspect $log and the staged BepInEx log."
    }
    Start-Sleep -Milliseconds 200
    $process.Refresh()
}
$process.WaitForExit()
if ($ready) { Get-Content -LiteralPath (Join-Path $ready.saveRoot 'smoke-result.txt') }
if ($process.ExitCode -ne 0 -or !$ready) { throw "QA failed (exit $($process.ExitCode)). Inspect $log and .local/runtime/BepInEx/LogOutput.log." }
Write-Host ("QA completed in {0:N1}s. Timing breakdown: {1}" -f $watch.Elapsed.TotalSeconds, (Join-Path $ready.saveRoot 'timings.csv'))
