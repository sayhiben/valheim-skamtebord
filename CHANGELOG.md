# Changelog

## 0.3.1

- Publish an installable GitHub release ZIP with SHA-256 checksum, r2modman metadata, and step-by-step installation/update instructions.
- Audible MP3 playback temporarily mutes Valheim's music source, preventing overlapping music while preserving sound effects, saved volume settings, and any pre-existing mute.
- Game music is restored when playback stops, the radio is disabled or silent, or a playlist is empty/unplayable. Music-volume changes made during a ride are retained.
- Added live radio tests for muting, restoration, volume changes, component cleanup, and invalid/empty playlists.

## 0.3.0

- Use forward plus Valheim's sprint input for faster skating (14 m/s by default), a Humanoid crouched tuck, and 6 stamina/second. Hold/toggle preferences are respected; ending sprint preserves momentum.
- Trick defaults moved to J/K/L/U/I, clear of Valheim's default actions. Legacy defaults migrate once; custom bindings are preserved. Removed the global interception of ordinary Use/Hide/Power/Walk/AutoRun inputs.
- QA ramps now have closed geometry with outward normals, explicit bounds, and a Valheim-compatible material. The interactive course starts in daylight with the camera facing the ramps.
- Developer QA uses the source checkout's radio-mp3s directory by default, with a RadioDirectory override. The radio reports empty/unreadable folders in the HUD; installed mods use a portable DLL-relative default directory.
- All MP3 extensions are gitignored throughout the repository. Added actual MP3 decoding/output tests, multi-angle ramp rendering checks, and sprint/control-migration checks.

## 0.2.1

- Ollies add their upward impulse to current momentum, preserving ramp-generated lift and horizontal travel.
- A 0.12-second takeoff grace window helps jumps near the lip; each flight permits one ollie impulse.
- Skating uses fresh ground contact, so pushing, braking and grip stop when leaving a ramp or terrain crest.
- Added isolated ramp trials, flight telemetry, GPU comparison captures, and a ready-to-skate interactive ramp course.

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
