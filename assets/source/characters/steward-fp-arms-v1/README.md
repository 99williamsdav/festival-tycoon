# Steward first-person arms v1

The steward shift's first-person arms, from the approved Steward shift board
(`Documents/Festival Tycoon concepts/steward-shift/`):
- **Style A:** bare forearm with a hi-vis wristband, as the steward is modelled (short-sleeved teal tee under the vest).
- **The user's change:** the **radio is in the left hand**, and the **right hand** eases in for actions.

**Rebuild:**
1. `blender -b --python build_fp_arms.py`
2. Copy `out/*.glb` to `game/assets/characters/fp/`, with copies in `assets/runtime/characters/fp/`.
3. Run the Godot import.

`fp_kit.py` is the geometry kit, shared with the concept board's renders. It builds the arms right-handed for the
radio; the build mirrors everything so the radio lands in the left hand.

## Files (`game/assets/characters/fp/`)

| File | Root | Nodes (tris) |
|---|---|---|
| `lwf_fp_arm_left_radio_v1.glb` | `LWF_FPArm_LeftRadio` | `LWF_FPArm_Left` (320): forearm, hi-vis wristband with reflective stripe, fist. `LWF_FPRadio` (140): handset, grille, antenna, gold knob, gold push-to-talk. `LWF_FPRadio_Screen` (12): the cyan screen, its own material. |
| `lwf_fp_arm_right_calm_v1.glb` | `LWF_FPArm_Right_Calm_Root` | `LWF_FPArm_Right_Calm` (232): open palm raised, "steady now" |
| `lwf_fp_arm_right_door_v1.glb` | `LWF_FPArm_Right_Door_Root` | `LWF_FPArm_Right_Door` (276): reaching straight out for a handle |
| `lwf_fp_arm_right_litter_v1.glb` | `LWF_FPArm_Right_Litter_Root` | `LWF_FPArm_Right_Litter` (276): reaching down. `LWF_FPLitterCup` (48): a crushed cup in the hand, hideable. |

- **Space:** everything is authored in the **first-person camera's own space** (Godot: +X right, +Y up, −Z forward,
  metres), so each GLB goes on as a child of the eye camera at the identity transform. Placement details are in
  `INTEGRATION.md`.
- **Materials:** flat named materials, the steward's palette:

| Material | Colour |
|---|---|
| `LWF_FP_Skin` | `c88f65` |
| `LWF_FP_HiVis` | `d8f126` |
| `LWF_FP_Reflective` | `bfc4b7` |
| `LWF_FP_Radio` | `242b2d` |
| `LWF_FP_RadioGrille` | `30373b` |
| `LWF_FP_RadioKnob` | `ffd166` |
| `LWF_FP_RadioScreen` | `6acfd1`, emission 0.6 |
| `LWF_FP_Cup` / `_CupBand` | `e8e2d4` / `c8352b` |

`out/fp_arms_report.json` lists the nodes, triangle counts, SHA-256s, and the radio's centre and screen centre in camera
space.

## Verification (`verification/`)

`render_arms.py` renders the **installed** GLBs through a 72° vertical-FOV camera at 1280 × 720, at the blend states in
`INTEGRATION.md`:

| Render | Shows |
|---|---|
| `_arms_idle` | The radio arm only |
| `_arms_calm` | Calm, fully in |
| `_arms_calm-half` | Calm, half way through the ease-in |
| `_arms_calm-rest` | Calm at the rest offset: fully off screen, as it must be |
| `_arms_door`, `_arms_litter` | The other two actions, fully in |

The HUD package's `verification/` composites these over the first-person captures, with the approved HUD.
