# Rigged walk test v1

4 October 2026. A one-session test of the skeletal route for guests: one skinned male and one skinned female v6 body,
each with `walk` and `idle` animations. This is a test, not a replacement for the pose bodies.

Rebuild: `blender -b --python build_rigged.py -- <female|male> out`, then copy `out/*.glb` to
`game/assets/characters/` and `assets/runtime/characters/`. Each build writes `out/<name>.json` (bones, stride,
attachment points, sha256).

## Files

| File | Tris | Bones | Animations |
|---|---:|---:|---|
| `lwf_attendee_male_rigged_test_v1.glb` | 1592 | 17 | `walk`, `idle`, `walk_carry`, `idle_carry`, `drink`, `drink_soft`, `carry_litter`, `walk_brisk`, `walk_brisk_carry` |
| `lwf_attendee_female_rigged_test_v1.glb` | 1620 | 17 | the same |

- **Source body:** the designer's generator, `attendee-v6-draft/build_attendee.py`, in its relaxed pose. That means
  the same parts, the same material (`DirectionA adult shared recolour palette`, 96×8) and the same slot contract as
  the shipped v6 bodies, so `ApplyGuestPalette` works unchanged.
- **Hair:** left out, as on the shipped bald bodies. Attach `lwf_hair_*_default_v1` (or a cap, crown, glasses or
  beard) to the Head bone.
- **Conventions:** origin at foot ground centre, facing Godot −Z, real metres. One mesh, `LWF_Attendee_Body`, skinned
  to `LWF_Attendee_Rig`.

## Skeleton

The bone names follow Godot's `SkeletonProfileHumanoid`, so a retarget or a `BoneMap` works later:

```
Hips
├─ Spine ─ Chest
│          ├─ Neck ─ Head
│          ├─ LeftUpperArm  ─ LeftLowerArm  ─ LeftHand
│          └─ RightUpperArm ─ RightLowerArm ─ RightHand
├─ LeftUpperLeg  ─ LeftLowerLeg  ─ LeftFoot
└─ RightUpperLeg ─ RightLowerLeg ─ RightFoot
```

Weights are authored per part rather than by heat weighting, which struggles with separate low-poly lofts:
- **Head parts:** Head, blended into the Neck and Chest below the chin.
- **Torso tee:** Hips → Spine → Chest by height.
- **Sleeves:** UpperArm, easing into the Chest at the armpit.
- **Arms:** UpperArm / LowerArm / Hand, with ±4.5 cm blends at the elbow and wrist.
- **Trousers:** the Hips above the leg split, then UpperLeg / LowerLeg with a ±6 cm blend at the knee.
- **Shoes:** Foot, with the top of the shoe blended into the LowerLeg.

The knees and elbows keep their volume through the walk's bends: up to about 60° at the knee in swing and 24° at the elbow.

## Animations

