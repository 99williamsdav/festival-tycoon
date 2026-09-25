# Open-sided security/steward post v1 — R0.04 review candidate

One asset prototype derived from the approved `r004-security-post-concept-v1` direction. A small timber platform and four open supports hold a shallow dark-teal lean-to roof. A waist-height timber counter and tiny hand radio signal a friendly steward base; an amber geometric shield pennant makes it identifiable at game distance. The front has one simple path tile and an unobstructed public approach to the counter. There is no enclosure, gate, weapon, branded uniform, police crest or surveillance tower.

This exact model was visually approved by the coordinator and user on 25 September 2026. Technical import alone would not have established that approval. Gameplay integration remains separate, and work stops before the security worker accessory or abstract incident-state cues.

## Review this exact version

- `01-front-game-angle.png`: complete post from the 35° downward game-style view.
- `02-open-counter-detail.png`: low front angle to inspect the counter, radio and open sides.
- `03-default-zoom-and-attendee.png`: normal game-camera scale next to the existing 1.75 m attendee; the attendee is review-only context, not part of the GLB.
- `04-side-open-sightline.png`: open-side view and legible entrance pad.
- `lwf_security_post_v1_palette.png`: embedded 96 × 8 matte palette.

## Files, size and use

- Editable Blender source: `assets/source/environment/lwf_security_post_v1.blend`.
- Godot-compatible GLB: `assets/runtime/environment/lwf_security_post_v1.glb`.
- Five separable mesh nodes: platform/approach tile, timber frame, teal roof, open counter, pennant/radio. 276 triangles total.
- Measured visual bounds including path tile and flag: 2.38 m wide × 2.33 m deep × 3.16 m high. The timber platform is 2.20 × 1.82 m; roof is 2.38 × 1.88 m. Grounded origin at platform centre. Customer front is Blender `-Y` / Godot `+Z`.

The approach tile is a visual hint only. The model includes no collision, navmesh, cover/line-of-sight logic, staffing slot, incident response, dispatch, gate or area closure mechanics. Simulation and UI own those states, including backlog and travel time. The counter is not a barrier across a route.

## Verification and provenance

Blender 4.1.1 reopened the source and reimported the GLB with matching bounds, five valid mesh nodes, 276 triangles, one UV/material per mesh and a grounded origin. Godot 4.7.2 imported and instantiated the GLB in an isolated project with the expected counts and embedded palette. Local certificate-store and user-directory warnings were non-blocking. All four renders were visually inspected. Exact counts and bounds are in `technical.json`.

Original project-authored procedural geometry and palette, based on the approved in-project vector concept. No downloaded model or texture. The attendee in the scale render is existing review context only. Exact approved source/export hashes are recorded in `../ASSET_APPROVALS.md`.
