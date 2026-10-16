# Blue tractor B integration

The approved cabbed workhorse package is archived here unchanged, with this integration note added. Its original manifest pins the Blender source, export, scripts and review evidence. Runtime `game/assets/environment/blue-tractor-v1/blue_tractor_B_v1.glb` is byte-identical to the approved export: SHA256 `2C501B42D2B440F8F5623281F4F3426FDD6261E3F1FA04E56DB74A0B557A187D`, 1,113,944 bytes, 14,962 triangles, seven mesh nodes and 34 surfaces.

The static tractor sits at barn-local (0,0,2.1), identity rotation/scale, grounded world (0,0,-20.9), front +Z. Raw world bounds X ±1.127 m, Z −22.760 to −18.710 m. Navigation expands this envelope once by the existing upright actor's 0.38 m body clearance, rasterizes 88 cells and checks continuous movement sweeps. The artist's optional 0.15 m margin is not added again. Both barn doors and remaining floor retain access.

`BarnTractorPresent` is shared by presentation, layout validation, traversal and movement. An active Barn Stage removes tractor mesh and obstruction together, including optional Classic stage selections. The mesh is a sibling of the fading barn shell, preserving opaque body materials and original transparent glass. Existing conversion kit/sign behavior remains conditional on the actual venue state.

Fresh production campaigns explicitly select persisted/hashed barn geometry revision one. Legacy revision-zero saves and immutable historical fixtures retain their original geometry and have no tractor. Loaded or running sessions are never silently migrated; next festival, retry and rebooking retain the originating revision. Start a new current campaign to see the tractor.

Native source/export evidence and independent review are recorded in `briefs/results/tractor-integration-handoff.md` and `reports/evidence/tractor/`. Original package review images are asset renders; only the latter native evidence represents gameplay. Solid-roof/default-zoom occlusion is expected. No driving, animation, new pricing, unlocks or performance changes are included.
