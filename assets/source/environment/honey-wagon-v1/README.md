# Honey wagon v1: Dav's Lav-Sucker

This is the toilet emptying service, from the approved board (`Documents/Festival Tycoon concepts/honey-wagon/`).

**Approved picks:**
- **Livery B, "Cheeky":** lime cab, white tank and a jaunty green name, with the tagline "No jobbie too big".
- **Rear face:** a friendly face painted on the round rear dish, with the rear valve as its nose. Guests call the truck
  "Dirty Henry". It's a loose homage in our own style, with no Henry hat, face, logo or red-and-black livery.
- **Rear sign (an easter egg):** "IF YOU CAN SMELL SHIT YOU'RE DRIVING TOO CLOSE", beside the "HOW'S MY SMELLING? 0800
  POO POO" sticker.
- **OUT OF ORDER sign:** option A, door-wide.
- **Hose:** as boarded, with a bulge rig.

**Rebuild:**
1. `python make_textures.py`
2. `blender -b --python build_honey_wagon.py -- out`
3. Copy `out/lwf_honey_wagon_v1.glb` and `out/lwf_honey_wagon_hose_v1.glb` to `game/assets/environment/`, and
   `out/lwf_portaloo_out_of_order_sign_v1.glb` to `game/assets/environment/portaloo/`. Copies go in the matching
   `assets/runtime/environment/` folders.
4. Run the Godot import. The four text textures (name board, rear face, rear plate, sign) are imported lossless with
   mipmaps and `detect_3d/compress_to=0`, so the lettering stays sharp.

**Fonts:** all text uses the game's OFL fonts (Zilla Slab, Source Sans 3). The sign's marker lettering is drawn
stroke by stroke in `make_textures.py`, with no font.

## `lwf_honey_wagon_v1.glb` (1,454 tris)

**Frame:** the origin is at the ground centre of the footprint. **Forward is Godot −Z** (cab); the rear is +Z. Size
2.0 × 2.6 × 5.2 m (2.2 m wide at the mirrors).

| Node | What |
|---|---|
| `LWF_HoneyWagon_Body` | Chassis, cab-over cab, the tank and its green bands, cradles, manlid, hose tubes, ladder, valve and lever (`LWF_HoneyWagon_MattePalette`) |
| `LWF_HoneyWagon_Wheel_FL` / `FR` / `RL` / `RR` | Wheels, pivot at the hub, radius 0.42 m; spin about local X |
| `LWF_HoneyWagon_Beacon` | Amber roof beacon at (0, 2.37, −1.55). Material `LWF_HoneyWagon_BeaconLamp` (emission 0.4); raise its emission or toggle it to flash. |
| `LWF_HoneyWagon_HosePort` | Empty at the end of the rear valve, (0, 1.45, 2.72), facing +Z. The hose attaches here. |
| `LWF_HoneyWagon_RearFace` | The rear dish with the face (`lwf_honey_wagon_rear_face_v1.png`, planar UVs) |
| `LWF_HoneyWagon_NameBoard_L` / `_R` | Name boards curved to the tank sides |
| `LWF_HoneyWagon_RearPlate` | 1.6 × 0.2 m plate on the rear crossmember with the easter-egg sign and the sticker. At 1536 px it reads up close in any future first-person view. |

## `lwf_honey_wagon_hose_v1.glb` (2,312 tris)

- **Frame:** authored in the **portaloo's own frame**: origin at the portaloo origin, with the door facing local −Z.
  Put it at the portaloo's transform, or make it a child of the portaloo.
- **Segments:** 40, `LWF_Hose_Seg_00` (at the truck) to `LWF_Hose_Seg_39` (at the loo), each 0.172 m, 6.87 m in all.
  Each segment pivots at its own centre with **local +Y along the hose**, so scaling local X and Z (to about 1.9)
  bulges it. A slug is a wave of a few consecutive segments travelling from Seg_39 to Seg_00. I'd suggest one every
  ~2 s, about 0.6 s to pass.
- **Couplings:** `LWF_Hose_PortCoupling` sits at the truck end, which meets `LWF_HoneyWagon_HosePort` when the truck is
  placed as below. `LWF_Hose_Coupling` sits on the door face at (−0.52, 0.13, −0.92).

## `lwf_portaloo_out_of_order_sign_v1.glb` (2 tris)

- `LWF_OutOfOrderSign`: 0.72 × 0.52 m kraft card, alpha clip (MASK 0.5). It faces Godot −Z with a 7° tilt baked in;
  the origin is the sign centre.
- **Parent:** the portaloo's `DoorPivot` node (at (0.45, 0.08, −0.795) in the portaloo), at local
  **(−0.45, 1.34, −0.04)**. That puts it 1.5 cm proud of the door leaf, so it swings with the door.
- While the sign is up, swap the indicator to `lwf_portaloo_indicator_occupied_v1`.

## Layout (`out/honey_wagon_layout_v1.json`), all relative to the portaloo being emptied

| Piece | Portaloo-local position | Yaw |
|---|---|---:|
| Truck | (−3.4, 0, −0.9) | 180 |
| Hose | the portaloo's own transform | 0 |
| Sign | under `DoorPivot` at (−0.45, 1.34, −0.04) | 0 |

The truck yaw of 180 puts it parked alongside, rear to the loo's door side. The truck drives in down the entrance lane
first.

## Verification

Everything in `verification/` was placed from the layout JSON with the built files (`place_from_layout.py`), using
the game camera:
- `parked_pumping.png`, `rear_face_and_plate.png` and `sign_on_door.png`;
- `hose_plain.png` against `hose_bulge_segments_16-19.png`: segments 16–19 scaled to 1.9 show as a slug.
