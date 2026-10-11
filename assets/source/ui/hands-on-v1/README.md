# Hands-on campaign v1: radial radio menu, "Your shift", title modes, view cues, YOU marker

From the approved hands-on board (`Documents/Festival Tycoon concepts/hands-on-campaign/`):
- **1B:** the radial radio menu.
- **2A:** the "Your shift" results tab.
- **3A:** title buttons, adapted to the user's three modes (MANAGEMENT ONLY, ALL-ROUNDER, STEWARD ONLY).
- The "V" view-switch cues, and the YOU marker.

The spec is in `INTEGRATION.md`.

**Rebuild:**
1. `python make_art.py`
2. Copy `out/*.png` to `game/assets/ui/handson/` and `map.svg` to `game/assets/ui/icons/`.
3. Run the Godot import (`map.svg` uses the same settings as the other Lucide icons).
4. `python verification/render.py`

## Art (`game/assets/ui/handson/`, 2×)

| File | What |
|---|---|
| `lwf_radial_wedge3_{normal,hover,busy,disabled}_v1.png` | Inner-ring wedge, 112°, r 58–150 (hover r 156), pointing up |
| `lwf_radial_crew{1..6}_{normal,hover,busy}_v1.png` | Crew-fan wedge for N crew in a 180° fan, r 162–236, pointing up |
| `lwf_radial_hub_v1.png`, `lwf_radial_backdrop_v1.png` | The centre hub (r 54, hi-vis rim), and the soft disc behind the ring |
| `lwf_results_mini_clipboard_v1.png` | The "Your shift" tab's mini Control's clipboard (no stamp; the grade stamp goes on top) |
| `lwf_you_tag_v1.png`, `lwf_you_ring_v1.png` | The YOU pill and pointer, and the top-down ground ring |
| `lwf_you_edge_disc_v1.png`, `lwf_you_edge_arrow_v1.png` | The off-screen edge badge: an upright disc and a rotating arrow |

**Icons** (`game/assets/ui/icons/`):
- **New:** `map.svg`, Lucide's map outline (ISC), for MANAGEMENT ONLY and "V · Manager view".
- **Existing:** `radio`, `user`, `users`, `truck`, `heart-pulse`, `shield`, `wrench`, `x`.

## Verification (`verification/`)

`render.py` draws everything from the installed art over:
- the first-person captures with the installed radio arm, raised to the menu pose (`arm_menu_raise.png`) and the talk
  pose (`arm_talk_raise.png`), both rendered by `render_raised_arm.py`;
- the current results-paper layout;
- the user's title screen;
- a manager-view farm render (`manager_bg.png`: a farm render with a hi-vis steward from `manager_you_hook.py`, under
  the live HUD's real top and bottom bars).

| Render | Shows |
|---|---|
| `radial-hover-medic`, `radial-hover-busy-henry`, `radial-crew-open`, `radial-tier1-crew-disabled`, `radial-released-sent` | The radial menu's states |
| `results-your-shift`, `results-tab-unseen-dot` | The new tab, and the dot before it's opened |
| `title-modes`, `title-modes-hover-allrounder` | The three mode buttons |
| `manager-you-tag`, `manager-you-edge`, `fp-manager-view-cue` | The YOU marker and the view cues |
