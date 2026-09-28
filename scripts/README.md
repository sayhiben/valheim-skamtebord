# Build and install

To play without building, download the compiled ZIP from [GitHub Releases](https://github.com/sayhiben/valheim-skamtebord/releases) and follow the [installation guide](../docs/install.md). The instructions below are for development from a source checkout.

For development playtests, use **`scripts/qa.ps1`**. It builds and launches a separate disposable QA session with intros skipped, a board in slot 1, every skating trick unlocked, and normal controls. `-Mode Keyboard` or `-Mode Physics` runs automated checks and exits; `-Mode Carving` checks push limits, downhill momentum and banked corners. Use `-NoBuild` for another run of the same binaries, `-FreshProgression` for level-zero checks, and `-FullWorld` when ordinary world generation matters. See the [QA harness instructions](../tests/Skamtebord.RuntimeSmoke/README.md).

Run from PowerShell on Windows with .NET SDK 8 or later and the current Steam installation of Valheim.

```powershell
.\scripts\build.ps1
```

Steam's registry entries and `libraryfolders.vdf` locate the game. Override detection with `-ValheimPath 'D:\SteamLibrary\steamapps\common\Valheim'` or the `VALHEIM_PATH` environment variable. The build downloads SHA256-pinned BepInEx 5.4.2350 and Jötunn 2.30.1 archives into `.local/packages`, compiles against your installed game assemblies, and runs the core console tests. `-SkipTests` explicitly skips those tests.

Outputs:

- `dist/Skamtebord/`: plugin DLL, documentation, package metadata, and an empty radio folder with instructions.
- `dist/Skamtebord-0.4.0.zip`: Thunderstore layout with the DLL under `BepInEx/plugins/Skamtebord/`, plus the manifest, icon, and documentation. No game assemblies, dependency DLLs, or music are bundled.
- `dist/Skamtebord-0.4.0.zip.sha256`: SHA-256 checksum of the release ZIP, suitable for publishing alongside it.

The plugin embeds `assets/bundles/skamtebord-animations`. The checked-in bundle is sufficient for normal builds. To regenerate it, run `scripts/build-animations.ps1 -BlenderPath <blender.exe> -UnityPath <Unity.exe>` with Blender 4.2 and Unity **6000.0.75f1**. Defaults point to the local tools used for authoring. The Unity builder rejects other editor versions and validates the Humanoid clips before bundling.

After closing Valheim and any local Valheim server, inspect the install plan:

```powershell
.\scripts\install.ps1 -WhatIf
```

Install with:

```powershell
.\scripts\install.ps1
```

The installer writes directly to the Steam game folder, **not an r2modman profile**. It uses the same `-ValheimPath` override and Steam detection. It adds missing files from the pinned loader and Jötunn packages, then installs Skamtebord. Existing compatible loader files and user configuration are preserved. Conflicting loader versions, different dependency binaries, duplicate Jötunn installations, and duplicate Skamtebord installations fail before any game files are copied. Upgrade or isolate an existing mod profile separately before retrying.

The installer updates its own `BepInEx/plugins/Skamtebord/Skamtebord.dll` and documentation, while keeping personal MP3 files. It does not launch the game. `-WhatIf` performs validation and reports the plan without downloads or file writes; run the build first to prepare its dependencies and output.

Compilation and core tests do not establish playability. Run the documented game-session checks on a test character and world before using a valued save.
