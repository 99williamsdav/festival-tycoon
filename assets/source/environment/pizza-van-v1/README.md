# Pizza van v1: "Pizza the Action" livery

The second food trader: a wood-fired pizza van. It is built the same way as `chip-van-v1`. The existing food van
(`lwf_food_van_chassis_v1` and `lwf_food_van_fascia_v1`) is unchanged, and the trader is a palette swap plus a name
panel decal.

Rebuild with:
1. `python make_textures.py`
2. `blender -b --python build_panel_glb.py`

Then copy `out/*.png` and `out/*.glb` to `game/assets/environment/` (copies in `assets/runtime/environment/`) and run the
Godot import.

## Files

| File | Notes |
|---|---|
| `lwf_food_van_palette_pizza_v1.png` | 96 × 8. Five slots changed: 0 body `b5532e` (wood-fired terracotta), 3 roof and skirts `5a2c1e` (dark roast brown), 4 trim and door `f1e3c4` (cream), 11 fascia frame `2f7d3e` (basil green), 1 fascia panel `f6f1e6`. Imported lossless, no mipmaps, no 3D VRAM compression. |
| `lwf_food_van_name_panel_pizza_v1.png` | 1024 × 128. "Pizza the Action" in tomato-red Zilla Slab Bold (OFL) with a crust-brown shadow, basil leaves, and a green/white/red strip, on cream. |
| `lwf_food_van_name_panel_pizza_v1.glb` | Node `LWF_FoodVan_NamePanel`: a 2.5 × 0.31 m decal facing +Z, with the panel texture embedded. |

## Putting it on the van (code)

Same as the chip van:
- Duplicate `LWF_FoodVan_MattePalette` (on the chassis and the fascia), set its albedo texture to the pizza palette and
  keep nearest filtering.
- Add the panel GLB as a child of the fascia node at `(0, 0, 0.056)`.
- Add `lwf_food_van_sign_pizza_v1.glb` from `food-van-signs-v1`.

At zoom 62 the terracotta body and brown roof read clearly against both the sky-blue chip van and the grass, by day and
at dusk (see `verification/`).
