# Approved role assets — production handoff

Design workspace only. Game repository integration belongs to Builder. This package implements the approved v3 workwear direction on the existing male v5/female v3 body family. It does not add rigs, gameplay inventory, tools in hands, hats or headsets.

## Package

- `runtime/`: 17 GLBs — eight fitted staff overlays, two partitioned band bodies, six body-specific band kits, one hardware-only drum kit.
- `source/`: matching editable Blender files. Staff sources include a dressed review body, but their GLBs export **only the overlay**. Band-kit sources include a review core and, for drummers, review hardware; kit GLBs contain only the specified kit meshes. Hardware source contains only hardware.
- `palettes/`: seven 96×8 body palette PNGs and one 128×8 garment/accessory palette. Textures are embedded in every GLB that uses them.
- `manifest.json`: authoritative absolute paths, SHA-256 hashes, node transforms, dimensions, triangle counts, palette rules, fit measurements and source provenance.
- `verification.json`: Blender export/reimport checks and sampled contact/clearance measurements.
- `godot-verification.json`: standalone Godot 4.7.2 import check; no game files changed.
- `pose-compatibility.json`: exact torso/leg/hip geometry comparison across all 12 approved body pose files; four combined carried-prop examples are rendered in `review/pose-fit-*.png`.
- `review/`: actual asset renders, not AI concepts. Crew front/back/gameplay scale, workwear detail, band front/side/contact and animation extrema.

## Root and coordinate contract

Metres, feet at ground centre. Blender +Y forward / Z up becomes Godot −Z forward / Y up. Attach each **GLB scene root** at identity (position zero, rotation zero, scale one) beneath the stable person root. Keep the exported child transforms; the playing-arm nodes have nonzero sleeve pivots and drum sticks are children of their arms.

Staff overlays must not be children of the body visual that is replaced on a pose switch. Select the overlay with the same deterministic male/female identity as the body. Existing tee/leg coordinates are unchanged. The garments are static, with open armholes; check carried-prop poses and activity transitions in the game. Do not stack the old medic cue, amber steward yoke or floating maintenance/sound identification box onto these garments.

## Staff

| Role | GLB pattern | Body palette | Identification |
|---|---|---|---|
| Medic | `lwf_medic_{male|female}_overlay_v1.glb` | `lwf_medic_body_palette_v1.png` | Green vest, white cross front/back, pale hem |
| Steward | `lwf_steward_{male|female}_overlay_v1.glb` | `lwf_steward_body_palette_v1.png` | Full yellow hi-vis vest, two horizontal and shoulder strips |
| Maintenance | `lwf_maintenance_{male|female}_overlay_v1.glb` | `lwf_maintenance_body_palette_v1.png` | Brown bib/pants, cyan bib mark, pocket, tool pouch, knee panels |
| Sound | `lwf_sound_{male|female}_overlay_v1.glb` | `lwf_sound_body_palette_v1.png` | Charcoal shirt, mustard mark, slate work trousers, utility pockets, knee panels, cable loop |

The material name `lwf_crew_trim_palette_v1` is **not** a body material. Never replace its texture with a 96×8 body palette.

Body slots: 0/1/2 skin (female mouth also slot1), 3/4 shirt, 5/6 identification, 7/8 trousers, 9 eyes **and shoes**, 10/11 hair. Preserve skin and eyes/shoes. Natural hair customization may replace 10/11 consistently; role clothing overrides guest clothing choices. Maintenance body slots5/6 deliberately match the shirt: its cyan mark is on the bib overlay, so no duplicate chest patch shows behind the straps.

## Band

Use `lwf_performer_{male|female}_body_v1.glb` with these exact node names:

- `LWF_Performer_BodyCore`
- `LWF_Performer_IdleLeftArm`
- `LWF_Performer_IdleRightArm`

All three meshes are partitions of the approved source body with unchanged vertex positions. Default embedded body palette is guitarist teal; apply the bassist plum or drummer clay-brown palette to all body partitions when needed. Purple performer identification remains.

Attach `lwf_{guitarist|bassist|drummer}_{male|female}_kit_v1.glb` at stable-person-root identity only while playing. Hide both idle-arm nodes when a kit is attached; remove the kit and restore idle arms on detach. Preserve the existing performance eligibility, fall/fight and stage/ramp behaviour.

Playing-arm nodes are `LWF_Guitarist_LeftPlayingArm`, `LWF_Guitarist_RightPlayingArm`, and corresponding `Bassist`/`Drummer` names. Drummer sticks are `LWF_Drummer_LeftStick` and `LWF_Drummer_RightStick`. Exact local transforms are in `node_contract` in the manifest; **do not zero those child transforms**.

The original acoustic guitar, bass, drum shells and cymbals/hardware geometry and original instrument palette are retained. Do not apply body recolouring to the whole kit: only materials named `lwf_*_body_palette_v1` belong to the skin/clothing palette. Original instrument material and `lwf_crew_trim_palette_v1` remain separate.

Each kit imports one AnimationPlayer clip called **`Animation`**. Godot imports its loop mode as `LOOP_NONE`; explicitly set `Animation.LoopModeEnum.Linear` (`LOOP_LINEAR` in GDScript) and play it while attached. Observed import duration is approximately 0.708333 seconds. These are small object-transform loops, not skeletal animations. Reset/remove them on detach. Static frame1 is the contact fallback.

## Fixed drums: important lifecycle change

Replace the old fixed compact-drum GLB with `lwf_drum_hardware_only_v1.glb`, retaining the existing stage placement/yaw. It contains exactly `LWF_Drummer_DrumShells` and `LWF_Drummer_CymbalsHardware`, 496 triangles, no arms, no sticks and no animation.

The drummer's playing-arm/stick kit is person-owned. Align the playing person root with the hardware root using the existing drummer position/yaw. Remove/hide that person-owned kit when the drummer is absent or ineligible. Do not keep both the old full kit and the new hardware GLB; that would recreate the floating-arm defect.

## Verification limits and game acceptance

Local checks cover embedded textures, original geometry/palette retention, exact body partition coordinates, GLB reimport within 0.03mm, Godot import, and sampled garment/hand fit. Garment clearance sampling is not a full continuous collision solver. It excludes side bridges/tools from the central-torso measurement; the render review covers those visually. Intentional shallow hand/instrument overlap can occur at grips.

Measured central vest clearance is at least 6.50mm male / 2.43mm female; sampled knee-panel clearance is at least 7.78mm. Maintenance central-front sampling includes projecting tools, so its maximum gap is not a cloth-fit value. The source sleeve entry rings are reused exactly on band arms. Final minimum sampled acoustic hand contact distances are about 0.74mm fretting / 0.55mm strumming. Small loop excursions are reported per arm in the manifest.

Godot's standalone check logged a Windows root-certificate-store warning, unrelated to these local GLB imports; all 17 imported successfully. No network resources are needed by these assets.

Builder should capture the integrated scene with both body variants, crew role silhouettes, stage performers, animation extrema, stage approach/detach and no drummer present. Check overlay persistence through pose swaps, stable natural hair, no doubled idle arms, no duplicated role markers, correct palette targeting, and no abandoned drum arms/sticks. Asset import success alone is not proof of those runtime transitions.

## Rebuild

Run Blender 4.1 in background with `--python build_roles.py`. `-- --staff-only` or `-- --band-only` updates one family; add `--no-render` for export only. Then run `verify_roles.py`, the standalone `godot-check/verify.gd`, `verify_pose_compatibility.py`, and `render_proof.py` / `render_band.py`. Run `finalize.py` after the checks and renders finish. A rebuild changes hashes; regenerate the manifest before copying files.
