# Rain guests v1: soaked guests, four rain clips, the clear poncho

From the approved heavy-rain board (`Documents/Festival Tycoon concepts/heavy-rain/`):
- **Wet guests:** the soaked palette and all four poses: hunched, hands on head, hurry, huddle.
- **Poncho:** option A, classic clear.

**Rebuild:**
1. `blender -b --python build_rain_guests.py -- male`, then the same with `female`
2. `python make_soaked_palette.py`
3. Copy `out/*.glb` to `game/assets/characters/` (copies in `assets/runtime/characters/`), and
   `out/attendee_palette_soaked_v1_contract.json` to `game/assets/characters/`.
4. Run the Godot import.

`rain_kit.py` holds the posing (two-bone IK on the rig's 17 bones) and the poncho builder. It's shared with the board
renders.

## Files

| File | What |
|---|---|
| `lwf_attendee_<sex>_rain_clips_v1.glb` | The rig alone (skin `LWF_Attendee_Rig`, the same 17 bone nodes as `lwf_attendee_<sex>_rigged_test_v1.glb`, no mesh) carrying four clips (below). |
| `lwf_attendee_<sex>_poncho_clear_v1.glb` | The rig plus `LWF_Poncho_Clear`: a hooded clear drape skinned to the same 17 joints. Materials: `LWF_Poncho_Clear` (`dfeef5`, alpha 0.35 BLEND, roughness 0.08, double-sided) and `LWF_Poncho_Trim` (an opaque white hem that keeps it readable at zoom). |
| `attendee_palette_soaked_v1_contract.json` | A full copy of `attendee_palette_v1_contract.json` (same schema, `version` 1), with each colourway's shirt (3, 4), trousers (7, 8) and hair (10, 11) slots × 0.58. Badge and skin don't soak. |

## The four clips (24 fps, looping)

Each clip is the rig's own clip re-evaluated frame by frame with the upper body overridden. Legs, hips, timing and
stride are the originals, so the walk speeds already tuned for `walk` and `walk_hurry` still hold.

| Clip | Base | Frames | What |
|---|---|---:|---|
| `rain_hunched` | `idle` | 49 | Shoulders up, head down, arms wrapped round the chest, a small shiver (8 shakes per loop) |
| `rain_huddle` | `idle` | 49 | Arms folded, a little hunched, a slow sway: for standing packed under cover |
| `rain_hands_head` | `walk` | 21 | Walking with both hands clasped on top of the head |
| `rain_hurry` | `walk_hurry` | 16 | Hunched mid-stride, arms tucked in and pumping |

## Verification (`verification/`)

`verify_rain_guests.py`, then `make_sheets.py`, render `rain-guests-<sex>-clips-and-poncho.png` from the **installed**
files. Rows 1–4 are the four clips from the library GLB, on the soaked body. Rows 5–8 are the poncho driven by the
rig's own `idle`, `walk`, `walk_hurry` and `drink`. Columns are four frames per clip.

- **Fit:** no tearing or limb clipping on either rig.
- **Drinking:** while drinking, the hand and cup come through the poncho's front, which reads as holding a pint under
  a poncho.
- **Not combined:** the rain clips aren't meant to be combined with the poncho. Poncho wearers keep their normal clips
  and their mood.
