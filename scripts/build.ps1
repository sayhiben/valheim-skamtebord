[CmdletBinding()]
param(
    [string] $ValheimPath,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Get-SkamtebordRoot
$game = Resolve-ValheimPath -ValheimPath $ValheimPath
$null = Get-SkamtebordPackages -Download
if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install a .NET SDK (8.0 or later) and ensure dotnet is on PATH.' }

$project = Join-Path $root 'src/Skamtebord/Skamtebord.csproj'
Write-Host "Building against $game"
& dotnet build $project --configuration Release "-p:ValheimPath=$game" --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
if (!$SkipTests) {
    $testProject = Join-Path $root 'tests/Skamtebord.Core.Tests/Skamtebord.Core.Tests.csproj'
    if (!(Test-Path -LiteralPath $testProject -PathType Leaf)) { throw "Core test project is missing: $testProject" }
    & dotnet run --project $testProject --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Core tests failed with exit code $LASTEXITCODE." }
}

[xml] $projectXml = Get-Content -LiteralPath $project -Raw
$version = @($projectXml.Project.PropertyGroup | Where-Object { $_.PSObject.Properties['Version'] } | ForEach-Object { $_.Version })[0]
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Unsupported package version: $version" }
$dll = Join-Path $root 'src/Skamtebord/bin/Release/netstandard2.1/Skamtebord.dll'
if (!(Test-Path -LiteralPath $dll -PathType Leaf)) { throw "Build output missing: $dll" }
$distribution = Join-Path $root 'dist/Skamtebord'
New-Item -ItemType Directory -Path $distribution -Force | Out-Null
Copy-SkamtebordFile -Source $dll -Destination (Join-Path $distribution 'Skamtebord.dll')
foreach ($name in @('README.md', 'CHANGELOG.md', 'LICENSE')) {
    $source = Join-Path $root $name
    if (Test-Path -LiteralPath $source -PathType Leaf) { Copy-SkamtebordFile -Source $source -Destination (Join-Path $distribution $name) }
}
if (!(Test-Path -LiteralPath (Join-Path $distribution 'README.md'))) { throw 'A root README.md is required for a distributable package.' }
$radioDirectory = Join-Path $distribution 'radio-mp3s'
New-Item -ItemType Directory -Path $radioDirectory -Force | Out-Null
$radioReadme = Join-Path $root 'radio-mp3s/README.txt'
if (Test-Path -LiteralPath $radioReadme -PathType Leaf) {
    Copy-SkamtebordFile -Source $radioReadme -Destination (Join-Path $radioDirectory 'README.txt')
} else {
    @'
Place your own .mp3 files in this directory for the personal skating radio.
The mod's Radio settings let you choose another directory and adjust volume.
Music files are local to your computer and are never uploaded or shared.
No music is bundled with Skamtebord.
'@ | Set-Content -LiteralPath (Join-Path $radioDirectory 'README.txt') -Encoding UTF8
}
$manifest = [ordered]@{
    name = 'Skamtebord'
    author = 'sayhiben'
    version_number = $version
    website_url = 'https://github.com/sayhiben/valheim-skamtebord'
    description = 'Craft a skateboard, ride downhill, push, jump and land tricks to develop your Skamtebord skill, with a personal MP3 radio.'
    dependencies = @('denikson-BepInExPack_Valheim-5.4.2350', 'ValheimModding-Jotunn-2.30.1')
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $distribution 'manifest.json') -Encoding UTF8

$iconSource = Join-Path $root 'assets/icon.png'
$iconTarget = Join-Path $distribution 'icon.png'
if (Test-Path -LiteralPath $iconSource -PathType Leaf) {
    Copy-SkamtebordFile -Source $iconSource -Destination $iconTarget
} else {
    # A small original geometric board icon keeps the package self-contained.
    Add-Type -AssemblyName System.Drawing
    $bitmap = [Drawing.Bitmap]::new(256, 256)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $boardBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(231, 177, 92))
    $wheelBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(231, 235, 219))
    $inkBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(25, 45, 42))
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([Drawing.Color]::FromArgb(25, 45, 42))
        $graphics.TranslateTransform(128, 128)
        $graphics.RotateTransform(-35)
        $graphics.FillEllipse($wheelBrush, -65, -51, 29, 25)
        $graphics.FillEllipse($wheelBrush, 36, -51, 29, 25)
        $graphics.FillEllipse($wheelBrush, -65, 26, 29, 25)
        $graphics.FillEllipse($wheelBrush, 36, 26, 29, 25)
        $graphics.FillEllipse($boardBrush, -105, -32, 64, 64)
        $graphics.FillRectangle($boardBrush, -73, -32, 146, 64)
        $graphics.FillEllipse($boardBrush, 41, -32, 64, 64)
        $graphics.FillEllipse($inkBrush, -51, -6, 12, 12)
        $graphics.FillEllipse($inkBrush, 39, -6, 12, 12)
        $bitmap.Save($iconTarget, [Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $graphics.Dispose(); $bitmap.Dispose(); $boardBrush.Dispose(); $wheelBrush.Dispose(); $inkBrush.Dispose()
    }
}

