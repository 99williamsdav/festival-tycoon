# Curry props v1: held tray and litter tray

These are the curry counterparts to the chips and pizza props, from the approved board
(`Documents/Festival Tycoon concepts/curry-van/`): held A and litter A. Each is a straight swap for the chips version,
chosen by the day's food trader.

Rebuild with `blender -b --python build_curry_props.py`. It writes `out/*.glb` and `out/curry_props_report.json`. Then
install:
- `lwf_curry_tray_v1.glb` to `game/assets/props/`, with a copy in `assets/runtime/props/`;
- `lwf_litter_curry_tray_v1.glb` to `game/assets/environment/litter-assets-v1/`.

Run the Godot import afterwards.

## `lwf_curry_tray_v1.glb` (held, 452 tris)

- **Contents:** a black plastic takeaway tray, 19.2 × 13.6 cm including the flange. Rice is on one side; on the other is
  an orange-brown curry with chunks and coriander. A naan lies across the rice.
- **Origin:** the grip centre, as `lwf_chips_tray_v1` and `lwf_pizza_plate_v1`. The tray's underside is at −0.025
  (Blender z; Godot y), so it sits on the `tray` anchor or on `LWF_RightHand_Food` with no pose change.
- **Base slab:** the base is a closed 9 mm slab, −0.025 to −0.016, as the pizza plate's. It keeps the rig's palm-up
  fingers (up to about −0.019 in socket space) inside.
- **Fillings:** the rice and curry are clamped 3 mm inside the tapered walls, so nothing shows through the sides from
  any angle.
- **Materials:** flat named materials, as the other held props: Black tray (double-sided), Rice, Curry sauce, Curry
  chunks, Naan, Naan char, Coriander.

## `lwf_litter_curry_tray_v1.glb` (litter, 266 tris)

- **Contents:** the empty black tray, with a curry smear across the floor, a few rice grains and a naan crust.
- **Origin:** the base centre, resting on z 0, as `lwf_litter_chips_tray_v1`.
- **Material:** one shared material, `LWF_Litter_Shared_Opaque`, on `lwf_litter_palette_v1` UVs. Slots used: 8 tray,
  2 curry smear, 3 rice, 7 naan, 6 naan char. Closest filtering and double-sided, as the other litter.
- **Grip:** the held grip anchor is (0, 0.025, 0) in Godot, as the pizza litter (for the empty tray carried before it's
  dropped).

## Verification

- `held-on-rig-male-socket.png` and `held-on-rig-female-socket.png`: close views centred on `LWF_RightHand_Food`. The
  rows are `idle_food` 1, `walk_food` 11, `eat` 30 and `walk_brisk_food` 5; the columns are four azimuths, the last from
  below. No finger or filling clipping.
- `sockcam.py` makes those views. `handspace.py` and `hand-in-socket-space-*.json` are the rig hand measurements,
  unchanged from `pizza-props-v1`.
