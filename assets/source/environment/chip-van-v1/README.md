# Chip van v1: "Chip Off The Old Block" livery

The approved trader from the chip-vans board (`Documents/Festival Tycoon concepts/chip-vans/`). The other two
liveries and the spare puns stay in the concepts folder.

The trader uses the existing food van unchanged: `lwf_food_van_chassis_v1` and `lwf_food_van_fascia_v1`, assembled as
`InstantiateImmersionVendor` does. Same meshes, origin and footprint.

Rebuild with:
1. `python make_textures.py`
2. `blender -b --python build_panel_glb.py`

Then copy `out/*.png` and `out/*.glb` to `game/assets/environment/` (copies in `assets/runtime/environment/`) and run the
Godot import.

## Files

| File | Notes |
|---|---|
| `lwf_food_van_palette_chip_block_v1.png` | 96 × 8. The van's own 12-swatch palette with five slots changed: 0 body `4e9cc4`, 3 roof and skirts `2f6f93`, 4 trim and door `f6f1e6`, 11 fascia frame `f6f1e6`, 1 fascia panel `f6f1e6`. Imported lossless, no mipmaps, no 3D VRAM compression. |
| `lwf_food_van_name_panel_chip_block_v1.png` | 1024 × 128 name panel: extruded block capitals in Source Sans 3 Black (OFL) on white, with a blue chequer strip. |
| `lwf_food_van_name_panel_chip_block_v1.glb` | A 2-triangle decal, node `LWF_FoodVan_NamePanel`, 2.5 × 0.31 m, centred on its origin and facing +Z, with the panel texture embedded. |

## Putting it on the van (code)

- **Livery:** duplicate the van's material (`LWF_FoodVan_MattePalette`, on both the chassis and the fascia) and set its
  albedo texture to `lwf_food_van_palette_chip_block_v1.png`. Keep the material's nearest filtering.
- **Name:** instantiate `lwf_food_van_name_panel_chip_block_v1.glb` as a child of the fascia node at
  `Position = (0, 0, 0.056)`. That puts it 6 mm in front of the fascia's front face, centred on the sign.

`verification/` holds a close render and a zoom-62 render made from these shipped files only, assembled as above.
