# Backstage v1: production

These are the production assets from the approved backstage v3 board, with two changes:
- a walk-through gap at the stage front
- no sign at Tier 1

Board: `Documents/Festival Tycoon concepts/backstage/backstage-board-v3.png`.

The geometry is project-original and procedural (Blender 4.1.1), in the matte low-poly palette style.

Rebuild with `blender -b --python build_backstage.py -- out`. Then copy `out/*.glb` to `game/assets/environment/` (copies
in `assets/runtime/environment/`) and run the Godot import.

`out/backstage_report.json` holds the hashes, triangle counts and bounds. `out/backstage_layout_v1.json` holds the
placements.

## Files

| File | Tris | Notes |
|---|---:|---|
| `lwf_trailer_stage_v3.glb` | 3850 | v2 with the chassis turned round: drawbar at the south end, band stairs centred on the north end. |
| `lwf_crowd_barrier_v1.glb` | 504 | One steel pedestrian barrier, 2.3 × 1.12 m. Origin at the centre bottom. Length along local +X (hook at +X, eye at −X). Feet 0.64 m deep. Built to read at zoom 62: a 72 mm frame tube and five 40 mm bars (it was 44 mm and fourteen 10 mm bars, which dropped in and out of the pixel grid as the camera moved), and a darker matte galvanised grey (`8a9093`). |
| `lwf_backstage_flight_case_stack_v1.glb` | 96 | Two black flight cases, 1.1 × 0.6 m footprint, 0.92 m tall. |
| `lwf_backstage_flight_case_tall_v1.glb` | 48 | One tall case, 0.6 × 0.6 × 0.8 m. |
| `lwf_backstage_crate_pile_v1.glb` | 144 | Three wooden crates, 1.27 × 0.62 m footprint, 0.8 m tall. |
| `lwf_folding_chair_v1.glb` | 120 | Navy folding chair, facing local −Z (Godot forward). |

All props have their origin at the ground centre. The barrier and props share `LWF_Backstage_MattePalette`
(`lwf_backstage_palette`, 64×8, Closest filtering, roughness 0.9, specular 0.25). Slots 0 and 1 (galvanised) are used only by the barrier.

## Trailer stage v3

- **Placement:** identical to v2. Swapping the path in `Main.cs` is enough.
- **Nodes:**
  - `LWF_TrailerStage_Base`, `LWF_TrailerStage_Rails` and `LWF_TrailerStage_Fittings`, as in v2. No code refers to
    them by name.
  - `LWF_TrailerStage_Access` (the old south stairs) is removed.
  - `LWF_TrailerStage_BandStairs` is new.
  - There is no root node, as in v2.
- **Deck:** unchanged. The chassis (Base, Rails) is mirrored about stage-local x −0.855, the deck's centre, so the deck
  surface, the Fittings (wedges and speakers), the performer marks and every stage set keep their stage-local
  coordinates.
- **Stairs (world, Godot axes):**
  - Top step at the north deck edge, z 7.89, through a 1.2 m opening in the end rail.
  - Steps x −17.22 to −16.02, down to the front edge of the bottom step at z 5.72.
  - 8 risers of 150 mm and 7 treads of 310 mm, with handrails both sides.
  - Walk-off x −17.22 to −16.02, z 5.72 to about 5.0.
- **Drawbar:** world z 15.82 to 17.66.
  - It is x −18.26 to −14.97 at the deck and narrows to x −16.83 to −16.41 at the hitch.
  - It doesn't touch the ground.
  - Stage bounds, stage-local: x −6.66 to 5.31 (drawbar to stairs).

## Layout (`out/backstage_layout_v1.json`)

The JSON uses Godot world metres. Yaw is in degrees about +Y and turns the barrier's local +X onto its run direction.

- **East line, five barriers along x −14:** centres at z 5.65, 3.25, 0.85, −1.55 and −3.95, yaw 90. The line runs from
  z 6.8 to −5.1.
- **Stage-front gap:** 1.53 m from the line's north end (−14, 6.8) to the deck's north-east corner (−15.08, 7.89).
  - People walk through here.
  - The line was moved 0.8 m north of the board's z 6.0 so the gap is about 1.5 m.
- **Angled end barrier:** centre (−14.937, −5.866), yaw 144.61. It runs from (−14, −5.2) to (−15.875, −6.532).
- **House gap:** 1.97 m to the farmhouse wall at z −8.5.
- **West closure, five barriers along z 7.75:** centres at x −19.45, −21.85, −24.25, −26.65 and −29.05, yaw 180. It
  runs from x −18.3 (beside the deck's north-west corner) to −30.2.
- **Props:**

  | Prop | x | z | Yaw |
  |---|---:|---:|---:|
  | Flight-case stack | −18.6 | 3.6 | 11.5 |
  | Tall case | −19.5 | 2.2 | −23 |
  | Crate pile | −20.5 | −1.5 | 17 |
  | Chair | −18.5 | −2.6 | 34 |
  | Chair | −17.65 | −2.2 | −17 |

  The stack and tall case moved off the stairs' walk-off line compared with the board.

`verification/placed-from-layout.png` is the farm scene with v3 and every barrier and prop placed from the JSON, the same
way the game will place them (`prodplace.py`).
