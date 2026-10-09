# Rain ground v1: straw on the mud, and grid track matting

From the approved heavy-rain board (`Documents/Festival Tycoon concepts/heavy-rain/`):
- **Straw** as a ground-map state. Revised after the picks: the edges are rough and roughshod, with ragged tufts
  spilling into the mud, uneven widths and nothing ruler-straight.
- **Track matting A:** plastic grid mats, a buildable walkway that never churns.

**Rebuild:**
1. `python make_straw_textures.py`
2. `python build_track_mat.py texture`, then `blender -b --python build_track_mat.py`
3. `python straw_reference.py` (verification ground only)
4. Copy `out/lwf_straw_*.png` and `out/lwf_track_mat_grid_v1.glb` to `game/assets/environment/`, with copies in
   `assets/runtime/environment/`.
5. Run the Godot import. The textures are lossless with mipmaps and `detect_3d/compress_to=0`.

## Straw (textures for the ground shader)

| File | Size | Tiles every | What |
|---|---|---|---|
| `lwf_straw_fresh_v1.png` | 1024² RGBA | 3 m | Loose golden strands, criss-crossed. Alpha is where straw covers the mud (about 85%). |
| `lwf_straw_trampled_v1.png` | 1024² RGBA | 3 m | Flattened along the flow of feet, browner, sparser, with semi-transparent boot-print smears of mud. |
| `lwf_straw_breakup_v1.png` | 512² RGB | 6 m | Edge noise. R is a broad wobble (about 1 m, for uneven widths), G is tuft clumps (20–30 cm, ragged spill), B is fray (a few cm, single strands). |

All three wrap seamlessly. The noise is FFT-filtered, so it has no grid artefacts.

`straw_reference.py` implements the shader rule in numpy, exactly as `INTEGRATION.md` gives it. The verification
ground is made with it.

## `lwf_track_mat_grid_v1.glb` (10 tris)

- **Shape:** one charcoal ground-protection panel, **2.0 × 1.0 m**, 4 × 2 ground cells, 3 cm thick.
- **Top:** `LWF_TrackMat_Grid`, alpha scissor 0.5, with diamond holes the ground shows through and gold connector lugs
  on the edges.
- **Sides:** a solid `LWF_TrackMat_Rim`.
- **Frame:** origin at the panel's ground centre. The long side (2.0 m) is on X, across the path; the short side
  (1.0 m) is on Z, along the path.

Tiling rules are in `INTEGRATION.md`.

## Verification (`verification/`)

`render.sh` renders on the farm under the game camera in a heavy shower: the straw reference ground, and the
**installed** mat GLB laid by the run rules (`place_ground.py`).

| Render | Shows |
|---|---|
| `straw-tileset-zoom13.png` (+ `-with-rain`) | The same layout fresh (left) and trampled (right): a straight, a T, a turn and three ends |
| `straw-fresh-close.png` and `straw-trampled-close.png` | Close-ups of each |
| `straw_tileset_ground.png` | The top-down reference ground itself (128 px per m) |
| `mats-zoom30.png` (+ `-with-rain`) | Two runs off the track by the gate: west then up to the stage crowd, east then up to the toilets |
| `mats-corner-close.png` | A corner |

The scene's grass tiles sit a few cm above 0 in places, so the verification lifts the straw plane to 6 cm and the mats
to 4 cm. In the game the straw is drawn by the ground shader, and the mats sit on the ground.
