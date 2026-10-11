# Steward first-person arms v1: integration

No code has been changed. This replaces `BuildStewardShiftArm()`'s placeholder: the yellow cylinder forearm and box
radio under `_eyeCamera`.

## Placement

All four GLBs are authored in the eye camera's local space (Godot: +X right, +Y up, −Z forward, metres) for the
prototype camera: `Fov = 72` (vertical, keep height), `Near = 0.05`. Instance each one and `AddChild` it to
`_eyeCamera` at **identity**, with no position or rotation of its own.

| Piece | Where it sits in camera space |
|---|---|
| Radio (left hand) | centre (−0.185, −0.115, −0.40); screen centre (−0.173, −0.093, −0.377), 34 × 24 mm |
| Left forearm | from the wrist at about (−0.20, −0.20, −0.41) out past the bottom-left corner. The elbow, at (−0.43, −0.52, −0.16), is off screen. |
| Right action hands | calm palm at about (0.215, −0.205, −0.47); door hand at (0.02, −0.17, −0.60); litter hand at (0.13, −0.215, −0.42) |

- **Nearest geometry:** about 0.14 m from the eye, comfortably past `Near`.
- **Shadows:** set `CastShadow = Off` on every arm mesh, as the placeholder does.
- **Clipping into walls:** the arms reach about 0.6 m. If they clip into walls or crowds when the steward stands close,
  draw them in a second pass. Use a `SubViewport` with its own `Camera3D` matching the eye camera, with the arms on a
  render layer the world camera culls. Or set the arm materials to `NoDepthTest` with `RenderPriority = 1`. The
  `SubViewport` keeps their lighting right.
- **FOV:** if the FOV ever changes (a run "kick", or a settings slider), keep the arms on a fixed 72° viewmodel camera
  so they don't stretch.

## The radio arm (always on)

`lwf_fp_arm_left_radio_v1.glb` stays up the whole shift.

**Walk bob** (camera space, per step phase φ from 0 to 1):

| | x | y |
|---|---|---|
| Walking | `0.004 · cos(π·φ)` | `0.006 · sin(2π·φ)` |
| Running | twice the walking amplitudes | twice the walking amplitudes |
| Idle breathing | 0 | `0.002 · sin(2π·t / 3.5 s)` |

**Look sway:** the arm lags the view by up to 2° of yaw and pitch, and returns at a rate of about 8 per second (lerp).

**Talk raise** when a radio call arrives or is answered:
- Translate by (+0.03, +0.05, +0.04), up and in towards the middle, over 0.25 s with ease-out.
- Hold while the call strip shows, then return over 0.3 s.
- Pulse `LWF_FP_RadioScreen` emission from 0.6 to 1.4 and back, at about 2 Hz, while a call is incoming.
- For an urgent call, tint the screen emission towards `b8551e` (alert).

## The right hand (actions only)

Exactly one of the three right-arm GLBs is visible at a time; the rest are hidden.

**Ease in and out** by offsetting the action GLB's root in camera space:

| Moment | Offset | Timing |
|---|---|---|
| Rest (hidden, fully off screen) | `REST = (0.12, −0.55, 0.15)` | n/a |
| Hold starts | from REST to (0, 0, 0) | 0.20 s, ease-out cubic: `e = 1 − (1 − t)³` |
| During the hold | stays at (0, 0, 0) | add the radio arm's walk bob × 0.5 |
| Success or fail | back to REST | 0.15 s ease-in cubic, after a 0.25 s pause so the result reads |
| Cancelled (released early) | back to REST | 0.15 s ease-in cubic, at once |

**Switching actions:** ease the current one out, then the new one in. Never cross-fade two hands.

`verification/_arms_calm-rest.png` confirms REST is off screen, and `_arms_calm-half.png` shows the slide.

| Action (prompt verb) | GLB | Notes |
|---|---|---|
| Calm, break up, escort | `right_calm` | Open palm, "steady now". The same pose works for breaking up a fight. |
| Open (toilet doors, gates) | `right_door` | The game opens the door on success. |
| Pick up (litter) | `right_litter` | Hide `LWF_FPLitterCup` while reaching. Show it on success, then ease out still holding it: the item went in the bag. |
| Help up (a collapse) | `right_door` | Reaching out and down reads as offering a hand. A dedicated pose can come later. |
