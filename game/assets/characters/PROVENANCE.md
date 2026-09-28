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
