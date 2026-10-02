# Crowd hair & hats — v1

2 October 2026. Production head pieces from the approved (cut-down) crowd hair & hats concept
(`Documents/Festival Tycoon concepts/crowd-hair-hats/`). Bald bodies plus separate hair, beard, cap, flower crown,
sunglasses and mohawk pieces. Matte low-poly, palette textures with Closest filtering.

## Bald bodies

The baked hair is removed from every shipped guest pose and both performer bodies:
- `lwf_attendee_{male,female}_{relaxed,drink_hold,food_hold,drinking_beer,drinking_soft,eating}_v2.glb`
- `lwf_performer_{male,female}_body_v2.glb`

How it is done:
- **`strip_baked_hair.py`** deletes the triangles on guest palette slots 10–11. Only hair uses those slots.
  - It rewrites only the index buffer. Vertices, nodes, sockets, materials and the embedded palette are byte-identical.
  - The unused hair vertices stay in the vertex buffer: 96 male, 494 female.
- **`apply_bald_bodies.py`** runs the strip on the game copies and the role-assets-v2 performer copies, then refreshes
  the hashes and triangle counts the tests check:
  - `attendee_poses_v6_manifest.json`
  - `role-assets-v2/lwf_performer_*_body_v2.json`
- Re-running either script is a no-op.
- `attendee-v6-draft/poses/` deliberately keeps the generator output **with** hair, and its own manifest copy.
  `build_crowd_pieces.py` cuts the default hair from it.
- To regenerate a body: run the generator, copy the result into `game/assets/characters`, then run
  `python apply_bald_bodies.py`.

Triangles per body:

| Body | Before | After |
|---|---:|---:|
| Male guest | 1776–1836 | 1592–1652 |
| Female guest | 2592–2652 | 1620–1680 |
| Performer, male | 1776 | 1592 |
| Performer, female | 2592 | 1620 |

We chose bald bodies plus a hair sibling over two body variants per pose: it avoids doubling the 14 GLBs, and every
hairstyle, hat and accessory works the same way.

## Pieces

Files are in `game/assets/characters/`, with copies in `assets/runtime/characters/`.

- **Frame:** every piece is authored in the guest root frame (feet at the origin, facing Godot −Z).
- **Placement:** add each piece as a **sibling of `GuestBody`** under the guest root, at the identity transform.
  - The head is byte-identical in all six guest poses and in the performer bodies, so the pieces fit every pose and
    survive `SetGuestBodyPose`.
  - For role bodies, put them under the role root the same way.

| File | Tris | Node | Material | Use |
|---|---:|---|---|---|
| `lwf_hair_{male,female}_default_v1.glb` | 184 / 972 | `LWF_Hair_Default` | guest palette | Today's baked hair, unchanged geometry. Everyone who isn't bald or mohawked. |
| `lwf_hair_{male,female}_default_under_cap_v1.glb` | 44 / 496 | `LWF_Hair_DefaultUnderCap` | guest palette | Default hair with everything above the cap band removed (band + 14 mm). Male: sides and nape. Female: the long hair below the band, and the fringe tips under the visor. Wear it **instead of** the default hair whenever the cap is on. |
| `lwf_hair_{male,female}_mohawk_v1.glb` | 86 | `LWF_Hair_Mohawk` | guest palette | Shaved sides (bare scalp) and a spiked fin, peak about 15 cm. Guests: punk fans only. Punk band performers: the same mesh. Dye it through the palette's hair slots, not with another mesh. |
| `lwf_beard_male_v1.glb` | 168 | `LWF_Beard` | guest palette | Jaw, chin and moustache with a mouth notch (an open slot at the mouth, thinner round it) for the drinking and eating poses. Combines with every male style, cap and glasses. |
| `lwf_hat_cap_{male,female}_v1.glb` | 187 | `LWF_Hat_Cap` | accessory palette | Crown over the scalp, eased out over the remaining hair at the band so the trim edge is hidden. Front visor, top button. Works bald (a slightly loose band). Not with the mohawk. |
| `lwf_hat_flower_crown_{male,female}_v1.glb` | 114 | `LWF_Hat_FlowerCrown` | accessory palette | One generous fit per sex over the **default hair**: a vine ring and six flowers. Default hair only (it would float on bald, and the mohawk is in the way). |
| `lwf_sunglasses_{male,female}_v1.glb` | 72 | `LWF_Sunglasses` | accessory palette | Fitted to the face. The arms run to the ear line, under long hair. Combines with everything. |

### Combination rules

- The **cap** swaps default hair for the `_under_cap` hair. Bald + cap is fine. Mohawk + cap is not allowed.
- The **flower crown** only goes with default hair. Not with the cap (one head piece at a time), bald, or the mohawk.
- **Sunglasses** and the **beard** (male) combine with everything.

### Palettes

**Guest palette (hair-coloured pieces).** The default hair, under-cap hair, mohawk and beard use the 96×8 guest
palette material, copied from the bodies, on **slot 10** (hair base).
- `ApplyGuestPalette` must paint these nodes too, using the same `(clothing, hair)` variant as the body. Today it only
  walks `GuestBody`.
- Because the source palette bytes are identical, the cached variants are shared with the body.
- Role bodies: `RoleBodyMaterial` already copies hair slots 10–11 from the guest palette.

**Accessory palette** (`lwf_crowd_accessory_palette`, 128×8, 16 slots, u = (8·slot + 4) / 128, material
`LWF_CrowdAccessory_MattePalette`):

| Slot | Default | Use | Recolour per guest? |
|---:|---|---|---|
| 0 | C9553A | cap crown | **yes**: the cap colour |
| 1 | A8432F | cap visor and band rim | **yes**: about 15% darker than slot 0 |
| 2 | C9553A | cap button | **yes**: same as slot 0 |
| 4 | 1A1A1A | sunglasses frame | **yes**: the frame colour |
| 5 | 2A3540 | sunglasses lenses | **yes**: the lens tint |
| 8, 9, 10 | E58FA5, F2EBDD, E8C547 | flower petals (pink, white, yellow) | optional |
| 11 | D9A33A | flower inner petals | no |
| 12 | 5E8A4A | vine | no |

## Checks (`verification/`)

- **`pieces-on-bodies.png`:** front and back three-quarter views on the stripped bodies of:
  - male: bald, default, default + beard, mohawk
  - male: cap, cap + glasses + beard, bald + cap, crown + glasses
  - female: default, mohawk, cap, cap + glasses, crown, glasses
  - drinking and eating poses with beard, cap, glasses and crown
  - the mohawk on both performer bodies
- **`beard-mouth-notch-and-poses.png`:** the notch close up, and the hand at the mouth in drinking_beer, drinking_soft
  and eating. The hand meets the mouth opening; at most its fingertips sit about 1 cm into the beard edge.
- **Tests:** `AttendeePoseTests` passes against the stripped files: the manifest hashes, anchors and role-assets-v2
  reports.

## Rebuild

```
blender -b --python build_crowd_pieces.py -- out
```

Then copy `out/*.glb` to `game/assets/characters/` and `assets/runtime/characters/`. The build report is
`out/crowd_pieces_report.json`.
