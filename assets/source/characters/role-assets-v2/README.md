# Role assets v2: staff and band refitted to the v6 attendee

30 September 2026. The approved role-assets-v1 package, rebuilt on the v6 attendee bodies from `../attendee-v6-draft/`. The in-game guests, staff and performers now share one body family. **This was a programmatic refit and still needs designer review.** The review `.blend` files and in-engine renders are listed under "Review" below.

## What changed

**Staff overlays** (medic, steward, maintenance, sound × male/female). The v1 garment meshes were moved from the male-v5/female-v3 body onto v6 (`refit_overlay.py`, `refit_common.py`). Topology, UVs, the `lwf_crew_trim_palette_v1` material and node names are unchanged; only vertex positions move.
- Each garment vertex is matched to the nearest old-body surface point in its region (tee, hip, each trouser leg, neck). That point is mapped to the same relative height and position on the v6 region, and the vertex keeps its original normal offset. Layering such as strips over the vest over the tee is therefore preserved.
- The displacement is smoothed (σ 12 mm). A clearance pass then keeps every vertex that was outside the old body outside the new one, by at least `min(original gap, 4 mm)`.
- On v6 the old garments floated: about half of every garment's vertices sat more than 25 mm off the body, worst on the slimmer female. After the refit, no vertex is inside the body, and the minimum clearance is 3.8–7.4 mm. Median gaps match v1 within 0.6 mm, except the sound crew's. Their median dropped from 17–21 mm to 11–12 mm because the v1 pocket and cable geometry was already partly inside the old leg, and the refit keeps it outside.

**Performer bodies**: `lwf_performer_<sex>_body_v2.glb`. This is the v6 relaxed body, split into `LWF_Performer_BodyCore`, `LWF_Performer_IdleLeftArm` and `LWF_Performer_IdleRightArm`. **Change from v1:** each idle arm now includes its tee sleeve and sleeve lining. The playing arms carry their own posed sleeves, so hiding the idle arms never leaves a relaxed sleeve behind a raised arm.

**Band kits**: `lwf_<role>_<sex>_kit_v2.glb`, built by `build_performer_v6.py`, which runs the designer's `build_attendee.py` with an added `play` pose.
- Playing arms use the generator's two-bone IK with relaxed segment lengths. Sleeves follow the posed upper arm. Each hand is a v6 mitten with thumb, aimed from the shoulder with a per-hand finger bias.
- The strum/pluck elbow sits out and forward so the forearm rests over the instrument's upper edge. With v1's back-and-down elbow, the forearm passed through the guitar body.
- **Instrument placement change.** v1 instruments float about 0.3 m in front of the body, held by stretched arms. True-length v6 arms reach only 0.42–0.44 m, so each instrument moves in toward the body until both hands reach. The build searches for the smallest move (closer to the body, then up to +0.2 m higher) plus a fret-hand slide of up to 0.3 m along the neck, keeping ≥12 mm between body and instrument:

| Kit | Closer / higher | Fret-hand slide | Body gap | Hand-to-instrument gap (L / R) |
|---|---|---|---|---|
| guitarist female | 75 / 120 mm | 75 mm | 16.1 mm | 1.2 / 0.0 mm |
| guitarist male | 80 / 160 mm | 0 | 19.6 mm | 0.9 / 0.4 mm |
| bassist female | 70 / 100 mm | 225 mm | 21.3 mm | 0.1 / 1.1 mm |
| bassist male | 80 / 140 mm | 150 mm | 19.6 mm | 0.5 / 0.3 mm |
| drummer (both) | 85 / 20 mm | — | ≥185 mm | sticks keep v1 grip offsets |

- A few mitten vertices overlap each instrument at the grip, as noted for v1.
- The drum hardware is placed on the stage and shared by both bodies, so it uses the female requirement for both. It ships as `lwf_drum_hardware_only_v2.glb`, moved by the same amount.
- Loops are the v1 keyframes (single-axis rotation, 17 frames at 24 fps). Each amplitude is scaled by old lever ÷ new lever so hand travel matches v1, since v6 arms pivot at the shoulder rather than the sleeve end. Each kit exports one merged clip named `Animation`, as in v1.
- The strap is refitted like a garment. Its off-body ends blend onto the instrument move.

## Contract kept

- Node names, per-kit node structure (sticks are children of drummer arms), materials and embedded palettes are the same as v1, so `Main.RoleBodies.cs` / `Main.LivePerformance.cs` only change file suffixes.
- Metres, feet at ground centre, Godot −Z forward. Attach GLB roots at identity under the stable person root.
- Role body palettes (`lwf_<role>_body_palette_v1.png`) are unchanged. The v6 slot layout is the same, and the v6 `SleeveLining` takes the shirt colour from slot 3.

## Rebuild

Blender 4.1, background mode, from this folder:

```
blender -b --python refit_overlay.py -- <steward|medic|maintenance|sound> <female|male> <this folder>
blender -b --python build_performer_v6.py -- <female|male> <this folder> <relaxed|guitarist|bassist|drummer>
blender -b --python build_performer_v6.py -- any <this folder> hardware
```

Each run writes `<name>.blend` (review scene: v6 body, and drum hardware or instrument where relevant), `<name>.glb` (runtime only) and `<name>.json` (build report with SHA-256 and fit measurements). Then copy the GLBs to `game/assets/characters/`. `AttendeePoseTests.RoleAssetsV2MatchTheirBuildReports` checks the installed copies against these reports.

## Review

- In Godot 4.7.2, all 17 GLBs import cleanly. Each kit has exactly one looping `Animation` clip, two `*PlayingArm` meshes, and hides both idle arms on attach. Close-up gameplay-angle renders are in `reports/evidence/roles-v6/` (`band-*.png`, `staff-*.png`).
- The in-game attendee pose capture passes with unchanged gameplay hashes.
- The in-game role capture (`--capture-r005q-roles`) currently fails on a simulation check before it reaches the performers ("did not place all three booked performers on deck"). It fails the same way on the committed `HEAD` with the v1 assets, so the cause is not this package. Performer kits have therefore not yet been seen through a live stage set in-engine.
- Designer review points: the raised instrument height (guitar/bass bodies now sit at belly–lower chest), the female bass fret hand sitting 225 mm down the neck, and whether any vest edge or pocket needs hand adjustment.
