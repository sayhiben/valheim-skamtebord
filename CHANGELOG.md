# Changelog

## 0.2.0

- Original Blender-authored Humanoid push cycle and coasting stance, embedded in the mod DLL.
- Per-rider animation overrides preserve Valheim's state machine and restore the original controller on dismount.
- Pushing state synchronized for remote rider presentation; movement remains Rigidbody-driven.
- Repositioned, scalable skating HUD with configurable placement, clear of the default health/stamina HUD.
- Isolated keyboard-event playtest exercises normal Valheim bindings, menus, animation, and movement without direct rider-control injection.
- Builds against Valheim 1.0.16 and matching Unity 6000.0.75; editable Blender/Unity source and reproducible asset build included.

## 0.1.0

- Craftable skateboard with immediately offered early-game recipe and original procedural art.
- Player-rigidbody pushing, gravity-driven downhill movement, steering, braking, and ollies.
- Six tricks, safe-landing combos, repetition penalties, and persistent Skamtebord XP unlocks.
- Local configurable MP3 radio with shuffled asynchronous playback and bounded error recovery.
- Owner-only movement, synchronized board/trick visuals, input guards, and dismount cleanup.
- Windows build/package/install scripts, pinned dependencies, and automated progression checks.

Initial release: visual polish, terrain tuning, and multiplayer playtesting remain necessary.
