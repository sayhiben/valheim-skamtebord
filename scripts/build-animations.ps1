[CmdletBinding()]
param(
    [string] $BlenderPath = (Join-Path $PSScriptRoot '../.tools/blender-4.2.3-windows-x64/blender.exe'),
    [string] $UnityPath = (Join-Path $PSScriptRoot '../.tools/Unity6000.0.75/Editor/Unity.exe')
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
foreach ($executable in @($BlenderPath, $UnityPath)) {
    if (!(Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Executable missing: $executable. Supply -BlenderPath and -UnityPath (Unity 6000.0.75f1)." }
}
$local = Join-Path $root '.local'
New-Item -ItemType Directory -Path $local -Force | Out-Null
& $BlenderPath --background --python (Join-Path $PSScriptRoot 'create-skate-animation.py') *> (Join-Path $local 'blender-animation-build.log')
if ($LASTEXITCODE -ne 0) { throw 'Blender export failed. See .local/blender-animation-build.log.' }
$project = Join-Path $root 'assets/SkamtebordAnimations'
$log = Join-Path $local 'animation-unity-build.log'
$editor = Start-Process -FilePath $UnityPath -ArgumentList @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $project + '"'), '-executeMethod', 'BuildSkateAnimations.Build', '-logFile', ('"' + $log + '"')) -WindowStyle Hidden -Wait -PassThru
if ($editor.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -Pattern 'SKATE_ASSET SUCCESS' -Quiet)) {
    throw "Unity animation build failed. See $log."
}
Write-Host 'Original Humanoid animations rebuilt. Run scripts/build.ps1 to embed them in the mod.'
