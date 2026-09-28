# Verification

## 0.4.0 speed limits and banked corners — 2026-09-27 Pacific

**38 live carving checks passed** on Valheim 1.0.16 / Unity 6000.0.75. Straight and continuously turning runs reach exactly **9.00 m/s pushing** and **14.00 m/s sprinting**, without a push-step overshoot. With sprint held above those limits, a two-second carve slows **20 → 19.35 m/s** and **32 → 31.35 m/s** from rolling resistance; turning neither adds speed nor discards the sideways component of momentum.

A long unpowered descent starting at 4 m/s reaches **29.16 m/s** with the default soft governor and **42.17 m/s** with `Physics/MaximumSpeed = 0`. The default 25 m/s setting is the onset of braking, not a hard cap: continued downhill gravity can balance it at a higher speed. Custom settings remain intact. The governor now acts only on supported surface velocity. A fast ollie retains **31.57 m/s** horizontal velocity during flight while its vertical velocity follows game gravity.

An untagged rounded quarter-pipe bowl tests changing pitch, roll and yaw together. Both left and right trials coast through **110.02°** of corner, reach **84.38°** of surface bank, and finish at **10.94 m/s**, with **zero measured support gaps** and no increase in mechanical energy. The rider's up axis tracks the changing surface normal. These trials seed velocity only on the flat approach and use steering inputs thereafter; they do not reposition the rider or inject corner forces. The bowl is a test fixture, not an additional craftable piece.

Reproduce with `scripts/qa.ps1 -Mode Carving`. Records: [checks](validation-carving-040.txt), [measurements](measurements-carving-040.txt), [left-turn trace](validation-corner-left-040.csv), and [right-turn trace](validation-corner-right-040.csv). The harness restores temporary configuration values and retains disposable-save isolation. Natural terrain varieties and other movement mods remain outside these controlled fixture checks.

## 0.4.0 skating flow — 2026-09-27 Pacific

On Valheim 1.0.16 / Unity 6000.0.75, **61 halfpipe/input checks**, **84 keyboard/rendering checks**, **52 ramp checks**, **30 owner-physics checks**, and **21 core checks** passed. An earlier keyboard run banked 560 XP from an ollie plus grab instead of the intended single ollie. The suite now bounds catch-up time during synthetic input, preventing a stalled render frame from stretching a short tap into a grab hold, and checks the actual tap duration. The corrected run records a 50 ms tap and exactly 100 ollie XP. This timing change is confined to QA.

The general-support follow-up also passed **35 live surface checks** using untagged MeshColliders, including a curved transition ending in a full vertical face and the same shape split across separate colliders. Both remain supported through the curve, feed those contacts into Valheim's ground bookkeeping, and launch from the lip. No halfpipe component or per-prefab ground exemption is present in these fixtures.

The seam probe checks geometry near both ends of the board and compares speed/curvature with gravity before counting support. It neither moves the rigidbody nor adds downward force. An unpowered downhill run accelerates from **4 to 15.34 m/s** with **zero measured unsupported gaps**; a 6 m/s approach to the tall transition stalls at **0.69 m** and rolls back. On the convex crest, the 14 m/s run leaves about **1.39 m earlier** than the 3 m/s run, and both trajectories follow gravity after departure. A head-on wall still bails using pre-impact velocity. Pushing fades toward zero on verticals. Evidence: [surface checks](validation-surfaces-040.txt) and [measurements](measurements-surfaces-040.txt). Reproduce with `scripts/qa.ps1 -Mode Surfaces`. These are real physics fixtures, not broad acceptance testing across natural world terrain or other movement mods.

- The registered halfpipe uses the ordinary Hammer build table, a workbench, and 80 recoverable Wood. Live instances retain a persistent ZNetView, curved collision, normal building damage and repair. Player-built removal returns all 80 Wood.
- Both 4 m lips redirect a 16 m/s approach almost vertically: about **7.23 m/s upward**, **0.31 m/s horizontal**, and **1.71 m above the lip**. A timed jump from the same approach reaches **4.63 m above the lip**. Rigidbody and board follow the curve through almost 90°. CSV traces record the actual collision-driven flights. The unattended timed jump drifts outside the pipe and lands on the flat floor at over 12 m/s downward; the updated ground tracking correctly bails on that hard landing. The test distinguishes this from a premature wall bail on the curve.
- At level zero, real keyboard taps perform a shuvit and holding jump performs a grab; both land and bank Skamtebord XP. Releasing lets go and holding does not repeatedly score. Stationary ollies award no XP. Beginner shuvit/grab transitions take 0.18/0.20 seconds, leaving room to finish during an ordinary ollie. A continuous forward hold completes **five push cycles with no measured coast interruption** at 9 m/s. Steering turns approximately **39.89° at 3 m/s versus 27.69° at 14 m/s** over 0.35 seconds.
- Existing bindings, direct trick keys, hold/toggle sprint, menus, HUD bounds, ordinary walking, banking and downhill gravity pass. The ramp suite uses the rider's supported-surface state and measures the lip flight, avoiding early termination on a brief contact transition at the ramp's base. Its timed lip ollie reaches **2.64 m**, versus **1.46 m** rolling off at the same speed. Repeated airborne requests add no extra impulse.

