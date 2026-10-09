# Modular riser — approved asset-only commit

9 October 2026: user approved the model (“Looks good”) and requested “commit now”, relayed by the established coordinator. [Approval and selected C concept](../../assets/source/environment/second-stage-concepts-v1/APPROVAL.md). The Designer's [complete package](../../assets/source/environment/modular-riser-stage-asset-v1/HANDOFF.txt) is preserved with deterministic Blender scripts, editable .blend, exports/palette, anchors, provenance and real model renders.

Reusable assets are added under `game/assets/environment/modular-riser-v1/`: `lwf_modular_riser_stage_v1.glb` (3,396 triangles, four surfaces) and separate optional `lwf_modular_riser_speaker_v1.glb` (308 triangles, one surface). Both use the matte 128×8 palette. They are not placed in the game. Existing trailer/source and all gameplay code remain unchanged by this commit. No second-stage booking/timetable/capacity/tier changes or EXE.

## Verification

- Selected source, scripts, concept and both export hashes match Designer provenance. Runtime SHA-256: stage `3ba3b4fdff84ec5c7cc1109e7aaa808503db3aa1f1b54cba90ff638d0e147759`; speaker `2144d593a2aaa65d1823cbe360113d7cce411dd6ab55d5b5a9915359be63b2d0`.
- Fresh Blender 4.1.1 GLB reimport checks passed: 35 deck raycasts (Y=.899–.900), all five treads match anchors, top landing joins deck, support-to-substrate interface, review band marks on deck and expected counts. [Structural check](../../reports/evidence/modular-riser-stage-v1/structural-check.log).
- Fresh Godot 4.7.2 direct decode and actual game-resource import checks passed: four structure meshes plus one independent speaker mesh, identity mesh transforms, one surface per mesh, palette dimensions and triangle counts, no physics/navigation/animation/audio nodes. [Imported resource report](../../reports/evidence/modular-riser-stage-v1/runtime-resources.json).
- Original trailer runtime GLB and editable Blender source hashes match the pre-copy baseline. [Recorded hashes](../../reports/evidence/modular-riser-stage-v1/original-trailer-hashes.json). Unrelated dirty imports, package lock and other assets are excluded.

## Limits

Inspected actual exported-model close and empty rear renders; preserved access, four-view and trailer/person comparison evidence is Blender/Cycles output, not gameplay screenshots. The .9 m deck and -X side access differ from the trailer's 1.19 m world/grid performance contract. Future integration must align performers, equipment and access explicitly. Native farm lighting, animated routes, audience sightlines and performance have not been measured. No manual playthrough claimed. Godot emitted sandbox log-directory warnings while completing all assertions.
