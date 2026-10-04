# Farm generators v1

4 October 2026. Production assets from the approved concept: the uncaged Tier 1 farm diesel, plus the rental hire set
(board: `Documents/Festival Tycoon concepts/farm-generator/farm-generator-v2-uncaged-board.png`). Project-original
procedural geometry (Blender 4.1.1). Matte low-poly, palette textures with Closest filtering.

Rebuild: `blender -b --python build_generators.py -- out`, then copy `out/*.glb` to `game/assets/equipment/`
(copies in `assets/runtime/equipment/`). The build report, with hashes, triangles, bounds and anchor points, is
`out/generators_report.json`.

## Files

| File | Tris | Nodes |
|---|---:|---|
| `lwf_farm_diesel_{normal,overloaded,fault,isolated}_v1.glb` | 1376 / 1500 / 1596 / 1316 | `LWF_FarmDiesel_Base`, `LWF_FarmDiesel_State_<State>` |
| `lwf_hire_generator_{normal,overloaded,fault,isolated}_v1.glb` | 916 / 1100 / 1116 / 884 | `LWF_HireGenerator_Base`, `LWF_HireGenerator_State_<State>` |

This is the same pattern as `lwf_towable_generator_*_v1`:
- a Base node identical in all four files
- a State node holding everything that changes: the exhaust (cold steel, red-hot when overloaded, sooty on fault),
  the socket end of the lead, and the effects

## Pivot, scale and rotation

- **Pivot:** origin at ground centre, no node transforms. Load it the way `RefreshEquipmentControls` loads the
  towable: `AddAsset(path, new Vector3(-20.25, 0, 14.5))` with no rotation.
- **Scale:** real metres. Nothing needs scaling.
- **Facing:** Blender +X is Godot +X. Both units put their working end on **+X**: the farm diesel's socket panel,
  and the hire set's control door and cable reel. From (-20.25, 14.5) that is towards the trailer stage's back rail,
  about 2.1 m away, and towards the default camera.
- **Orange lead:** runs from the sockets (or the reel) to Godot (+2.05, 0, +0.08) local, about world (-18.2, 0, 14.4),
  so it ends at the stage edge.
- **Sizes:**
  - Farm diesel: about 1.45 × 0.95 m on its pallet and 1.35 m to the top of the exhaust. With the lead, coil and
    jerry can its bounds run x -0.73..+2.06, z -0.50..+0.83 (Godot).
  - Hire set: 1.8 × 1.05 m canopy, 1.6 m tall, on a trailer about 3.3 m long. Bounds x -1.73..+2.06, z -0.73..+0.73.
  - The hire set's corner steadies dip up to 4 cm below the ground.
- **Picking:** the towable's pick box (5 × 2.6 × 3 m centred at y 1.3) still covers both units, and the 4 m hazard
  ring is unchanged.

## Materials

- Each file has `<Prefix>_MattePalette`: a palette texture (`lwf_farm_diesel_palette` / `lwf_hire_generator_palette`,
  248×8, 31 slots of 8 px), roughness 0.92, no metal.
- The glowing parts use `<Prefix>_Glow`: the same texture as emission, strength 3. These are the red-hot exhaust,
  the glowing engine side or louvres, the sparks and the flash. In Godot it imports as a second surface on the State
  mesh. To pulse the glow, animate that material's `emission_energy_multiplier`.
- Smoke is opaque low-poly puffs, so there is no transparency sorting:
  - normal: grey
  - overloaded: dark
  - fault: black

## Anchors for runtime effects (Godot local coordinates)

| | Smoke origin (exhaust top) | Sparks |
|---|---|---|
| Farm diesel | (-0.32, 1.35, 0.25) | the socket panel, (0.70, 0.79, 0.12) |
| Hire set | (-0.45, 1.72, -0.25) | the control door, (0.95, 0.98, -0.25) |

## Suggested runtime animation (code, optional)

The static state meshes already read in a still frame. To bring them to life:
- **Smoke:** add a `CPUParticles3D` at the smoke origin, with low-poly sphere meshes rising about 0.6 m/s and
  growing ×2 over 1.5 s.
  - Rate: normal 1.5/s grey; overloaded 4/s dark; fault 6/s black.
  - Hide the State node's static puffs, or keep them as the first frame.
- **Jitter:** when overloaded or faulted, offset the visual node by a random ±1.5 cm in x/z at about 20 Hz.
  Optionally also slowly pulse the `_Glow` material between energy 2 and 5.
- **Sparks:** a one-shot `CPUParticles3D` burst at the spark anchor every 0.6–1.2 s, using the palette's spark
  colour.

## World chip assets (`game/assets/ui/`)

| File | Use |
|---|---|
| `lwf_power_chip_zap_v1.svg` | Normal chip icon: gold disc `#d8a43b` with a dark green `#17302a` zap. Lucide `zap`, the same path as `icons/zap.svg`, at 2.4 stroke. |
| `lwf_power_chip_zap_alert_v1.svg` | Overloaded chip icon: white disc with an alert-orange `#b8551e` zap. |

Both are 24 × 24.

Chip spec, at the HUD's 1280 design size (scale with `Ui.S`):
- **Size:** height 32, fully rounded (radius 16), padding 5 / 10 / 5 / 7. Icon disc 22 with its zap at 13. Gap 7.
- **Text:** Source Sans bold 15.
- **Normal:**
  - background `Ui.Bar` `#17302a`, 1.5 px `Ui.Gold` border
  - value in `Ui.BarText` `#f5ebd6`, "/ 100" in regular `Ui.BarMuted` `#b9c4b8`
  - mini bar 46 × 6, radius 3: track `Ui.BarDeep` `#0e1f1a`, fill `Ui.Good` `#8fd1b5`
- **Overloaded:**
  - background `Ui.Alert` `#b8551e`, 1.5 px `Ui.AlertWash` `#f8dcc3` border, white text
  - the alert icon above
  - the HUD's `icons/triangle-alert.svg` in white after the value
- **Shadow and placement:** shadow 0 4 10 at 35% black. Anchored about 1.9 m above the generator's origin, with a
  2 px leader line down to it.
