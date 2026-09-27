# Skating animation pipeline

Researched and implemented against Valheim 1.0.16 / Unity 6000.0.75 on 2026-09-26.

## Guidance and implementation

- The [Valheim Modding animation guide](https://github.com/Valheim-Modding/Wiki/wiki/Replacing-Valheim-Animations) recommends Humanoid clips and `AnimatorOverrideController` to retain Valheim's animator state machine and movement blend trees. We create a controller per rider and restore its predecessor on dismount; we do not replace a shared controller globally.
- [Unity's override-controller reference](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimatorOverrideController.html) describes retaining state-machine logic while substituting clips. The rider's existing locomotion blend selects the coast/push clips. Rigidbody motion remains authoritative; the animation cannot add root-motion travel.
- [Jotunn's asset-creation guide](https://valheim-modding.github.io/Jotunn/tutorials/asset-creation.html) describes the Unity asset-bundle workflow and keeping original assets separate from game reference assets. This bundle contains only our three original clips.
- Unity does not guarantee loading a newer editor's asset bundle in an older player ([AssetBundle compatibility](https://docs.unity3d.com/6000.0/Documentation/Manual/AssetBundlesIntro.html)). The installed game reports 6000.0.75, so the source project and generated bundle target 6000.0.75f1, not the newer editor also installed on this machine.

## Editable source

`assets/animation-source/skate-push.blend` contains an original Humanoid armature, a simple segmented preview character, an unexported preview board, and three actions:

- `SkateTuck`: static low crouch used for sprinting, with both feet fixed to the deck.
- `SkateCoast`: knees bent, feet across the deck, relaxed balance stance.
- `SkatePush`: 1.2-second loop; front foot supports the body while the rear foot reaches out, plants, sweeps behind, and returns to the deck. Torso and arms counterbalance the motion.

The proxy character is only for authoring. Valheim renders its own character and clothing. No game mesh, vanilla animation, external animation, or third-party character is redistributed. The animation and proxy were authored for this mod and have no third-party asset attribution requirements.

`scripts/create-skate-animation.py` reproducibly generates the Blender source and exports `assets/SkamtebordAnimations/Assets/Source/Skater.fbx`. The Unity editor script `BuildSkateAnimations.Build` maps the source to a Humanoid avatar, checks the clips, bakes root movement into the poses, enables looping, and writes `assets/bundles/skamtebord-animations`.

The plugin embeds the small bundle in its DLL, so the mod remains a single-DLL installation.

Build with `scripts/build-animations.ps1` (explicit Blender/Unity executable overrides are supported), then `scripts/build.ps1`. The source FBX includes the original proxy skin so its bind matrices provide an unambiguous T-pose for the Humanoid avatar. Only the three animation clips enter the bundle. Coasting and tuck curves are checked for unwanted motion in both Blender and Unity. All parents are evaluated before child bone keyframes are baked.

The adapter leaves `Animator.applyRootMotion` untouched: [Unity documents that changing it reinitializes the animator](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animator-applyRootMotion.html), which can replay Valheim's standing-up state and block movement after dismount. Root translation/rotation are instead baked into the clips at import; the live test measures root deltas while pushing. Swapping back to the base controller also requires preserving layer states, weights, and parameters, so both directions explicitly carry that state across.

## Independent input test

The Windows UI automation tool did not deliver F5 to Valheim's gameplay input in either fullscreen or windowed mode, although text fields and mouse controls worked. Windowed mode alone did not resolve that limitation.

The developer-only `-skamtebord-keyboard-test` path instead creates a virtual keyboard with Unity's supported [Input System event API](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.11/manual/Events.html). It sends held/released keys through `ZInput`, the normal `PlayerController`, and the mod's real bindings. It retains the inventory/console input guards and does not call `Toggle` or `CaptureControls` to simulate input. This verifies the game input path, **not physical OS keyboard delivery**.

The synthetic keyboard has its own layout with background execution enabled, following Unity's [background device guidance](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.11/manual/Devices.html#background-and-focus-change-behavior). Only this device receives that setting; physical devices retain their normal focus policy. The layout is removed after the test. This lets the automated launcher run without requiring the user to focus the test window.

The existing isolated test bootstrap disables saving, cloud access, and achievement writes for that process. The test plugin is never included in the release or normal r2modman profile. See its [README](../tests/Skamtebord.RuntimeSmoke/README.md) for running the tests.
