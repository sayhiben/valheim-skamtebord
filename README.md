# Skamtebord

A Valheim 1.0 skateboarding mod: craft a wooden board, push through the Meadows, race down hills, and land tricks to build your **Skamtebord** skill. Inspired by Tony Hawk's Pro Skater, shield surfing, Skate, and 1080°.

Version **0.3.1**, built against **Valheim 1.0.16 / Unity 6000.0.75**, with **BepInExPack 5.4.2350** and **Jötunn 2.30.1**. Includes procedural board art and original Humanoid pushing/coasting/tuck animations. See [verification status](docs/testing.md) for what has actually been tested.

## Install with r2modman (recommended)

**[Download Skamtebord 0.3.1](https://github.com/sayhiben/valheim-skamtebord/releases/download/v0.3.1/Skamtebord-0.3.1.zip)** · [All releases](https://github.com/sayhiben/valheim-skamtebord/releases)

You only need the release ZIP to play; no .NET SDK, Blender, Unity editor, or source checkout is required. These steps were prepared for Windows and Steam Valheim 1.0.16.

1. Close Valheim. Open **r2modman**, select **Valheim**, and create or select a profile.
2. In **Online**, install **BepInExPack_Valheim** by **denikson** and **Jotunn** by **ValheimModding**. The tested versions are **5.4.2350** and **2.30.1** respectively; choose those in the download version selector. Install both explicitly: importing a local mod does not automatically install its dependencies.
3. Download **Skamtebord-0.3.1.zip** from the link above. Keep it zipped. GitHub's **Source code** downloads do not contain the compiled mod.
4. Open **Settings**, search for **local mod**, and choose **Import local mod** (called **Install local mod** in some versions). Select the ZIP and confirm the import. If asked, use author **sayhiben**, name **Skamtebord**, and version **0.3.1**.
5. Check that all three mods are enabled, then click **Start modded**. Enter a world and craft **Skamtebord** from your inventory: **8 Wood, 4 Resin, 2 Leather scraps**. Press **B** to ride.

For **manual installation, updating, finding the MP3 folder, and troubleshooting**, see the [installation guide](docs/install.md). Local releases are updated by downloading another GitHub release; r2modman does not fetch these GitHub updates automatically. The import flow follows [r2modman's local-mod guide](https://github.com/ebkr/r2modmanPlus/wiki/Installing-mods-locally) and [Thunderstore's dependency guidance](https://wiki.thunderstore.io/mods/mod-not-visible).

All players and the server need Skamtebord and Jötunn for a modded world containing the board. Remote visuals are implemented; a two-client session still needs verification. Movement and XP follow Valheim's client-authoritative player model.

## First ride

The **Skamtebord** recipe is offered immediately, including to existing characters. Craft it in your inventory using **8 Wood, 4 Resin, and 2 Leather scraps**; no crafting station is required.

Keep the board in your inventory. Press **B**, or use its hotbar slot, to mount on solid ground. Using another item dismounts you. The board travels with your inventory and can be dropped, picked up, and teleported as an ordinary item.

| Input | Action |
| --- | --- |
| B / board hotbar slot | Mount or dismount |
| Forward / left-stick up | Push; uses a little stamina |
| Sprint + forward (Shift+W by default) | Accelerate faster with a crouched tuck; honors Valheim's hold/toggle run setting |
| Left, right / left stick | Steer relative to the board |
| Backward / left-stick down | Brake |
| Jump / gamepad jump | Ollie; costs 5 stamina |
| J, in the air | Shuvit, unlocks at level 3 |
| K, in the air | Kickflip, level 8 |
| L, in the air | Heelflip, level 12 |
| U, in the air | Grab, level 18 |
| I, in the air | 360, level 25 |
| F8 | Toggle skating radio |
| F9 | Skip track |

Movement, sprint and jump follow Valheim's bindings. Other keys are configurable in `BepInEx/config/com.skamtebord.valheim.cfg`. J/K/L and the U/I row above form a compact right-hand trick cluster, leaving normal Valheim default actions available. Updating migrates the old Q/E/R/F/C defaults once and preserves custom bindings. User-rebound Valheim keys or other mods can still require personal adjustments. Keyboard tricks can also be rebound to supported joystick button codes; there is no dedicated gamepad trick layout yet.

Pushing reaches approximately 9 m/s on level ground; sprinting reaches 14 m/s with a lower stance and greater stamina use. Releasing sprint keeps accumulated momentum, and empty stamina stops the extra acceleration. Gravity can take you faster downhill, with a soft limit around 25 m/s. Steering gets gentler as speed rises. Rolling friction, directional grip, braking, and the existing player rigidbody handle terrain contact. The board is a visual beneath the player, so small rocks and steps still use Valheim's character collider. Ordinary fall damage remains active.

Ramps and terrain crests redirect your current momentum into the air. Ollies add an upward impulse to that velocity: jump near the lip for more height than jumping early. A 0.12-second grace window accepts a slightly late takeoff; further airborne presses cannot stack boosts. Pushing and braking act only while supported by the ground. See the [ramp comparison](media/05-ramp-momentum.mp4).

## Tricks and progression

An ollie scores 100 base points. Chain different completed tricks to increase the multiplier, land, and stay grounded for two seconds to bank the combo. Another jump during that window continues it. Hard landings, unfinished tricks, crashes, and dismounting lose pending points. A stationary hop gives no XP; takeoff and landing need at least 2 m/s.

Banked points become exactly that many lifetime Skamtebord XP. Progress is saved on the character and mirrored into the custom skill panel. Death does not remove banked Skamtebord XP. Unlock thresholds follow `100 × level² + 200 × level`: shuvits unlock at 1,500 XP and kickflips at 8,000 XP.

Repeating a trick in one combo halves its base award each time. Combos are capped at 32 tricks, a ×6 multiplier, and 10,000 points. Trick animations must finish before landing. Manual tricks, grinds, reverts, ramps as craftable pieces, dedicated skeletal trick animations, and competitive leaderboards are future work.

The rider plants the front foot and pushes with the rear foot while accelerating, then returns to a balanced coasting stance. Original Humanoid clips retarget to Valheim's character; animation does not supply movement forces. The skating panel defaults to the middle-right, away from health, stamina, and the minimap. Adjust `Interface/HudPosition` (normalized X/Y), `HudScale`, or `ShowHud` in the configuration.

Optional athletics rewards are **off by default**. Enabling `Progression/AthleticsExperience` adds just 0.05 of a normal Jump-skill raise per bank, at most once every 30 seconds. Pushing and coasting award no Run XP.

## Personal skating radio

Put your `.mp3` files in the **`radio-mp3s` folder beside the installed `Skamtebord.dll`** (normally `BepInEx/plugins/Skamtebord/radio-mp3s/`; mod managers may rename the plugin folder). Set `Radio/Directory` to another absolute path or a path relative to the mod folder, and adjust `Radio/Volume` and `Radio/Enabled` in `BepInEx/config/com.skamtebord.valheim.cfg`. Empty or unreadable folders are reported in the skating HUD.

For development, `scripts/qa.ps1` explicitly uses this source checkout's `radio-mp3s` directory. Use `-RadioDirectory 'D:\Music\Skating'` to override it. A normal r2modman profile uses its own configuration; set its `Radio/Directory` to the source folder if you want the same playlist. MP3s, including mixed-case extensions and nested files, are gitignored throughout this repository and excluded from release archives.

Tracks shuffle while riding and stop on dismount. Remount to discover added files, or use Next after an empty/exhausted playlist. Uppercase `.MP3` and names containing spaces, Unicode, `#`, or `%` are supported. Subfolders are ignored. Empty, missing, and corrupt-file playlists do not prevent skating. Scans are bounded to 8,192 directory entries and 2,048 tracks. `scripts/qa.ps1 -Mode Radio` tests discovery, decoding, advancing audio output, skipping, toggling, and dismount cleanup using your local files.

Music remains on your computer; no songs are included or sent to other players. While an MP3 is playing at nonzero volume, the radio temporarily mutes Valheim's music source to prevent overlap. It restores the previous mute state when playback stops, including dismounting, toggling the radio off, empty/broken playlists, and disabling the component. Sound effects and your saved music-volume settings stay unchanged; volume adjustments made during the ride are retained. A zero-volume radio lets game music play normally.

## Development

To build from a source checkout on Windows, install a **.NET SDK 8 or newer** and the **.NET 8 runtime** for the tests, then run:

```powershell
.\scripts\build.ps1
```

The script finds the Steam installation, downloads pinned build dependencies, runs the core checks, and writes `dist/Skamtebord-0.3.1.zip` plus its `.sha256` checksum. Override game detection with `-ValheimPath 'D:\SteamLibrary\steamapps\common\Valheim'`. The checked-in animation bundle is included automatically. See [script details](scripts/README.md) for source builds and the optional direct-to-game installer; that installer does not target r2modman profiles.

Start a ready-to-skate disposable QA session with `scripts/qa.ps1`: intros skipped, board in hotbar slot 1, all tricks unlocked, and automatically mounted. Run `scripts/qa.ps1 -Mode Keyboard` or `-Mode Physics` for automated checks. The default fixture mode skips world-wide location generation; `-FullWorld` restores it and `-FreshProgression` starts at level zero. These shortcuts affect only the isolated developer harness. See [QA instructions](tests/Skamtebord.RuntimeSmoke/README.md).

Use `scripts/qa.ps1 -Ramps` for an interactive course: a 20° kicker straight ahead, a 35° kicker to the left, and a terrain-shaped crest to the right. `-Mode Ramps` runs the momentum/timing tests; add `-Record` for game-rendered comparison frames.

`src/Skamtebord` contains the plugin, recipe, physics, presentation, and radio. `src/Skamtebord.Core` holds pure scoring/progression logic, compiled into the single plugin DLL. Run checks separately with:

```powershell
dotnet run --project tests/Skamtebord.Core.Tests --configuration Release
```

Dependencies are pinned and hash-verified by the build script. Unity/Valheim assemblies are read from your own installation and never redistributed. Downloaded dependencies, local API inspections, and runtime staging remain under ignored `.local/`.

The checked-in animation bundle is embedded in the DLL. To change its original Blender source, see [the animation pipeline](docs/animation-pipeline.md) and run `scripts/build-animations.ps1` with Blender 4.2 and Unity 6000.0.75f1 before building the plugin. The [developer harness](tests/Skamtebord.RuntimeSmoke/README.md) supports an isolated Input System keyboard test with the ordinary player controller enabled.

Research and source attribution are in [docs/research.md](docs/research.md). This implementation uses Jötunn's supported item and skill registration and original movement code informed by ValheimVehicles' ownership and ground-contact patterns. No vehicle-mod code or music assets are bundled.
