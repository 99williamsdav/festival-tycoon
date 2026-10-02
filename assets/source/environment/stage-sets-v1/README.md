# Tier-1 stage sets — v1

2 October 2026. Production assets from the approved tier-1 stage-set concept, with the user's changes applied:
electronic is a rug, a lamp and a trestle table with a laptop and mixing desk as set dressing on the back half (nobody plays it this pass), metal has a black rug
under its stack, and the pop curtain is lowered and thinned so it never hides the band from the other three
camera rotations. Project-original geometry (Blender 4.1.1, Pillow palette). Static and presentation only:
no colliders, no animation, and no picking or simulation identity.

Rebuild: `python make_palette.py`, then `blender -b --python build_stage_sets.py -- out [curtain_h=1.6] [strip_w=0.07]`.
`out/stage_sets_report.json` lists nodes, tris and placements.

## Frame and placement

Every GLB is authored in the **trailer stage's local frame**: parent it straight to the stage node
(Godot (−16, 0, 11), rotated 90°) with an identity transform. In that frame:
- x runs along the trailer, with the steps at the −x end (x ≈ −4.25 to −3.2). The usable deck is x ≈ −4.66 to +2.9;
  beyond that is the hitch.
- +Z points to the audience: the front edge is z = +1.22, the back rail z ≈ −2.13.
- The deck top is y = 1.2.

Performer marks in this frame: (96,150) → (−0.25, 1.2, 0.25); (94,146) → (1.75, 1.2, −0.75);
drum mark (93,152) → (−1.25, 1.2, −1.25). Nothing tall stands on a mark or on the walk from the steps; rugs are flat
(0.015 m) and walkable.

## Files (`game/assets/environment/`, copies in `assets/runtime/environment/`)

| File | Tris | Nodes | Contents (sizes in m: along x × deep z × tall; positions are stage-local centres) |
|---|---:|---|---|
| `lwf_stage_set_folk_v1.glb` | 96 | Rug, Props, LampShade | Rug #A8432F, 3.6 × 2.2 at (0.3, 1.2, −0.4); standard lamp 0.45 × 0.45 × 1.81 at (2.4, 1.2, −1.85) |
| `lwf_stage_set_indie_v1.glb` | 36 | Rug, Props | Rug #7E6A9A, as folk; cream combo amp 0.8 × 0.3 × 0.62 at (2.5, 1.2, −1.6), facing +Z |
| `lwf_stage_set_electronic_v1.glb` | 372 | Rug, Props, LampShade | Rug #2F4A35 (dark green), as folk; standard lamp with a teal cap at (2.4, 1.2, −1.85); trestle table 1.5 × 0.55 × 0.78 (top y 1.98) centred (0.45, 1.2, −1.75) on the back half between the drum kit and mark (94,146), with a laptop (lid on the audience side) and a small mixing desk. Set dressing only: nobody plays it this pass. |
| `lwf_stage_set_metal_v1.glb` | 72 | Rug, Props | Rug #1C1C1C, 4.3 × 2.9 at (0.65, 1.2, −0.55), running under the stack; amp stack (2 cabs + head) 0.75 × 0.3 × 1.68 at (2.3, 1.215, −1.8) |
| `lwf_stage_set_pop_v1.glb` | 396 | Curtain, Props | Tinsel curtain, pink and silver strips 0.07 wide at a 0.2 m pitch, 1.6 tall, x −3.4 to 2.6 at z −2.0, on a grey rail with end posts |
| `lwf_stage_set_punk_v1.glb` | 96 | Props, Banner, LetteringArea | Bedsheet 5.0 × 0.95, bottom 1.45 above the deck, centred (−0.1, 3.125, −2.0), on two poles at the back rail |

- **Rugs** are their own `Rug` mesh with their own untextured material `LWF_StageSet_Rug`, so the colour can come from
  the act at runtime (override the albedo). The colours above are the defaults.
- **Lamp shades** (`LampShade`) use an emissive material, `LWF_StageSet_LampGlow`, which the concept-5 dusk lighting
  can drive.
- **The punk banner** is a `Banner` mesh (bedsheet, palette) with a `LetteringArea` node at its face centre,
  (−0.1, 3.125, −1.987), with extras `width_m` = 4.6 and `height_m` = 0.8. As with the gate sign, the node's local
  +Z faces the audience, +X is text-right and +Y is up; parent a Label3D to it for the act's real name.
- **The pop curtain** is its own `Curtain` mesh in case it ever sways. It was checked from all four camera
  rotations (`verification/stage-sets-on-stage-and-pop-rotations.png`): at 1.6 m tall and about 35% coverage, the
  band stays visible through it from behind.
- Everything else is in `Props`, on one matte 128×8 palette `LWF_StageSet_MattePalette` (Closest filtering,
  swatch-centre UVs).
