# Verification

## Fast QA bootstrap — 2026-09-26 Pacific

The developer harness now defaults to an isolated fixture world with world-wide location placement skipped. Startup cinematics, world intro, and the Valkyrie are bypassed. It grants a board in hotbar slot 1, Skamtebord level 25 (all current tricks), Run 25, and Jump 25. Readiness is based on actual ownership, inventory/skill state, ground contact, and movement availability. Play mode automatically mounts; test modes verify mounting themselves. The distributed 0.2.0 DLL and normal r2modman profile are unchanged.

Measured with `scripts/qa.ps1` on the installed Valheim 1.0.16:

| Run | Ready from process launch | Complete including shutdown | Result |
| --- | --- | --- | --- |
| Fast physics, unlocked preset | 22.4 s | 35.2 s | 30 checks passed |
| Full location generation, fresh level 0 | 41.8 s | 54.6 s | 29 checks passed |
| Fast graphical keyboard, background launcher | 29.3 s | 50.0 s | 45 checks passed |
| Interactive play, unlocked preset | 29.9 s | Stays open | Mounted with normal controls |

The graphical launcher passed all **45 keyboard integration checks**, including no active cinematic/Valkyrie, board slot, skill presets, movement, animation, input guards, and restored walking. Its synthetic keyboard permits background execution, so the automated run needs no manual focus or keypresses. Physical-device focus policy is unchanged. Initial hidden-window captures were black until a resolution change; the harness now initializes the display before capture and checks a rendered frame. Final screenshots were visually inspected. Per-run `timings.csv` and `qa-ready.json` distinguish engine time from the launch-to-ready measurements above. Granting QA progression does not bypass scoring verification: ollies must add exactly 100 new XP above their initial balance.

Records: [fast keyboard](validation-fast-qa-keyboard.txt), [fast physics](validation-fast-qa-physics.txt), [full-world fresh progression](validation-full-world-fresh.txt), [interactive play](validation-fast-qa-play.txt). The full-world comparison restores location generation but still tests physics on a temporary platform; it is not a natural-terrain acceptance test. See the [launcher instructions](../tests/Skamtebord.RuntimeSmoke/README.md).

## 0.2.0 animation and keyboard validation — 2026-09-26 Pacific

Valheim **1.0.16**, Steam build **25527674**, Unity **6000.0.75**, with the same pinned BepInEx/Jotunn versions. Original Humanoid clips were authored in Blender 4.2.3 and bundled with matching Unity 6000.0.75f1. Rebuilding the asset pipeline reproduces bundle SHA-256 `777A36FD0F4E7F569CD1A596604DFFADACAB10EBCC9C0CED6959CF22388EB429`.

- **38 live keyboard integration checks passed** in a disposable single-player world. The normal PlayerController and input guards stayed enabled; Unity Input System keyboard events exercised B/W/D/Space/S/Tab/F5. No direct rider Toggle/CaptureControls calls or input-guard patch were used in this mode.
- Pushing accelerated 0 → 7.78 m/s in 1.5 seconds. The Humanoid push clip played, root-motion travel was 0.00000 m/frame, and the planted support foot moved about 1 cm per axis while the rear foot made its push/recovery stroke.
- Steering, ollie, landing, 100 banked XP, braking, inventory suppression, console, remount, and repeated dismount passed. After dismount, ordinary W movement traveled 2.365 m in 0.7 seconds with the original Jog clips restored. No standing-up lock remained.
- Actual rendered poses and the HUD were inspected at 1920×1080, 1280×720, and 2560×1440. The panel clears default health/stamina/minimap areas. `HudPosition` and `HudScale` are configurable.
- **All 25 physics checks passed again**: 9.04 m/s pushing, 7.10 → 0 m/s braking, and 9.39 m/s under gravity alone on a 12-degree incline.
- The six-second [pushing capture](../media/04-pushing-animation.mp4) includes the new pose and actual HUD. The platform/camera/lighting are fixtures; frames come from the game renderer.
- Save suppression, separate save roots, and disabled cloud/achievement writes remained active. The test plugin is excluded from normal installations and release archives.

Records: [keyboard result](validation-keyboard-020.txt), [physics result](validation-physics-020.txt), [animation pipeline and research](animation-pipeline.md). Windows UI automation still did not deliver physical gameplay keys in windowed mode; the independent test uses Unity keyboard events instead. Multiplayer animation observers, alternate character/avatar mods, all terrain types, and audio playback remain outside this validation.

