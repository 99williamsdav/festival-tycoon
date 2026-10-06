# Food van signs v1: chips and pizza roof boards

These are big picture signs, so a food van reads at a glance as chips or pizza. Each is a double-sided cut-out board on a
flat iron pole and roof plate.

Rebuild with:
1. `python make_sign_textures.py`
2. `blender -b --python build_signs.py`

Then copy `out/*.glb` and `out/*.png` to `game/assets/environment/` (copies in `assets/runtime/environment/`) and run
the Godot import.

## Files

| File | Tris | Board | Top above roof |
|---|---:|---|---:|
| `lwf_food_van_sign_chips_v1.glb` (node `LWF_FoodVan_Sign_Chips`) | 28 | 1.4 × 1.75 m: a striped paper cone of chips with a wooden fork | 2.05 m |
| `lwf_food_van_sign_pizza_v1.glb` (node `LWF_FoodVan_Sign_Pizza`) | 28 | 1.55 m round: a pepperoni and basil pizza with one slice pulled out | 1.85 m |

- **Pictures:** `lwf_food_van_sign_chips_v1.png` (512 × 640) and `lwf_food_van_sign_pizza_v1.png` (640 × 640) are
  embedded in the GLBs; copies sit beside them.
  - They're drawn as stickers: a cream border and an ink outline around the shape, so it holds against grass, sky and
    roof.
  - The alpha is a hard cut-out (glTF MASK, cutoff 0.5).
  - The pictures use no fonts.
- **Two faces:** each board is two single-sided quads 30 mm apart, facing the van's front (+Z) and back (−Z). The back
  quad's U is mirrored, so the picture reads the right way round from either side.
- **Camera:** the game camera is always diagonal (yaw 45° + 90°k), so the board is never seen edge-on.
- **Pole:** the 70 × 20 mm pole runs between the two faces, from a 0.36 × 0.22 m roof plate up to the board's middle.

## Placement

The origin is the foot of the pole. Add the sign as a child of `ApprovedFoodVanAssembly` at **(2.10, 2.75, 0.0)**:
the centre of the roof top. The roof slab spans assembly x −0.06 to 4.26, z ±1.14, with its top at y 2.75. In vendor
space that's (−0.05, 2.75, 0.0).

- Chip van (`chip_block` livery): `lwf_food_van_sign_chips_v1.glb`
- Pizza van (`pizza` livery): `lwf_food_van_sign_pizza_v1.glb`

## Verification

`verification/` holds renders made from the installed files, assembled exactly as above with both vans side by side:
- `both-vans-zoom62-rot0..3.png`: all four camera rotations, so the front and back of each board;
- `both-vans-zoom62-dusk.png`;
- `both-vans-close-front.png` and `both-vans-close-back.png`.

At zoom 62 both pictures read from every rotation, by day and at dusk.
