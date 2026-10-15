# Coffee van v2 — canopy removal and forward menu

Non-destructive continuation of the selected v1 Blender asset. V1 and the approved concept are preserved. No game repository edits or integration.

## Changes

Removed the open overhead hatch canopy, sage underside, two gas-strut assemblies and redundant hinges (8 mesh objects). The original service opening, oak header, jambs and counter remain, with no replacement overhang. Moved the complete existing menu assembly 0.30 m outward toward the counter; its face is now approximately Y -1.008 m. Other van geometry and materials are preserved.

## Files and reproduction

- `coffee-van.blend`: editable van and neutral studio only.
- `coffee-van.glb`: asset-only runtime geometry/materials; no person, animations, studio or textures.
- `coffee-van-server-review.blend`: separate review scene containing the actual current game food-server rig and its packed palette texture, posed at the final frame of `server_offer`. The rig is review-only and excluded from the van export. Blender's imported Icosphere bone-display helper is hidden and excluded from geometric fit checks.
- `render-main.png`: actual van render without server.
- `render-main-server.png`: actual server-context render.
- `gameplay-four-angle-server.png`: four actual elevated review renders with server context; individual `game-angle-1.png` through `game-angle-4.png` are included.
- `build.py`: loads preserved sibling `../blender-v1/coffee-van.blend`, applies the exact edits, exports, imports the current server, checks all animation frames, saves the review scene and renders. Run `blender -b --python build.py` with Blender 4.1.
- `verify.py`, `export-verification.json`: GLB reimport and geometry checks.
- `fit-review.json`: per-clip fit checks and measured server/socket bounds.
- `check-machine-station.py`, `machine-reach-review.json`: separate machine-reach investigation; this is not a successful operational pose.

## Actual server and camera evidence

Read-only current source: `C:/Projects/festival-tycoon/game/assets/characters/service-servers-v1/lwf_server_food_male_v1.glb`. SHA256: `142d14414b781dcee00461da4d54cb26e26e314ff90db811bcf41ffaabf81b6d`. Used at original scale 1.0, no resculpting or animation changes. Existing repo `game/CameraRig.cs` uses horizontal radius 72 and elevation 58: 38.853 degrees, reproduced in the four-angle sheet. The sheet is closely framed for inspection, not a screenshot or default gameplay zoom claim.

Server root/feet translation in Blender metres: **(0.86, -0.52, 0.715)**, Z yaw **180 degrees**, facing service-side negative Y. This supersedes the untested v1 suggestion at Y -0.08, which was too far behind the counter for the existing offer motion.

Checked all 170 frames: idle 97, turn 9, pickup 13, offer 13, retract 13, reset 25. Evaluated animated body, apron, hair and hat meshes show no triangle-surface intersections with the van, excluding intentional standing contact with the floor. Lowest foot vertex is Z 0.7163 m, about 1.3 mm above the floor. Maximum hat height is Z 2.4569 m; the central roof is Z 2.76 m, approximately 0.303 m higher. No canopy remains. Header/roof geometry does not intersect the tested rig.

The central aisle between the counter's inner edge (Y -0.89) and back worktop front (Y 0.31) is 1.20 m at this station; v1's 0.84 m figure was a conservative reserved strip. The tested arm reaches the offer socket at **(0.67, -0.96, 1.83)**, over the rear portion of the counter and approximately 0.198 m above its top. The menu occupies the far end and does not intersect or cover the handoff position.

## Important limits

The supplied food-serving clips clear the counter, but are not coffee-machine operating animations. A separate candidate station 0.54 m toward the machine, at (0.32, -0.52, 0.715), puts its controls 0.622 m from the shoulder versus a 0.652 m arm-plus-hand bone length. **That candidate idle pose intersects the grinder and equipment shelf**, so it fails the space check. Machine operation is not validated and would require a different approach/pose or a separately approved interior adjustment. The earlier broad claim of a roomy working interior should not be taken as proof of a complete coffee-making workflow.

Surface intersection tests do not establish solid-volume containment or all intermediate navigation paths. No props, customer handoff, gameplay logic or collision system were tested. The roof/header still hide the server's head at high game angles and the opposite wall hides the server from rear/opposite views. Canopy removal improves counter/menu visibility, but does not make the full interior visible. Tiny menu lettering remains too small to read at ordinary distant game zoom.

## Final asset metrics and provenance

75,234 triangles; 356 mesh objects; 15 PBR materials; zero van textures, skins or animations. GLB reimport passes with zero zero-area triangles, zero nonfinite vertices and no server/studio/canopy nodes. Full bounds including bumpers, mirrors and counter: **4.927 × 2.469 × 2.761 m**. Body shell remains approximately 4.55 × 2.03 × 2.76 m. Tyre tread extends 1 mm below the nominal ground plane as in v1.

Van geometry, lettering and materials are the original procedural v1 work with the two requested edits. No new downloaded assets or generated imagery. Existing server and palette provenance remain with the project; the palette is packed into the review blend only. All supplied PNGs are true Blender renders of the supplied geometry.
