[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param([string] $ValheimPath)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-SkamtebordRoot
$game = Resolve-ValheimPath -ValheimPath $ValheimPath
$running = @(Get-Process -Name 'valheim', 'valheim_server' -ErrorAction SilentlyContinue)
if ($running.Count) { throw 'Close Valheim and its dedicated server before installing. No files were changed.' }
$distribution = Join-Path $root 'dist/Skamtebord'
$modDll = Join-Path $distribution 'Skamtebord.dll'
if (!(Test-Path -LiteralPath $modDll -PathType Leaf)) { throw 'Build the mod with scripts/build.ps1 before installing.' }
$packages = Get-SkamtebordPackages
$loaderSource = Join-Path $packages.BepInEx 'BepInExPack_Valheim'
$existingCore = Join-Path $game 'BepInEx/core/BepInEx.dll'
$expectedCore = Join-Path $loaderSource 'BepInEx/core/BepInEx.dll'
$expectedVersion = [Reflection.AssemblyName]::GetAssemblyName($expectedCore).Version
$coreDirectory = Join-Path $game 'BepInEx/core'
if (!(Test-Path -LiteralPath $existingCore -PathType Leaf) -and (Test-Path -LiteralPath $coreDirectory -PathType Container)) {
    $unrecognizedCore = @(Get-ChildItem -LiteralPath $coreDirectory -File -Force)
    if ($unrecognizedCore.Count) {
        throw "An unrecognized or partial BepInEx core exists at $coreDirectory. This may be another major version. Update or isolate your loader first; no files were changed."
    }
}
if (Test-Path -LiteralPath $existingCore -PathType Leaf) {
    try { $existingVersion = [Reflection.AssemblyName]::GetAssemblyName($existingCore).Version }
    catch { throw "Existing BepInEx core is unreadable: $existingCore. Manage that installation separately before retrying." }
    if ($existingVersion -ne $expectedVersion) {
        throw "Existing BepInEx assembly $existingVersion differs from required $expectedVersion (pack 5.4.2350). Update or isolate your mod profile first; this installer will not overwrite the loader."
    }
}
$copies = [Collections.Generic.List[object]]::new()
foreach ($file in (Get-ChildItem -LiteralPath $loaderSource -File -Recurse -Force)) {
    $relative = $file.FullName.Substring($loaderSource.Length).TrimStart('\', '/')
    $target = Assert-SkamtebordChildPath -Path (Join-Path $game $relative) -Parent $game
    if (Test-Path -LiteralPath $target -PathType Leaf) {
        # Preserve all existing user configuration and launcher scripts; never upgrade core binaries silently.
        if ($file.Extension -in @('.cfg', '.ini', '.xml', '.txt', '.sh')) { continue }
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) {
            throw "Existing loader file differs from the pinned pack: $target. Update or isolate that loader first; no files were changed."
        }
    } else {
        $copies.Add(@{ Source = $file.FullName; Destination = $target })
    }
}
$plugins = Join-Path $game 'BepInEx/plugins'
$jotunnSource = Join-Path $packages.Jotunn 'plugins/Jotunn.dll'
$existingJotunn = @()
$existingMods = @()
if (Test-Path -LiteralPath $plugins -PathType Container) {
    $existingJotunn = @(Get-ChildItem -LiteralPath $plugins -Filter 'Jotunn.dll' -File -Recurse)
    $existingMods = @(Get-ChildItem -LiteralPath $plugins -Filter 'Skamtebord.dll' -File -Recurse)
}
if ($existingJotunn.Count -gt 1) { throw 'Multiple Jotunn.dll copies were found under BepInEx/plugins. Resolve duplicate installations first.' }
if ($existingJotunn.Count -eq 1) {
    if ((Get-FileHash -LiteralPath $existingJotunn[0].FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $jotunnSource -Algorithm SHA256).Hash) {
        throw "Existing Jotunn differs from pinned version 2.30.1: $($existingJotunn[0].FullName). Update or isolate your mod profile first; no files were changed."
    }
} else {
    $copies.Add(@{ Source = $jotunnSource; Destination = (Join-Path $plugins 'Jotunn/Jotunn.dll') })
}
$modTarget = Join-Path $plugins 'Skamtebord'
$targetDll = Join-Path $modTarget 'Skamtebord.dll'
foreach ($existingMod in $existingMods) {
    if (![StringComparer]::OrdinalIgnoreCase.Equals($existingMod.FullName, $targetDll)) {
        throw "Another Skamtebord copy exists at $($existingMod.FullName). Resolve duplicate installations before using this installer."
    }
}
$copies.Add(@{ Source = $modDll; Destination = $targetDll })
foreach ($name in @('README.md', 'CHANGELOG.md', 'LICENSE', 'radio-mp3s/README.txt')) {
    $source = Join-Path $distribution $name
    if (Test-Path -LiteralPath $source -PathType Leaf) { $copies.Add(@{ Source = $source; Destination = (Join-Path $modTarget $name) }) }
}
Write-Host "Target: $game"
Write-Host "Plan: copy $($copies.Count) files; preserve existing loader configuration and personal radio files."
if ($PSCmdlet.ShouldProcess($game, 'Install Skamtebord and missing files from pinned BepInEx 5.4.2350 / Jotunn 2.30.1')) {
    foreach ($copy in $copies) {
        $null = Assert-SkamtebordChildPath -Path $copy.Destination -Parent $game
        Copy-SkamtebordFile -Source $copy.Source -Destination $copy.Destination
    }
    if ((Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $modDll -Algorithm SHA256).Hash) {
        throw 'Installed Skamtebord DLL failed hash verification.'
    }
    Write-Host "Installed: $targetDll"
    Write-Host 'Launch Valheim through Steam when ready. The installer does not launch the game.'
}
