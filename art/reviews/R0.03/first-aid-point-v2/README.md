# Square pop-up first-aid tent v2

Revised R0.03 first-aid visual approval candidate. V1 remains intact for comparison. The user-requested reference informed only the broad silhouette and green canvas colour: this is a square, straight-sided festival pop-up tent with a shallow peaked canopy. A large simple white first-aid cross sits directly on the front roof slope. There is no separate signboard, brand, lettering, vehicle, person or medical-detail clutter. It is basic free coverage, not a hospital or commercial stall.

## Review this exact version

- `01-front-close.png`: whole revised tent and clear entrance/approach.
- `02-front-and-roof-cross.png`: front opening and roof-mounted white cross.
- `03-gameplay-distance-and-scale.png`: v2 beside the approved water point and 1.75 m attendee at true scale from a 35° downward game-style camera.
- `04-v1-v2-shape-comparison.png`: preserved cream A-frame v1 beside the square green pop-up v2, review context only.
- `lwf_first_aid_v2_palette.png`: packed colour palette used by the export.

Water point, attendee and v1 tent appear only in review source/context; none is included in the v2 GLB.

## Files, size and use

- Editable source: `assets/source/environment/lwf_first_aid_point_v2.blend`.
- Godot-compatible export: `assets/runtime/environment/lwf_first_aid_point_v2.glb`.
- Five mesh nodes: pop-up frame/floor, straight green walls, peaked canopy, roof cross and response approach. Total 320 triangles with one embedded 192 × 8 matte palette.
- Measured visual bounds: 3.365 m wide × 4.1225 m deep × 2.8125 m high, including the front approach pad. The square covered tent footprint is about 3.0 × 3.0 m. Origin is grounded at the centre of that footprint.
- Front entrance clear gap is about 1.48 m; visual response pad extends to Blender `Y=-2.44`. Entrance faces Blender `-Y` / Godot `+Z`. The pad is a visual approach cue only, not an authoritative navmesh or treatment reservation.

Simulation owns medic identity, dispatch, route, response time, treatment and capacity. This is presentation only: no medic character, event cue, diagnosis, animation, gameplay integration or service economy is included.

## Verification and provenance

Blender 4.1.1 reopened the source and reimported the GLB: five valid meshes, 320 triangles, one UV/material per mesh, matching bounds, grounded origin and embedded palette. Godot 4.7.2 imported and instantiated the GLB in an isolated project with five mesh nodes and the expected triangle and palette counts. Non-blocking local certificate-store/user-directory warnings occurred. The four review renders were visually inspected. See `technical.json` for measured bounds.

Original project-authored procedural geometry and palette; no photograph pixels, logo, text or other source asset were reused. Coordinator and user approved this exact version on 24 September 2026. Gameplay integration remains separate work.
