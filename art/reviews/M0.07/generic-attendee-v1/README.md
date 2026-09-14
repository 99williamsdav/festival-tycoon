# Generic attendee placeholder v1

Lightweight presentation-only attendee for the M0.07 autonomous-navigation slice. The design is a friendly, neutral adult festival-goer with a readable facing direction, a relaxed planted stance, separated arms, and a restrained casual-outfit palette. The revised head has no facial hair and uses a compact low-poly back hair wedge rather than a prominent fringe. It deliberately has no detailed face, accessories, role variants, or mascot-like proportions.

## Deliverables

- Source: `assets/source/characters/lwf_generic_attendee_v1.blend`
- Runtime: `assets/runtime/characters/lwf_generic_attendee_v1.glb`
- Review renders: `01-front-three-quarter.png`, `02-rear-side-facing.png`, `03-farm-asset-scale.png`, and `04-gameplay-distance.png`
- Machine-readable checks: `technical.json`

The scale and gameplay renders use existing approved farm assets for context only. The six figures in the gameplay-distance render are instances of this same mesh, not character variants.

## Technical summary

- Height: 1.75 m
- Bounds in Blender: `(-0.405704, -0.148000, 0.000000)` to `(0.405704, 0.200500, 1.750000)` m
- Geometry: one mesh, 148 authored vertices / 496 runtime split vertices, 236 triangles
- Shading: one material and one embedded 96 x 8 packed palette texture
- Transform: applied location/rotation/scale; origin at `(0, 0, 0)` between the planted feet on ground level
- Forward: Blender `+Y`; glTF/Godot conversion yields Godot `-Z`
- Rigging/animation: none

The asset contains no armature, skinning, animation, root motion, collision, navigation, selection behaviour, AI, facial animation, accessories, or alternate colorways. Stable identity, position, facing, action, destination, movement, and interpolation remain owned by the simulation; this mesh is presentation only.

## Verification

Verified by reopening the `.blend` and re-importing the `.glb` in Blender 4.1.1, checking mesh/material/palette counts, triangle and vertex counts, bounds, ground contact, origin, applied transforms, lack of armature, and documented forward-axis mapping. Also imported by the standard Godot 4.7.2 editor pipeline and instantiated headlessly, confirming one `MeshInstance3D`, no `Skeleton3D` or `AnimationPlayer`, one surface, the expected geometry and axis-converted AABB, and the embedded 96 x 8 palette.

## Provenance

Original procedural/project-authored geometry and palette. No downloaded mesh, texture, or copied character design is used.
