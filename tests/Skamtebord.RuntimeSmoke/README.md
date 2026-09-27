# Developer runtime smoke harness

This is a temporary test plugin, **not a release dependency or distributable mod**. It drives the real installed Valheim Unity runtime in a separate staged game directory. Keep the DLL out of release archives and normal player installations.

## Fast QA loop

From the repository root, with Steam running and Valheim closed:

```powershell
.\scripts\qa.ps1                       # Build and open a mounted, ready-to-skate QA session
.\scripts\qa.ps1 -Mode Keyboard        # Build, run keyboard/animation/HUD checks, exit
.\scripts\qa.ps1 -Mode Physics         # Build, run headless physics checks, exit
.\scripts\qa.ps1 -Ramps                # Interactive ramp course, already mounted
.\scripts\qa.ps1 -Mode Ramps           # Automated momentum/timing trials, headless
.\scripts\qa.ps1 -Mode Ramps -Record   # Same trials with offscreen GPU capture
.\scripts\qa.ps1 -Mode Radio           # Decode/play local MP3s and verify audio samples
```

The launcher stages current binaries under `.local/runtime`, automatically enters a disposable solo world, and reports startup timing. It skips the startup movie, world intro text/movie, and Valkyrie ride. Default fixture mode also skips world-wide dungeon/village location placement: those locations are irrelevant to the test platform. A solid platform exists before the player spawns; readiness waits for actual ground contact and `CanMove()` rather than a fixed delay.

The board is granted and placed in **hotbar slot 1**. Default QA progression is **Skamtebord 25 (67,500 XP)**, unlocking all current tricks, plus **Run 25 / Jump 25**. Play mode mounts the board automatically, keeps normal controls, and stays open until closed. Automated modes start unmounted so the mount path is still tested. XP assertions measure the amount earned above the preset, so granted XP cannot make scoring tests pass.

Options:

- `-NoBuild`: reuse compiled binaries for another scenario or capture.
- `-FreshProgression`: start at level 0 for unlock/progression tests.
- `-FullWorld`: retain ordinary world-wide location generation for integration checks. Tests still use their temporary platform.
- `-Mode Keyboard -Record`: record the six-second pushing/HUD sequence.
- `-Mode Ramps -Record`: capture ramp roll-off, early jump, and lip jump into `ramp-video/` under the session root. `scripts/encode-ramp-comparison.py <session-root> media/05-ramp-momentum` creates the labelled half-speed comparison (requires `imageio-ffmpeg`).
- `-ValheimPath <path>`: choose the installed Steam game explicitly.
- `-RadioDirectory <path>`: choose a playlist. QA defaults to the source checkout's `radio-mp3s` folder, independently of a normal mod-manager installation.
- `-Ramps -RenderDiagnostics`: capture the ordinary game camera with each postprocessing component temporarily disabled, then without effects/fog and in forward rendering. The original component, fog and rendering states are restored before leaving the session open. This diagnosed the ramp's transparent material fallback; the ordinary baseline capture is the acceptance view.

`qa-ready.json`, `timings.csv`, screenshots, and results are written under the unique isolated save root. `.local/runtime/latest-qa-session.txt` points to the latest ready session. The launcher never changes the normal Steam/r2modman play profile. The QA process retains save/cloud/achievement suppression for its entire lifetime, including shutdown. Rebuilding the mod requires closing that process and rerunning the command.

Measured on this machine: the fast physics launcher reached ready state in **22.4 seconds from process launch**, and finished checks plus shutdown in **35.2 seconds**, versus **41.8 / 54.6 seconds** with full location generation. The graphical keyboard launcher took **29.3 / 50.0 seconds** and passed all 45 checks unattended. Interactive play was mounted and ready in **29.9 seconds**. The synthetic test keyboard permits background execution; physical-device focus policy is unchanged. Measurements vary between runs. Engine timers exclude the process startup/shutdown overhead included in these launcher measurements.

## Manual harness setup

Ramp mode seeds velocity once on the flat approach, then lets the ordinary owner physics and real MeshColliders handle ascent, takeoff, gravity, and landing. It injects control requests, as the physics suite does; it is not an OS keyboard test. Eleven trials cover 10/12/16 m/s approaches, 20°/35° ramps, a terrain-shaped crest, early/lip/grace/late jumps, repeated jump presses, and attempted pushing in the air. Telemetry is in `ramps.csv` and per-flight `ramp-*.csv`. Peak heights are measured from the flat floor; the lip is 1.25 m high. These repeatable fixtures do not replace natural-terrain, wood-roof, or multiplayer acceptance tests.

Build:

```powershell
dotnet build tests/Skamtebord.RuntimeSmoke/Skamtebord.RuntimeSmoke.csproj --configuration Release
```

Copy the resulting `bin/Release/netstandard2.1/Skamtebord.RuntimeSmoke.dll` into the staged runtime's `BepInEx/plugins/RuntimeSmoke/` alongside a built Skamtebord plugin and its normal dependencies. Launch the staged executable with:

```text
-skamtebord-smoke -batchmode -nographics -logFile <absolute-local-log-path>
```

On Windows, launch background smoke processes with `Start-Process -WindowStyle Hidden`. Steam must be running and the staged runtime must contain its normal game/runtime dependencies. The harness never launches or terminates other processes. It exits its own game process with code 0 on success, 1 on failure, and has a 420-second real-time deadline. Native engine stalls may still require an external process timeout.

The explicit `-skamtebord-smoke` argument is required. In early plugin `Awake`, the harness:

