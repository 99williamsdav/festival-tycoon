# Hedge gate v1: production

The west-hedge crew gate from the approved hedge-gate board (`Documents/Festival Tycoon concepts/hedge-gate/`). The
user changed the timber gate on the board to wrought iron. Bands come in from the lane beyond the hedge; staff come
out of the farmhouse front door. The 2 m gap at the house end of the barrier line is unchanged.

The geometry is project-original and procedural (Blender 4.1.1), in the matte low-poly palette style.

Rebuild with `blender -b --python build_hedge_gate.py -- out`. Then copy `out/*.glb` to `game/assets/environment/`
(copies in `assets/runtime/environment/`) and run the Godot import.

`out/hedge_gate_report.json` holds the hashes, triangle counts, bounds and leaf data. `out/hedge_gate_layout_v1.json`
holds every placement.

## Files

| File | Tris | Notes |
|---|---:|---|
| `lwf_garden_gate_iron_v1.glb` | 1404 | Two stone piers with an iron overthrow arch, and the iron leaf as its own node. Origin at the opening centre on the ground. Gate line along local z; field side +x. 2.64 m to the top of the overthrow. |
| `lwf_garden_path_flagstones_v1.glb` | 804 | The whole path as one mesh: flagstone pairs, two landing stones and a threshold stone. Origin at the opening centre (it is placed there with yaw 0). |
| `lwf_garden_pot_flowers_v1.glb` | 196 | Terracotta pot with flowers, 0.6 m tall. Origin at the ground centre. |
| `lwf_hedge_gate_lane_v1.glb` | 196 | A 22 m stretch of lane with verge, ruts, a gravel apron to the gate and a van pull-in. The ends taper out. Origin at the opening centre. |

All four share `LWF_HedgeGate_MattePalette` (`lwf_hedge_gate_palette`, 128×8, Closest filtering, roughness 0.85).

## Gate

- **Piers:** 0.44 m square coursed stone in the farmhouse's stone colours, 1.55 m to the cap, with ball finials
  reaching 1.9 m. Centres at local z ±0.80, which is world z −1.2 and −2.8. Clear opening 1.16 m.
- **Overthrow:** two concentric iron arcs, 90 mm and 70 mm, springing from the piers' inner faces. Three ties, and
  a ring with a spike on top.
- **Leaf (`LWF_GardenGate_Leaf`):** 1.10 m long and 1.2 m tall, plus spear tops. It has a 65 mm stile frame, 60 mm
  rails, three 42 mm uprights, dog-bar loops and a solid kick panel. Its bottom is 0.10 m above the ground, clear of
  the path.
- **Zoom 62:** the iron is sized so it does not shimmer. `verification/zoom62-shimmer-three-offsets.png` shows three
  sub-pixel camera offsets rendered with 1 sample: the piers, the arch and the leaf frame stay whole in all three.

### Swinging the leaf

- `LWF_GardenGate_Leaf` is a direct child of the imported scene, beside `LWF_GardenGate_Piers`.
- Its origin is the hinge axis at gate-local (0, 0, −0.55), which is world (−32.0, −2.55). That is 3 cm off the north
  (house-side) pier.
- The leaf geometry runs along the node's own +x.
- **Shut:** `RotationDegrees.Y = -90`. The leaf runs south (+z) to the latch on the south pier.
- **Open:** `RotationDegrees.Y = 0`. The leaf points east (+x) into the field, along the house side of the path. It
  never swings over the lane.

## Layout (`out/hedge_gate_layout_v1.json`)

All positions are Godot world metres. Yaw is in degrees about +Y.

- **Hedge cut:**
  - Remove the west run `lwf_hedge_straight_8m_c_v1` at (−32, 0), yaw 90. That is `BuildHedgeBoundary`'s
    `Hedge(i + 6)` for i = 0; it covers z 0 to −8.
  - Add `lwf_hedge_gate_end_v1` at (−32, 0), yaw 90.
  - Add `lwf_hedge_gate_end_v1` at (−32, −4), yaw 270.
  - Add `lwf_hedge_straight_4m_a_v1` at (−32, −4), yaw 90.
  - The opening runs z −1.0 to −3.0. The new hedge pieces probably want `RegisterBreezeHedge`, like the run they
    replace.
- **Gate, path and lane:** each at (−32, −2), yaw 0.
- **Path route:** (−31.95, −2.0) → (−26.2, −2.0) → (−21.35, −6.3) → the door step at (−21.35, −8.55). It is 0.95 m
  wide, with its top at 7 cm. It keeps clear of the apple tree at (−29.2, −6.6) and the crate pile.
- **Pots:** (−22.45, −8.85) and (−20.25, −8.85) either side of the door step, and (−31.2, −0.75) just inside the gate.
- **Lane:** the default camera shows the ground beyond the west hedge (`FarmSurround`), so the lane is included.
  - It spans world x −38.1 to −32.0 and z −14 to 8.
  - It sits at y −0.018 to −0.006: above the surround ground (−0.025), below the field (0).
  - Its colours match the surround's own lane (`b7a17b`) and verge (`a6a17b`).
- **Barrier gap closure:** `lwf_hedge_gate_end_v1` at (−31.45, 7.75), yaw 0. It is a short hedge spur from the west
  hedge to 0.25 m short of the backstage west closure's end at x −30.2, and it closes the 1.1 m visual gap.

## Verification

All renders are in `verification/`, made from the installed `game/assets` files and placed from the layout JSON
(`prodgate.py`), with a stand-in for the `FarmSurround` ground:
- `gate-shut.png` and `gate-open.png`
- `from-lane.png`
- `mid-zoom.png` and `zoom62.png`
- `barrier-gap-closure.png`
- `zoom62-shimmer-three-offsets.png`
