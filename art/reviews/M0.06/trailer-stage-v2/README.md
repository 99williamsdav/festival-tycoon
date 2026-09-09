# Inherited trailer stage v2

New approval candidate: awaiting coordinator review and explicit user approval. V1 source, runtime and review files are preserved unchanged as history. The user did not approve v1 because the canopy obscured the performance area.

V2 removes all canopy fabric, posts, beams, braces and suspended lamps. The exposed timber performance deck is visible from the four game-camera orientations. No replacement rain cover is designed; that decision is deferred. The green farm chassis, tandem wheels, towing eye, supports, corner stairs, salvaged rails and small deck-mounted speakers retain the inexpensive agricultural conversion character.

## Deliverables and review

- `assets/source/environment/lwf_trailer_stage_v2.blend`
- `assets/runtime/environment/lwf_trailer_stage_v2.glb`
- Four 1920 x 1080 cardinal renders: 01 south/audience, 02 east/hitch, 03 north/rear, 04 west.
- 05 countryside context and 06 true-scale comparison with approved small barn and a 1.75m human proxy standing on the audience side.
- All views use orthographic projection at 35 degrees downward. The source contains named REVIEW camera, lighting and context objects, excluded from runtime export.

## Modularity and runtime contract

The original conceptual modules were base trailer, canopy, access, rails and fittings. V2 has exactly four runtime mesh nodes and does not depend on any hidden canopy object:

| Node | Contents | Triangles |
| --- | --- | --- |
| LWF_TrailerStage_Base | Chassis, timber deck, wheels, axles, hitch, stabilising feet | 3,068 |
| LWF_TrailerStage_Access | Timber stairs, stringers and handrails | 316 |
| LWF_TrailerStage_Rails | Rear/end salvaged boards and uprights | 180 |
| LWF_TrailerStage_Fittings | Two small freestanding deck-mounted speakers | 288 |

The canopy module exists only in preserved v1 history. V2 source and runtime contain no canopy assembly. Lamps were removed with their mounting structure; no lighting towers or replacement supports were added. These four components remain independently replaceable for later visual states; no state logic or other variant is supplied.

Total: 3,852 triangles, four surfaces, one matte material using one original packed 64 x 64 palette image embedded in the GLB. Metres, glTF Y-up, applied source transforms, ground-centred shared origin. Full bounds approximately 9.905 x 4.915m horizontally and 2.200m high, within the nominal 10 x 5m operational footprint. Deck top 1.1995m. Audience direction Blender -Y / Godot +Z; hitch +X.

Gameplay identity, footprint, audience direction and upgrade state remain simulation-owned. No collisions, navigation, gameplay code, animation or functional audio is included.

## Provenance and validation

Original Blender primitive/custom-polygon geometry and original palette, reused from the authored v1 trailer with the focused removal above. No downloaded assets. The approved barn and human proxy are review-only context.

Blender 4.1.1 source reopen passed: four asset parts, applied transforms, shared UV layer, no canopy object. GLB reimport passed: four meshes and embedded 64 x 64 palette. Standard Godot 4.7.2 editor import and instantiation passed: four mesh nodes, four surfaces, one textured material, 3,852 triangles and expected bounds; no canopy node. Sandbox log/certificate-store warnings did not prevent validation.

All six final renders visually inspected for exposed deck readability, silhouette, framing, audience access, palette and scale. These are Blender review renders; in-game lighting/integration is outside this assignment.

STOP pending separate coordinator and user approval. No later asset, commit or push.
