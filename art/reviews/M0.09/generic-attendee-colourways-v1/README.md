# Generic attendee colourways v1

One combined M0.09 approval package for ten restrained casual-outfit colourways of the approved generic attendee. The intent is to make 50 autonomous attendees easier to track at gameplay distance without changing character geometry or implying gender, role, subculture, cuisine or vendor identity.

## Deliverables

- Editable source: `assets/source/characters/lwf_generic_attendee_colourways_v1.blend`
- Immutable approved base used at runtime: `assets/runtime/characters/lwf_generic_attendee_v1.glb`
- Runtime overrides: `assets/runtime/characters/generic-attendee-colourways-v1/palette-01.tres` through `palette-10.tres`, each with a matching 96 x 8 PNG palette
- `01-labelled-lineup.png`: all ten variants, labelled
- `02-close-comparison.png`: larger two-row comparison
- `03-gameplay-distance-50.png`: 50 attendees, five of each palette, in a farm-scale composition
- `04-grayscale-value-check.png`: colour-independent value/readability check
- `technical.json`: machine-readable counts, bounds, hashes and verification results

## Stable palette IDs

| Stable ID | Descriptive combination |
| --- | --- |
| `palette-01` | sage-clay |
| `palette-02` | ochre-navy |
| `palette-03` | brick-stone |
| `palette-04` | blue-rust |
| `palette-05` | plum-sand |
| `palette-06` | cream-forest |
| `palette-07` | teal-charcoal |
| `palette-08` | rose-denim |
| `palette-09` | sky-brown |
| `palette-10` | rust-olive |

The descriptive combinations are review language only; the stable implementation keys are `palette-01` to `palette-10`.

## Builder assignment recommendation

Choose the palette deterministically as `(stable attendee ordinal/id mapping) modulo 10`. For zero-based ordinals, ordinal 0 maps to `palette-01`, ordinal 1 to `palette-02`, and so on. Normalize negative numeric IDs before modulo if the simulation permits them. This package does not implement that mapping or any simulation/presentation code.

## Technical and ownership boundary

The source contains ten identity-transform objects sharing one mesh datablock and using ten object-linked materials. Runtime uses the existing approved GLB plus a small material override and palette texture; it contains zero geometry copies. The silhouette, 1.75 m height, origin, neutral pose, topology, UVs and facing remain exact: Blender +Y maps to Godot -Z. The base mesh remains one mesh, one surface, 148 authored vertices / 496 runtime split vertices and 236 triangles.

This is presentation only. The simulation continues to own stable identity, position, facing, action, destination, movement and interpolation. There is no rig, animation, root motion, collision, navigation, AI or selection behaviour.

## Exclusions

No geometry, accessories, hats, bags, detailed faces, logos, text, emissive/neon materials, stereotypes or role-signalling were added. Skin and hair treatment is consistent across all ten. Only top, trouser, shoe and chest-accent palette cells vary. Review labels and the simple farm context are not runtime assets.

## Verification

Blender 4.1.1 reopened the colourway source successfully: ten objects share exactly one 148-vertex / 236-triangle mesh, all transforms are identity, bounds match the approved asset within 0.00001 m, ten palette materials are present, and there is no armature or action data. Godot 4.7.2's standard editor pipeline imported the approved base GLB and all ten PNG/TRES pairs; ten fresh instances accepted the overrides with no missing resources, one mesh, one surface, 236 triangles, and the expected Godot AABB. Host certificate-store, user-log and editor-settings warnings did not prevent validation.

Approved base hashes were identical before and after authoring:

- Source SHA-256: `A7087CF5F7EF644A3117E9B841FD90BD1C802771412E0EB56A49E3A76B67DCE2`
- Runtime SHA-256: `D16EC421374D79DB0C3F2927C9C7A4A2854D3E3CD436A43CFA79A5766970DE2B`

All four final renders were visually inspected for label legibility, restrained palette coherence, non-neon appearance, silhouette preservation and gameplay-distance tracking. The grayscale sheet deliberately shows that several pairs rely partly on hue; top/trouser boundaries remain readable, while the colour render is the primary tracking reference.

## Provenance

Derived solely from the approved project-authored `lwf_generic_attendee_v1` mesh and packed palette. No downloaded mesh, texture, logo, third-party design or new geometry is used. The approved base source, runtime asset and M0.07 review package were not modified.
