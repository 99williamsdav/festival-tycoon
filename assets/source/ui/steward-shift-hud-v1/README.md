# Steward shift HUD v1

The first-person HUD for the steward shift, from the approved Steward shift board
(`Documents/Festival Tycoon concepts/steward-shift/`): **prompt A** (cream card under the crosshair), **compass A**
(slim top strip), **radio A** (bottom-left strip) and **needs A** (thirst and heat bars).

| File | What |
|---|---|
| `INTEGRATION.md` | The layout spec: positions and sizes in `Ui.S` units, `Ui.cs` colours, fonts, states and timings |
| `hud_reference.py` | The reference renderer the spec was measured from. The unpicked variants (prompt B/C, compass B, radio B, needs B) are still in it, for comparison only. |
| `make_icons.py` → `out/hand.svg`, `radio.svg`, `thermometer.svg` | The three new Lucide-style icons (installed in `game/assets/ui/icons/`), plus `out/png/` renders at 14–26 px and 2× in white and gold |
| `verification/render_hud.py` | The approved HUD over the first-person captures with the installed arms: `prompt-*`, `compass-radio`, `urgent-clamped`, `needs-*` |
| `verification/bg/` | The prototype tour captures (run 3) with the placeholder arm and labels painted out (`clean_captures.py`) |

**Related packages:**
- `assets/source/characters/steward-fp-arms-v1/`: the arms.
- `assets/source/ui/steward-report-v1/`: the end-of-shift clipboard report.

**Rebuild:**
1. `python make_icons.py`
2. Copy `out/*.svg` to `game/assets/ui/icons/` and run the Godot import.
3. `python verification/render_hud.py`
