# Lower Wittering Farm barn — v2 approval study

8 September 2026. Single barn revision; v1 is preserved. Original authored geometry and palette, guided by GAME_DESIGN_SPEC §§8, 16–17, STYLE_STUDY.png left panel and user/coordinator feedback. Awaiting aesthetic approval; no other farm kit or game implementation is included.

## Changes from v1

Darker, warmer slate replaces the large blue-grey panels. Ten overlapping courses per slope, staggered slate joints, slight edge variation and segmented ridge caps make the roof read as slate at a high camera angle. Structural proportions remain close to v1 so the comparison does not conceal roof dominance by lowering the camera. Overhang is slightly reduced.

Warm irregular stone courses with mortar gaps replace smooth wall faces. Corner blocks wrap onto the ends. Door bracing and jambs are heavier; gables have broad tie beams and braces. Simple gutters/downpipes and sparse low-level moss supply working-farm character. Interior remains an empty shell suitable for later storage or covered-stage fit-out.

Runtime materials are consolidated from ten materials / eleven surfaces to **one material / two surfaces**, using an original embedded **64×64 palette PNG**. This image contains only flat colour swatches, not photographic or surface-detail textures. All geometry, colours and review proxies are original; no external assets or copyrighted textures. AI-assisted procedural mesh authorship in Blender. Renders are images of the actual model.

## Deliverables

- `assets/source/environment/lwf_barn_v2.blend`: editable Blender 4.1.1 source, packed palette and review setup.
- `assets/runtime/environment/lwf_barn_v2.glb`: self-contained glTF 2.0, embedded palette.
- `01-south.png`, `02-east.png`, `03-north.png`, `04-west.png`: neutral-ground 1920×1080, 35° downward orthographic, exact cardinal yaw increments, 29 m horizontal framing.
- `05-context.png`: 1920×1080, same 35° downward angle, yaw 35°, 34 m horizontal framing; simple warm grass, track and hedge proxies only. These proxies are review-only and excluded from GLB. They are not additional kit assets.

Blender source opens in the neutral setup. Context proxies have `REVIEW_` names and render visibility disabled. To reproduce context, enable their render visibility, assign REVIEW_Grass to REVIEW_Ground, and use the context camera framing above. Review lighting uses Cycles 48 samples, denoising and AgX; in-game lighting will differ.

## Technical envelope

Nominal wall footprint **14 × 9 m**, eaves approximately **4 m**, ridge **7.10 m**. Complete Godot X/Y/Z bounds including trim/gutters: **14.650 × 7.100 × 10.060 m**, minimum **(-7.325, 0, -5.030)**. Bounds differ slightly from v1 due to reduced verge overhang and added gutters; nominal simulation footprint is unchanged.

**5,058 triangles; 2 mesh nodes; 1 material; 2 surfaces; 1 embedded 64×64 texture.** Increased geometry versus v1 is spent on stone courses, slates and corner bevels; reduced material surfaces offset some rendering overhead, but this is not a measured performance claim. No LODs, rig, animation or authored collision.

Nodes `LWF_Barn_Shell` and `LWF_Barn_Roof` both have ground-centre origin (0,0,0), applied transforms and unit scale. Roof and gutters hide together. Blender metres, unit scale 1, Z-up; selected asset meshes exported to GLB with Y-up and modifiers applied, no Draco. Front is Blender −Y / Godot +Z. Palette UVs sample swatch centres; material `LWF_Matte_Palette` is nonmetallic, roughness 0.9 and double-sided for thin slate/stone faces. Keep palette UVs when editing. Mesh islands remain editable; no packed external dependencies are needed.

Broad front opening is approximately **5.45 m clear × 3.3 m high** after heavier jamb/lintel trim; wall aperture is 5.6 m wide. Sliding leaves are static and parked open. Entrance ground centre is approximately Godot (0,0,4.34).

Suggested selection box: centre (0,3.55,0), size (14.65,7.10,10.06). Suggested initial simulation obstacle: nominal 14×9 m rectangle with reserved front access. Future interior navigation should use simple wall boxes leaving the entrance gap. Simulation data owns immovable identity, footprint and entrance; mesh names never supply authoritative identity.

## Palette, sRGB

| Use | Colours |
| --- | --- |
| Mortar / stone / corner stones | #8F856D, #AA9874, #B4A582, #96886A, #B4A17C |
| Brick plinth | #9B6348 |
| Oak / lighter planks / heavy framing | #795331, #936D42, #503A27 |
| Slate / restrained variation | #47483F, #505047 |
| Sage shutters / iron / moss | #687B59, #39453F, #646744 |

## Verification and limitations

Final Blender source reopens with two asset nodes, applied transforms and palette UVs. Blender GLB reimport preserves bounds and the embedded 64×64 palette. Godot 4.7.2 isolated editor import and instantiated-scene inspection verify both mesh nodes, bounds, 5,058 triangles, two surfaces, one material and the active palette texture. All five final PNGs were visually inspected for framing and material appearance.

The direct headless GLTFDocument texture import path crashed on this host; verification therefore uses the successful standard editor import in an isolated scratch project. Certificate-store and user-log-directory warnings also occurred. No game code or project configuration was changed. In-game rendering, selection, roof fading and crowd performance remain untested.

A single doorway is visible from the front and front oblique context view, but opaque walls hide it from the other cardinal views. Access overlays or a wall/roof visibility treatment are still needed for all-angle inspection; separate roof geometry alone does not solve this. Stone/slate sizes are deliberately simplified and somewhat enlarged for readability. No interior rafters, conversion fit-out, moving doors or functional drainage are included.

Approval gate: assess warmer slate, masonry, timber weight and overall rural character in the neutral and grass-context views. Stop for feedback before any remaining kit or Builder work. No commit or push.
