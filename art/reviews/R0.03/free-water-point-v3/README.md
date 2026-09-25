# Simple drinking-water standpipe v3

R0.03 user-approved revision. This is an ordinary galvanized standpipe with exactly one front-facing tap, a small concrete foot and a clearly printed `DRINKING WATER` sign on the pipe. No roof, reservoir, second spigot, counter, tray or basin. The single tap and open front support a one-at-a-time service presentation. The exact version was approved by the user on 25 September 2026; targeted independent review accepted its game integration with no findings.

## Review

- `01-standpipe-close.png`: complete standpipe, sign and concrete foot.
- `02-sign-and-single-tap.png`: straight-on sign and tap inspection.
- `03-default-game-camera-and-scale.png`: 35° downward game-style camera next to the existing 1.75 m attendee. The attendee is review context only, not part of the export.
- `lwf_water_v3_palette.png`: embedded matte palette for the pipe, sign and base.

## Files and size

- Editable Blender source: `assets/source/environment/lwf_free_water_point_v3.blend`.
- Godot-compatible export: `assets/runtime/environment/lwf_free_water_point_v3.glb`.
- Five mesh nodes, 1,980 triangles: concrete foot, one-tap pipe, signboard and two lines of mesh lettering. Lettering accounts for 1,672 triangles. The other three meshes use an embedded 80 × 8 palette; the lettering uses a matte cream material.
- Measured bounds: 1.10 m wide × 0.546 m deep × 1.91 m high, grounded origin under the pipe. Concrete foot is 0.46 × 0.46 m. Tap outlet is approximately 0.81 m high. Front is Blender `-Y` / Godot `+Z`.

This is presentation only. There is no collision, navmesh, queue slot, flowing-water animation, plumbing simulation or service logic. The simulation owns free water service, hydration, queue order, route and operational state.

## Verification and provenance

Blender 4.1.1 reopened the source and reimported the GLB with matching bounds and triangle count. Godot 4.7.2 imported and instantiated the GLB with all five meshes, the palette and both lettering meshes. The three renders were visually checked. Exact counts and bounds are in `technical.json`.

Original project-authored procedural geometry, palette and mesh lettering. No downloaded models or textures. V1 and v2 remain preserved separately. The v3 source, runtime GLB and review package were committed as `d7733c3d675a90d492fc021402dce1c01cd1123b`; the game integration and rendered checks are recorded separately in [R0.03 water-v3 evidence](../../../../reports/evidence/R0.03-water-v3/integration.md). The wide default camera makes mesh lettering/tap small; the in-game floating `DRINKING WATER` cue supplements the approved model sign. Human playtest is still pending.
