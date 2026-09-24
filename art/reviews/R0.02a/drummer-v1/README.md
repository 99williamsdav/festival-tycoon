# Modular drummer prototype v1

Third R0.02a band-member visual approval candidate. One 1.75 m neutral performer travels offstage; a compact standing drum kit and two playing arms attach at set start. The earth-brown top, sandy hair, wine-coloured shells and brass cymbals distinguish this member from the approved guitarist and bassist while using their scale and subdued farm-festival palette.

## Review this exact version

- `01-drummer-close.png`: close performance silhouette, drums and sticks.
- `02-neutral-and-performing.png`: the same performer offstage without the kit and onstage with it.
- `03-three-on-stage-gameplay-distance.png`: guitarist, bassist and drummer on the approved trailer stage at gameplay-camera distance.
- `04-three-member-silhouettes.png`: all three members without the stage for shape comparison.
- `lwf_drummer_palette.png`: packed colour palette used in both exports.

The stage, guitarist and bassist are review-only context, not included in either drummer GLB. This drummer is neither approved nor integrated into the game.

## Files and attachment contract

- Editable source: `assets/source/characters/lwf_performer_drummer_v1.blend`.
- Neutral body: `assets/runtime/characters/lwf_performer_drummer_body_v1.glb` (body, separate hair and two idle arms; 256 triangles).
- Performance attachment: `assets/runtime/characters/lwf_performer_compact_drum_kit_v1.glb` (drum shells, cymbals/hardware and two playing arms with sticks; 592 triangles).

Both exports share a feet-centred origin, Blender `+Y` forward and Godot `-Z` forward. The neutral body is grounded and reaches 1.75 m. The simulation retains one stable performer identity. During travel, use the body and idle arms without the kit. At the authoritative set start, attach the kit at that origin, hide the two named `Idle*Arm` nodes and play the two `DrumLoop_*_24fps_17frames` arm/stick clips. At set end, detach the kit and restore the idle arms. The appearance/disappearance of the kit is an abstract set-state transition; no pickup, carry, backstage handoff or root movement is authored.

This is a compact standing kit, chosen to keep the neutral/performing body identical and readable on the existing stage. The 17-frame, 24 fps animations are presentation-only wrist/arm cues. There is no rig or finished character animation set.

## Verification, ownership and provenance

Blender 4.1.1 reopened the source and reimported both GLBs: four mesh/material/UV objects each, expected 256/592 triangle counts, matching bounds, grounded 1.75 m body, no armature or body animation, and two kit arm/stick actions. Godot 4.7.2 imported and instantiated both in an isolated project: four mesh nodes each, matching triangle counts, embedded 192 × 8 palette and both kit animation clips. All four review renders were visually inspected. See `technical.json` for measured bounds.

The simulation owns person identity, travel, stage slot and set timing; this asset is presentation only. No audio, gameplay integration, navigation, collisions, other assets or stage changes are included.

The body derives from the approved project-authored guitarist body geometry. The hair, compact drums, cymbals, stands, playing arms, sticks and palette are project-authored. No downloaded mesh, texture or instrument asset was used. Coordinator and user visual approval are both outstanding.
