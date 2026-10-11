# Site map v1: the held laminated plan (B1) and the rotating corner minimap (A1)

From the approved site-map board (`Documents/Festival Tycoon concepts/site-map/`). The spec is in `INTEGRATION.md`.

**Rebuild:**
1. `python make_art.py`
2. `python compose_reference.py`
3. `blender -b --python verification/render_held.py -- <dir>` then `python verification/render_hud.py`
4. The arm GLB is `assets/source/characters/steward-fp-arms-v1/build_siteplan_arm.py`.

## Files

| Path | What |
|---|---|
| `plan_reference.py` | The ground and icon drawing: positions from the game's default layouts |
| `sheet_reference.py` | The board-era composer, kept for reference |
| `compose_reference.py` | **The reference implementation** of the plan texture and the minimap, from `out/` art only, with the label and note rules |
| `out/plan_*` (installed to `game/assets/ui/sitemap/`) | The printed plan art (texture px): bases per tier, stand icons, stickers, you, crew, laminate |
| `out/mini_*` (installed to `game/assets/ui/sitemap/`) | The minimap art (2× HUD): bases per tier, icons, pins, you, crew, north, frame, mask |
| `game/assets/characters/fp/lwf_fp_arm_right_siteplan_v1.glb` | The right hand holding the clipboard, raised (copy in `assets/runtime/characters/fp/`) |

**Fonts:** Zilla Slab and Source Sans 3 (OFL). Control's "marker" is Zilla Slab SemiBold, slanted. The concept board
used Segoe Print, which can't ship.

**Installed:** all of `out/*.png` is in `game/assets/ui/sitemap/`. All of it is imported lossless with mipmaps (`detect_3d/compress_to=0`).

## Verification (`verification/`)

| Render | Shows |
|---|---|
| `plan-t2-allrounder`, `plan-t2-steward-only`, `plan-t1` | The plan texture |
| `minimap-t2-allrounder`, `minimap-t2-steward-only`, `minimap-t1-facing-ssw` | The minimap on its own |
| `_held-up`, `_held-half` | The installed GLB through the 72° camera |
| `hud-*` | Both in the HUD |
