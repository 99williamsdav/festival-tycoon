# Farmhouse / office v1 — approval review

9 September 2026. Asset 3 only, awaiting separate coordinator review and user approval. Both barns are preserved. The user's non-blocking request for more rustic character on the approved large barn informs this farmhouse: less uniform warm stone, deep window reveals, aged timber, asymmetric openings and unequal chimneys.

## Deliverables and review views

- `assets/source/environment/lwf_farmhouse_v1.blend`: editable Blender 4.1.1 source with packed palette and isolated review setup.
- `assets/runtime/environment/lwf_farmhouse_v1.glb`: self-contained glTF 2.0, embedded 64×64 palette image.
- `01-south.png`, `02-east.png`, `03-north.png`, `04-west.png`: 1920×1080, orthographic 35° downward, exact cardinal yaws, 30 m horizontal framing.
- `05-context.png`: same resolution/angle, yaw 35°, 34 m framing, simple countryside review proxies.
- `06-scale-comparison.png`: same resolution/angle, 70 m framing. Left: approved large barn; centre: farmhouse; right: approved small storage barn. All at original scale.
- `technical.json`: numerical mesh inventory and palette.

All six final PNGs inspected for clipping, material appearance, entrance orientation and relative scale. Source opens in the neutral review setup. `REVIEW_` context and comparison objects are excluded from the GLB, and are not new kit assets. Cycles 48 samples, denoising, AgX and fixed daylight lighting; actual game lighting remains to be tested.

## Design and dimensions

Modest two-storey exterior on a **12×9 m structural footprint**, wall eaves **5.8 m**, ridge approximately **8.29 m**, tallest chimney pot **9.911 m**. Main door is offset to the right on the south/front face, marked by a shallow slate porch, stone steps, sage timber and a blank office plaque. A rear service door and coal hatch give the back a practical role. A fixed timber bench is attached to the front wall. Window openings have actual 0.45 m wall depth, with frames/glass recessed approximately 0.2–0.3 m.

Full Godot X/Y/Z AABB including porch, steps and chimneys: **12.64×9.931×10.29 m**, minimum **(-6.32,-0.02,-4.87)**. The shallow sill geometry extends 2 cm below ground; the pivot remains at ground (0,0,0). Front is Blender −Y / Godot +Z. Principal doorway centre is approximately Godot **(0.65,0,4.5)**; nominal door opening 1.28×2.3 m before frames. Steps/porch extend to +Z 5.42 m. This is visual guidance, not authoritative entrance data.

## Technical envelope

**13,002 triangles; 2 mesh nodes; 1 material; 2 surfaces; one embedded 64×64 palette texture.** `LWF_Farmhouse_Shell` contains walls, deep openings, details, porch and steps. `LWF_Farmhouse_Roof` contains the main slate roof, gutters and chimneys for future hiding/fading. No collision, animation, rig or LOD. More geometry than the barns is used for the many real window openings and stone faces; crowd performance has not been benchmarked.

Both node origins are at the structural footprint centre at ground level; location/rotation applied, unit scale. Blender metric scale 1; export selected asset nodes only, GLB, Y-up, modifiers applied, no Draco. Material `LWF_Farmhouse_MattePalette` is roughness 0.92, metallic 0. Flat-colour swatches are sampled at UV centres, preserving colour in Godot without engine-specific shader changes. Keep the palette UV layer when editing. All required imagery is packed/embedded.

Suggested selection proxy: box size (12.64,9.94,10.29), centred around (0,4.95,0.275). Suggested simulation obstacle: main 12×9 m rectangle plus entrance/step reservation if required by movement rules. Permanent identity, placement, footprint, access and office function are simulation-owned; mesh names carry no gameplay identity or metadata.

## Palette and provenance

Original authored Blender geometry and original palette. No downloaded meshes, photographs, external textures or copyrighted artwork. Palette PNG is only a grid of solid colours; stone/slate detail is geometric. AI-assisted procedural mesh authorship. Approved barn copies are used only for comparison.

| Use | sRGB colours |
| --- | --- |
| Mortar / warm local stone / weathered stone | #8D8067, #B09A75, #BEAB87, #9F8C6B, #766A54 |
| Brick and chimney pots | #9B6348 |
| Aged oak / dark timber / bench | #795331, #503A27, #96744D |
| Slate | #47483F, #505047 |
| Sage door / iron / old cream frames / glass / moss | #687B59, #39453F, #D2C5A4, #4C5C59, #646744 |

## Verification and known limits

Blender source reopen passed: two asset nodes, applied transforms, one UV layer each. GLB reimport passed with matching bounds, materials and 64×64 palette. Standard Godot **4.7.2** isolated editor import and instantiated-scene inspection passed: 13,002 triangles, two nodes/surfaces, one material, active palette texture and AABB above. The first sandboxed editor run could not save global editor preferences; the authorized rerun succeeded. Host log/certificate warnings during the scene check did not prevent verification.

Exterior-only model: no furnished office, internal rooms, stairs or upper floor slab. Windows use opaque stylized glass. Stone relief and corner shadows are intentionally strong for game-camera readability; no photoreal weather maps. The plaque is blank, with future labels/localization owned by presentation. Principal entrance is hidden from opposite angles by opaque walls; access overlays remain an integration concern. Roof hiding, picking, gameplay navigation, night/weather and crowd performance are unverified. No game code changed.

Stop here for coordinator and user approval. Trailer stage and later assets remain Not started.
