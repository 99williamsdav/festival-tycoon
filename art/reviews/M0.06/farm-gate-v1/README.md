# Inherited farm gate v1 — M0.06 asset 7

Approved by coordinator and user on 13 September 2026. Technical verification completed that date. No asset 8 work or game integration is included.

## Scope and deliverables

`assets/source/environment/lwf_farm_gate_v1.blend` is the editable source. Runtime delivery is deliberately split into two independently placeable GLBs:

- `lwf_farm_gate_posts_v1.glb`: static hinge/latch post assembly, 152 triangles, 96 vertices.
- `lwf_farm_gate_leaf_v1.glb`: moving five-bar leaf, 188 triangles, 120 vertices.

Total unique gate geometry is 340 triangles. Each component has one mesh, one surface, one opaque matte material and one embedded 96 × 8 palette texture. The restrained weathered timber and dark iron palette is original and procedurally authored in Blender for this project; there are no downloaded meshes or photographic textures.

The asset is a recognisably British rural timber field gate: five horizontal rails, four uprights, a diagonal brace, simple hinge pins/straps and a plain latch tongue/keeper. It has no pedestrian gate, fencing extension, signage, branding, padlock detail, cattle grid, traffic-control equipment or decorative clutter.

## Scale, assembly and pivot

Units are metres; Blender is Z-up and Godot imports Y-up. Both component objects have applied identity location, rotation and scale. The posts assembly origin is ground-level at the centre of the approved hedge opening. Its bounds are X -1.99 to 1.99 m, depth 0.385 m and height 1.625 m, keeping the complete static assembly within the 4 m clear gap.

The leaf origin is its local hinge axis at (0,0,0), with the visual geometry extending along local +X. Place it at (-1.65,0,0) relative to the post assembly for the demonstrated closed state; its closed world-space X bounds are -1.68 to 1.77 m, including the latch tongue, and it aligns with the latch keeper. Rotate around local +Z in Blender / +Y in Godot. The review open state uses 72°, demonstrating the pivot without authoring animation or behavioural state. Leaf local bounds are X -0.03 to 3.42 m, depth 0.295383 m and Z 0.25 to 1.33 m.

The approved `lwf_hedge_gate_end_v1` modules are review context only. Their terminal tips remain 4 m apart at X -2 and 2 m; neither approved hedge meshes nor their runtime files were altered. Hedge context, ground, track and the 1.75 m human proxy are excluded from the gate GLBs.

## Ownership boundary

These meshes are visual/pickable presentation only. The game/simulation owns authoritative identity, open/closed/locked state, collision, navigation entitlement, traversal, interaction and leaf rotation. No animation, state machine, collision or navigation data is baked into the art files. This is an inherited supplied entrance/exit, not a purchasable festival turnstile.

## Review renders

The four 1920 × 1080 orthographic renders are the complete review set:

- `01-components-closed.png`: readable closed silhouette and post/leaf relationship.
- `02-open-state.png`: plausible 72° leaf rotation around the hinge axis.
- `03-hedge-gap-context.png`: closed assembly within the approved 4 m hedge gap.
- `04-human-scale.png`: 1.75 m human scale reference in hedge context.

## Verification — 13 September 2026

Blender 4.1.1 reopened the source successfully. Both originals were found with identity transforms, one UV layer and one material. Assertions passed for source triangle counts, ground-level/non-negative geometry, metadata bounds within 0.00001 m, post assembly inside X ±2 m, leaf hinge origin and the placed closed leaf inside the same opening. Each GLB was then imported independently into an empty Blender scene and passed one-mesh, identity-transform, exact triangle-count, one-material, embedded 96 × 8 texture and bounds checks. Outputs: `FARM_GATE_SOURCE_PIVOT_DIMENSIONS_PASS`, two `FARM_GATE_GLB_REIMPORT_PASS` records and `FARM_GATE_ALL_2_GLB_REIMPORT_PASS`; exit 0.

Godot 4.7.2 stable Mono (official ed1daf0bf) completed standard headless editor import for both GLBs in an isolated project. Both scenes then instantiated into the scene tree and passed identity global transform, one MeshInstance3D, one surface, exact triangle count, embedded 96 × 8 albedo texture and axis-converted AABB checks within 0.00001 m. Outputs: two `FARM_GATE_GODOT_PASS` records and `FARM_GATE_ALL_2_GODOT_EDITOR_INSTANTIATION_PASS`; exit 0 with no errors.

Full-precision dimensions and placement data are in `technical.json`. Local reproducibility tools are `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/verify_farm_gate.py` and `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/gate-check/check.gd`; the isolated verification project is not game integration.
