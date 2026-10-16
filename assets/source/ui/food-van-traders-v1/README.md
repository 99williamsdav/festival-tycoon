# Food van traders v1: the trader flyer board

From the approved board (`Documents/Festival Tycoon concepts/food-van-traders/`): **direction A**, the trader flyers,
**opened B's way** (click a van on the field, or its drawer line). The coffee trader is named **Brew Fighters**, with the
strapline "The Best of Brew". The spec is in `INTEGRATION.md`.

| File | What |
|---|---|
| `out/trader_art_{chip_block,pizza,korma}_v1.png` | 3/4 renders of the installed van liveries (`render_van_art.py`, then `make_art.py`), 2×, fitted to 324 × 160 |
| `out/trader_art_coffee_concept_v1.png` | Coffee: a crop of the approved concept painting (`coffee-van-v2/references`). Replace it with a render once the parked coffee van is integrated. |
| `reference_render.py` | The reference renderer: pips from the FoodTraders.cs values, comparison chips, every state |
| `verification/` | The board at Tier 1 and Tier 2, two chips vans in Classic, and the card states |

**Installed:** `game/assets/ui/traders/` holds the four art PNGs, lossless with mipmaps.

**Rebuild:**
1. `blender -b --python render_van_art.py -- <art> <sign> van_<art>.png` for `chip_block chips`, `pizza pizza`,
   `korma curry`
2. `python make_art.py`
3. `python reference_render.py`

**Fonts:** Zilla Slab and Source Sans 3 (OFL). Icons are the game's Lucide set.
