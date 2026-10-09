# Approved reusable modular riser assets

[User authorization](../second-stage-concepts-v1/APPROVAL.md) selects this exact Designer package. Original Blender source/scripts, palette, concept snapshot, anchors and useful actual GLB review renders are preserved. The source/export/script hashes in `PROVENANCE-sha256.json` match the copied files.

Runtime assets live in `game/assets/environment/modular-riser-v1/`: structural stage GLB (3,396 triangles/four surfaces) and independent optional speaker GLB (308 triangles/one surface), with embedded/extracted 128×8 palette and native import records. These are available for reuse; no scene instantiates them. Existing trailer/source hashes are unchanged.

Deck height is 0.9 m, audience forward +Z, origin at ground centre. Side stair and equipment marks are documented in `anchors.json`. Existing trailer performer/access code uses different world/grid anchors and a 1.19 m deck; it cannot be reused unchanged. No nav, collision, functional audio, animation, booking, capacity or tier mechanics are introduced.

Fresh Blender structural checks and Godot import checks passed. The preserved review images are Blender/Cycles renders, not gameplay captures. Native lighting, animated performers, audience sightlines and runtime performance remain future integration checks. See `briefs/results/modular-riser-stage-asset.md` for validation evidence.
