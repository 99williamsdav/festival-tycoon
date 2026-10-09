# Heavy rain v1: rain sprites and the shower look

These are Tier 2's weather visuals, from the approved heavy-rain board (`Documents/Festival Tycoon concepts/heavy-rain/`).
**Rain B (heavy showers) is the norm. Rain C (downpour) is a rare treat**, and both are specified here. The particle setup,
puddle ripples, wet sheen and shower lighting are in `INTEGRATION.md`.

Related packages:
- `../stretch-tent-v1/`: the marquee.
- `../rain-ground-v1/`: straw and track matting.
- `../../characters/rain-guests-v1/`: soaked guests, rain clips and the poncho.
- `../../ui/rain-forecast-icons-v1/`: the forecast chip.

**Rebuild:**
1. `python make_rain_sprites.py`
2. Copy `out/lwf_rain_streak_v1.png`, `out/lwf_rain_ripple_v1.png` and `out/lwf_rain_splash_v1.png` to
   `game/assets/environment/`, with copies in `assets/runtime/environment/`.
3. Run the Godot import. All three are imported lossless with mipmaps and `detect_3d/compress_to=0`.

## Files

| File | Size | What |
|---|---|---|
| `lwf_rain_streak_v1.png` | 32 × 256 | One streak: white, with alpha carrying the shape. Soft gaussian sides, a faint tail (top) and a bright head (bottom). Used on a velocity-aligned particle quad. |
| `lwf_rain_ripple_v1.png` | 512 × 512 | A 4 × 4 flipbook, 16 frames: a ring (and a fainter inner ring) expanding to the cell edge and fading, seen from above. For puddle cells in the ground shader. |
| `lwf_rain_splash_v1.png` | 512 × 128 | A 4 × 1 flipbook: a drop's crown of beads rising and falling. A billboard with its base at the bottom centre, for ground splashes. |
| `out/lwf_rain_ripple_strip_v1.png` | n/a | Preview of the 16 ripple frames on the puddle colour (not installed). |

## Verification (`verification/`)

`sprite_rain.py` is a 2D stand-in for the particle setup. It draws with the **installed** streak sprite at the
documented numbers over the rain board's renders, which are in `base/`.

| Render | Shows |
|---|---|
| `rain-B-zoom24.png` and `rain-B-zoom62.png` | Heavy showers, the norm |
| `rain-C-zoom24.png` and `rain-C-zoom62.png` | The rare downpour, with the veil and drifting bands |
| `rain-B-dusk-zoom24.png` | Streaks lit only where the festoons and stage light catch them |
| `rain-B-puddles-zoom7.png` and `ripple-frames.png` | Puddle rings, and the 16 ripple frames |

At zoom 62 the crowd stays readable in both B and C: the streak count per screen is the same at every zoom.
