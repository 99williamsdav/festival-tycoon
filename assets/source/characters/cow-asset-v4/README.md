# Cow v4: locomotion clips for loose cows

These are the v3 cow's mesh, skeleton, weights and palette, unchanged (approved asset, `../cow-asset-v3`), with
production clips in place of the v3 previews. Like every guest rig, the walk is foot-planted, in place, with the root
moved in game (see `../rigged-test-v1/README.md`).

- **Rebuild:** `blender -b --python build_cow_v4.py` writes `runtime/lwf_cow_v4.glb`, `source/lwf_cow_v4.blend` and
  `build-report.json`. Then copy the GLB to `game/assets/characters/animals/` (copy in
  `assets/runtime/characters/animals/`) and run the Godot import.
- **Check:** `blender -b --python verify_cow_v4.py -- <dir>` checks the exported GLB independently and renders the side
  and turn strips. `verify_clips.py` renders `graze`, `idle` and `walk` side views.

Forward is Godot +X, as in v3. Turning is a root yaw in game.

## Clips (glTF animation names)

| Clip | Frames | Notes |
|---|---|---|
| `walk` | 41 at 24 fps (1.667 s), loops | A slow amble. **0.50 m/s at 1×, 0.8333 m per cycle.** |
| `graze` | 97 (4.0 s), loops | Head down at the grass. Four cropping tugs a loop, a slow side-to-side search, ears moving, and two tail flicks (a double flick at about 38% and a single one at about 82%). Hooves stay at their rest marks. |
| `idle` | 97 (4.0 s), loops | Head up, chewing the cud: six sideways grinds a loop. Breathing, an ear twitch, and a slow tail sway. |
| `alert` | 37 | v3's `Alert`, unchanged. |

## Walk

- **Gait:** a lateral-sequence walk. Hooves strike left hind, left fore, right hind, right fore, a quarter cycle apart,
  with 66% of each leg's cycle in stance, so two or three hooves are always down.
- **Planting:** a planted hoof travels back under the body at exactly the walk speed, so it stays fixed while the root
  moves forward at 0.5 m/s.
  - The hoof lands toe-up and rolls flat about the heel.
  - It stays flat, then rolls up onto the toe before lifting.
  - In each phase the contact point stays fixed.
- **Legs:** two-bone IK on every leg. The front carpus flexes forward and the hind hock back, as on the rest pose. The
  legs are nearly straight at rest, so the build lowers the body until every pose is reachable: 4 cm.
- **Weight and head:**
  - Two body dips a cycle as each pair takes the weight.
  - The body sways and rolls towards the side in stance, with a little pitch and yaw.
  - The head is carried 14° below its rest line and nods with each foreleg strike.
  - The tail and ears swing with the stride.
- **Speed:** to walk at another speed, set the playback speed scale to actual speed ÷ 0.5. The hooves stay planted at any
  rate.

## Verification

All measured from the exported GLB, with the root moving at 0.5 m/s, over two cycles:

- **Planted-hoof drift:** 0.0 mm on all four hooves (heel while flat, toe while rolling over).
- **Ground creep during swing:** 0.0 mm.
- **Sole height in stance:** 0.0 mm.

See `verification/walk_drift_from_glb.json`.

- `side_0..7.png` (sheet: `walk_side_and_turn_sheet.png`): side view, the camera following the cow over a fixed 0.25 m
  grid. The planted hooves stay on their grid lines.
- `turn_0..7.png`: the game camera, with the root walking a 20°/s arc at 0.5 m/s. There's no leg crossing; the feet keep
  their tracks either side of the body. A planted hoof turns with the body during a turn; at 20°/s that is 9 to 11 mm per
  frame (a hoof is 0.6 to 0.8 m from the cow's origin). That's a quarter of a pixel at zoom 30.
- `clips_sheet.png`: walk, graze and idle from the side.
