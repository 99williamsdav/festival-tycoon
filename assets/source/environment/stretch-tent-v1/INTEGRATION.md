# Stretch tent v1: integration

No code has been changed. All values are Godot metres in the tent's local frame: origin at the ground centre, front
(banner side) +Z. The full numbers are in `out/lwf_stretch_tent_layout_v1.json`.

## Placement and footprint

- **Asset:** `res://assets/environment/lwf_stretch_tent_v1.glb`. Instance it at identity under the build's node. Yaw
  in 90° steps rotates the footprint with it.
- **Build footprint:** 9.4 × 6.9 m including the guy-rope pegs, x −4.7 to 4.7 and z −3.45 to 3.45. On the 0.5 m ground
  grid that is **19 × 14 cells** (round up to 9.5 × 7.0 m).
- **Sail only:** 7.4 × 5.4 m, if guy ropes may cross a path. Guests walk under the ropes in the renders, and they read fine.

## Colliders (navigation obstacles)

Only the poles and pegs block. Everything under the sail is walkable.

| Kind | x | z | Radius |
|---|---:|---:|---:|
| Main pole | −1.70 | −0.30 | 0.07 |
| Main pole | 1.90 | 0.30 | 0.07 |
| Corner pole | ±3.70 | ±2.70 | 0.05 |
| Peg | ±4.60 | ±3.35 | 0.05 |

On the 0.5 m grid, block the one cell under each of these 10 points. Guy ropes don't block.

## Shelter area (shade and cover)

- **Rectangle for gameplay:** x −3.0 to 3.0, z −2.1 to 2.1. That's **25.2 m²**, or **12 × 8 cells**: 12 guests at
  2 m² each, or 15 packed at 1.7 m². Headroom inside is at least 2.03 m.
- **Exact polygon:** `shelter_polygon_m` in the layout JSON, 64 points: the sail's scalloped outline pulled 0.3 m inward.
  Use it if the cover check runs per position rather than per cell.
- **Huddle fill rule** (from the board): fill cells nearest the centre first, at about 0.6 m spacing, facing outward
  or at random. Guests who don't fit stay hunched outside.
- **Shade in a heatwave:** treat the shelter rectangle as shade regardless of sun angle. The real shadow shifts with the
  sun, but the rectangle is what the player can read and plan around.

## The faded roof (standard for every roofed build)

The game camera looks down at about 39°, so an opaque roof hides everyone more than about 0.6 m (heads) or 2.7 m (feet)
in from the eave facing the camera. The fix is to fade the roof while it's occupied:

1. On instance, duplicate the `LWF_StretchTent_Canvas` surface material for this tent. It's a `StandardMaterial3D`
   from the glTF. Set `Transparency = Alpha` and keep `CullMode = Disabled` (the sail is double-sided).
2. **Fade rule:** when one or more guests are inside the shelter rectangle, tween `AlbedoColor.A` from 1.0 to
   **0.30** over 0.25 s. Fade back to 1.0 over 0.5 s, 1 s after the last guest leaves.
3. Optionally also fade while the build is hovered or selected, so the player can see inside an empty tent.
4. **Keep shadow casting on** (`GeometryInstance3D.CastShadow = On`). Godot's shadow pass draws transparent geometry
   opaque, so the shade patch stays at full strength while the roof is faded. `tent-sun-shade-faded.png` shows the
   intended look.
5. Only the sail fades. Poles, ropes and the banner stay opaque, so the tent still reads as a solid object.
6. **Sorting:** the sail is a single transparent surface over opaque guests, so there's nothing to sort against. If
   two roofed builds ever overlap on screen, `Transparency = Alpha Depth Pre-Pass` keeps them stable.
7. Other roofed builds: give the roof its own material, named `*_Canvas` or `*_Roof`, so the same code can find and
   fade it.

## Wetness

The sail joins the shared wet-sheen uniform described in the heavy-rain INTEGRATION (darker, roughness down to about 0.22).

## Not included

- Side walls: deliberately none.
- Night lighting: the dusk festoons can be strung from the two tall poles' steel tips at (−1.7, 3.96, −0.3) and
  (1.9, 3.76, 0.3).
