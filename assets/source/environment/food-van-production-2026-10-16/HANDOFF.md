# Approved three food van bodies — production package

Status: independently reviewed PASS, no unresolved material findings. See [REVIEW.md](REVIEW.md), supplied by the parent after review of this frozen production package. No game integration or repository edits made.

The three approved PNG concepts were inspected directly. Chips uses cream/blue, a faceted rounded roof silhouette, cream wheel arches, towbar and blue wrap at the ends. Pizza uses separate timber planks, dark posts, a shallow terracotta gable and oven/flue. Curry uses a compact cab in the former hitch zone, ribbed plum body/roof, mustard hatch shutters and staff door, three visible axles and twin steam vents. All have hollow interiors with floors, walls, kitchen equipment, an open service hatch and static closed rear staff doors. No raised flap or awning is included.

## Files

Open `review.html` for the local gallery. `source/ft_food_{chips,pizza,curry}_production_v1.blend` contains named editable component meshes, packed textures, review camera/lights/ground. `runtime/ft_food_{chips,pizza,curry}_production_v1.glb` is the consolidated production mesh with embedded textures. People, review ground, lights and cameras are excluded from GLBs. Curry additionally exports the two empty steam markers `LWF_FoodVan_SteamVent_L` and `LWF_FoodVan_SteamVent_R`.

| Variant | Triangles | GLB mesh count | Materials/surfaces | GLB bytes |
|---|---:|---:|---:|---:|
| Chips | 2,370 | 1 | 4 | 272,372 |
| Pizza | 2,066 | 1 | 4 | 311,364 |
| Curry | 5,244 | 1 | 4 | 488,540 |

Materials are one shared 21-colour palette, one name artwork, one roof icon artwork, and the retained iron icon support material. Each GLB embeds three PNG images and has no external image URIs. The original approved name/icon artworks are reused; they are packed in the `.blend` and included as reproducible read-only source copies under `references/`.

## Fixed contract and fit

Blender metres, Z up, X body length, service side Y negative. Convert to Godot `(X,Z,-Y)`. Keep existing `ApprovedFoodVanAssembly` offset Godot `(-2.15,0,0)`. Server assembly coordinates stay Blender `(2.15,-.88,.47)`, yaw PI, scale 1. Equivalent existing vendor anchor is Godot `(0,.47,.88)`.

Hatch remains X `.9..3.4`, Z `1.21..2.23`, public plane Y `-1.1`. Counter remains X `.79..3.51`, Y `-1.5..-1.1`, top Z `1.235`. Floor top stays `.47`. The front lower skin is `.02m` thick extending inward to Y `-1.08`, which clears the existing apron/shoes without moving staff. Main body remains X `0..4.2`, nominal Y `+/-1.1`. Rear door visible panel is `.81m` wide and `1.67m` tall; it is static production geometry with a handle, hinges, inset frame and step, not an animated door.

| Variant | Full Blender X | Full Blender Y | Maximum Z |
|---|---|---|---:|
| Chips | -1.255..4.600 | -1.500..1.314 | 3.9595 |
| Pizza | -1.255..4.600 | -1.500..1.314 | 3.9415 |
| Curry | -1.470..4.600 | -1.500..1.314 | 3.8415 |

Old physical X was `-1.26..4.59`. Curry's bumper extends `.21m` beyond the old hitch at the front. All variants' rear step reaches `.01m` beyond the old physical rear. The existing reserved Blender area is X `-1.60..4.90`, Y `+/-1.75`; all geometry fits. Curry retains `.13m` front margin and `.30m` rear margin; no footprint expansion is required. These are measured bounds, with no concept deformation by scaling the staff or shrinking the hatch.

## Verification and render interpretation

`verification/checks.json` records the final exported/reimported geometry: triangle counts and bounds match the source, four materials, three embedded images, no armature, and correct curry marker names. Final roundtrip artwork was inspected in all three `*-05-roundtrip.png` views.

Actual source male and female food rigs were sampled against each final GLB for every integer frame of all six food clips, 170 frames per rig/variant, 1,020 total. Body, apron, hair and cap have zero BVH triangle intersections. Foot displacement is zero. Held tray/plate meshes were tested with full triangle BVH at all 13 offer frames and the final pickup frame: three prop types × 14 frames × six cases = 252 prop/environment checks, zero intersections. Lowest vertical clearance above counter is `.2962m` for pizza plate; trays `.3100m`. Socket tilt is zero in these checked source poses.

These are keyed-frame static geometry checks. Runtime animation crossfades, customer attachments, terrain and game integration remain untested. The server in evidence is review context only and is never included in runtime GLBs.

Each variant has nine PNG views. `01` enlarged close, `02` rear, `03/04` honest min/default gameplay without staff, `05` enlarged reimport, `06` enlarged reimport with actual server, `07` review-only cutaway with roof/fascia/lintel hidden, `08/09` honest min/default gameplay with actual server. Cutaways are explicitly inspection aids and are not runtime geometry. Enlarged close/rear/reimport scale is horizontal9.6m (curry10.7m). Gameplay images use elevation38.853deg, 16:9, vertical18m/horizontal32m and vertical62m/horizontal110.222222m. The model is deliberately small at the actual default zoom; roof and fascia naturally hide the server's face at elevated views.

## Reproduce

Run serially with Blender4.1.1, each command only after the previous exits. Running concurrent Blender render processes caused an ntdll crash during development; final serial runs completed successfully.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 4.1\blender.exe' --factory-startup -b -t 2 --python '.\build.py'
& 'C:\Program Files\Blender Foundation\Blender 4.1\blender.exe' --factory-startup -b -t 2 --python '.\verify.py'
& 'C:\Program Files\Blender Foundation\Blender 4.1\blender.exe' --factory-startup -b -t 2 --python '.\render_cutaway.py'
```

Run from this package directory. `build.py` is self-contained with its delivered `references/`; it writes only this package. `verify.py` and `render_cutaway.py` intentionally read the actual external source rigs in `C:/Projects/festival-tycoon/assets/source/characters/service-server-assets-v1`, and the actual existing held props under `game/assets/props`. They do not modify those references. Runtime GLBs need no external files.

See `INTEGRATION-NOTES.md` before any later integration: these GLBs replace the entire old assembly decorations together, so adding the old fascia/name/icon/kitchen on top would duplicate them. Bypass the old palette replacement, preserve the server/queue contract, attach steam to retained markers and keep the chalk menu external. No such integration is included in this commission.

`PROVENANCE-sha256.json` lists final packaged sources, runtime models, artwork references, evidence and scripts. `verification/history/` preserves the pre-repair wall diagnostic only; current acceptance evidence is `verification/checks.json`.
