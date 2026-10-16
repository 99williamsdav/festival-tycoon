# Independent production review — PASS

Reviewed 16 October 2026 by the independent GPT-6 Astra reviewer, separately from the builder, under WORKING-AGREEMENTS.md. Scope is the three approved Blender food-van production assets only. No game integration or playtest approval is implied.

No unresolved material findings remain in the frozen runtime models listed below. The approved PNG concepts were inspected directly alongside final source, rear, reimport, actual-server, cutaway and honest gameplay-scale evidence. The cream/blue rounded chip trailer, timber/terracotta pitched pizza wagon and ribbed plum/mustard curry cab van retain the approved defining shapes and materials. All have genuine interior geometry, an open serving aperture, fixed counter, staff-door treatment and no awning. Review people are excluded from exports.

## Evidence and checks

- Read ACTIVE-BRIEF.md, INTEGRATION-NOTES.md, HANDOFF.md, build.py and verify.py. Independently checked the actual Main.ServiceServers.cs anchor and yaw against the package contract: Godot (0,.47,.88), yaw PI; equivalent Blender assembly anchor (2.15,-.88,.47). No server rescaling or repositioning was used to manufacture clearance.
- Independently parsed the final binary GLBs with Python: valid declared file lengths, one mesh each, four materials, three embedded images with buffer views and no external image URIs, no skins or animations, populated UV accessors, no review cameras/lights/ground/people nodes. Curry retains both named steam-marker nodes at its vent outlets.
- Final reimport renders retain name and pictogram textures. Direct visual inspection of final reimported geometry confirms the repaired floor artifacts are absent. Rear and cutaway images confirm the pizza back-wall overlap is removed.
- Reviewed the actual-source-rig BVH verification implementation and final checks.json. Six rig/van cases each sample 170 integer frames across all six clips: 1,020 total frames, zero body/apron/hair/cap intersections. Full held-prop triangle BVH checks cover the offer and final pickup poses for all three props: zero intersections. Minimum reported plate clearance is .2962m; trays .3100m. This is evidence review, not an independently rerun Blender rig test.
- Production build uses delivered artwork references, creates named editable component meshes in .blend files, and exports a consolidated mesh plus curry markers. Verification rigs and held props remain explicit external read-only dependencies. Successful final serial build/verification logs supersede earlier concurrent Blender crash attempts.
- Gameplay evidence uses the documented 38.853-degree elevation and horizontal orthographic spans 32m/110.222222m at 16:9. Final refreshed frames with actual servers were inspected without enlarging the model. At default zoom, fine detail and faces are naturally tiny or occluded; this is disclosed rather than concealed by the enlarged inspection views.

| Asset | Triangles | Bytes | SHA-256 |
|---|---:|---:|---|
| Chips | 2,370 | 272,372 | e3bfe879f1422e825c10b46c509865c5c3613cdd34f66c7a0522fa3827202f98 |
| Pizza | 2,066 | 311,364 | 540361a894e103d6c7c5cc878b9f47afa2502c17b4c7fac844bb9c674d0b762d |
| Curry | 5,244 | 488,540 | 63f9fd9489441a509dd726ac6e606a14f60f76a5560f92c54948160657f880cf |

## Findings closed during bounded repair

1. Imported artwork disappeared in the first joined GLB roundtrip. UV-layer normalization repairs it; all three final reimports visibly preserve the artwork.
2. The original thick lower front wall intersected the unchanged rig's apron and shoes. A thin inward skin retains the public plane and clears both sexes; final full frame checks contain no intersections.
3. Coplanar chassis/floor tops produced black diagonal artifacts through the hatch. Lowering the chassis top while retaining floor height removes these artifacts.
4. The pizza rear lintel overlapped the full back planks. Restricting the lintel to the serving face removes the visible stippling.
5. The missing broad mustard hatch-side panels and chip blue end wrap were restored before final acceptance.

## Fit and scope limits

Curry extends .21m beyond the old physical hitch-front bound, but its -1.47m nose remains .13m inside the reserved -1.60m front edge. All rear steps reach 4.60m, .01m beyond the old rear physical bound and .30m inside the reservation. All three fit the existing reserved X/Y area. This practical fit does not claim identical old physical bounds.

Staff doors are static visual geometry, not animated or traversable doors. The review does not validate runtime animation crossfades, terrain, customer transfers, queue behaviour, picking, all game orientations or renderer-specific integration. Later integration must replace the complete old assembly/decorations, preserve service anchors, avoid legacy palette substitution and use the named curry steam markers. These limitations are consistent with the production-only commission and do not block this PASS.
