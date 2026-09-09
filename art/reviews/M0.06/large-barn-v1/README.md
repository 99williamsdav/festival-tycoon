# Large convertible barn v1 — approval review

Completed 9 September 2026. Awaiting separate coordinator review and user approval. This is the second asset only; no farmhouse, later kit asset or integration is included. The approved small storage barn and all review history remain intact.

## Files and appearance

Editable source: `assets/source/environment/lwf_large_barn_v1.blend` (Blender 4.1.1). Runtime: `assets/runtime/environment/lwf_large_barn_v1.glb` (glTF 2.0). The source contains review-only context and an original-scale copy of approved `lwf_barn_v2`; neither is exported.

The major barn carries the approved warm stone, brick plinth, heavy oak and charcoal slate language. Taller walls, a broad opening, support piers and high gable ventilation distinguish it. Three simplified long-span trusses enclose one uninterrupted double-height volume: no second floor, internal columns or finished stage fit-out. It is a permanent building intended to support a future covered-stage refurbishment.

Six PNGs, each 1920×1080 with an orthographic camera 35° downward:

- `01-south.png`, `02-east.png`, `03-north.png`, `04-west.png`: exact cardinal yaw increments, neutral ground, 44 m horizontal framing.
- `05-context.png`: warm grass, simple hedge and track proxies; yaw 35°, 46 m framing.
- `06-scale-comparison.png`: large barn left, approved small barn right, both at authored scale; south view, 51 m framing. The small barn is only a comparison reference.

All six final images visually inspected. Cycles 48 samples, denoising and AgX; review lighting is not a promise of identical Godot lighting. The source opens in the neutral setup; `REVIEW_` objects identify context and comparison assets.

## Dimensions and runtime cost

Nominal wall footprint **20 × 12 m**, eaves **7 m**, ridge cap **11.133 m**. “Double height” means one tall volume, not a literal doubling of every small-barn dimension. Compared with the small barn's 14 × 9 m footprint and 7.1 m ridge, the large barn has about 1.9× the ground area and 1.57× ridge height.

Full Godot X/Y/Z bounds including trim and gutters: **20.929 × 11.133 × 13.413 m**, minimum **(-10.4643, 0, -6.7067)**. Ground-centred origins at (0,0,0); applied location/rotation/scale; Blender metre units, scale 1; glTF Y-up. Front faces Blender −Y / Godot +Z.

Opening is approximately **7.8 m clear width × 5.8 m clear height**, with static sliding leaves parked open. Entrance ground centre is approximately Godot (0,0,5.79), at the front wall. Actual simulation entrance/access reservations must be authored from scenario data.

**7,978 triangles; 2 mesh nodes; 1 material; 2 surfaces; one embedded original 64×64 palette texture.** Nodes: `LWF_LargeBarn_Shell` and `LWF_LargeBarn_Roof`. Roof and gutters are separately hideable; trusses remain with the shell. There are no LODs, rig, animation or collision shapes. Modest geometry is not a verified crowd-performance result.

Material `LWF_Matte_Palette` is nonmetallic, roughness 0.9, double-sided for thin authored faces. UVs sample centres of flat colour swatches. Image is packed in the blend and embedded in the GLB; no external texture file is required. Export only the two large-barn nodes, selection enabled, Y-up, applied modifiers, no Draco.

## Palette and provenance

Original geometry adapted from the approved small-barn construction, with rebuilt stone courses, supports, high vents and trusses. Original flat-colour palette; no downloaded meshes, photographs, copyrighted textures or generated concept-image surfaces. AI-assisted procedural authorship in Blender. Review proxies are original simple geometry, not additional runtime kit assets.

| Use | sRGB colours |
| --- | --- |
| Mortar / stone / quoins | #8F856D, #AA9874, #B4A582, #96886A, #B4A17C |
| Brick | #9B6348 |
| Oak / planks / framing | #795331, #936D42, #503A27 |
| Slate | #47483F, #505047 |
| Sage / iron / moss | #687B59, #39453F, #646744 |

## Verification and limitations

Final Blender reopen passed: two large-barn nodes, zero origins, applied transforms, palette UVs. Blender GLB reimport passed: matching bounds, two mesh nodes and embedded palette. Standard Godot **4.7.2** editor import in an isolated scratch project passed; instantiated-scene inspection confirms bounds, 7,978 triangles, two nodes/two surfaces, one material and active 64×64 albedo texture. Raw numerical authoring results are also in `technical.json`.

The end-view ridge clipping and rear pier/shutter overlap found in the first review were corrected before final handoff. Host certificate-store and user-log warnings appeared during the successful Godot scene check. In-game rendering, picking, roof hiding and crowd performance remain untested. No game code or project configuration was changed.

Recommended selection proxy: box centred (0,5.567,0), size (20.929,11.133,13.413). Initial simulation obstacle can use the nominal 20×12 m footprint with reserved front access. Future interior traversal should use simple wall bounds leaving the entrance gap. Authoritative identity, immovability, footprint and entrance belong to simulation data, not mesh names.

Opaque walls hide the single front entrance from rear/end views; future access overlays or visibility treatment are needed. Hiding the roof alone leaves trusses and walls. Interior and drainage details are simplified; no stage, audience capacity, acoustic treatment, doors animation or working services are specified by this mesh.

Stop at this asset's dual approval gate. Do not begin asset 3 until explicit coordinator and user approval are recorded.
