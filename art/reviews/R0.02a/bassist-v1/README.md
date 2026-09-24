# Modular bassist prototype v1

Second R0.02a band-member visual approval candidate. This is one 1.75 m performer with a neutral travel/idle body and a separate solid four-string bass kit for the live set. A muted plum top, compact dark hair, teal bass body and long neck distinguish the bassist from the approved guitarist while keeping the same low-poly scale and subdued farm-festival palette.

## Review this exact version

- `01-bassist-close.png`: close playing silhouette and bass detail.
- `02-neutral-and-playing.png`: the same performer without and with the instrument.
- `03-stage-gameplay-distance.png`: bassist on the approved trailer stage at game-camera distance; approved guitarist appears only as scale/readability context.
- `04-two-member-silhouettes.png`: separate guitarist and bassist silhouettes for comparison.
- `lwf_bassist_palette.png`: packed colour palette used by both exports.

The stage and guitarist are review-only context. They are not included in either bassist export. The bassist itself is not yet approved or integrated into the game.

## Files and attachment contract

- Editable source: `assets/source/characters/lwf_performer_bassist_v1.blend`.
- Neutral body: `assets/runtime/characters/lwf_performer_bassist_body_v1.glb` (body, separate hair, left idle arm, right idle arm; 256 triangles).
- Bass kit: `assets/runtime/characters/lwf_performer_solid_bass_kit_v1.glb` (bass, strap, fretting arm, plucking arm; 488 triangles).

The exports share a feet-centred origin, Blender `+Y` forward and Godot `-Z` forward. The body is grounded and reaches 1.75 m. A single persistent performer travels with the neutral body and idle arms. At the authoritative set start, attach the kit at that origin, hide the two `Idle*Arm` mesh nodes, and play `PluckLoop_24fps_17frames`. At set end, detach the kit and restore the idle arms. No pickup, carry, backstage handoff or simulation movement is authored here.

Hair and idle arms are separate meshes. Clothing, skin, hair and instrument colours occupy cells in one packed 192 × 8 palette, supporting later colour variation. The 17-frame, 24 fps pluck cue moves only the right arm, with no rig or root motion. This is not a finished character animation set.

## Verification, ownership and provenance

Blender 4.1.1 reopened the source and reimported both GLBs: four mesh/material/UV objects each, 256 and 488 triangles, matched bounds, grounded 1.75 m body, no armature or body animation, and a kit pluck action. Godot 4.7.2 imported and instantiated both in an isolated project: four mesh nodes each, expected triangle counts, embedded 192 × 8 palette, and the kit's animation. The review renders were visually inspected. See `technical.json` for measured bounds.

The simulation owns person identity, travel, stage slot and set timing; this asset is presentation only. No drummer, audio, gameplay integration, navigation, collisions or stage changes are included.

The body derives from the approved project-authored guitarist body geometry. The hair cap, bass, strap, arm geometry and palette are project-authored. No downloaded mesh, texture or instrument asset was used. Coordinator and user visual approval are both outstanding.