Records: [flow](validation-flow-040.txt), [keyboard](validation-keyboard-040.txt), [ramps](validation-ramps-040.txt), [ramp measurements](validation-ramps-040.csv), [physics](validation-physics-040.txt), and [game-rendered halfpipe](../media/07-halfpipe.png). Reproduce with `scripts/qa.ps1 -Mode Flow`, `-Mode Keyboard`, `-Mode Ramps`, and `-Mode Physics`. Keyboard modes submit Unity Input System events through the normal controller; they do not emulate physical Windows keypresses. Natural terrain and subjective skating feel still need wider playtesting.

**Two separate Valheim processes also passed the complete multiplayer observer sequence: 41 host and 32 observer checks**, using real ZNet/ZDO traffic on a loopback TCP connection. The observer verifies an already-mounted rider on joining, movement, push/tuck clips, turn lean, every trick's visible board rotation, sustained grabs, the halfpipe's curved pose, dismount/restored controller, remount, and piece removal. The host verifies disconnect cleanup; the observer's personal radio stays silent. The harness caps both render loops at 60 FPS and holds sustained actions until observed, rather than relying on short states surviving snapshot coalescing. Quick flat-ground grabs are covered by the ordinary keyboard suite. Records: [host](validation-network-host-040.txt), [observer](validation-network-client-040.txt), [observer screenshot](../media/08-multiplayer-halfpipe.png).

The speed/banking build also passed the complete surface, halfpipe/input, keyboard, ramp, owner-physics and two-process replication regressions above. Tested and installed development DLL SHA-256: `3648A2BD2BC4EBBF70A7A618336682D0779DEF72E20C40155C46D46995DD5EB3`.

This caught two real presentation issues. World-clock corrections could discard short tricks; new events now retain a visible tail, while old first snapshots expire and remounts do not replay the last trick. Valheim 1.0.16 also omits identity rotations from packets without clearing the receiver's prior rotation. A decoder correction applies only to tagged skating-player ZDOs, restoring north-facing riders upright after a wall ride without changing network revisions. Dismount also replaces any queued physics rotation.

Reproduce with `scripts/qa-multiplayer.ps1`. This is separate-process replication over the game's TCP backend, **not verification of Steam/PlayFab transport, internet latency, dedicated-server deployment, or world reload persistence**. The test adapter is bound to localhost and excluded from release packages. Legacy-transport platform-ID warnings are recorded separately from the checks. All peers must use compatible 0.4.x builds and Jötunn.

## 0.3.1 automatic game-music muting — 2026-09-27 Pacific

**54 live radio checks passed** on the installed Valheim 1.0.16 / Unity 6000.0.75 runtime. All six local MP3s decoded, advanced through audio samples, and produced nonzero Unity AudioSource output while Valheim's separate music source was muted. The radio releases that mute immediately when stopped or dismounted, and also when disabled, at zero volume, or when a playlist is empty or fails decoding. Re-enabling audible playback mutes game music again. A pre-existing mute is preserved, as are music-volume changes made during a ride. The plugin explicitly disables and destroys its radio on teardown.

The checks confirm saved music preferences and master audio volume remain unchanged. They verify engine audio output, not physical speaker audibility. No music is included in the evidence or package. All **21 core checks** also passed. The tested DLL SHA-256 is `A05BDCB9A13E2ABAA7BD0E9CD73736F656B7A252B7CB4626FA2B49AAF43D66C8`.

Record: [radio checks](validation-radio-031.txt). Reproduce with `scripts/qa.ps1 -Mode Radio`, using local tracks in `radio-mp3s` or `-RadioDirectory <path>`.

## 0.3.0 controls, sprint, ramp visibility and radio — 2026-09-27 Pacific

Tested in the installed Valheim 1.0.16 / Unity 6000.0.75 runtime, using the isolated QA harness.

