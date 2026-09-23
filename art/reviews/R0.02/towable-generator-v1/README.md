# Towable generator with integrated breaker panel v1

R0.02's first equipment concept: a small temporary generator that can sit beside the approved trailer stage. Its olive housing, cream tin lid, timber guards, dark wheels and salvaged-looking chassis use the established farm palette. The hitch points along Blender `+X`; the accessible breaker panel faces Blender `-Y` (Godot `+Z`).

## Review this exact version

- `01-normal-three-quarter.png`: full form, visible side panel and emergency cutoff.
- `02-four-states-gameplay-camera.png`: left to right **normal, overloaded, isolated, fault**, rendered from the same 35° downward orthographic camera. The state changes use both shape and colour.
- `03-trailer-stage-context.png`: overloaded version at approximate scale beside the approved open trailer stage. The stage is review context only.
- `04-breaker-cutoff-detail.png`: load gauge, breaker lever and guarded red emergency cutoff. The red cutoff is part of the common base and remains accessible in every state.

The physical differences are a single green top tile and two filled gauge bars in normal; two raised amber flags and a full gauge in overloaded; a folded grey top tile, empty gauge and down lever in isolated; a tall red warning block with exclamation mark, full gauge and tripped lever in fault. These are static presentation states. The red cutoff is deliberately separate from the small breaker handle so the emergency response has a clear target. A named overload alert and precise load/condition values still need UI; this model alone cannot carry the 45-second warning contract.

## Files and technical contract

- Editable source: `assets/source/equipment/lwf_towable_generator_v1.blend`. Five authored meshes: one common base and one overlay for each of four states. Named `REVIEW_` objects and stage context are excluded from runtime exports.
- Godot exports: `assets/runtime/equipment/lwf_towable_generator_{normal,overloaded,isolated,fault}_v1.glb`. Each complete variant contains exactly two mesh nodes (base and state overlay), two surfaces and one embedded 192 × 8 matte palette texture. Variants range from 740 to 824 triangles.
- Full ground footprint including hitch and wheels: about 2.685 × 1.603 m. Top height varies from 1.548 m isolated to 1.840 m fault. Origin at ground level under the housing; transforms applied.
- `technical.json` records triangle counts, bounds, axes, state meanings and verification.

The simulation owns identity, load, condition, warning time, fault progression and interventions. The presentation layer may select the matching static GLB. No light emission, animation, fuel or cable-network behaviour, collision, navigation, gameplay code, sound rig, maintenance worker or exclusion marker is supplied in this approval candidate. The sound rig is a power consumer outside this model.

## Verification and provenance

Blender 4.1.1 reopened the source and reimported all four GLBs; mesh counts, triangle counts, identity transforms, UV/material presence, embedded palette, bounds and ground contact passed. Godot 4.7.2 imported and instantiated all four using its standard editor pipeline; each had two mesh nodes, the expected triangles, grounded AABB and textured material. All four renders were inspected at full size. Visual acceptance remains with the coordinator and user.

Original procedural/project-authored geometry and palette. No downloaded mesh or copied design. The existing trailer stage is used only in a review render; it is not included in the generator exports.
