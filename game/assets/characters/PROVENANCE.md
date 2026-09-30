# Runtime character asset provenance

`lwf_generic_attendee_v1.glb` is a byte-for-byte runtime copy of the project-owned approved source at
`assets/runtime/characters/lwf_generic_attendee_v1.glb`. Its review and technical metadata are recorded
under `art/reviews/M0.07` and `assets/source/characters`.

The mesh has no rig, animation or root motion. Godot renders and interpolates it from authoritative
integer-millimetre simulation read state; the asset cannot advance or choose the attendee position.

R0.05q role assets are the approved v3 production handoff derived from the existing
male-v5/female-v3 attendee sources and original acoustic/bass/drum hardware. The 17
runtime GLBs and eight palette PNGs were copied byte-for-byte from the frozen
designer package into this directory. Editable Blender sources, the exact file
hash/node/fit manifest, verification outputs and integration contract are kept
at `assets/source/characters/role-assets-v1/`. The reviewed frozen manifest
SHA-256 is `CF20DA71F92A8C150B64E78241EB2A435FEE2165A59820428C52101FFBABB744`.
Garment accessories are visual only; role choice, kit ownership and facing do
not control authoritative simulation movement.

Attendee v6 guest bodies (`lwf_attendee_<sex>_<state>[_<product>]_v2.glb`, 12 files) and
`attendee_poses_v6_manifest.json` are byte-for-byte copies of the designer package at
`assets/source/characters/attendee-v6-draft/poses/`. Every GLB matches its manifest SHA-256.
Guests, staff and performers all load the v6 manifest.

The 17 `_v2` role GLBs (eight staff overlays, two performer bodies, six band kits and
`lwf_drum_hardware_only_v2.glb`) are byte-for-byte copies of the builds in
`assets/source/characters/role-assets-v2/`. That folder holds the build scripts, the review
`.blend` files and one JSON build report per GLB, including its SHA-256. Staff overlays are the
approved v1 garments refitted to the v6 body. Only the vertex positions change; topology, UVs and
the trim palette are kept. Performer bodies and playing arms are generated from the designer's
v6 build script. Instruments, sticks and drum hardware keep their v1 geometry, moved in toward
the body so the true-length v6 arms reach them. The v1 role and attendee-pose GLBs remain in this
folder but are no longer loaded.
