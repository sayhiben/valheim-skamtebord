# Skamtebord gameplay captures

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
