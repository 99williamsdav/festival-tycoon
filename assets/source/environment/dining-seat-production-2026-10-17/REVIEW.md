# Independent review — PASS

17 October 2026. Fresh independent review of the frozen H1/P1 asset package against ACTIVE-BRIEF.md, WORKING-AGREEMENTS.md and the two actual approved concept PNGs. Approval is limited to these furniture assets and the supplied static seating evidence; it is not gameplay, traversal or animation approval.

## Reviewed checkpoint

- H1 GLB SHA-256: `642d91afe7808986bac05b38476bee786917373618f7cd92c0ec299eac75ed09`.
- P1 GLB SHA-256: `ded217a490181b3fd4e87941b78b0d2ae9d623e346248404d815c1e93872cf9a`.
- Reviewed build.py, source and occupied Blender files, manifests, verification scripts/results, HANDOFF.md, SEATING-AND-AXES.md, gallery source and final rendered evidence.

## Result

No remaining blocking findings for the scoped delivery. H1 retains the golden irregular rectangular bale, two continuous exterior bands and two adult places. Its economical faceting is coarser than the illustration but retains the approved identity. P1 retains honey timber, attached benches, dark A-frames and four adult places. The documented slight H1 envelope increase is reasonable for its irregular perimeter.

Earlier missing hair textures, interrupted hay bands, regular checkerboard sides, body/furniture crossings, opposed foot overlap and off-ground export pivot were repaired before this checkpoint. Final brown hair, whole shoes, visible bands, rear views and contact-side evidence were visually checked.

## Independent verification

`reviewer_glb_audit.py` directly parsed both GLB JSON and binary position/index accessors. Each has one identity-transform furniture mesh, one embedded palette material, the expected two/four anchors and no skins, animations or cameras. Bounds begin at ground zero. H1 is 566 triangles, approximately 1.2014 x .6000 x .4550 m. P1 is 1196 triangles, 1.9600 x 1.7120 x .7540 m including bolt heads. Seat surface and table slab are .450/.750 m as documented.

`reviewer_blender_audit.py` opened both occupied scenes in a separate serial Blender process. For all six guests, native mesh coordinates and rest-bone head/tail coordinates exactly match their current source assets within 1e-7 m, and both rig and body world scales are (1,1,1). Independently rebuilt evaluated-mesh BVHs found zero body/furniture surface crossings.

Support was checked against real furniture triangles rather than a global pelvis minimum. Each P1 guest has five low buttock vertices with downward bench hits at approximately 1 mm. These span about 20 cm across and 5 cm in depth, situated roughly 3–8 cm inside the inner bench edge. Thus the apparent inner-edge sitting has actual support. Both H1 guests likewise have five low vertices over the broad top at .6–1 mm. Some forward H1 vertices are above the chamfer, which is consistent with edge sitting and does not invalidate the supported rear patch. The final tucked-shin and grounded-foot poses are visually credible for static review.

The builder's explicit, unmasked evaluated body/food/furniture, solid-containment, pairwise guest and clipped-thigh checks were inspected. They report zero crossings/contained vertices, hand/food contact gaps below 1 mm, approximately .140–.144 m thigh clearance and minimum opposed P1 body separation .158 m. These agree with the inspected close and side images. The documented 5–6 mm review-only food correction is transparent and does not modify the production assets.

`reviewer_roundtrip.py` reimported both actual GLBs into Blender and rendered them independently. Both embedded 112 x 8 palettes load and remain packed; material colors and UV mapping survive the roundtrip. See `verification/independent-hay-glb-roundtrip.png` and `verification/independent-picnic-glb-roundtrip.png`. No missing material or actor leaked into either runtime asset.

Gallery evidence includes actual approved concepts, close, rear, top, occupied, occupied rear, contact-side and uncropped gameplay frames. Camera scale matches the repository's minimum18/default62 orthographic settings. At default62 these small props occupy few pixels, honestly shown. Anchors, Blender/Godot conversion and suggested reservation spaces are documented coherently. P1's obstructed end entry and required future leg-swing/mount transition are explicitly acknowledged.

## Limits

These six static poses establish useful asset fit, not a motion envelope or pathfinding contract. Future integration must author and validate mount/dismount, stand-up, collision proxies and changing food/guest poses. The hand-to-mouth pose uses the existing eat sample; separate bite objects are outside this commission. Reviewer scripts and JSON/PNG evidence are included and do not edit production geometry.
