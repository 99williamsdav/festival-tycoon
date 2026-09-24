# Modular guitarist/front-person prototype v1

First R0.02a live-performance visual approval candidate. This is one 1.75 m performer with a neutral travel/idle presentation and a separate acoustic guitar kit for the live set. The torso, head, clothes and palette stay close to the approved generic attendee scale and farm colours; the warm timber guitar and two hand positions make the set readable from the high isometric camera.

## Review this exact version

- `01-front-three-quarter.png`: close playing silhouette and instrument detail.
- `02-neutral-and-playing.png`: the **same performer**, neutral without an instrument on the left and performing with the guitar on the right.
- `03-stage-gameplay-distance.png`: the performer on the approved open trailer stage, at approximate game-camera distance and true deck height.
- `04-attendee-scale-comparison.png`: neutral performer beside the approved generic attendee, both 1.75 m.
- `05-strum-pose-ends.png`: the two ends of the small right-arm strum motion.

The stage and generic attendee appear only in review renders and source review context. They are not included in either performer export.

## Files and attachment contract

- Editable source: `assets/source/characters/lwf_performer_frontperson_v1.blend`.
- Neutral body: `assets/runtime/characters/lwf_performer_frontperson_body_v1.glb` (body, swappable hair, left idle arm, right idle arm; 244 triangles).
- Guitar kit: `assets/runtime/characters/lwf_performer_acoustic_guitar_kit_v1.glb` (guitar, strap, fretting arm, strumming arm; 388 triangles).
- Both GLBs share the same feet-centred scene origin and Blender `+Y` forward / Godot `-Z` forward. The body reaches 1.75 m; the kit overlays it without changing the actor root or simulation identity.

The performer walks to their stage position with the neutral body and idle arms. When the authoritative set state starts, attach the guitar kit at the same scene origin, hide the two named `Idle*Arm` mesh nodes, show the kit and play its `StrumLoop_24fps_17frames` clip. At set end, detach the kit and restore the idle arms before the performer walks away. No pickup, carry or backstage handoff animation is needed. The guitar is never part of the body mesh.

The hair is a separate mesh. Clothing, skin and hair use distinct cells in a single packed palette, allowing later colour variations without a new body mesh. Idle arms are separate shoulder-pivot objects, so a later walk cycle can move them without skinning. The only authored motion here is a 17-frame, 24 fps right-arm strum clip, with no root motion or simulation effects. This is not a finished character animation set.

## Ownership, exclusions and provenance

The simulation owns the persistent performer ID, travel, stage slot, set state, timing and enjoyment. Presentation chooses the neutral or playing parts and their animation. This prototype supplies no other band members, audio, audience logic, collisions, navigation, AI, facial detail, stage changes or gameplay integration.

Blender 4.1.1 reopened the source and reimported both GLBs, confirming mesh/material/UV counts, 1.75 m grounded body, matched attachment origin, and the separate kit animation. Godot 4.7.2 imported and instantiated both through the standard editor pipeline: four mesh nodes in each, expected triangle counts and embedded 192 × 8 palettes; the kit imported its `StrumLoop` animation. All five review renders were visually inspected.

Original procedural/project-authored geometry and palette. No downloaded mesh, instrument, texture or copied character design. Both coordinator and user visual approval remain outstanding.