- **`walk`:** 28 frames at 24 fps (27 intervals, 1.125 s for two steps), loop. It's in place and **foot-planted**.
  - Legs are solved with two-bone IK against a planned foot path. Through stance (62% of each leg's cycle) the heel,
    then the whole foot, then the ball stay fixed on the ground: heel-strike rocker, flat, then toe-off rocker. The
    swing lifts the foot 7.5 cm.
  - Soft knees: the pelvis rides about 4 cm lower than rest, with a ±1.2 cm bob that peaks at mid-stance.
  - Opposite arm swing of about ±19° (female ±17°), ±1.2 cm hip sway, a 4° pelvis turn against an 8° chest
    counter-turn, and a small head stabilise.
- **`idle`:** 49 frames, 2.0 s, loop. Breathing (chest and spine), a small weight shift, and slight arm drift.

### Stride: match the root speed to the cycle

| | Step length | Metres per cycle | Cycle | Speed at 1× playback | Planted-foot slide |
|---|---:|---:|---:|---:|---:|
| Male | 0.53 m | 1.06 m | 1.125 s | 0.942 m/s | ≤ 5 mm |
| Female | 0.515 m | 1.03 m | 1.125 s | 0.916 m/s | ≤ 5 mm |

- Move the guest's root at exactly metres-per-cycle ÷ 1.125 s at 1× playback. The planted foot then stays put: the
  build measures each contact point with the root moving at that speed and reports the worst slip
  (`planted_foot_max_slide_mm` in the json).
- For other walking speeds, set `AnimationPlayer.speed_scale = guestSpeed / speedAt1x`. Feet stay planted at any
  scale, because stride and cadence scale together.
- Cadence looks natural within about 0.7–1.4×.
- The stride is limited by leg reach (these legs are straight at rest). The build lowers it automatically if a pose
  would over-extend.

## Carrying and drinking

The right arm is solved with two-bone IK so that a cup frame follows defined targets. The hand grips the cup the way
the pose generator's `cup_hand` does. Legs, hips and the left arm are identical to `walk` and `idle`, so the stride
and speed figures above are unchanged.

| Animation | Frames | What it does |
|---|---|---|
| `walk_carry` | 28 (1.125 s) | Walk, right hand holding the cup in front at the pose bodies' `drink_hold` anchor. The cup rides with the chest, with a 1.2 cm swing. |
| `idle_carry` | 49 (2.0 s) | Idle, holding the cup. |
| `drink` | 67 (2.75 s) | Standing beer sip from the hold position: hold 0.35 s, raise 0.6 s, sip 1.0 s with small sips and the head tipped back, lower 0.6 s, hold. At the lips the cup matches the `drinking` beer anchor: rim at the mouth, tilted 55°. |
| `drink_soft` | 67 (2.75 s) | The same, to the soft-drink anchor (tilted 25°, straw at the lips). |
| `carry_litter` | 28 (1.125 s) | Walk, right hand low in front, for carrying a bit of litter or an empty cup to a bin. |

### Cup socket: `LWF_RightHand_Cup`

- A node parented to the `RightHand` bone. Godot imports it as a `BoneAttachment3D` child under the `Skeleton3D`, so
  it follows the hand in every animation.
- Add the cup (or the litter) as a child of that node at the identity transform.
- Convention: the same as the manifest's prop anchors. The prop's origin goes at the socket, unrotated, and the cup
  is upright while carrying.
- Measured on the exported GLB (Godot root-local):

| | `idle_carry` frame 1 | Manifest `drink_hold` anchor | `drink`, mid-sip | Manifest `drinking` beer anchor |
|---|---|---|---|---|
| Male | (0.197, 1.142, −0.236), rot 0 | (0.197, 1.142, −0.236) | (−0.003, 1.491, −0.172), rot x 56° | (0, 1.491, −0.172), rot x 55° |
| Female | (0.171, 1.090, −0.236), rot 0 | (0.171, 1.090, −0.236) | (−0.003, 1.423, −0.168), rot x 56° | (0, 1.423, −0.169), rot x 55° |

- In `walk_carry` the cup tilts about 3° forward with the walk's spine lean, and swings ±1.2 cm.
- In `carry_litter` the socket points along the hanging hand (rotated −90° about X compared with the carry pose).
  That suits a crumpled item. An empty cup would need a +90° X rotation under the socket to read upright.

Verification: `walk_carry.gif`, `drink.gif` (game camera, beer and soft cups attached to the socket),
`walk_carry_side_view.png`, `carry_litter_side_view.png`, and `carrycheck.py` (the socket-against-anchor
measurement).

## Brisk walk (`walk_brisk`, `walk_brisk_carry`)

Tuned to the game's guest pace. It uses the same foot-planted IK as `walk` (heel, then flat, then ball fixed on the
ground through stance), with faster, longer-striding parameters.

| | Step length | Metres per cycle | Cycle | Speed at 1× | Planted-foot slide | Pelvis drop |
|---|---:|---:|---:|---:|---:|---:|
| Male | 0.708 m | 1.417 m | 0.833 s (21 frames) | 1.70 m/s | ≤ 6 mm | 6.0 cm |
| Female | 0.708 m | 1.417 m | 0.833 s (21 frames) | 1.70 m/s | ≤ 6 mm | 6.5 cm |

- **Cadence and stance:** 144 steps a minute, with 56% of each leg's cycle in stance. That's still a walk, since a
  double-support phase remains; the walk-to-run change is around 50%.
- **Bigger rockers:** a heel strike of 18° and a toe-off of 38°, with 9.5 cm of swing clearance.
- **Liveliness:** 1.8 cm of bounce, 1.4× the hip sway and pelvis turn, about ±26° of arm swing with bent elbows,
  and a 6° forward lean.
- **Why the knees bend more:** these legs are straight at rest, so a 1.42 m stride needs the pelvis about 6 cm lower
  than at rest. The build searches for the smallest drop that fits, which reads as a purposeful, slightly bent-knee
  stride.
