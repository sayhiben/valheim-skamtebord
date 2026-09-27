# Local r2modman playtest

Prepared on 2026-09-26 using the installed Steam release, **Valheim 1.0.16** (Steam build 25527674).

## Launch again

1. Open r2modman and select Valheim.
2. Select the **Skamtebord Dev** profile and click **Start modded**.
3. Select character **Skamtebord Test** and world **Skamtebord Test**. Leave **Start Server** unchecked.

This is a normal single-player session. The new character and world use the game's default cloud-save setting. The existing Default mod profile and existing character/world were not used for this test.

The development profile contains only BepInExPack 5.4.2350, Jotunn 2.30.1, and Skamtebord 0.1.0. No runtime smoke-test or capture plugin is installed there. Physics, controls, radio, and progression use the generated default configuration.

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

2026-09-26 update: **0.2.0** was installed into this same **Skamtebord Dev** profile after its isolated keyboard and physics tests passed. The previous DLL and package metadata are backed up in `.local/profile-backups/pre-0.2.0/`. Configuration and personal radio files were preserved. The normal profile contains no developer test harness. Launch **Start modded** again to use the new pushing/coasting clips and repositioned HUD. See [current validation](testing.md) and [six-second gameplay capture](../media/04-pushing-animation.mp4).

Close Valheim before rebuilding/copying. Run `scripts/build.ps1` and copy `dist/Skamtebord/Skamtebord.dll` over the profile's `BepInEx/plugins/Local-Skamtebord/Skamtebord.dll`, then launch **Start modded** again. Preserve the profile's configuration and personal radio files. The general `scripts/install.ps1` targets the Steam game directory, not this r2modman profile.