- **83 keyboard/rendering checks passed**, including all five new trick keys through the real bindings, legacy-default migration, preservation of customized controls, ordinary Use input, menus and restored walking.
- Sprint reaches approximately **14 m/s**, versus **9 m/s** for normal pushing. Ending a held sprint retains its momentum instead of resetting to the ordinary pushing cap. Both Valheim hold-run and toggle-run preferences passed, as did stamina use and exhausted-stamina suppression. The first release assertion assumed hold mode while this installation used toggle mode; the harness now explicitly tests both and restores the original preference without saving it.
- The Humanoid tuck lowered the rider's hips **0.95 → 0.68 m**, kept both feet planted, and contributed no animation root travel. The new pose and HUD were visually inspected, including panel bounds at 720p and 1440p. [Live sprint screenshot](../media/06-sprint-tuck.png).
- Both closed ramp shapes rendered from front/back/left/right and from each end at **60 m** (12 GPU image checks using the normal game camera's DeferredShading path). Closed sides/bottom/caps now use outward normals; fixture renderers also disable dynamic occlusion. The normal-camera follow-up found the main disappearance cause: Shader.Find missed the bundled shaders and fell back to **Sprites/Default, queue 3000**, allowing the large transparent floor to overpaint ramps. The fixtures now clone an opaque material from the game's Wood prefab (**Standard, queue 2000**). All three ramps were visually confirmed in the ordinary game view with postprocessing enabled. The regression renderer now includes the floor as well as the ramp. The interactive course starts in clear daylight facing the course. **52 ramp physics checks passed again**, with unchanged roll-off and timed-jump measurements.
- The supplied local playlist contained **six MP3s**. The source checkout's folder had the tracks, while the staged DLL-adjacent default folder was empty. QA now selects the source folder explicitly; normal installations retain a configurable DLL-relative default. Radio tests verify decoding, advancing sample position, nonzero AudioSource output, shuffled skipping, toggle off/on, and stopping on dismount. No music is included in test evidence or packages. Engine audio output is verified; physical speaker audibility is not asserted.
- MP3 extensions are ignored case-insensitively throughout the repository, including nested paths. No MP3 files are tracked. Core scoring/playlist checks: **21/21 passed**.

Regression results also passed **30 physics checks** and **34 radio checks**. The checked DLL SHA-256 is `3E7930BB345A22719DD2C6D631AE0114E74B7611C7455564A7C6EB71684FB7A7`.

Records: [keyboard/rendering](validation-keyboard-030.txt), [ramps](validation-ramps-030.txt), [flight measurements](validation-ramps-030.csv), [physics](validation-physics-030.txt), [radio](validation-radio-030.txt). Reproduce with `scripts/qa.ps1 -Mode Keyboard`, `-Mode Ramps`, `-Mode Physics` and `-Mode Radio`; the radio mode requires local music. Keyboard mode injects Unity Input System events through the ordinary controller, not OS keyboard events. Multiplayer observer animation and natural-terrain acceptance remain unverified.

## 0.2.1 ramp momentum — 2026-09-27 Pacific

**52 live ramp checks passed** on Valheim 1.0.16. Eleven controlled flights use real MeshColliders and the ordinary owner-physics update. Approach velocity is assigned only on the flat run-in; there is no velocity injection at the lip. The test temporarily bypasses UI focus and drives `CaptureControls`, just like the existing physics suite. Separate keyboard checks exercise normal bindings.

The baseline ollie replaced upward ramp momentum: 3.05 m/s became 4.80 m/s after the next gravity step. The new additive impulse produces 7.85 m/s from the same starting velocity. Horizontal takeoff speed remains 8.38 m/s. Skating now distinguishes fresh contact from Valheim's 0.2-second walking grace, and offers its own single-use 0.12-second jump grace.

| 20° ramp, initial approach | Peak above flat floor | Flight time |
| --- | --- | --- |
| Roll off, 10 m/s | 1.31 m | 0.46 s |
| Roll off, 16 m/s | 1.70 m | 0.64 s |
| Roll off, 12 m/s | 1.43 m | 0.52 s |
| Jump early, 12 m/s | 0.71 m | 0.40 s |
| Jump near lip, 12 m/s | 2.69 m | 0.96 s |
| Jump within grace, 12 m/s | 2.19 m | 0.88 s |

The lip is 1.25 m high. Early jumping lands back on the incline. All eleven trials land while retaining the ride. Expired-grace jump requests produce no boost; repeated airborne requests match the single-jump flight; pushing in the air matches the unpowered roll-off. A 35° ramp and a terrain-shaped convex crest also launch through their collision geometry. Different terrain shapes, natural Valheim heightmaps/build pieces, moving platforms and multiplayer remain outside this fixture test.

Evidence: [checks](validation-ramps-021.txt), [flight measurements](validation-ramps-021.csv), [game-rendered comparison](../media/05-ramp-momentum.mp4). Reproduce with `scripts/qa.ps1 -Mode Ramps`; use `-Ramps` for the interactive three-lane course or `-Mode Ramps -Record` for frames.

Regression validation also passed: **30 physics checks**, **45 normal keyboard/animation/HUD checks**, and **21 core scoring/playlist checks**. Records: [physics](validation-physics-021.txt), [keyboard](validation-keyboard-021.txt). The DLL SHA-256 is `3789B75C0CA141CD482B907CC35D198CD523B7C3582764D473E03495529550F1`.

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
- Additional MP3 coverage beyond the six supplied tracks: corrupt/long/variable-bitrate tracks, skipping while loading, volume extremes, and disconnect cleanup.

Physics constants and trick timings are initial tuning values. Automated checks are not a substitute for judging whether the skating feels good.
