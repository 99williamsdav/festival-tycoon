# Cow pasture v1: the field, the gate, and the chewed cable

This is the production art for the cow mechanic, from the approved board (`Documents/Festival Tycoon concepts/cow-pasture/`):
- the pasture beyond the east hedge, as boarded;
- gate A, the weathered oak five-bar;
- chewed cable C, the frayed lead plus the world icon.

The geometry is project-original and procedural (Blender 4.1.1), with one shared matte palette (`lwf_cow_pasture_palette`,
192×8, Closest) in the farm's timber and grass tones, plus one emissive material for the spark.

Rebuild:
1. `blender -b --python build_cow_pasture.py -- out` writes the GLBs, `out/cow_pasture_layout_v1.json` and
   `out/cow_pasture_report.json`.
2. `python make_icon.py` writes `out/pin_chewed_cable.png`.
3. Copy the GLBs to `game/assets/environment/` (copies in `assets/runtime/environment/`) and the icon to
   `game/assets/ui/field-notes/` (copy in `assets/runtime/ui/field-notes/`).
4. Run the Godot import. The icon's import is lossless with mipmaps and `detect_3d/compress_to=0`, like the other pins.

## Files

| File | Tris | Origin | Notes |
|---|---:|---|---|
| `lwf_field_gate_oak_v1.glb` | 924 | Centre of the hedge opening on the ground | Gate line along local Z; pasture +X, site −X. Posts at local z ±1.5. Nodes are listed below. |
| `lwf_cow_pasture_ground_v1.glb` | 2426 | Pasture centre | The ground dressing: grass base, grazed and worn patches, a muddy gateway, cowpats and thistles. It covers world x 32.4–58, z −26..22. The base is at y −0.018, above FarmSurround's ground at −0.025. |
| `lwf_cow_pasture_fence_v1.glb` | 576 | Pasture centre | Post-and-rail on the three outer sides; the hedge is the fourth. |
| `lwf_water_trough_v1.glb` | 48 | Base centre | Galvanised, 0.65 × 0.6 × 2.3 m, long axis along local Z. |
| `lwf_hay_ring_feeder_v1.glb` | 612 | Base centre | Round bale in a galvanised ring, 2.06 m across. |
| `lwf_power_lead_v1.glb` | 108 | Where the lead leaves the equipment, on the ground | An intact lead running out along local +X, with a plug at the far end. |
| `lwf_power_lead_chewed_v1.glb` | 296 | As the intact lead | The lead bitten through: a frayed copper end, the bitten-off far end, and a scorch mark. Plus `LWF_Spark_Anchor` at (2.45, 0.12, 0.62) with the emissive `LWF_Spark` under it. |
| `pin_chewed_cable.png` (in `game/assets/ui/field-notes/`) | | | The world icon at 2x, 60 × 75: an amber badge, ink outline, a lightning bolt and a bite out of the rim. It shows 30 × 37 at 1280 × 720, with the pointer tip at about pixel (30, 69). |

## The gate

- **Posts:** `LWF_FieldGate_Posts` holds both posts, the hinge pins and the catch keeper.
- **Pivot:** `LWF_FieldGate_LeafPivot` sits at local (0, 0, 1.37), on the hinge axis, with **rotation.y = 90** when the
  gate is shut. Swinging it towards **0** opens the intact leaf into the pasture; 0 is fully open.
- **Leaf children** of the pivot: `LWF_FieldGate_LeafIntact`, `LWF_FieldGate_LeafBroken` and
  `LWF_FieldGate_LeafRepaired`.
- **Repair:** `LWF_FieldGate_Repair` is a sibling of the posts: three loops of orange baler twine on the slam post, and a
  pallet wired across the gap on the site side.

| State | Show | Hide | Pivot |
|---|---|---|---|
| Intact | `LeafIntact` | `LeafBroken`, `LeafRepaired`, `Repair` | 90 (shut) .. 0 (open) |
| Broken | `LeafBroken` (baked hanging off the bottom hinge, swung 55° into the pasture, far end on the ground) | the others | 90 |
| Repaired | `LeafRepaired` (sagging shut, the smashed bar out) and `Repair` | `LeafIntact`, `LeafBroken` | 90 |

## Layout (`out/cow_pasture_layout_v1.json`)

All values are Godot world metres; yaw is in degrees about +Y.

**Hedge cut (east side):**
- Remove `lwf_hedge_straight_8m_d_v1` at (32, 0, 0), yaw 90. That is `BuildHedgeBoundary`'s east run for i = 0
  (`Hedge(i + 7)` = `d`), covering z 0 to −8.
- Add `lwf_hedge_gate_end_v1` at (32, 0, 0), yaw 90.
- Add `lwf_hedge_gate_end_v1` at (32, 0, −5.3), yaw 270.
- Add `lwf_hedge_straight_4m_a_v1` at (32, 0, −4.6), yaw 90.
- The opening runs z −1.0 to −4.3. The new hedge pieces probably want `RegisterBreezeHedge`, like the run they replace.

**Pieces:**

| Piece | Position (x, z) | Yaw |
|---|---|---:|
| Gate | (32, −2.65) | 0 |
| Pasture ground | (45.2, −2) | 0 |
| Fence | (45.2, −2) | 0 |
| Trough | (35.5, −6.6) | 0 |
| Feeder | (44, 6) | 0 |

**Cows:** the JSON suggests ten `lwf_cow_v3` positions and yaws.

**Cable:** the lead's origin goes at the equipment's socket side, heading away from it; the JSON's example is the
generator. Show `lwf_power_lead_chewed_v1` when the cable is chewed, flicker `LWF_Spark` (a 0.1 s flash about once a
second), and put the icon over the equipment like the field-note pins.

## Verification

Everything in `verification/` was placed from the layout JSON with the built files, using the game camera:
- `g_intact.png`, `g_broken.png`, `g_repaired.png`: the three states.
- `g_open.png`: the intact leaf at pivot 10, i.e. 80° open.
- `c_chewed.png`: the chewed lead.
- `z24.png`: the pasture and herd.
