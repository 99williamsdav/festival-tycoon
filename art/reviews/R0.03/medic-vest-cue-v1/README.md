# Modular medic vest cue v1

Third R0.03 asset approval candidate: a simple green medical vest with a white cross on front and back, fitted over the existing approved generic attendee. It gives the medic a clear colour/shape cue at gameplay zoom without inventing a new person mesh, rig, animation system, vehicle or equipment. The white-on-green symbol is not the protected red-cross emblem.

## Review this exact version

- `01-front-vest-vs-attendee.png`: same approved attendee body with and without the accessory.
- `02-rear-cue-vs-attendee.png`: the rear marker remains readable when the actor turns.
- `03-gameplay-distance-near-first-aid.png`: one medic among five plain attendees beside the approved v2 first-aid tent at a 35° downward game-style camera.
- `lwf_medic_vest_palette.png`: packed accessory palette.

The attendee and first-aid tent are review-only context. The runtime export contains only the vest and white cross meshes.

## Files and attachment contract

- Editable source: `assets/source/characters/lwf_medic_vest_cue_v1.blend`.
- Godot-compatible accessory: `assets/runtime/characters/lwf_medic_vest_cue_v1.glb`.
- Existing unchanged body: `assets/runtime/characters/lwf_generic_attendee_v1.glb`.
- Accessory has two mesh nodes and 144 triangles, one embedded 96 × 8 matte palette; its visual bounds are approximately 0.50 m wide × 0.503 m deep × 0.535 m high at chest level.
- The accessory and body share a feet-centred origin. Front is Blender `+Y` / Godot `-Z`. Parent the accessory to the existing visual root with identity local transform so it follows the same simulation-owned position and facing. Do not create a second person entity or duplicate the base mesh.

The vest was moved in front of the attendee's existing chest-accent geometry, so its white cross is not occluded. If this cue is later integrated, the current generic staff chest-box cue should be suppressed for the medic to avoid overlapping role markers. No game code is changed in this proposal.

## Verification and provenance

Blender 4.1.1 reopened the source and reimported the GLB: two accessory-only meshes, 144 triangles, one material/UV layer each, embedded palette, matching bounds, no armature or animation. Godot 4.7.2 imported and instantiated the approved attendee and overlay together in an isolated project: one base mesh plus two accessory meshes, expected triangle and palette counts, no skeleton. Non-blocking local certificate-store/user-directory warnings occurred. All three review renders were inspected.

Simulation owns identity, routing, dispatch, response timing and treatment. The cue is presentation-only and makes no clinical claim. Original project-authored geometry and palette; no downloaded model, logo or texture. Coordinator and user approved this exact version on 24 September 2026. Gameplay integration remains separate work.