## Earlier 0.1.0 baseline

Baseline: Windows, Steam build 25390630, Valheim 1.0.15, Unity 6000.0.75, BepInExPack 5.4.2350, Jötunn 2.30.1. Checked 2026-09-20.

The subsequent Valheim 1.0.16 r2modman session is recorded separately in [local-playtest.md](local-playtest.md), including its current interactive verification limits.

## Automated checks

- Release compilation succeeds against the installed game assemblies.
- All 21 executable checks pass: 16 scoring/progression checks plus 5 playlist checks.
- Core checks cover exact XP awards, landing grace, bails, stale/unfinished tricks, repeat penalties, cooldowns, caps, invalid input, unlock thresholds, and progression overflow.
- Playlist checks cover file extensions, paths, and shuffle behavior, including malformed Windows paths.
- Packaging checks ensure the release archive includes only one DLL, with no game or dependency assemblies.
- Installer fixture checks cover dry run, first install, repeated install preserving configuration/MP3s, and rejecting conflicting loaders.

The .NET SDK reports two MSB3277 version-unification warnings for `System.IO.Compression` and `System.Net.Http`: netstandard reference assemblies differ from Unity's BCL. Neither is used by skating logic; no BCL DLLs are copied into the package. The real Unity startup check below loads the resulting plugin successfully.

## Real Unity startup

An isolated staged game was launched with `-batchmode -nographics`. BepInEx successfully loaded Skamtebord 0.1.0. Jötunn registered its custom skill, and Skamtebord created its item and recipe. No Skamtebord load or Harmony patch exceptions occurred. Expected missing-renderer/video errors occur in this headless client run; this does not verify rendering or audio playback.

Staging uses the installed game assets through directory junctions. Do not recursively clean those junction targets. No installation changes were made to the Steam game folder.

## Real Unity physics

All 25 checks in the developer-only runtime harness (`tests/Skamtebord.RuntimeSmoke` in the source checkout) passed in a disposable solo world, using a fresh in-memory character. Game saves, cloud access, and achievement writes were disabled for the harness process. A compact record is in [validation-run.txt](validation-run.txt).

- Live ObjectDB registration, ingredient resolution, immediate recipe discovery, and inventory insertion passed.
- The locally owned player settled on a real collider and mounted successfully.
- Two seconds of pushing accelerated from 0.00 to 9.04 m/s.
- Steering rotated heading, an ollie left the ground, and a safe landing retained the ride.
- The landed ollie banked exactly 100 Skamtebord XP.
- After landing, coasting maintained 7.10 m/s from 8.13 m/s over three seconds; braking then reduced 7.10 m/s to zero in one second while retaining the ride. Dismounting restored walking.
- With no pushing, a 12-degree slope accelerated the board to 9.47 m/s in 2.5 seconds.
- Save suppression remained active through simulation and shutdown.

The initial harness attempt ran before the headless character had settled into a movable animation state. The corrected fixture forces animation updates and waits for real stable ground contact and `CanMove()`; it does not bypass the mod's movement restrictions. The harness bypasses UI focus only to inject controls. These checks exercise actual Unity rigidbody movement through the mod's Harmony patch, but do not establish rendering, manual key input, audio playback, or multiplayer quality.

An earlier chained braking assertion failed without recording its starting speed/state. The final harness explicitly checks the moving baseline and logs coasting and braking measurements. The final run passed without needing to push again before braking; the earlier failure's cause was not established.

## Hands-on acceptance still required

- Craft with a new character and an existing character; save/reload the board and XP.
- Meadows ground, forest rocks, built wood ramps, steep mountains: evaluate grip, speed, camera, collision bails, and landing forgiveness.
- Verify rider feet, board tilt, trick silhouettes, inventory icon, dropped-item model, and HUD at different screen sizes.
- Menus, chat, stamina exhaustion, item switching/loss, swimming, death, portal, and logout: check restoration of movement, friction, and audio.
- Two clients plus a dedicated server: owner-only forces, remote board visuals, rejoining, and custom skill synchronization.
- Actual MP3 decoding/playback: valid and corrupt tracks, long/variable-bitrate tracks, skipping while loading, repeated mounting, volume, and disconnect cleanup.

Physics constants and trick timings are initial tuning values. Automated checks are not a substitute for judging whether the skating feels good.
