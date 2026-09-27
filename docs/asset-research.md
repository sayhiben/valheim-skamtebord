# Replacement skateboard art and animation candidates

Checked 2026-09-21. This is an asset shortlist, not a claim of tested Valheim compatibility. The Kenney archive was downloaded and inspected; other candidates were assessed from creator listings or first-party catalog/license pages. No replacement has been integrated into the mod.

## Recommended candidates

| Source | License evidence | What it offers | Assessment for Skamtebord |
| --- | --- | --- | --- |
| [Kenney — Mini Skate 1.2](https://kenney.nl/assets/mini-skate) | CC0 on the creator page and in the downloaded `License.txt`. [Creator's itch listing](https://kenney-assets.itch.io/mini-skate) also confirms CC0. | Lightweight board, ramps, rails, wooden platforms, two animated miniature characters; FBX, GLB, OBJ. | Easiest board import to test. Its simplified character rig is unsuitable for direct human-rig retargeting; use skating clips as editable motion references. |
| [Stellarator — Skateboard](https://blendswap.com/blend/24364) | Creator explicitly releases the model as CC0. | Detailed Blender 2.8x board; 26.8 MB listing; Eevee/Cycles materials. | Better candidate for detailed trucks/deck/wheels. Inspect polygon count and materials, optimize, and adapt its finish to Valheim. Download requires a BlendSwap account; archive not inspected. |
| [blendernerd143 — Skateboard ollie animation](https://blendswap.com/blend/13256) | Creator listing specifies CC-BY; exact version and bundled notices still need archive inspection. | Blender 2.7x character/ollie scene tagged Rigify; 2.33 MB. | Concrete source for a humanoid crouch/pop/landing sequence. Needs current Blender conversion, baking, and retargeting. Does not provide a complete skate controller or trick library. |
| [Quaternius — Universal Animation Library](https://quaternius.itch.io/universal-animation-library) | Creator listing explicitly CC0. | General humanoid animations; FBX/GLB, with `.blend` in the paid Source tier. Free Standard is a subset; the advertised 120+ total is not all in the free download. | Useful supporting locomotion/transition material and a conventional humanoid rig. Not verified to contain skateboard pushing, kickflips, or heelflips. |

For a visual upgrade, evaluate Stellarator's detailed board alongside Kenney's very inexpensive mesh. Preserve Valheim's existing player appearance and armor; adapt movement to its skeleton. The ollie source is a more promising starting point for articulated human motion than the Kenney miniature rigs. A polished push loop, carving lean, stable stance, and feet leaving/catching the board will still require animation work.

## Verified Kenney inventory

Official archive: <https://kenney.nl/media/pages/assets/mini-skate/00b0c2b304-1709221152/kenney_mini-skate.zip>

Local cache: `.local/asset-research/kenney/unpacked/`.

SHA256: `82582F6DE507E93090C16BB4802A7C361015FE246EDDD571E6E96F816B12E8C6`.

- `Models/GLB format/skateboard.glb`: static, one mesh, 240 triangles, 372 exported vertices. No wheel/deck animation channels.
- `character-skate-boy.glb` and `character-skate-girl.glb`: 29 clips each, but only four skate-specific names: `skate` (0.517 s), `skate-stand` (0.500 s), `skate-air` (0.667 s), `skate-grab` (0.500 s).
- There are no named kickflip, heelflip, or shuvit clips. The combined count of 58 animations must not be described as 58 skate tricks.
- Rig nodes: root, leg-left, leg-right, torso, arm-left, arm-right, head. No separate knee, elbow, hand, or foot joints. Ordinary humanoid retargeting cannot recover articulation absent from the source.
- License is CC0; retaining creator credit in our asset notices is still useful for provenance.

## Useful movement source under custom terms

[CMU subject 134 — Skateboard Motions](https://mocap.cs.cmu.edu/search.php?subjectnumber=134) contains 15 trials, including forward movement, leaning turns, pushing turns, starts, stop-and-go, and a pump jump. This is a potentially stronger source for natural human skating movement than miniature-character animation. Original data is ASF/AMC and C3D, with catalog entries at 120 Hz. It needs conversion, cleanup, and retargeting; there are no named kickflip/heelflip trials in this subject.

CMU uses its own terms, **not a Creative Commons license**. Its [FAQ](https://mocap.cs.cmu.edu/faqs.php) permits copying, modification, and redistribution; its [homepage](https://mocap.cs.cmu.edu/) permits inclusion in commercial products but disallows selling the motion data itself, including converted data. Keep it distinct from the CC shortlist. The retrieved first-party pages are saved under `.local/asset-research/cmu-*.html` because the web reader intermittently timed out.

[Haley Tuffles' motion pack](https://haleytuffles.com/motioncapture) advertises a skating category and BVH/FBX/Blender formats with broad reuse permission, but does not name a CC license on its page. The skating subtype and exact clip inventory were not inspected, so this is a secondary lead only.

## Additional CC candidates requiring file inspection

- [Sungebob — Skateboarder](https://sketchfab.com/3d-models/skateboarder-9d6a933e1e77421594a5e2b9ed049f01): Sketchfab public metadata reports CC BY 4.0, downloadable, five animations, about 40.9k triangles. Clip names and skeletal suitability remain unverified.
- [Greased Ghoul — Basic Skateboard Complete Setup](https://sketchfab.com/3d-models/basic-skateboard-complete-setup-30b4467ee0644f469b5d29d3e6b084cd): listing specifies CC BY 4.0 and board actions including ollie, kickflip, heelflip, pop shuvit, and powerslide. This animates the board, not a human rider. At roughly 61k triangles plus 4K texture, it needs optimization; the description also requests reading the included license, which has not been inspected.
- [Chenchanchong — FS 180 Nosegrab](https://sketchfab.com/3d-models/fs-180-nosegrab-on-halfpipe-skateboard-1dd191a0b8004f4893906032a45d7933): CC BY 4.0 in public metadata; one action-specific sequence in a roughly 518k-triangle scene. A possible motion-extraction candidate, not a lightweight scene to ship wholesale.

## Integration considerations

These are recommendations inferred from the current mod and source metadata, not completed compatibility tests. Keep the actual Rigidbody responsible for travel and jumps; strip or isolate imported root translation so animation does not move the player twice. Retarget body motion onto the existing player skeleton, synchronize the board's trick phase, and correct foot placement for deck height and ground slope. Blend out cleanly on landing, bail, dismount, death, and swimming. Test both player appearances and armor before shipping.

For CC BY assets, preserve the creator/source/license notice and document modifications in the distributed asset credits. Confirm the exact license version and bundled notices for each downloaded file. [Creative Commons attribution terms](https://creativecommons.org/licenses/by/4.0/).
