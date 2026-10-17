# H1 + P1 asset production handoff

Approved scope: H1 plain tied hay bale and P1 classic A-frame picnic table only. This is a new isolated asset package. No repository files, old assets, gameplay, placement data or runtime animation systems were changed. Fresh independent GPT-6 Astra medium review: PASS. See REVIEW.md for the independent native-mesh, seating-support and GLB roundtrip audit; accepted for this asset-production scope only.

## Deliverables

Open `review.html` for actual approved concept PNGs alongside close, rear, above, occupied, side-contact and honest gameplay renders.

| Asset | Production GLB | Editable source | Triangles | Material | Seats |
|---|---|---|---:|---:|---:|
| H1 | `runtime/ft_hay_bale_h1_v1.glb` | `source/ft_hay_bale_h1_v1.blend` | 566 | 1 embedded palette | 2 |
| P1 | `runtime/ft_picnic_aframe_p1_v1.glb` | `source/ft_picnic_aframe_p1_v1.blend` | 1196 | 1 embedded palette | 4 |

The production GLBs contain one consolidated mesh and named local seat empties. They contain no people, food, skeleton, animation, ground, light or camera. Mesh transforms are identity at world origin. Geometry begins at ground zero. Blender files retain separately named editable components and the packed palette. `source/dining_palette.png` is the shared 112 × 8 pixel swatch palette; materials use nearest sampling and roughness .87.

`*_occupied_review.blend` files contain the same furniture plus actual current native male/female rigs, separate actual default hair, and actual pizza plate/chips tray props. They are review scenes, not production exports. All images are packed. Review lighting, cameras and ground remain editable.

## Dimensions and interpretation

H1 is approximately 1.20 × .60 × .455 m including its irregular perimeter and twine. The main seating surface is .45 m high; two twine bands reach .455 m and sit outside the two measured pelvis patches. The modest envelope growth from the approximate 1.15 × .55 m brief allows visible irregular broad straw facets and continuous exterior bands. There are no separate straw strands.

P1 is 1.96 m long, 1.712 m overall width, .75 m tabletop height (.754 m including bolt heads), with attached .45 m high benches. Tabletop width is .808 m. Each bench is .302 m deep, in two planks. Four flat-cut A-frame feet touch z=0. Bolts, dark frame, honey planks and sparse broad timber facets follow P1.

## Seating evidence

The review uses the actual `rigged-test-v1/out/lwf_attendee_{male,female}_rigged_test_v1.blend` bodies. Native rest mesh height is approximately 1.728 m male / 1.658 m female. Rig and body scale remain 1.0. No guest mesh, bone length or foot was scaled or shortened.

The source `eat` action at frame31 supplies the upper-body eating gesture. That sample is frozen; review-only hip/leg posing keeps a relaxed thigh slope with shins tucked back beneath the knees. The right arm is solved around the source food socket. Actual game props are held near the palm; a 5–6 mm review-only vertical prop adjustment avoids the source mitten penetrating the plate/tray. The nearest measured hand/food surface gaps are .685–.734 mm.

All six posed guests have evaluated pelvis vertices 1 mm above a real upward-facing seat triangle, and both real shoe sole minima within .012 mm of ground. `verification/Seat_*-contact-patch.json` records the evaluated vertex and nearest seat coordinates. Contact-side images show support and foot posture.

All six guests have zero evaluated body/furniture, food/furniture and food/body triangle crossings. A separate oblique ray containment pass finds no body or food vertices inside closed solid furniture components. All neighbor pairs have zero body/body, food/food and food/neighbor-body crossings. The closest opposing P1 body vertex/surface separation is .158 m. Exact clipping of evaluated thigh triangles against the tabletop footprint gives underside clearances of approximately .140 m male / .144 m female.

These checks concern six static review poses. They do not establish a seated runtime animation, any transition, traversal, collision proxy or navigation behavior. The source eating sample has a hand-to-mouth gesture; separate bite items and animated consumption are not authored here.

## Reproduction and validation

Run Blender 4.1 serially with two threads:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 4.1\blender.exe' --factory-startup -b -t 2 --python '.\build.py' -- hay picnic
python .\verify_exports.py
python .\make_gallery.py
```

`--check` after the asset names skips render calls but rebuilds sources, GLBs and evaluated evidence. Keep it outside the Blender arguments before `--python`; the documented invocation passes it after `-- hay picnic`.

Build dependencies are the read-only current native rigs, hair GLBs and their sidecar palettes, and held props under `C:/Projects/festival-tycoon`. `references/` preserves copies of the exact approved PNGs. `PROVENANCE-sha256.json` hashes the package and source references. Pure-Python `verify_exports.py` validates GLB structure, identity transforms, embedded palette, seat counts, no actors/skins/animations, grounded exported bounds and all generated seating checks. `verification/export-verification.json` is the result.

See `SEATING-AND-AXES.md` for the anchor convention and suggested access spaces. No EXE or project integration is part of this delivery.