$stagingParent = Join-Path $root '.local/package-staging'
$staging = Join-Path $stagingParent ([Guid]::NewGuid().ToString('N'))
$null = Assert-SkamtebordChildPath -Path $staging -Parent $stagingParent
New-Item -ItemType Directory -Path $staging -Force | Out-Null
try {
    $plugin = Join-Path $staging 'BepInEx/plugins/Skamtebord'
    Copy-SkamtebordFile -Source $dll -Destination (Join-Path $plugin 'Skamtebord.dll')
    Copy-SkamtebordFile -Source (Join-Path $radioDirectory 'README.txt') -Destination (Join-Path $plugin 'radio-mp3s/README.txt')
    foreach ($name in @('README.md', 'CHANGELOG.md', 'LICENSE', 'manifest.json', 'icon.png')) {
        $source = Join-Path $distribution $name
        if (Test-Path -LiteralPath $source -PathType Leaf) { Copy-SkamtebordFile -Source $source -Destination (Join-Path $staging $name) }
    }
    $docs = Join-Path $root 'docs'
    if (Test-Path -LiteralPath $docs -PathType Container) {
        foreach ($doc in (Get-ChildItem -LiteralPath $docs -File -Recurse | Where-Object { $_.Extension -in @('.md', '.txt', '.csv') })) {
            $relative = $doc.FullName.Substring($docs.Length).TrimStart('\', '/')
            Copy-SkamtebordFile -Source $doc.FullName -Destination (Join-Path (Join-Path $staging 'docs') $relative)
        }
    }
    $archive = Join-Path $root "dist/Skamtebord-$version.zip"
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $archive -Force
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $dllEntries = @($zip.Entries | Where-Object { $_.FullName -match '\.dll$' })
        if ($dllEntries.Count -ne 1 -or $dllEntries[0].FullName.Replace('\', '/') -ne 'BepInEx/plugins/Skamtebord/Skamtebord.dll') {
            throw 'Distribution validation failed: the archive must contain only the Skamtebord plugin DLL.'
        }
        if (@($zip.Entries | Where-Object { $_.FullName -match '\.mp3$|RuntimeSmoke' }).Count) {
            throw 'Distribution validation failed: personal music and the QA harness must not be packaged.'
        }
    } finally { $zip.Dispose() }
} finally {
    $checkedStaging = Assert-SkamtebordChildPath -Path $staging -Parent $stagingParent
    Remove-Item -LiteralPath $checkedStaging -Recurse -Force
}
Write-Host "Package ready: $archive"
$archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$archiveHash  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath "$archive.sha256" -Encoding ascii
Write-Host "SHA256: $archiveHash (also written to $archive.sha256)"
Write-Host 'Build completion does not verify in-game behavior. See docs for runtime checks.'
