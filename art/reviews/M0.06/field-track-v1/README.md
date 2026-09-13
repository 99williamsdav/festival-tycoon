# Prototype field and inherited vehicle-track treatment v1 — M0.06 asset 8

Approved by coordinator and user on 13 September 2026. Technical verification completed that date. This is the final M0.06 art checkpoint; no game integration is included.

## Deliverables

Editable source: `assets/source/environment/lwf_field_track_v1.blend`. Two independently reusable runtime GLBs provide the smallest set needed for the farm blockout:

- `lwf_field_grass_tile_8m_v1.glb`: 8 × 8 m grass tile, 25 vertices and 32 triangles.
- `lwf_vehicle_track_straight_8x4m_v1.glb`: 8 × 4 m inherited vehicle-track straight, 81 vertices and 128 triangles.

Both components contain one mesh, one surface, one opaque matte palette material and one embedded 96 × 8 texture. Total unique geometry is 160 triangles. The palette and geometry were authored procedurally in Blender for this project; there are no downloaded meshes or photographic textures.

A separate turn piece is not required for the current gate-to-farm blockout: the 8 m straight rotates freely in 90° increments on the 0.5 m grid and chains end-to-end. A future route needing a curved turn can add one without changing these approved interfaces. Future woodchip, timber and metal trackway upgrades are out of scope.

## Field treatment

The grass tile uses broad low-poly tonal facets and restrained height variation up to 0.046 m. Its six grass colours are a compressed family of muted British summer greens, avoiding strong light/dark jumps so buildings, agents and selection UI remain dominant. Every vertex on all four boundary loops is exactly Z=0, with boundaries at X/Y 0 and 8 m. Adjacent copies therefore share exact positions without cracks; rotations retain the same boundary. The calm coarse variation is intended to sit below buildings and agents, not act as a detailed terrain shader.

Grass is ordinary walkable field presentation. It does not encode paths, traversal, collision or terrain state. The four review-only swatches establish a compact colour reference for healthy grass, lightly worn/dry grass, exposed dirt and wet mud. They are not runtime modules, a blend system or gameplay states.

## Vehicle track treatment

The inherited compacted-earth track runs along local +X from 0 to 8 m, with a nominal 4 m width and low profile from Z 0.016 to 0.025 m. The start and end cross-sections are identical at Y ±2 m, so repeated pieces meet exactly. Nine longitudinal cross-sections ease gradually from full-width endpoints to a maximum 0.07 m interior inset, removing sharp tab/notch transitions while remaining within the 4 m envelope. Slightly lowered/darker bands suggest twin-wheel wear without deep ruts or permanent mud.

For the approved gate context, rotate the piece 90° so travel runs perpendicular to the gate leaf and centre it in the 4 m opening. Review copies are raised only 0.002 m above grass to prevent coplanar rendering; runtime assembly should apply an equivalent controlled layer offset or omit the covered grass surface. The separate surfaces do not occupy the same plane, avoiding z-fighting.

No flowers, crops, tyre props, puddles, deep ruts, litter, fences, extra vegetation, terrain shader or landscape sculpt are included.

## Ownership boundary

The runtime meshes are visual surface presentation only. The future simulation owns per-cell base surface, wear, moisture, drainage, slope, contamination, hardening, desire-path emergence and mud. It also owns collision, navigation and traversal. Nothing in these GLBs creates player-painted footpaths or authoritative terrain state.

Approved gate, hedge and barn assets appear only as review context and are excluded from the two field/track GLBs. No earlier asset was altered.

## Review renders

The complete review set contains four 1920 × 1080 orthographic views:

- `01-module-state-overview.png`: both runtime modules and four presentation-only state swatches.
- `02-tiled-seam-check.png`: four rotated grass tiles and two chained track pieces.
- `03-gate-connected-track.png`: vehicle track centred through the approved gate/hedge opening.
- `04-approved-farm-context.png`: the treatment beneath approved small and large barns and gate context.

## Verification — 13 September 2026

Blender 4.1.1 reopened the source successfully. Both original component objects passed identity-transform, one-UV-layer, one-material, exact triangle-count and metadata-bounds checks within 0.00001 m. The grass test checked that every boundary-loop vertex is exactly Z=0 at X/Y 0 or 8 m. The track test checked identical sorted start/end cross-sections and exact endpoint width Y -2 to 2 m. Each GLB was independently imported into an empty Blender scene and passed one-mesh, identity-transform, triangle, material, embedded 96 × 8 palette and bounds checks. Outputs: `FIELD_TRACK_SOURCE_DIMENSIONS_SEAMS_PASS`, two `FIELD_TRACK_GLB_REIMPORT_PASS` records and `FIELD_TRACK_ALL_2_GLB_REIMPORT_PASS`; exit 0.

Godot 4.7.2 stable Mono (official ed1daf0bf) completed standard headless editor import for both GLBs in an isolated project. Both scenes instantiated into the scene tree and passed identity global transform, one MeshInstance3D, one surface, exact triangle count, embedded 96 × 8 albedo texture and axis-converted AABB comparisons within 0.00001 m. Outputs: two `FIELD_TRACK_GODOT_PASS` records and `FIELD_TRACK_ALL_2_GODOT_EDITOR_INSTANTIATION_PASS`; exit 0 with no errors.

Full-precision dimensions and ownership notes are in `technical.json`. Local reproducibility tools are `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/verify_field_track.py` and `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/field-check/check.gd`; these scratch tools are not game integration or deliverables.