- Redirects game save paths into `<staged-game-root>/smoke-saves/<unique-test-name>/`.
- Enables `SaveSystemSessionFlags.DontSaveAnything` and preserves suppression through shutdown.
- Disables cloud-storage support for the process and replaces saved-world/character enumeration with empty lists.
- Suppresses achievement/stat writes and campaign-activity progression during this test.
- Creates a unique local world and character in memory, with no world/profile file save call.
- Selects the Steamworks backend with a closed, nonpublic solo server configuration.

The run inspects live `ObjectDB` prefab/recipe registration, ingredient costs, recipe discovery, local network ownership, board mounting, pushing, steering, real-gravity jumping and landing, banking XP, braking, dismounting, and gravity-driven travel on a 12-degree slope. It creates disposable static physics colliders above the generated terrain, grants the test character a board, and uses the real `BoardRider`/Harmony walking integration.

Search logs for `SKAMTEBORD_SMOKE PASS`, `FAIL`, and `SUCCESS`. The same checks are written to `smoke-result.txt` inside the unique test save directory. Prefab/recipe checks run before waiting for the player, so they can produce useful evidence when scene loading or world generation prevents the later physics checks.

Limitations: the harness bypasses the UI-focus input guard and disables `PlayerController` while injecting controls into the actual rider adapter. It does not validate keyboard mapping, rendered poses/HUD, audible MP3 playback, multiplayer observers, natural-terrain handling, or subjective skating feel. Headless engine warnings should be reviewed separately from explicit harness failures. Normal Steam/platform initialization still runs; this is not a test of offline platform bootstrapping.

## Keyboard, animation, and HUD playtest

For normal keyboard bindings and rendered screenshots, launch the same isolated staged runtime with:

```text
-skamtebord-smoke -skamtebord-keyboard-test -console -force-d3d11 -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -logFile <absolute-local-log-path>
```

Omit `-batchmode` and `-nographics`: the normal HUD must be created. This mode keeps `PlayerController` enabled and does **not** patch the input guard or call the rider's control methods. A temporary Unity Input System keyboard submits B/W/D/Space/S/Tab/F5 press, hold, and release events. Checks cover mounting, accelerating, the Humanoid push clip, coasting, steering, jumping/landing/XP, braking, inventory input suppression, the console, restoration of the original controller, and walking after dismount. It writes screenshots and the result record under the unique test save root and exits automatically.

The fixed camera, lighting, and flat platform are test fixtures. These keyboard events exercise Unity/Valheim's input path; they do not establish that Windows UI automation can deliver physical gameplay key presses. Save/cloud/achievement isolation is identical to the physics mode. Neither harness belongs in the normal r2modman profile or release archive.

The test also checks that the support foot stays planted, push clips add no root travel, a second mount/dismount restores movement, and the HUD fits 1280×720 and 2560×1440 windows. Add `-skamtebord-keyboard-capture` to record six seconds at 30 frames per simulation second under `<test-root>/push-video/`. Those PNGs include the actual HUD; encode them at 30 fps with FFmpeg. Capture uses S for one second, W for 3.5 seconds, then releases W for 1.5 seconds. It does not inject forces or animation poses.

## Recording short gameplay demonstrations

In 0.3.0, Keyboard mode also tests sprint acceleration/stamina, the Humanoid tuck's planted feet and hip height, momentum on sprint release, hold/toggle run preferences, and one-time control migration. The game preference is restored in memory without saving. The same run renders both ramp shapes over a large floor from six viewpoints, including front/back at 60 metres, using the normal game camera's rendering path. Keeping the floor in this test catches the transparent-sprite fallback that isolated mesh rendering missed. Fixtures clone an opaque material from the game's Wood prefab instead of relying on Shader.Find to locate bundled shaders.

Radio mode requires at least one local MP3. It checks up to eight shuffled tracks for successful decoding, advancing sample position and nonzero AudioSource output, then exercises skipping, toggle off/on, and stopping on dismount. It also checks temporary game-music muting, restoration of a pre-existing mute, retained music-volume changes, zero radio volume, component disable/re-enable, and empty/corrupt playlists. The harness restores its temporary settings; it never saves game music preferences. This verifies Unity audio output, not physical speaker audibility. No music is copied into evidence or packaged.

Add `-skamtebord-capture` and enable the renderer by omitting `-nographics`:

```text
-skamtebord-smoke -skamtebord-capture -batchmode -force-d3d11 -screen-fullscreen 0 -screen-width 960 -screen-height 540 -logFile <absolute-local-log-path>
```

The same save isolation runs first. After the initial mount checks, this mode records three takes on a temporary marked platform: pushing/steering/braking, jumping/tricks/XP, and a 12-degree gravity descent. These are actual Unity camera renders of the mod's movement and board presentation. Controls are injected by the harness. The trick take grants the temporary character enough XP to unlock its demonstrated tricks; telemetry records subsequently earned XP separately. The test course, lighting, and tracking camera are capture fixtures, not shipped game features.

Frames (960×540 at 30 frames per simulation second) and `telemetry.csv` are written to `../captures/<UTC timestamp>/` relative to the staged game root. `capture-complete.txt` marks a complete run. Normal movement smoke checks after the initial mount are skipped in capture mode; this mode does not replace their validation.

Encode MP4 and GIF copies with the developer utility, using Python with `imageio-ffmpeg==0.6.0` installed:

```powershell
python scripts/encode-captures.py <take-directory> media/pushing --title "PUSH / CARVE / BRAKE" --subtitle "Actual game capture | Temporary test course"
```

Optional `--start`, `--end`, and `--speed` select a range and playback speed. Label slowed footage explicitly in the title or subtitle. Caption speed and earned XP come from telemetry. Audio and the normal gameplay HUD are not captured.
