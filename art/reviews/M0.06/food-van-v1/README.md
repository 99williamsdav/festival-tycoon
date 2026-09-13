# Generic towable food van v1 — future-use art asset

Approved by coordinator and user on 13 September 2026. Authored after the M0.06 checkpoint assets; it is explicitly not part of M0.06 game integration. Technical verification completed that date.

Future direction only, not an approved or required task: a later replacement or upgrade may explore a cuter rounded retro catering-trailer silhouette inspired by compact polished-metal food trailers, while retaining generic branding and the modular hatch/fascia concepts.

## Scope and deliverables

Editable source: `assets/source/environment/lwf_food_van_v1.blend`. Runtime delivery is split into three independently reusable GLBs:

- `lwf_food_van_chassis_v1.glb`: trailer body, chassis, tow frame, wheels, four stabilisers, service aperture/counter and rear staff door; 236 vertices, 376 triangles.
- `lwf_food_van_serving_flap_v1.glb`: separately pivoted serving hatch/flap; 16 vertices, 24 triangles.
- `lwf_food_van_fascia_v1.glb`: blank replaceable header panel; 16 vertices, 24 triangles.

Total unique geometry is 424 triangles. Each component contains one mesh, one surface, one opaque matte material and one embedded 96 × 8 palette texture. The neutral cream, muted green, timber and dark-metal palette and all geometry are original and procedurally authored in Blender for this project; no downloaded mesh or photographic texture is used.

The asset is a generic practical towable catering shell, not a permanently branded cuisine. The silhouette deliberately shows an A-frame tow bar/coupler, one axle with two visible wheels, four ground-contact stabilising legs, large public serving opening and counter, and a distinct staff-access door and step on the rear face. No decorative props, text, logos, menu content, internal kitchen equipment, staff, food, smoke, particles, lights, animation, collision or navigation are included.

## Dimensions, assembly and pivots

Units are metres. Blender is Z-up; Godot imports Y-up. All three source/runtime component objects have identity position, rotation and scale.

The chassis uses a ground-level origin beneath the front/body centreline. Its core body is approximately 4.2 m long × 2.2 m wide × 2.75 m high. Including coupling and rear step, measured bounds are Blender X -1.26 to 4.59 m, Y -1.50 to 1.28 m and Z 0 to 2.75 m: 5.85 m overall length.

Place the serving flap origin at Blender `(2.15, -1.17, 2.25)` relative to the chassis. Its local origin is the top-centre hinge and local X is the rotation axis. The mesh extends from local Z -1.09 to 0, verifying that the pivot lies exactly on its top edge. Rotation 0° is closed; the review/default presentation uses -105° in Blender, mapping naturally to the corresponding Godot hinge transform. No open/closed state or authored animation is embedded.

Place the fascia origin at Blender `(2.15, -1.18, 2.49)`. It is centred around its local origin, 2.75 m wide × 0.10 m deep × 0.40 m high, and is intentionally blank. The runtime can hide, replace or recolour this component without duplicating the chassis mesh.

## Ownership boundary

The meshes are visual/pickable presentation only. Simulation/data owns vendor identity, cuisine/menu, capacity, stock, pricing, queue/service anchor, condition, interaction, open/closed state and hatch rotation. The model contains no product text, brand, cuisine name or gameplay metadata that would make variants authoritative art assets.

## Review renders

The complete review set contains four 1920 × 1080 orthographic views:

- `01-service-side-open.png`: service side with the flap opened -105° and blank fascia fitted.
- `02-rear-staff-access.png`: separate staff door, handle and step.
- `03-closed-hatch-modular.png`: closed flap with fascia lifted above its mount to make separability readable.
- `04-human-approved-scale.png`: 1.75 m human proxy and approved generic service point for scale/style comparison.

Only the van’s three components are exported. Ground, human and approved service-point geometry are review context and excluded from the GLBs.

## Verification — 13 September 2026

Blender 4.1.1 reopened the final source successfully. All three originals passed identity-transform, one-UV-layer, one-material, exact triangle-count and metadata-bounds checks within 0.00001 m. The chassis test confirmed ground contact at Z=0 and measured 5.85 m overall length. The serving-flap test confirmed symmetric width around local X=0 and its top edge exactly at local Z=0. The fascia test confirmed centring around its local X/Z origin. Every GLB was then imported independently into an empty Blender scene and passed one-mesh, identity-transform, triangle-count, one-material, embedded 96 × 8 palette and exact bounds checks. Outputs: `FOOD_VAN_SOURCE_PIVOTS_DIMENSIONS_PASS`, three `FOOD_VAN_GLB_REIMPORT_PASS` records and `FOOD_VAN_ALL_3_GLB_REIMPORT_PASS`; exit 0.

Godot 4.7.2 stable Mono (official ed1daf0bf) completed standard headless editor import for all three GLBs in an isolated project. Every scene instantiated into the scene tree and passed identity global transform, one MeshInstance3D, one surface, exact triangle count, embedded 96 × 8 albedo texture and axis-converted AABB checks within 0.00001 m. Outputs: three `FOOD_VAN_GODOT_PASS` records and `FOOD_VAN_ALL_3_GODOT_EDITOR_INSTANTIATION_PASS`; exit 0 with no errors.

Full-precision component bounds and assembly placements are in `technical.json`. Local reproducibility tools are `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/verify_food_van.py` and `C:/Users/99wil/Documents/ChatGPT/Festival Tycoon/food-check/check.gd`; these scratch tools are not repository deliverables or integration.
