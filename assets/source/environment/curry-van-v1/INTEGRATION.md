# Curry van v1: integration

This is for after the two-food-van refactor. No code has been changed. All positions are Godot metres in the parent's
local space; the van's front (hatch) is local +Z, as for the other vans.

## Keys

| Key | Value | Used by |
|---|---|---|
| `FoodTrader.Art` | `korma` | `lwf_food_van_palette_{art}_v1.png`, `lwf_food_van_name_panel_{art}_v1.glb`, `lwf_food_van_menu_board_{art}_v1.glb` |
| Product, lower case | `curry` | `lwf_food_van_sign_{product}_v1.glb` |

With `Art = "korma"` and a `Curry` product, today's `InstantiateImmersionVendor` already picks up the palette, the name
panel and the sign through its `ResourceLoader.Exists` checks. Only the two new pieces below need new code.

## 1. Palette, name panel, roof sign (existing paths)

| Piece | Parent | Position | Notes |
|---|---|---|---|
| `lwf_food_van_palette_korma_v1.png` | n/a | n/a | `ApplyFoodVanLivery`, nearest filtering |
| `lwf_food_van_name_panel_korma_v1.glb` | fascia node | (0, 0, 0.056) | |
| `lwf_food_van_sign_curry_v1.glb` | `ApprovedFoodVanAssembly` | (2.10, 2.75, 0) | vendor space (−0.05, 2.75, 0) |

## 2. Kitchen dressing: `lwf_food_van_kitchen_dressing_v1.glb`

**Placement:** child of `ApprovedFoodVanAssembly` at (0, 0, 0), yaw 0. It is authored in assembly space. It's generic,
so a future cooking trader (burgers, noodles) can reuse it; chips and pizza can stay without it.

The mesh is `LWF_FoodVan_KitchenDressing`, five flat materials, 496 tris:
- **Vents:** two roof vent stacks, r 0.12 m, 0.16 m high with a 0.17 m cap, at assembly (0.95, 2.75, 0.55) and
  (3.50, 2.75, 0.55). That is clear of the sign board, which spans x 1.325 to 2.875 at z ±0.015.
- **Foil trays:** stacks on the counter top (y 1.24), either side of the hatch centre (x 2.15), so the serving gap stays
  clear:

| x | z | Trays |
|---:|---:|---:|
| 1.30 | 1.36 | 3 |
| 1.55 | 1.30 | 2 |
| 2.85 | 1.34 | 2 |
| 3.12 | 1.38 | 4 |

**Steam anchors** are child empties of the mesh:

| Node | Assembly position | Vendor position |
|---|---|---|
| `LWF_FoodVan_SteamVent_L` | (0.95, 2.90, 0.55) | (−1.20, 2.90, 0.55) |
| `LWF_FoodVan_SteamVent_R` | (3.50, 2.90, 0.55) | (1.35, 2.90, 0.55) |

Each sits just under its vent's cap, unrotated, so its local +Y is world up.

**Suggested steam:** one `GpuParticles3D` (or `CpuParticles3D`) per marker:
- **Particles:** soft white billboard quads, alpha about 0.45 fading to 0, 0.12 m growing to 0.35 m.
- **Motion:** rise 0.5 to 0.7 m/s with a slight drift, lifetime 1.6 to 2.2 s, 6 to 10 alive.
- **When:** emitting while the van is serving or has a queue. Optionally thicker with queue length, which suits "slow
  to serve".
- **Sizing:** at zoom 62 the plume should read as 2 to 3 px wide puffs, the size of the stand-ins in
  `verification/three-vans-zoom62.png`.

## 3. Menu A-board: `lwf_food_van_menu_board_korma_v1.glb`

- **Placement:** child of `ApprovedFoodVanAssembly` at (4.10, 0, 2.30), yaw 0. That's vendor (1.95, 0, 2.30): front
  right of the hatch, about 0.8 m in front of the counter edge (z 1.50), beside the queue rather than in it. Both leaves
  carry the menu, so it reads from every camera rotation.
- **Footprint:** 0.44 × 0.30 m, origin at its ground centre, 40 tris.
- **Clearance:** the queue's first slot is about vendor (0, 2.3), so the board is about 2 m to its right. If the
  service queue ever bends right, or if the vendor's footprint and blocking cells don't cover this spot, move the board
  inside the footprint or treat it as a small blocker. It must not take a queue cell (see "queues flow, not reserved").

## 4. Held and litter props

These live in `assets/source/props/curry-props-v1/`; see its README.
- `HeldAsset(ImmersionProduct.Curry)` → `res://assets/props/lwf_curry_tray_v1.glb`
- `LitterAsset(ImmersionProduct.Curry)` → `lwf_litter_curry_tray_v1.glb` in `assets/environment/litter-assets-v1/`

They use the same origin contracts as the chips tray and the pizza plate, so there's no pose or anchor change.
