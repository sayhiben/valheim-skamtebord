Set-StrictMode -Version Latest

function Get-SkamtebordRoot {
    [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
}

function Resolve-ValheimPath {
    param([string] $ValheimPath)
    $candidates = [Collections.Generic.List[string]]::new()
    if ($ValheimPath) {
        $candidates.Add([IO.Path]::GetFullPath($ValheimPath))
    } else {
        if ($env:VALHEIM_PATH) { $candidates.Add($env:VALHEIM_PATH) }
        $steamRoots = [Collections.Generic.List[string]]::new()
        foreach ($key in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam')) {
            $value = Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue
            if ($value) {
                foreach ($property in @('SteamPath', 'InstallPath')) {
                    if ($value.PSObject.Properties[$property] -and $value.$property) { $steamRoots.Add($value.$property) }
                }
            }
        }
        if (${env:ProgramFiles(x86)}) { $steamRoots.Add((Join-Path ${env:ProgramFiles(x86)} 'Steam')) }
        foreach ($steamRoot in ($steamRoots | Select-Object -Unique)) {
            $candidates.Add((Join-Path $steamRoot 'steamapps/common/Valheim'))
            $libraryFile = Join-Path $steamRoot 'steamapps/libraryfolders.vdf'
            if (!(Test-Path -LiteralPath $libraryFile -PathType Leaf)) { continue }
            $vdf = Get-Content -LiteralPath $libraryFile -Raw
            # Modern Steam VDF nests numbered library records with a quoted path.
            foreach ($match in [regex]::Matches($vdf, '"path"\s+"((?:\\.|[^"\\])*)"')) {
                $libraryRoot = $match.Groups[1].Value.Replace('\\', '\').Replace('\"', '"')
                $candidates.Add((Join-Path $libraryRoot 'steamapps/common/Valheim'))
            }
            # Older VDF files map the library number directly to a path.
            foreach ($match in [regex]::Matches($vdf, '(?m)^\s*"\d+"\s+"([A-Za-z]:[^"\r\n]*)"')) {
                $candidates.Add((Join-Path $match.Groups[1].Value.Replace('\\', '\') 'steamapps/common/Valheim'))
            }
        }
    }
    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if ((Test-Path -LiteralPath (Join-Path $candidate 'valheim.exe') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $candidate 'valheim_Data/Managed/assembly_valheim.dll') -PathType Leaf)) {
            return (Resolve-Path -LiteralPath $candidate).ProviderPath
        }
    }
    throw 'Valheim was not found. Pass -ValheimPath "D:\SteamLibrary\steamapps\common\Valheim" or set VALHEIM_PATH. The directory must contain valheim.exe and valheim_Data/Managed/assembly_valheim.dll.'
}

function Assert-SkamtebordChildPath {
    param([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][string] $Parent)
    $fullPath = [IO.Path]::GetFullPath($Path)
    $parentPrefix = [IO.Path]::GetFullPath($Parent).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (!$fullPath.StartsWith($parentPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing an operation outside '$Parent': $fullPath"
    }
    $fullPath
}

function Get-SkamtebordPackages {
    param([switch] $Download)
    $packageRoot = Join-Path (Get-SkamtebordRoot) '.local/packages'
    $specifications = @(
        @{ Name = 'BepInEx'; Version = '5.4.2350'; PackageName = 'BepInExPack_Valheim'; Url = 'https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/5.4.2350/'; Sha256 = '37A91C000B4E88F2ED7A4BD7D812239852D2E36CBF0FF0A9F5FAACFBA46B105F'; Required = 'BepInExPack_Valheim/BepInEx/core/BepInEx.dll' },
        @{ Name = 'Jotunn'; Version = '2.30.1'; PackageName = 'Jotunn'; Url = 'https://thunderstore.io/package/download/ValheimModding/Jotunn/2.30.1/'; Sha256 = 'B06CD3F6CECA2BC3883F7818B150725EF8440B920EA22D91B2665251F5883797'; Required = 'plugins/Jotunn.dll' }
    )
    $result = @{}
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($spec in $specifications) {
        $archive = Join-Path $packageRoot ($spec.Name + '.zip')
        $directory = Join-Path $packageRoot $spec.Name
        if (!(Test-Path -LiteralPath $archive -PathType Leaf)) {
            if (!$Download) { throw "Dependency archive missing: $archive. Run scripts/build.ps1 first." }
            New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
            Write-Host "Downloading $($spec.Name) $($spec.Version)..."
            Invoke-WebRequest -Uri $spec.Url -OutFile $archive -UseBasicParsing
        }
        $actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
        if ($actualHash -ne $spec.Sha256) {
            throw "SHA256 mismatch for $archive. Expected $($spec.Sha256), got $actualHash. Remove the incorrect cache file and retry."
        }
        if ($Download) {
            # Re-extract only the verified archive, preventing modified cached DLLs from becoming build references.
            Expand-Archive -LiteralPath $archive -DestinationPath $directory -Force
        }
        $manifestPath = Join-Path $directory 'manifest.json'
        $requiredPath = Join-Path $directory $spec.Required
        if (!(Test-Path -LiteralPath $manifestPath -PathType Leaf) -or !(Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Incomplete $($spec.Name) package in $directory. Run scripts/build.ps1 to restore it."
        }
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($manifest.name -ne $spec.PackageName -or $manifest.version_number -ne $spec.Version) {
            throw "Unexpected dependency manifest in $manifestPath."
        }
        # Read-only callers (including -WhatIf installs) also verify every extracted dependency file.
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        try {
            $knownFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($entry in $zip.Entries) {
                if (!$entry.Name) { continue }
                $extractedPath = Assert-SkamtebordChildPath -Path (Join-Path $directory $entry.FullName) -Parent $directory
                $null = $knownFiles.Add($extractedPath)
                if (!(Test-Path -LiteralPath $extractedPath -PathType Leaf)) { throw "Missing dependency file: $extractedPath" }
                $stream = $entry.Open()
                $hasher = [Security.Cryptography.SHA256]::Create()
                try { $entryHash = [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '') }
                finally { $stream.Dispose(); $hasher.Dispose() }
                if ((Get-FileHash -LiteralPath $extractedPath -Algorithm SHA256).Hash -ne $entryHash) {
                    throw "Modified dependency cache: $extractedPath. Run scripts/build.ps1 to restore it."
                }
            }
            foreach ($extractedFile in (Get-ChildItem -LiteralPath $directory -Recurse -File -Force)) {
                if (!$knownFiles.Contains($extractedFile.FullName)) {
                    throw "Unexpected file in the dependency cache: $($extractedFile.FullName). Remove the stale package directory and rebuild."
                }
            }
        } finally { $zip.Dispose() }
        $result[$spec.Name] = $directory
    }
    $result
}

function Copy-SkamtebordFile {
    param([Parameter(Mandatory)][string] $Source, [Parameter(Mandatory)][string] $Destination)
    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}
