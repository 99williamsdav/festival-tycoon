# Free drinking-water refill point v1

First R0.03 asset approval candidate. A small practical water-refill stand, not a sales stall: an open timber frame, low corrugated roof, raised green reservoir, two metal taps and separate blue catch trays. The two pale frontage pads suggest side-by-side fill positions without fencing off an approach or prescribing a queue. There is no branding, menu, price or paid-service sign.

## Review this exact version

- `01-front-close.png`: entire stand and clear front approach.
- `02-twin-taps-detail.png`: two independently legible taps, handles and trays.
- `03-gameplay-distance-and-scale.png`: true-scale stand beside the approved generic service kiosk and 1.75 m attendee, at a 35° downward isometric camera. Kiosk and attendee are review context only.
- `lwf_water_palette.png`: packed palette shared by the exported parts.

## Files, size and use

- Editable source: `assets/source/environment/lwf_free_water_point_v1.blend`.
- Godot-compatible export: `assets/runtime/environment/lwf_free_water_point_v1.glb`.
- Five separable mesh nodes: frame, reservoir, twin taps/trays, roof and approach apron; 668 triangles total, one embedded 192 × 8 matte palette.
- Measured visual bounds: 2.55 m wide × 2.35 m deep × 2.1775 m high, including the 1.08 m-deep front apron. Origin is at ground level under the stand. The counter is about 0.88 m high; tap outlets are about 1.05 m high and 1.0 m apart.
- Customer front is Blender `-Y` / Godot `+Z`; the clear apron extends to Blender `Y=-1.60`. Its two pads are visual hints, not authoritative interaction or reservation slots.

The simulation owns free-water availability, need relief, identity, queues, routes, operational state and safe approach. No collision/navmesh, service logic, pricing, audio or animation is included. This is a visual prototype, not a potable-water engineering specification.

## Verification and provenance

Blender 4.1.1 reopened the source and reimported the GLB: five meshes, 668 triangles, single UV/material per mesh, matching bounds and grounded origin. Godot 4.7.2 imported and instantiated the GLB in an isolated project with five mesh nodes, expected triangle count and embedded palette. Local certificate-store/user-directory warnings did not prevent import. All three final renders were inspected. Measurements and part counts are in `technical.json`.

Original project-authored procedural geometry and palette. No downloaded models or textures. The approved kiosk and attendee appear in the review source and context render only, not in the export. Coordinator and user visual approval are both outstanding. Do not integrate, stage or commit this candidate yet.
