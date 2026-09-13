# Modular hedgerow kit v1 — M0.06 asset 6

Candidate awaiting coordinator and user approval. Technical verification completed 13 September 2026; technical success is not visual approval. No farm gate or game integration is included.

## Deliverables and scope

Editable source: `assets/source/environment/lwf_hedge_kit_v1.blend`. Runtime directory: `assets/runtime/environment/`. Each name below is an individual `.glb`, containing one independently placeable mesh, one surface and one palette material. One organised source and eight separate exports avoid importing an overlapping library scene into the game.

Original low-poly solid foliage, embedded crown clumps and restrained woody stems suggest a dense native British farm hedge. Muted greens and brown twig hints; no leaf cards, transparency, flowers, fruit, nests, litter, animations or seasonal states. The original geometry and generated 16-colour 64 × 64 palette were authored procedurally in Blender for this project; no downloaded mesh or photographic texture is used. AI-generated project mockups are direction references, not runtime assets. Review scenery, labels, human and approved service-point reference are not included in the hedge GLBs.

## Module inventory

Dimensions below are measured bounding-box sizes in metres, rounded to six decimals. Height is Blender Z / Godot Y. Full-precision bounds and the mating polygon are in `technical.json`.

| Runtime basename | Length / X extent | Width / ground depth | Height | Triangles |
| --- | ---: | ---: | ---: | ---: |
| lwf_hedge_straight_2m_v1 | 2 | 1 | 1.727725 | 308 |
| lwf_hedge_straight_4m_a_v1 | 4 | 0.979254 | 1.718786 | 648 |
| lwf_hedge_straight_4m_b_v1 | 4 | 1 | 1.708274 | 648 |
| lwf_hedge_straight_8m_a_v1 | 8 | 1 | 1.759590 | 1,296 |
| lwf_hedge_straight_8m_b_v1 | 8 | 1 | 1.752578 | 1,296 |
| lwf_hedge_corner_90_v1 | 1.47 | 1.47 corner envelope | 1.709991 | 232 |
| lwf_hedge_end_cap_v1 | 0.5 | 0.94 | 1.670000 | 148 |
| lwf_hedge_gate_end_v1 | 1 | 0.977500 | 1.670000 | 148 |

Total unique kit geometry: 4,724 triangles. Every module uses one opaque matte palette material with one embedded 64 × 64 texture. Separate GLB imports may create separate material resources; this does not imply a shared runtime resource or automatic batching.

## Placement and connectors

Metres, 0.5 m placement grid, applied identity object transforms and ground-level start pivots at (0,0,0). Blender uses Z-up; export maps Blender (x,y,z) to Godot (x,z,-y). Straights extend along +X, ending at (L,0,0) in both conventions. Their flat mating planes have precisely matching symmetric 12-vertex cross-sections: maximum width 0.94 m and crown 1.67 m at the connection. Interior crown height varies. Abutting endpoints gives no longitudinal gap or double-thick overlap; closed end faces coincide at the joint. This is not a welded continuous mesh.

The 90° corner starts along +X and ends at Blender (1,1,0), tangent +Y; in Godot this is (1,0,-1), tangent -Z. It is a curved quarter-turn centreline of radius 1 m with grid-aligned endpoints, not a 1.47 m placement step. Rotate the next straight to match that tangent. Corner start/end sections match the straights, including reversed placement due to bilateral symmetry.

End cap and gate end each have only the start connector. Their terminal tips are at +X 0.5 m and 1 m respectively and are not mating connectors. The cap rounds/tapers the termination; the gate end narrows and lowers towards its tip. For a 4 m clear gap, place left gate-end origin at X=-3 pointing +X and right origin at X=3 rotated 180°; their tips are X=-2 and X=2. The future gate is deliberately absent.

Named source collections contain the eight original meshes at local origin, hidden for review presentation. Unhide the relevant original object to edit; `REVIEW_*` copies/context are presentation-only. Do not export those copies. Simulation code owns hedge identity, ownership, removal, collision, navigation and placement rules. None of those systems is implemented here.

## Existing review renders

All seven existing PNGs are 1920 × 1080, orthographic, approximately 35° elevation. They were not changed during this technical handoff. Visual acceptance remains with coordinator and user.

| File | Purpose |
| --- | --- |
| 01-labelled-lineup.png | Labelled eight-module inventory |
| 02-joins-south.png | Joined straights, corner and endpoint layout, south view |
| 03-joins-east.png | Same join layout, east view |
| 04-joins-north.png | Same join layout, north view |
| 05-joins-west.png | Same join layout, west view |
| 06-countryside-gate-gap.png | Boundary returns and 4 m clear future-gate gap |
| 07-human-kiosk-scale.png | 1.75 m human and approved service-point scale reference |

## Verification — 13 September 2026

Blender 4.1.1 reopened the existing source successfully. Automated assertions passed for all eight originals: identity position/rotation/scale, one UV layer, bounding minima/maxima matching `technical.json` within 0.00001 m, identical start sections, identical straight end sections, correctly rotated corner end section, and symmetric mating polygon. Section comparison used five decimal places. All eight GLBs were independently reimported into empty Blender scenes: one mesh, one material, one 64 × 64 texture, matching triangle counts, matching bounds within 0.00001 m, and matching start/end connector sections. Output: `HEDGE_SOURCE_AND_CONNECTIONS_PASS` and `HEDGE_ALL_8_GLB_REIMPORT_PASS`; exit 0.

Godot 4.7.2 stable Mono (official ed1daf0bf) standard headless editor import ran in an isolated scratch project, followed by loading and instantiating every GLB into the scene tree. All eight passed: one MeshInstance3D, one surface, identity global transform, 64 × 64 albedo texture, exact triangle counts from the table, and axis-converted AABB position/size within 0.00001 m of source metadata. Output: eight `HEDGE_GODOT_PASS` records and `HEDGE_ALL_8_GODOT_EDITOR_INSTANTIATION_PASS`; final run exit 0 with no errors.

Initial restricted editor execution reported environment certificate/user-directory/editor-settings access errors; rerunning with normal editor-data access exited cleanly. An initial instantiation harness queried global transforms before tree initialization; the harness was deferred until initialization and rerun cleanly. Neither issue required an asset change.

Local reproducibility tools (outside the repository): `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/verify_hedge_kit.py` and `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/hedge-check/check.gd`. Commands: Blender `-b --python verify_hedge_kit.py`; Godot `--headless --editor --path hedge-check --import`, then `--headless --path hedge-check --script res://check.gd`. The isolated project contains copies of the eight GLBs and `technical.json`, not game integration.

This pass changes only this README and approval-register row 6. Existing source, runtime files, renders, technical metadata and commit history are untouched. Both approvals remain pending; stop before asset 7.
