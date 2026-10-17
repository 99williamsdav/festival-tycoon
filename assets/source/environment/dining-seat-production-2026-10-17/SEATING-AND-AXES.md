# Seat anchors, axes and access

## Coordinate convention

Metres, native scale1. Blender: X along each asset's long direction, Z up, +Y is the unrotated guest's facing direction. GLB export uses Blender's standard Y-up conversion: **Blender (x,y,z) → Godot (x,z,-y)**. Therefore an unrotated guest faces Godot -Z. All production mesh transforms are identity at origin; no tabletop or off-ground export pivot.

The local `Seat_*` empties mark a seat-surface reference, not a standing guest root or a pelvis bone. Their local +Y arrows indicate facing in Blender. Their Z/.45 surface height becomes Godot Y/.45. A later seated pose must apply its own pelvis/pose offset so the real mesh contacts that surface. Do not place a standing guest origin at the empty and assume the pose fits. No runtime mount/animation contract is implemented by these names.

| Anchor | Blender surface position m | Facing | Godot surface position m |
|---|---|---|---|
| Seat_H1_01 | (-.286, .235, .450) | +Y | (-.286, .450, -.235) |
| Seat_H1_02 | (.286, .235, .450) | +Y | (.286, .450, -.235) |
| Seat_P1_North_West | (-.372, -.565, .450) | +Y inward | (-.372, .450, .565) |
| Seat_P1_North_East | (.372, -.565, .450) | +Y inward | (.372, .450, .565) |
| Seat_P1_South_West | (-.372, .565, .450) | -Y inward | (-.372, .450, -.565) |
| Seat_P1_South_East | (.372, .565, .450) | -Y inward | (.372, .450, -.565) |

Exact exported positions/facing vectors are in `manifest-hay.json` and `manifest-picnic.json`. Review sources show the actual fitted body placement. Contact patches lie behind the hip joint, so P1 pelvis projections are near the inner edge of the benches while the supported buttock mesh rests on the bench top.

## H1 approach and stand-up suggestion

The twine bands at X±.475 lie outside the measured pelvis contact patches centered at X±.286. Preserve an approximately .6 m wide approach lane per seat on the facing side, and .8 m clear beyond the front edge (Blender +Y) for standing/turning. Suggested reservation rectangle: X[-.90,.90], Y[-.45,1.15]. This is a design reservation, not a tested navigation collider. Current seated shoe envelope extends to +Y .721; the suggested front reservation leaves about .43 m beyond those toes.

## P1 frame and access constraints

A-frame planes are X±.755, with .085 m beam thickness, giving inner faces at X±.7125. Seat centers are X±.372. At seated leg height the tested native body/foot geometry remains clear of those frame planes. The horizontal bench crossbearers are at Z.3415–.3965, centered on those planes. Tabletop lower surface is Z.690; exact posed thigh clearance is .140–.144 m.

Overall plank ends are X±.98, so the end overhang past the A-frame center is .225 m; past the outer beam face it is about .1825 m. That is **not** an adult passage. Do not route a walking guest through this narrow end slot or through the A-frame triangle. Table legs and crossbearers obstruct direct end entry to the under-table area.

Reserve open space outside both long benches (Blender Y±.856) and beyond the ends. Suggested outer approach/turn space: .75 m along each long side, .65 m beyond each end. Suggested reservation rectangle: X[-1.63,1.63], Y[-1.61,1.61]. Suggested standing points per seat: (seat X, sign(seat Y)×1.20, 0), facing inward. These points are outside the bench envelope and need a later mount approach/leg-swing pose; a straight translation into the final occupied pose would cross the attached bench. A credible runtime solution must author that transition and test frame, knee and held-food clearance over time.

The four static occupied samples demonstrate actual fitted sitting and held-food clearance only. Entry, dismount, stand-up motion, pathfinding, approach occupancy and collision shapes are untested. No traversal or runtime integration is claimed.

## Camera evidence

Close/rear/above renders are inspection views. `*-07-gameplay-min18.png`, `*-08-gameplay-default62.png`, and `*-09-gameplay-empty-min18.png` are full 1600×900 frames, camera elevation38.853°, yaw325°, orthographic horizontal spans32 m and110.222222 m respectively (vertical18 m /62 m). The camera is placed farther away as span increases so the ground plane remains in front of it. No crop or enlargement is presented as gameplay scale.
