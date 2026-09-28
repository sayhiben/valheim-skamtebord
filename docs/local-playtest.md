# Local r2modman playtest

Prepared on 2026-09-26 using the installed Steam release, **Valheim 1.0.16** (Steam build 25527674).

## Current development build — 2026-09-27

The installed DLL in **Skamtebord Dev** is now the **0.4.0 development build**, including total-speed push limits, momentum-preserving carving and banking, shared skating support for terrain and vertical transitions, tap/hold beginner tricks, the wooden halfpipe, continuous pushing, and replicated multiplayer visuals. SHA-256: `3648A2BD2BC4EBBF70A7A618336682D0779DEF72E20C40155C46D46995DD5EB3`. Previous DLLs are backed up under `.local/profile-backups/`. Configuration and radio files are preserved. The isolated validation results are in [testing.md](testing.md).

For an immediate ramp session, run `scripts/qa.ps1 -Ramps`: the player starts mounted, with all tricks unlocked, in the disposable three-lane course. The halfpipe is farther right; a nearby workbench, Hammer and 80 Wood let you try building another. The ordinary r2modman profile still uses normal progression and saves. The observations below describe the earlier 0.1.0 hands-on session.

## Launch again

1. Open r2modman and select Valheim.
2. Select the **Skamtebord Dev** profile and click **Start modded**.
3. Select character **Skamtebord Test** and world **Skamtebord Test**. Leave **Start Server** unchecked.

This is a normal single-player session. The new character and world use the game's default cloud-save setting. The existing Default mod profile and existing character/world were not used for this test.

The development profile contains only BepInExPack 5.4.2350, Jotunn 2.30.1, and Skamtebord (originally 0.1.0 in this hands-on session; now updated as listed above). No runtime smoke-test or capture plugin is installed there.

Profile directory:

```text
C:\Users\bmene\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Skamtebord Dev
```

The installed mod is `BepInEx\plugins\Local-Skamtebord\Skamtebord.dll`. Its default radio folder is the adjacent `radio-mp3s` directory. Configuration is `BepInEx\config\com.skamtebord.valheim.cfg`; the current session log is `BepInEx\LogOutput.log`.

## Results so far

- Rebuilt the current source against the installed 1.0.16 assemblies without changing gameplay code. Build succeeded with the two previously documented MSB3277 warnings.
- All 21 core checks passed.
- Confirmed the installed DLL matches the fresh build (SHA-256 `44A6567BE75BB81239513531743914738E7C54547048E6BE6BB0ED4564C16DF5`).
- r2modman displayed all three packages enabled and launched the profile through Steam.
- The live log confirmed Skamtebord loaded, the board and hand-crafting recipe registered, and the Skamtebord skill registered.
- A fresh character entered the fresh single-player world and arrived at the Meadows spawn. The log queued the Skamtebord recipe unlock for that character.
- No Skamtebord exceptions were found in the startup/world-entry log. Unity emitted three LoxChest texture-property errors; their cause has not been isolated.
- Automated gameplay key presses did not open their game UI, although mouse/menu actions and text entry worked. The user physically pressed F5 and confirmed the console opened; physical Tab also opened the inventory.
- Spawned only the exact recipe ingredients through the game's console, then turned developer commands off again. The user collected the materials.
- Verified the recipe in the ordinary inventory UI and clicked Craft. It consumed 8 Wood, 4 Resin, and 2 Leather scraps, producing one Skamtebord in hotbar slot 2 with no crafting station.
- During the user's physical-key test, observed the player riding through the Meadows with the board rendered beneath their feet. The live HUD showed 38 km/h and 100 banked Skamtebord XP.
- Visual issue observed at the current display/UI scale: the skating HUD overlaps the vanilla health display near the lower-left corner. This was recorded without changing the as-is build.
- The log check after the ride showed no new error/exception entries in its recent output. The radio correctly reported an empty MP3 folder; music playback was not tested.

The game was left open for continued hands-on testing. Braking/steering feel, individual trick inputs, and save/reload persistence are not yet signed off by this session's observations.

The earlier harness results in [testing.md](testing.md) are separate evidence and do not replace this interactive check.

## Quick recipe test

The r2modman launch arguments include `-console`. In this disposable test world, open F5 and enter each command separately if you want the materials immediately:

```text
devcommands
spawn Wood 8
spawn Resin 4
spawn LeatherScraps 2
```

