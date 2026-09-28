# Skamtebord gameplay captures

## 0.4.0 craft, build, and ride demo

`09-build-halfpipe-demo.mp4`: **40 seconds**, 1280×720, 30 fps, H.264 video and
48 kHz stereo AAC game audio, recorded from Valheim 1.0.16 on 2026-09-27 Pacific.

- 0–7s: craft the skateboard in the normal inventory menu.
- 7–13s: select and place the wooden halfpipe using the Hammer menu.
- 13–31s: separate kickflip and shuvit runs over opposite lips.
- 31–40s: boosted ollie/grab and a successful 560 XP bank.

Actual GPU frames and Unity mixer audio, at normal speed. Native crafting and
placement consume 8 Wood / 4 Resin / 2 Leather Scraps and 80 Wood respectively.
This is a scripted QA demonstration on prepared, cleared terrain with a fixed
camera and explicit cuts between run-ins. Cheats: Skamtebord 25, Jump 100,
300 replenished stamina, god mode, and an ollie impulse of 8. Flat-ground run-ins
start at 16 / 16 / 3 m/s; airborne motion, collisions, tricks, landings, and XP
use the released mod without airborne forces or landing overrides. The first
two tricks are visible but do not bank XP; the final ollie/grab banks normally.
Radio is disabled for this capture; audio is the game's music, ambience and
effects, with no personal MP3s or replacement soundtrack.

Captured with the published v0.4.0 DLL (SHA256
`3648a2bd2bc4ebbf70a7a618336682d0779def72e20c40155c46d46995dd5eb3`).
Source: `.local/runtime/smoke-saves/skamtebord_smoke_20260928002638_28d6cb/`.
All demo checks passed; the encoded file decodes to 1,200 frames and exactly
40 seconds of stereo audio (mean −25.5 dB, peak −7.5 dB).
Reproduce with `scripts/qa.ps1 -Mode Demo`, then
`scripts/encode-build-demo.py <session-root> media/09-build-halfpipe-demo.mp4`.
The capture harness and encoder are documented in
[the runtime QA guide](../tests/Skamtebord.RuntimeSmoke/README.md).

## 0.2.1 ramp momentum

`05-ramp-momentum.mp4` / `.gif` compares rolling off and jumping near the lip of the same 20° ramp, with a 12 m/s initial approach. Peak height above the flat floor is 1.43 m vs 2.69 m; the lip is 1.25 m high. Both flights land successfully. Four seconds of half-speed replay, silent, with labels added during encoding; the shorter flight holds its last frame at the end. Frames come from the actual Valheim GPU renderer with scripted control requests and real owner physics. The camera, lighting, floor, and ramps are QA fixtures, and no gameplay HUD is captured.

Source: `.local/runtime/smoke-saves/skamtebord_smoke_20260927192335_d9d66c/ramp-video/`. Reproduce with `scripts/qa.ps1 -Mode Ramps -Record`, then `scripts/encode-ramp-comparison.py <session-root> media/05-ramp-momentum`. See [ramp measurements](../docs/validation-ramps-021.csv).

## 0.2.0 pushing animation

`04-pushing-animation.mp4` / `.gif`: six seconds from Valheim **1.0.16**, recorded on 2026-09-26 Pacific (2026-09-27 UTC). Original Humanoid pushing and coasting clips on Valheim's character, with the actual revised gameplay HUD. Input System keyboard events drive the normal bindings and PlayerController; the harness does not inject rider controls or animation poses. The flat platform, camera, and lighting remain disposable test fixtures. MP4: 1280×720, 30 fps; GIF: 960×540, 15 fps. Normal playback speed, no audio, no added captions. Source: `.local/runtime/smoke-saves/skamtebord_smoke_20260927044436_7441c8/push-video/`.

## Earlier prototype footage

Recorded from Valheim 1.0.15 with Skamtebord 0.1.0 in an isolated, disposable test world on 2026-09-21 UTC. These are actual game-rendered frames and the mod's real Rigidbody movement. The marked platform, camera, lighting, and injected controls belong to the developer capture harness. Character poses and board art are still prototype quality.

| File | Duration | Content |
| --- | --- | --- |
| `01-push-carve-brake.mp4` / `.gif` | 10 seconds | Pushing from rest, carving both directions, coasting, braking to a stop. |
| `02-kickflip-xp.mp4` / `.gif` | 7.6 seconds | Ollie and kickflip, landing, then banking 700 Skamtebord XP. Half-speed replay of source seconds 2.2–6.0. |
| `03-downhill.mp4` / `.gif` | 9 seconds | Starting on a 12-degree slope, with no push input; real gravity accelerates the rider. |

The trick take uses an unlocked temporary character (level 9); the 700 XP shown is newly earned during this take. Caption speed is the rider's recorded speed, converted from metres per second to kilometres per hour. Captions are added for these demonstrations and are not the mod's gameplay HUD. Footage is silent and does not demonstrate radio playback, recipe interaction, natural terrain, or multiplayer.

MP4: H.264, 960×540, 30 fps for normal speed / 15 fps for half-speed replay. GIF: 720×405, approximately 12 fps, looping. The corresponding `.ass` files contain the telemetry-based captions.

Source run: `.local/captures/20260921-042218/`. All three source takes completed successfully; the longer raw trick take also includes a shuvit for 1,220 total earned XP. The staged process exited without saving the temporary character/world. Recording instructions are in `tests/Skamtebord.RuntimeSmoke/README.md`; encoding uses `scripts/encode-captures.py`.
## Sprint tuck (0.3.0)

`06-sprint-tuck.png` is a live Valheim screenshot from the keyboard QA run. Shift+W drives the normal player input path, reaching about 14 m/s while the Humanoid tuck keeps both feet planted. It also shows the updated trick-key hints and separate radio controls. No audio is included.
