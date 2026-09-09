# Lower Wittering Farm — barn v1 approval asset

Single aesthetic approval candidate for M0.06, 8 September 2026. Original Blender geometry and solid-colour materials, guided by GAME_DESIGN_SPEC §§8, 16–17 and the left panel of STYLE_STUDY.png. Warm limestone, brick plinth, oak sliding doors, timber gables, slate roof and muted sage shutters. No downloaded assets, external textures or third-party geometry. AI-assisted procedural mesh authorship in Blender; these PNGs are actual model renders, not generated concept imagery.

## Files and review

- Editable source: `assets/source/environment/lwf_barn_v1.blend` (Blender 4.1.1).
- Runtime: `assets/runtime/environment/lwf_barn_v1.glb` (glTF 2.0).
- `01-south.png`, `02-east.png`, `03-north.png`, `04-west.png`: four 1920×1080 orthographic views, 35° downward, exact 90° yaw increments, consistent 29 m horizontal framing. Neutral ground, Cycles 32 samples with denoising, AgX. Review lighting/ground/camera exist only in the blend; they are excluded from GLB.

## Dimensions and integration

Nominal structural footprint 14 × 9 m; wall eaves 4 m; ridge cap reaches 7.10 m. Visible roof overhang, fascia, doors and plinth make the complete exported bounds **14.830 × 7.100 × 9.872 m (Godot X/Y/Z)**. Bounds minimum `(-7.415, 0, -4.926712)`. Do not mistake roof bounds for the simulation footprint.

Both mesh origins are `(0,0,0)`, at ground level at the footprint centre. Blender metres, unit scale 1, applied location/rotation/scale. Blender Z up exports to glTF/Godot Y up. Front is Blender −Y / Godot +Z. Opening centre on the front wall is approximately Godot `(0,0,4.34)`; opening is 5.6 m wide × 3.66 m high before jamb/threshold trim. Nominal door leaves are parked open and static.

Two mesh nodes: `LWF_Barn_Shell` and `LWF_Barn_Roof`. **2,032 triangles, 10 shared runtime materials, 11 material surfaces** (8 shell, 3 roof). Flat face normals; no textures, animation, rig, LOD or collision. Roof node can be hidden/faded by a future presenter. The shell and door components remain editable as disconnected mesh islands in Blender. Export selected two asset meshes only, GLB, Y-up, apply modifiers; no Draco dependency.

Recommended selection proxy: one box centred near Godot `(0,3.55,0)`, size about `(14.83,7.10,9.88)`. Recommended initial obstacle footprint: simulation-owned 14 × 9 m rectangle; reserve the broad front access separately. If interior traversal is later introduced, replace the solid obstacle with simple wall boxes leaving the entry gap. Stable object identity, immovability, authoritative footprint and entrance belong to simulation/scenario data, never mesh names. Storage or covered-stage refurbishment should retain this identity and shell.

## Palette (sRGB)

| Material | Hex |
| --- | --- |
| LWF_Limestone | #A79B7A |
| LWF_LimestoneLight | #B9AD8C |
| LWF_Brick | #9B6348 |
| LWF_Oak | #826440 |
| LWF_OakLight | #96794F |
| LWF_TimberDark | #514A37 |
| LWF_Slate | #626D68 |
| LWF_SlateLight | #707970 |
| LWF_PaintedSage | #687B59 |
| LWF_Iron | #39453F |

All runtime materials use roughness 0.88 and metallic 0. Review ground #D8D3BE is not a runtime material.

## Verification and limitations

Blender background reopen passed: both asset nodes exist, origin zero and unit transforms. GLB reimport into Blender passed, same bounds and ten materials. Godot **4.7.2** headless `GLTFDocument.append_from_file` / `generate_scene` import passed; imported meshes converted to ArrayMesh for inspection: two mesh nodes, 2,032 triangles, ten materials, bounds as above. Godot emitted a host certificate-store warning unrelated to the local GLB. No game scene, game code or simulation data was modified; in-game lighting, picking and crowd performance are not verified by this asset check.

All four final PNGs inspected for full silhouette and framing. Broad entrance is readable from the front; **a single physical entrance cannot remain visible from the three other exact cardinal views through opaque walls**. Meeting all-angle access inspection requires the future entrance overlay or wall/roof visibility treatment. A separate roof alone does not make the far wall transparent. This is a declared acceptance limitation, not a claim that the four-angle doorway requirement is fulfilled.

Interior is only an empty floor and credible wall shell, with no structural rafters or stage/storage fit-out. Door animation, rain drainage details and conversion variants are deferred. Ten materials are acceptable for this isolated candidate but palette sharing and batching need measurement in the eventual crowd benchmark. This is an approval study, not accepted final production art.

Approval needed: whether this shape, material palette and restrained detail level fit the intended farm/game vision; whether access overlays are the agreed solution to rear-view entrance readability. Stop here until feedback; no remaining farm kit or Builder implementation is included.