- **`walk_brisk_carry`:** the same legs, with the right arm on the cup socket as in `walk_carry`. For carrying at
  about 85% of brisk (about 1.45 m/s), play `walk_brisk_carry` at speed_scale 0.85, or blend it with `walk_carry` by
  speed. Both are foot-planted at any playback rate.
- The `stride_brisk` block in `out/*.json` has these figures.
- **Verification:** `walk_brisk.gif`, `walk_brisk_zoom30.gif`, `walk_brisk_zoom62.gif` (root moving at 1.70 m/s over
  the 0.5 m grid) and `walk_brisk_side_view.png`.

## Staff bodies and the hurry walk

### Staff: `lwf_{medic,steward,maintenance,sound}_{male,female}_rigged_test_v1.glb`

Build with `blender -b --python build_rigged.py -- <sex> out <role>`.

- The same rigged body as the guest file: mesh `LWF_Attendee_Body`, the guest palette material, the same skeleton,
  every clip, and the `LWF_RightHand_Cup` socket. `ApplyRoleBodyPalette` repaints it as before.
- Plus the role garment `LWF_<Role>_Garment`: the v2 overlay (`lwf_<role>_<sex>_overlay_v2.glb`) with unchanged
  geometry and its own `lwf_crew_trim_palette_v1` material. It's skinned to the same armature by transferring the
  body's weights from the nearest surface, so vests, belts and pouches follow the torso, hips and arms.
- Exclude the garment from the body repaint, as with the overlays today. It is a separate mesh with its own material.

### `walk_hurry` (in all ten files, guests included)

A staff power walk, tuned to 2.4 m/s at 1×, using the same foot-planted IK as the other walks.

| | Step length | Metres per cycle | Cycle | Speed at 1× | Planted-foot slide | Pelvis drop |
|---|---:|---:|---:|---:|---:|---:|
| Male | 0.80 m | 1.60 m | 0.667 s (17 frames) | 2.40 m/s | ≤ 7.4 mm | 6.5 cm |
| Female | 0.80 m | 1.60 m | 0.667 s (17 frames) | 2.40 m/s | ≤ 7.3 mm | 7.5 cm |

- **Cadence and stance:** 180 steps a minute with 52% stance. That's still just a walk, since a short double-support
  phase remains.
- **Rockers and clearance:** a 20° heel strike, a 42° toe-off, and 11 cm of swing clearance.
- **Upper body:** a 9° lean, with arms pumping at about ±29° and elbows bent to about 60°.
- **Faster staff:** 2.8 m/s is speed_scale 1.17. The feet stay planted at any rate, and cadence reaches about 210
  steps a minute, a fast power walk. Beyond about 1.25× it starts to read as a jog.
- The `stride_hurry` block in `out/*.json` has these figures.

Verification:
- `staff_walk_hurry.gif`, `staff_walk_hurry_zoom30.gif`, `staff_walk_hurry_zoom62.gif`: the four roles with the
  role palettes, the root at 2.4 m/s.
- `walk_hurry_side_view.png`
- `staffrender.py`

## Attachment points

Positions are Godot rest-pose, root-local:

| | Bone | Male | Female |
|---|---|---|---|
| Head (hair, hats, glasses, beard) | `Head` | (0, 1.445, 0) | (0, 1.377, 0) |
| Right hand (held drinks) | `RightHand` | (0.235, 0.838, −0.014) | (0.209, 0.804, −0.013) |

- Attach with `BoneAttachment3D` on the imported `Skeleton3D`.
- The hair and head pieces are authored in the root frame. Add them under the BoneAttachment with the inverse of the
  head bone's rest transform, i.e. keep their root-space rest position. That's what the verification renders do.
- Held drinks: the relaxed hand hangs at the side. The pose bodies' cup anchors belong to differently posed arms, so
  drinking while walking would need a held-arm overlay. That's a later step if the test is approved.

## Verification (`verification/`)

- `walk_side_view.png`: eight frames from the side, both bodies.
- `walk_sheet.gif`, `walk_contact_sheet.png`: the game camera, four guests with the root moving at exactly
  stride ÷ cycle time over a 0.5 m grid. Watch the grid to spot foot sliding.
- `walk_zoom30.gif`, `walk_zoom62.gif`, `walk_zoom62_x4_strip.png`: ten walkers at true game pixel density, zoom 30
  and 62, enlarged with nearest-neighbour.
- `walkrender.py`, `strip.py`: the render scripts.
