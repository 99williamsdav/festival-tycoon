# Pizza props v1: held slice and litter plate

These are the pizza counterparts to the chip props, from the approved board
(`Documents/Festival Tycoon concepts/pizza-props/`): held option B and litter option 1. Each is a straight swap for the
chips version, chosen by the day's food trader.

Rebuild with `blender -b --python build_pizza_props.py`. It writes `out/*.glb` and `out/pizza_props_report.json`. Then
install:
- `lwf_pizza_plate_v1.glb` to `game/assets/props/` (copy in `assets/runtime/props/`);
- `lwf_litter_pizza_plate_v1.glb` to `game/assets/environment/litter-assets-v1/`.

Run the Godot import afterwards.

## `lwf_pizza_plate_v1.glb` (held, 256 tris)

- A 15 cm white paper plate with a fluted rim. One big slice sits on it: cheese, four pepperoni, tomato on the cut sides
  and a raised crust. The tip droops over the plate's far edge.
- **Origin:** as `lwf_chips_tray_v1`, the grip centre. The plate's underside is at −0.025 (Blender z; Godot y), so it
  sits on the `tray` anchor or on `LWF_RightHand_Food` with no pose change.
- **Base slab:** the plate's base is a closed 9 mm slab, −0.025 to −0.016. On the rig, the palm-up fingers rise to about
  −0.019 in socket space, so a paper-thin base would let them poke through. The slab keeps them inside.
- **Droop:** the hand sits under the plate's −Y half (Blender; Godot +Z) in every frame of `idle_food`, `walk_food`,
  `walk_brisk_food` and `eat`. In socket space it spans about x −0.02 to 0.08, y −0.10 to 0, z −0.07 to −0.02 (see
  `verification/hand-in-socket-space-*.json`). So the slice's tip droops over the +Y edge (Godot −Z), away from the
  hand, down to z −0.039.
- **Materials:** flat named materials like the held-props trio. Paper plate, Paper plate rim, Plate fluting, Cheese,
  Cheese melt, Tomato, Pepperoni, Crust, Pizza base. The plate materials are double-sided.

## `lwf_litter_pizza_plate_v1.glb` (litter, 158 tris)

- A 19 cm paper plate with one side creased up at 35°, a grease stain, a tomato smear and a leftover crust.
- **Origin:** the base centre, resting on z 0, as `lwf_litter_chips_tray_v1`.
- **Material:** one shared material, `LWF_Litter_Shared_Opaque`, on `lwf_litter_palette_v1` UVs. Slots: 9 paper, 3 rim,
  6 grease, 5 crust, 2 tomato smear. Closest filtering, double-sided, as the other litter.

## Verification

- `held-on-rig-male-socket.png` and `held-on-rig-female-socket.png`: close views centred on `LWF_RightHand_Food`. The
  rows are `idle_food` 1, `walk_food` 11, `eat` 30 and `walk_brisk_food` 5; the columns are four azimuths, the last from
  below. No finger or tip clipping.
- `held-food-clips-sheet.png`: the clip sheet from the rig's `foodrender.py`, with the plate swapped in.
- `handspace.py` and `sockcam.py` produce the measurements and the close views.
