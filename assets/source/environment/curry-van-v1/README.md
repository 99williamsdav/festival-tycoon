# Curry van v1: "Korma Chameleon" (third food trader, Tier 2)

The curry van, from the approved board (`Documents/Festival Tycoon concepts/curry-van/`).

**Approved picks:**
- **Livery:** The Naan Stop's colours: aubergine body, plum roof, gold door and fascia frame, palace-arch name panel.
- **Name:** "Korma Chameleon".
- **Music puns throughout:** "Karma karma karma karma korma" on the panel, a "Today's setlist" menu, and "Hold the line! It's
  worth the wait".

The van is built the same way as the chip van and the pizza van. The existing food van (`lwf_food_van_chassis_v1` and
`lwf_food_van_fascia_v1`) is unchanged; the trader is a palette swap, a name panel decal and a roof sign. On top of that,
the curry van has two new pieces that make it read as a busy kitchen: a kitchen dressing set and a chalk menu A-board.

**Rebuild:**
1. `python make_textures.py`
2. `blender -b --python build_curry_van.py`
3. Copy `out/*.png` and `out/*.glb` to `game/assets/environment/`, with copies in `assets/runtime/environment/`.
4. Run the Godot import.

The palette is imported lossless with no mipmaps. The name panel, sign and menu textures (the PNGs and the copies embedded
in the GLBs) are imported lossless with mipmaps and `detect_3d/compress_to=0`, as the pizza van's.

## Files

| File | Tris | Notes |
|---|---:|---|
| `lwf_food_van_palette_korma_v1.png` | n/a | 96 × 8. Five slots changed: 0 body `5b2a4f` (aubergine), 3 roof and skirts `3a1a33` (plum), 4 trim and door `d9a43b` (gold), 11 fascia frame `d9a43b`, 1 fascia panel `2a1426`. |
| `lwf_food_van_name_panel_korma_v1.png` / `.glb` | 2 | 1024 × 128. "KORMA CHAMELEON" in gold Zilla Slab Bold capitals on dark plum, with a gold line frame and an onion-dome arch at each end. The strapline "Karma karma karma karma korma" sits between two drawn quavers. Node `LWF_FoodVan_NamePanel`, a 2.5 × 0.31 m decal facing +Z. |
| `lwf_food_van_sign_curry_v1.png` / `.glb` | 28 | Roof sign: a bowl of curry with a naan dipped in and steam rising. The bowl rim is aubergine and the pattern gold, to match the van. Built like `food-van-signs-v1`: a 1.55 × 1.55 m double-sided cut-out (alpha clip 0.5), with a flat iron pole and roof plate. The top is 1.85 m above the roof. Node `LWF_FoodVan_Sign_Curry`. |
| `lwf_food_van_kitchen_dressing_v1.glb` | 496 | Generic; it suits any future cooking trader. Node `LWF_FoodVan_KitchenDressing`: two galvanised roof vent stacks with rain caps, and four stacks of foil takeaway trays (2 to 4 high, cream lid and gold sticker on top) on the counter either side of the hatch. Child empties `LWF_FoodVan_SteamVent_L` and `_R` mark the steam outlets. |
| `lwf_food_van_menu_board_korma_v1.png` / `.glb` | 40 | 512 × 720 chalk A-board, "TODAY'S SETLIST": Smells Like Tikka Spirit £6, Tikka Massala Massive £6, Korma Chameleon £5, Bhaji Ballad £3, Naan Stop Party £1, then "Hold the line! It's worth the wait". The menu is on both leaves. Node `LWF_FoodVan_MenuBoard_Korma`, 0.44 m wide and 0.62 m tall. The origin is at its ground centre. |

`out/curry_van_report.json` lists the nodes, triangle counts, placements, steam-marker positions and SHA-256s.

The menu prices are set dressing only. If the game's curry prices differ, edit `DISHES` in `make_textures.py` and rebuild.

**Fonts:** Zilla Slab and Source Sans 3 (OFL), from `game/assets/ui/fonts/`. The quavers are drawn, not font glyphs.

## Placement

The full spec is in `INTEGRATION.md`. In short, everything goes on the same `ImmersionFoodVan` /
`ApprovedFoodVanAssembly` nodes that `InstantiateImmersionVendor` already builds:

| Piece | Parent | Position |
|---|---|---|
| Palette | `LWF_FoodVan_MattePalette` surfaces (`ApplyFoodVanLivery`) | n/a |
| Name panel | fascia node | (0, 0, 0.056) |
| Roof sign | `ApprovedFoodVanAssembly` | (2.10, 2.75, 0) |
| Kitchen dressing | `ApprovedFoodVanAssembly` | (0, 0, 0) |
| Menu A-board | `ApprovedFoodVanAssembly` | (4.10, 0, 2.30) |

## Verification

The files in `verification/` were rendered from the installed files in `game/assets/environment/`, assembled exactly as
`InstantiateImmersionVendor` does by `assemble_like_game.py` (a `farm_scene.py` hook). The curry van stands beside the
chip van and the pizza van, with queues of 3, 2 and 8.
- `korma-van-close.png`, `counter-and-menu-board.png`;
- `three-vans-zoom24.png`, `three-vans-zoom62.png` and `three-vans-zoom62-dusk.png`.

The white puffs in these renders are stand-ins at the two steam markers. In the game they should be particles (see
`INTEGRATION.md`).