Collect the materials, open the inventory with Tab, select **Skamtebord**, and craft it. It requires no station. Keep the board in your inventory, close the inventory, and press **B** to mount. **W** pushes, **A/D** steer, **S** brakes, and **Space** ollies. Press **B** again to dismount. Advanced tricks unlock through Skamtebord XP.

## Updating the development DLL

2026-09-27 speed/banking follow-up: the current **0.4.0 development** DLL passed 38 carving, 35 surface, 61 halfpipe/input, 84 keyboard/rendering, 52 ramp, 30 physics, 21 core, and 73 two-process replication checks. Push limits remain 9/14 m/s through turns; terrain can exceed them, carving preserves momentum, and left/right rounded corners maintain contact through an 84° bank. `Physics/MaximumSpeed` remains a separate soft terrain governor, default 25 m/s, with `0` disabling it; airborne motion is unaffected. The prior build, profile metadata and configuration are backed up in `.local/profile-backups/pre-0.4.0-20260927-165508/`. Configuration was preserved byte-for-byte and MP3s were untouched.

2026-09-27 earlier surface-support follow-up: that **0.4.0 development** DLL passed 35 surface, 61 halfpipe/input, 83 keyboard/rendering, 52 ramp, 30 physics, 21 core, and 73 two-process replication checks. It replaces the halfpipe-only ground exemption with shared support rules, bounded seam probes, natural crest departure, and pre-impact wall detection. The previous build and profile metadata/configuration are backed up in `.local/profile-backups/pre-0.4.0-20260927-163305/`. Configuration was preserved byte-for-byte; MP3s and normal saves are unchanged.

2026-09-27 earlier update: **0.4.0 development** was installed after 60 halfpipe/input, 83 keyboard/rendering, 52 ramp, 30 physics, 21 core, and 73 two-process replication checks passed. That earlier DLL's SHA-256 was `5529DB913B99275541718C2B6BEA9FC97251F30B7F4F6296F950202301E68FBA`. The prior 0.3.1 DLL, metadata, configuration and mod-manager record are backed up in `.local/profile-backups/pre-0.4.0-20260927-160036/`. Configuration was preserved byte-for-byte, MP3 files were untouched, and the normal profile contains no QA harness. Multiplayer evidence uses the game's loopback TCP backend; Steam/PlayFab transport and dedicated servers remain untested.

2026-09-27 update: **0.3.1** is installed after **54 live radio checks** and **21 core checks** passed. Its DLL hash matches the current build above. The previous DLL, metadata, configuration, radio-folder instructions, and mod-manager record are in `.local/profile-backups/pre-0.3.1-20260927-132622/`. MP3 playback now temporarily mutes only Valheim's music source and restores its prior mute state when playback ends. Sound effects and saved music-volume preferences are unchanged. The profile configuration was preserved byte-for-byte, and its music files were not moved or copied.

2026-09-27 update: **0.3.0** is installed in **Skamtebord Dev** after 83 keyboard/rendering, 52 ramp, 30 physics, 34 radio, and 21 core checks passed. The installed DLL matches SHA-256 `3E7930BB345A22719DD2C6D631AE0114E74B7611C7455564A7C6EB71684FB7A7`. Previous DLL, metadata, configuration and mod-manager record are in `.local/profile-backups/pre-0.3.0-20260927-125704/`. The normal profile contains no QA harness. Its `Radio/Directory` now points to `D:\dev\valheim-skamtebord\radio-mp3s`, where the six supplied tracks were verified to decode and produce Unity audio output. The MP3 files themselves were not moved or copied. Legacy default trick bindings migrate to J/K/L/U/I when the new plugin loads. Sprint follows the game's existing hold/toggle preference; this installation currently uses toggle run.

2026-09-26 update: **0.2.0** was installed into this same **Skamtebord Dev** profile after its isolated keyboard and physics tests passed. The previous DLL and package metadata are backed up in `.local/profile-backups/pre-0.2.0/`. Configuration and personal radio files were preserved. The normal profile contains no developer test harness. Launch **Start modded** again to use the new pushing/coasting clips and repositioned HUD. See [current validation](testing.md) and [six-second gameplay capture](../media/04-pushing-animation.mp4).

Close Valheim before rebuilding/copying. Run `scripts/build.ps1` and copy `dist/Skamtebord/Skamtebord.dll` over the profile's `BepInEx/plugins/Local-Skamtebord/Skamtebord.dll`, then launch **Start modded** again. Preserve the profile's configuration and personal radio files. The general `scripts/install.ps1` targets the Steam game directory, not this r2modman profile.
