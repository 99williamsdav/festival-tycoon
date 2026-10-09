# Stretch tent v1: "UNDER MY UMBRELLA"

The buildable marquee, from the approved heavy-rain board (`Documents/Festival Tycoon concepts/heavy-rain/`), option C.
It gives shade in a heatwave (from Tier 1) and cover in rain (Tier 2). It ships with the **faded roof**: the sail
drops to 30% opacity while people are under it. That fade is the standard for every roofed build. See `INTEGRATION.md`.

**Rebuild:**
1. `python make_banner.py`
2. `blender -b --python build_stretch_tent.py`
3. Copy `out/lwf_stretch_tent_v1.glb` to `game/assets/environment/`, with a copy in `assets/runtime/environment/`.
4. Run the Godot import. The embedded banner texture is imported lossless with mipmaps and `detect_3d/compress_to=0`.

## `lwf_stretch_tent_v1.glb` (2,356 tris)

**Frame:** Godot metres. The origin is at the ground centre of the sail, and the front (banner side) is **+Z**.

| Node | Tris | What |
|---|---:|---|
| `LWF_StretchTent` | n/a | Root |
| `LWF_StretchTent_Sail` | 2,016 | One 7.4 × 5.4 m sand sail (`cfae7c`), smooth-shaded and double-sided. It is the **only** surface on `LWF_StretchTent_Canvas`, so fading that material fades the roof and nothing else. |
| `LWF_StretchTent_Frame` | 336 | Two tall wooden poles with steel tips, four corner poles, four guy ropes, four steel pegs. Materials `LWF_StretchTent_PoleWood`, `_Rope`, `_Steel`. |
| `LWF_StretchTent_Banner` | 4 | 2.8 × 0.29 m "UNDER MY UMBRELLA" (gold on festival teal, with two little umbrellas), hung under the front edge. Two faces, each reading the right way round. Material `LWF_StretchTent_Banner`; it stays opaque when the roof fades. |
| `LWF_StretchTent_Shelter` | n/a | Empty at the origin: the reference point for the shelter area below. |

**Shape:**
- Sail peaks of 3.7 m and 3.5 m at the two tall poles, (−1.7, −0.3) and (1.9, 0.3).
- Corner heights: front 2.35 and 2.25 m, back 1.85 and 1.95 m.
- The edges scallop inward between the corners.
- **Minimum headroom inside the shelter area is 2.03 m**, so guests never clip the sail.

`out/lwf_stretch_tent_layout_v1.json` holds the footprint, colliders, pegs, shelter polygon and rectangle, banner
placement and SHA-256.

**Fonts:** Zilla Slab Bold (OFL), from `game/assets/ui/fonts/`. The umbrellas are drawn.

## Verification

`verification/render.sh` places the **installed** `game/assets/environment/lwf_stretch_tent_v1.glb` on the real farm at
(8, 0, 10) under the game camera (`place_tent.py`). Guests are packed inside the shelter rectangle.

| Render | Shows |
|---|---|
| `tent-sun-shade.png` and `tent-sun-shade-faded.png` | Heatwave shade, with the roof opaque and faded. The shade patch stays when faded. |
| `tent-rain-opaque.png` and `tent-rain-faded.png` (plus `*-with-rain.png` with heavy rain B over them) | A heavy shower and a huddle. Faded, the whole crowd shows. |
| `tent-rot2-faded.png` | Camera rotation 2, from the back. |
| `tent-zoom24-sun.png` | Gameplay zoom. |

The renders use the rain board's scene scripts (`farm_scene.py`, `rainscene.py`, `people.py`, `overlay.py`), copied into
`verification/tools/`. Cycles draws the faded sail's shadow lighter than Godot will; in game the shade stays at full strength (see `INTEGRATION.md`).
