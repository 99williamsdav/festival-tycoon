# Painted WATER standpipe sign v4 — review candidate

This is a sign revision of the approved single-tap v3 standpipe. The simple pipe, tap, concrete foot and overall bounds are unchanged. The raised `DRINKING WATER` letter meshes are removed. `WATER` is instead hand-lettered into a matte blue sign texture mapped flush to the board's front face, so the word cannot float in front of or disappear behind the sign. The blue board remains readable from the normal angled, zoomed-out game camera. This exact v4 asset was approved by the producer and user on 25 September 2026. Gameplay integration is a separate step.

## Review this exact version

- `01-painted-water-sign-close.png`: complete one-tap standpipe with the painted sign.
- `02-painted-face-and-tap.png`: straight-on sign and tap.
- `03-default-angle-and-zoom.png`: 35° downward game-style view beside the approved 1.75 m attendee, which is review context only.
- `water-painted-sign.svg`: original editable vector lettering/layout used to generate the texture.
- `water-painted-sign.png`: 1024 × 384 embedded sign-face texture.

## Source, export and dimensions

- Blender source: `assets/source/environment/lwf_free_water_point_v4.blend`.
- Godot-compatible GLB: `assets/runtime/environment/lwf_free_water_point_v4.glb`.
- Three mesh nodes, 308 triangles, including exactly one front board face with the painted texture. The other surfaces use the retained matte palette.
- Measured bounds: 1.10 m wide × 0.546 m deep × 1.91 m high. Concrete foot 0.46 × 0.46 m. Tap outlet about 0.81 m high. Front is Blender `-Y` / Godot `+Z`.

Presentation only: no collision, navmesh, queue slot, flow animation, plumbing simulation or service logic. The simulation owns the one-at-a-time free-water service, hydration, routes and operational state.

## Verification and provenance

Blender 4.1.1 reopened the source and reimported the GLB with matching bounds and triangle count. Godot 4.7.2 imported and instantiated all three mesh nodes and the embedded painted-sign texture. The close and game-camera renders were visually inspected. See `technical.json` for counts and bounds.

The approved v3 geometry was retained; the new sign art is original project-authored vector brush lettering rasterized for the GLB. The user's watermarked photograph was visual reference only and was not used as a texture or copied. V3 and the untracked first-aid draft remain untouched. Visual approval applies to this exact source/export pair, with hashes recorded in `../ASSET_APPROVALS.md`; it does not mean the asset is integrated into gameplay.
