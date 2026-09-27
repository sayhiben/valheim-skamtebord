# Skamtebord

A Valheim 1.0 skateboarding mod: craft a wooden board, push through the Meadows, race down hills, and land tricks to build your **Skamtebord** skill. Inspired by Tony Hawk's Pro Skater, shield surfing, Skate, and 1080°.

Version **0.2.0**, built against **Valheim 1.0.16 / Unity 6000.0.75**, with **BepInExPack 5.4.2350** and **Jötunn 2.30.1**. Includes procedural board art and original Humanoid pushing/coasting animation. See [verification status](docs/testing.md) for what has actually been tested.

## Install

Use a mod manager to install BepInExPack Valheim 5.4.2350 and Jötunn 2.30.1, then import `dist/Skamtebord-0.2.0.zip` as a local mod. For a manual installation, place the `Skamtebord` folder from `dist` under `Valheim/BepInEx/plugins/` after installing those dependencies.

From this source checkout on Windows, the scripts can build and install everything:

```powershell
.\scripts\build.ps1
.\scripts\install.ps1 -WhatIf
.\scripts\install.ps1
```

The scripts locate Valheim through Steam. Supply `-ValheimPath 'D:\SteamLibrary\steamapps\common\Valheim'` to override. Build requires a .NET SDK 8 or newer and the .NET 8 runtime for tests. The installer requires the game to be closed and checks existing dependencies before writing. It preserves your radio files and configuration. See [script details](scripts/README.md).

All players and the server need Skamtebord and Jötunn for a modded world containing the board. Remote visuals are implemented; a two-client session still needs verification. Movement and XP follow Valheim's client-authoritative player model.

## First ride

The **Skamtebord** recipe is offered immediately, including to existing characters. Craft it in your inventory using **8 Wood, 4 Resin, and 2 Leather scraps**; no crafting station is required.

Keep the board in your inventory. Press **B**, or use its hotbar slot, to mount on solid ground. Using another item dismounts you. The board travels with your inventory and can be dropped, picked up, and teleported as an ordinary item.

| Input | Action |
| --- | --- |
| B / board hotbar slot | Mount or dismount |
| Forward / left-stick up | Push; uses a little stamina |
| Left, right / left stick | Steer relative to the board |
| Backward / left-stick down | Brake |
| Jump / gamepad jump | Ollie; costs 5 stamina |
| Q, in the air | Shuvit, unlocks at level 3 |
| E, in the air | Kickflip, level 8 |
| R, in the air | Heelflip, level 12 |
| F, in the air | Grab, level 18 |
| C, in the air | 360, level 25 |
| F8 | Toggle skating radio |
| F9 | Skip track |

The movement and jump bindings follow Valheim's controls. Other keys are configurable in `BepInEx/config/com.skamtebord.valheim.cfg`. Keyboard tricks can also be rebound to supported joystick button codes. Default advanced trick bindings are keyboard-based; there is no dedicated gamepad trick layout yet. Dismount before interacting with doors or using guardian powers: their default keys are reserved while skating.

Pushing reaches approximately 9 m/s on level ground. Gravity can take you faster downhill, with a soft limit around 25 m/s. Steering gets gentler as speed rises. Rolling friction, directional grip, braking, and the existing player rigidbody handle terrain contact. The board is a visual beneath the player, so small rocks and steps still use Valheim's character collider. Ordinary fall damage remains active.

## Tricks and progression

An ollie scores 100 base points. Chain different completed tricks to increase the multiplier, land, and stay grounded for two seconds to bank the combo. Another jump during that window continues it. Hard landings, unfinished tricks, crashes, and dismounting lose pending points. A stationary hop gives no XP; takeoff and landing need at least 2 m/s.

Banked points become exactly that many lifetime Skamtebord XP. Progress is saved on the character and mirrored into the custom skill panel. Death does not remove banked Skamtebord XP. Unlock thresholds follow `100 × level² + 200 × level`: shuvits unlock at 1,500 XP and kickflips at 8,000 XP.

Repeating a trick in one combo halves its base award each time. Combos are capped at 32 tricks, a ×6 multiplier, and 10,000 points. Trick animations must finish before landing. Manual tricks, grinds, reverts, ramps as craftable pieces, dedicated skeletal trick animations, and competitive leaderboards are future work.

The rider plants the front foot and pushes with the rear foot while accelerating, then returns to a balanced coasting stance. Original Humanoid clips retarget to Valheim's character; animation does not supply movement forces. The skating panel defaults to the middle-right, away from health, stamina, and the minimap. Adjust `Interface/HudPosition` (normalized X/Y), `HudScale`, or `ShowHud` in the configuration.

Optional athletics rewards are **off by default**. Enabling `Progression/AthleticsExperience` adds just 0.05 of a normal Jump-skill raise per bank, at most once every 30 seconds. Pushing and coasting award no Run XP.

## Personal skating radio

Put your `.mp3` files in **`BepInEx/plugins/Skamtebord/radio-mp3s/`**. The default directory is always beside the installed DLL. Set `Radio/Directory` to another absolute path or a path relative to the mod folder, and adjust `Radio/Volume` and `Radio/Enabled` in the configuration.

Tracks shuffle while riding and stop on dismount. Remount to discover added files. Uppercase `.MP3` and names containing spaces, Unicode, `#`, or `%` are supported. Subfolders are ignored. Empty, missing, and corrupt-file playlists do not prevent skating. Scans are bounded to 8,192 directory entries and 2,048 tracks.

Music remains on your computer; no songs are included or sent to other players. The radio has independent volume and does not change Valheim's music settings. Lower the game's music volume if you prefer only your playlist.

## Development

Start a ready-to-skate disposable QA session with `scripts/qa.ps1`: intros skipped, board in hotbar slot 1, all tricks unlocked, and automatically mounted. Run `scripts/qa.ps1 -Mode Keyboard` or `-Mode Physics` for automated checks. The default fixture mode skips world-wide location generation; `-FullWorld` restores it and `-FreshProgression` starts at level zero. These shortcuts affect only the isolated developer harness. See [QA instructions](tests/Skamtebord.RuntimeSmoke/README.md).

`src/Skamtebord` contains the plugin, recipe, physics, presentation, and radio. `src/Skamtebord.Core` holds pure scoring/progression logic, compiled into the single plugin DLL. Run checks separately with:

```powershell
dotnet run --project tests/Skamtebord.Core.Tests --configuration Release
```

Dependencies are pinned and hash-verified by the build script. Unity/Valheim assemblies are read from your own installation and never redistributed. Downloaded dependencies, local API inspections, and runtime staging remain under ignored `.local/`.

The checked-in animation bundle is embedded in the DLL. To change its original Blender source, see [the animation pipeline](docs/animation-pipeline.md) and run `scripts/build-animations.ps1` with Blender 4.2 and Unity 6000.0.75f1 before building the plugin. The [developer harness](tests/Skamtebord.RuntimeSmoke/README.md) supports an isolated Input System keyboard test with the ordinary player controller enabled.

Research and source attribution are in [docs/research.md](docs/research.md). This implementation uses Jötunn's supported item and skill registration and original movement code informed by ValheimVehicles' ownership and ground-contact patterns. No vehicle-mod code or music assets are bundled.
