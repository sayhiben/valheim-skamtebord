# Skamtebord implementation research

Checked 2026-09-20. These are design references, not evidence that Skamtebord itself has passed a game session.

## Current game and library baseline

Valheim 1.0 launched on September 9, 2026. The official notes include a Unity engine upgrade, so old beta-era Unity assemblies and private-member assumptions should be discarded. Read the installed Steam game's assembly metadata and compile against that installation. [Iron Gate release notes](https://www.valheim.com/news/valheim-1-0-has-arrived-/)

Jötunn's changelog says 2.30.0 updated most systems for Valheim 1.0.7; 2.30.1 updates the overhauled build-menu categories. Use a tested 2.30.x release rather than a pre-1.0 library. An item recipe does not need a hammer category. [Jötunn changelog](https://github.com/Valheim-Modding/Jotunn/blob/2d4d875ce16c21ad8c99e99864d42e2c4821886b/CHANGELOG.md)

## Relevant existing work

| Reference | Observed pattern | Useful lesson |
| --- | --- | --- |
| [ValheimRAFT / ValheimVehicles](https://github.com/zolantris/ValheimMods) | Networked water and land vehicles, physical bodies, rider controls | Authority, dismount cleanup, ground filtering, and collision handling matter as much as acceleration. |
| [Jötunn item tutorial](https://valheim-modding.github.io/Jotunn/tutorials/items.html) | Clone a loaded vanilla item through `OnVanillaPrefabsAvailable`, then register `CustomItem` | Build a normal inventory item and recipe; do not fake crafting through a UI-only button. |
| [Jötunn custom skills](https://valheim-modding.github.io/Jotunn/tutorials/skills.html) | Register `SkillConfig` with a stable unique identifier; raise via normal skill API | Let Valheim persist the Skamtebord skill; never rename its identifier after release. |
| [MusicMod](https://github.com/joeyparrish/valheim-musicmod) | Resolves MP3 paths beside its plugin assembly and overrides music entries | Resolve the default radio folder relative to the mod DLL, not the working directory. Its 2022 music-key list is historical, not a current compatibility promise. |
| [OdinOnDemand](https://github.com/modestimpala/OdinOnDemand) | Wearable and placed in-world media players, synchronization | A personal skate soundtrack can be much smaller: keep files and playback local, with no uploads or multiplayer audio transfer. |

### Physics and multiplayer details

The examined ValheimVehicles revision was `066014e29653387e7c2f5c02577923c72192ff10`. Its [land controller](https://github.com/zolantris/ValheimMods/blob/066014e29653387e7c2f5c02577923c72192ff10/src/ValheimRAFT.Unity/Assets/ValheimVehicles/SharedScripts/VehicleLandMovementController.cs) projects velocity onto the contact plane, applies separate forward and lateral forces, steers with torque, filters ground raycast hits, and uses low-friction collider materials. Its [movement controller](https://github.com/zolantris/ValheimMods/blob/066014e29653387e7c2f5c02577923c72192ff10/src/ValheimVehicles/ValheimVehicles.Controllers/VehicleMovementController.cs) gates force application on network ownership and enables gravity only on the owner to avoid fighting transform synchronization. It uses the Unity 6 names `Rigidbody.linearVelocity` and `PhysicsMaterial`.

Design recommendation, inferred from those examples: use the local player's existing rigidbody and network transform for the first board implementation. Keep rider/board visual state small and synchronized, and avoid spawning a second independently simulated physical body under a moving character. Replace only the mounted movement calculation. Restore normal movement and animation on death, water entry, teleport, logout, item loss, and manual dismount.

Apply push and grip forces during physics updates. Gravity should supply downhill acceleration, with bounded grip and rolling resistance rather than a forced downhill speed. Preserve vertical velocity; slope contact should not flatten jumps. Cap excessive speed gradually. Do not award score merely for holding a trick key: record takeoff, actual airtime, completed trick, rotation, and landing/bail state. Airborne turning should be weaker than grounded steering.

### Items, recipe visibility, and progression

Jötunn's recipe system supports named ingredients and stations before game assets finish loading, resolves recipes after items, and can create recipes through `ItemConfig` or `RecipeConfig`. [Recipe tutorial](https://valheim-modding.github.io/Jotunn/tutorials/recipes.html)

Design recommendation: expose an inexpensive early-game workbench recipe and explicitly learn only that recipe if it must be offered immediately on installation. Verify visibility on both a fresh character and an existing save. Registration alone may still leave normal material-discovery requirements in effect.

Register one immutable custom skill identifier. The custom skill initially stays hidden until it has some XP; that is expected Jötunn behavior. Use modest, capped skill gains from successfully landed combos. Track pending combo score separately from banked XP; bailing discards the pending combo. A small optional Jump-skill contribution can reward genuine airtime, subject to cooldown and per-combo limits. Avoid Run XP from passive coasting. Unlocks can read the custom skill level directly.

For keys, Jötunn provides a game input abstraction and key hints. Keep bindings configurable and do not process skating actions while chat, console, inventory, or menus own input. [Jötunn inputs](https://valheim-modding.github.io/Jotunn/tutorials/inputs.html)

### Local radio

Unity's `UnityWebRequestMultimedia.GetAudioClip` supplies an audio download handler; `AudioType.MPEG` is the native MP2/MP3 format. [Unity audio type reference](https://docs.unity.com/en-us/engine/6000.7/script-reference/unityengine/audiotype)

Design recommendation: default to `Path.Combine(pluginAssemblyDirectory, "radio-mp3s")`, allow an absolute configured directory, enumerate `.mp3` case-insensitively, and construct an escaped absolute file URI. Load asynchronously, keep only the current/next clip, skip malformed files once, and stop retrying when every file fails. Use a nonspatial personal `AudioSource` with explicit volume and skip controls. Resume or fade when mounting/dismounting and dispose clips, requests, and coroutines on shutdown. Do not bundle songs.

`DownloadHandlerAudioClip.streamAudio` reduces memory use through streaming, but an application must ensure enough data is loaded before playing. For local files, await the request before assigning the clip; verify variable-bitrate files and paths with spaces, Unicode, and `#`. [Unity streaming reference](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Networking.DownloadHandlerAudioClip-streamAudio.html)

## Source reuse boundary

These references inspired behavior and architecture; no third-party implementation was copied into this research document. Jötunn is [MIT licensed](https://github.com/Valheim-Modding/Jotunn/blob/dev/LICENSE). ValheimMods uses [GPL-3.0](https://github.com/zolantris/ValheimMods/blob/main/LICENSE), and MusicMod uses [GPL-3.0](https://github.com/joeyparrish/valheim-musicmod/blob/main/LICENSE.md). Keep original implementation separate; review the relevant license before incorporating source or redistributing assets. Do not package Valheim or Unity game assemblies with the mod.

## Runtime acceptance checks

- Fresh/existing character: recipe appears, crafts, saves, reloads, and does not duplicate items.
- Flat ground, natural slope, wood ramp, rock transition: push, coast, steer, brake, jump, land, bail.
- Combo score banks only after a valid landing; invalid/repeated tricks cannot farm unlimited XP.
- Menu input, stamina exhaustion, swimming, death, portal, world exit: leave no stuck mount state.
- Two clients and a dedicated server: one owner applies movement; remote board visuals agree; reconnect preserves the skill.
- Radio: empty folder, invalid directory, malformed MP3, upper-case extension, unusual filename, skip during load, repeated mount/dismount.
