# Detachable amber steward yoke v1 — R0.04 review candidate

One exact accessory prototype based on the user-approved amber/teal shoulder-yoke concept. It sits on the existing 1.75 m generic attendee without replacing or duplicating the person. Ochre shoulder caps, short open-front/rear strips and narrow upper-arm bands give a recognisable festival-steward silhouette from front, rear and the 35° downward game camera. A little dark-teal edge echoes the approved open-sided security post. No police insignia, badge, weapon, tactical kit or militarised equipment.

This exact model was visually approved by the coordinator and user on 25 September 2026. Technical import alone would not have established that approval. Gameplay integration remains separate, and work stops before incident-marker assets.

## Review

- `01-front-vs-plain.png`: same approved attendee body with and without the accessory.
- `02-rear-vs-plain.png`: rear band and arm flashes on the same body.
- `03-default-zoom-role-comparison.png`: one steward among plain attendees and the approved green/white-cross medic cue at normal game-camera scale.
- `04-shoulder-and-arm-detail.png`: placement and thickness of the detachable parts.
- `lwf_steward_yoke_v1_palette.png`: packed matte accessory palette.

The current generic attendee has its own amber chest accent, visible in the open centre of the steward yoke. It is part of the unchanged base visual, not the accessory. The user approved colour-matching or hiding that generic chest cue for stewards during the separate gameplay-integration step, so different clothing colours remain visible. The review renders deliberately show the unmodified body and this limitation rather than hiding it.

## Source, export and attachment

- Editable Blender source: `assets/source/characters/lwf_steward_yoke_cue_v1.blend`.
- Godot-compatible accessory-only GLB: `assets/runtime/characters/lwf_steward_yoke_cue_v1.glb`.
- Three mesh nodes (amber yoke, upper-arm bands, teal edge), 240 triangles, one embedded 64 × 8 matte palette. No base human mesh in the export.
- Measured accessory bounds: 0.766 m wide × 0.449 m deep × 0.358 m high, positioned from 1.0395 to 1.397 m above the shared feet-centred origin.
- Front is Blender `+Y` / Godot `-Z`. Parent the accessory to the existing attendee visual root at identity local transform. It follows the same simulation-owned person; it does not create another identity, rig or animation state.

The cue is presentation only. Staffing, security coverage, dispatch, route and incidents belong to simulation and UI. The current attendee is unrigged and does not yet support hair/face swapping; this accessory leaves those regions clear but does not claim the feature exists.

## Verification and provenance

Blender 4.1.1 reopened the source and reimported the GLB with matching bounds and counts. Godot 4.7.2 imported and instantiated the unchanged attendee plus accessory with one body mesh and three accessory meshes, the expected 240 accessory triangles and embedded palette. Local certificate-store and user-directory warnings were non-blocking. All four review renders were visually inspected. Exact numbers are in `technical.json`.

Original project-authored procedural geometry and palette based on the approved concept. The attendee and medic visible in review renders are existing context only. No downloaded model, logo or texture. Exact approved source/export hashes are recorded in `../ASSET_APPROVALS.md`.
