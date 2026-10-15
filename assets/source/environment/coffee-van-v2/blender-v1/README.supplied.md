# Coffee van — selected concept replication

Editable Blender 4.1 asset and GLB built from the selected common concept at `../../coffee_concept_sol/coffee-van-concept.png`. The large service-side image governs this version. No game files were edited or integrated.

## Deliverables

- `coffee-van.blend`: named editable mesh components, materials and separate STUDIO camera/lights/ground.
- `coffee-van.glb`: asset geometry and PBR materials only; no studio, camera, light, person or reference image.
- `build.py`: reproducible generation, export and actual Cycles renders. Run with Blender 4.1: `blender -b --python build.py`.
- `render-main.png`, `render-rear-opposite.png`, `render-front.png`, `render-rear.png`: actual model renders.
- `game-scale-four-angle.png`: four actual 480 × 360 renders arranged in a 960 × 720 sheet; individual `game-angle-1.png` through `game-angle-4.png` are also supplied.
- `asset-statistics.json` and `export-verification.json`: geometry metrics and GLB import checks.
- `verify.py`: repeatable GLB structure, finite vertex, degenerate triangle and reimport checks.
- `clean-export.py`: final tiny-face cleanup/export utility; the same cleanup is included in `build.py`.

## Scale and standing space

Metres, Z up in Blender, front at negative X, service side at negative Y. Body shell: 4.55 m long, approximately 2.03 m wide, 2.76 m roof height. Full exported bounds including bumpers, mirrors and open hatch: 4.927 m long × 3.308 m wide × 2.799 m high. Tyre tread reaches -0.001 m, a deliberate negligible ground overlap.

The central interior reserves a conservative 0.84 m clear standing aisle and approximately 1.48 m useful length behind the counter. Suggested server feet position: (0.86, -0.08, 0.715) m. A 1.75 m server fits beneath the roof. The equipment occupies the cab end; cabinets sit against the opposite wall. This is geometric provision, not a rig/navigation or accessibility test. No server is baked in.

## Geometry, materials and provenance

Final GLB: 76,290 triangles, 364 separate mesh objects, 15 materials, zero image textures. All mesh, lettering and materials are authored procedurally in the included script. Blender's built-in font creates lettering, converted to mesh. No third-party downloaded models, texture libraries or photographic decals are used. The user-selected AI concept is a visual reference only and is not embedded in the model.

Materials are simple portable Principled PBR values. The runtime mesh is the rendered asset; there are no render-only detail meshes or image planes. Neutral studio illumination and soft shadows are presentation only. Many small bevelled components and text meshes produce a relatively high object/triangle count; this version has no LODs, collision proxies, animation or draw-call merging.

## Inspection and caveats

The main-view horizontal black grille, single service hatch, cream/teal/gold division, bean/COFFEE door identity, coffee equipment and menu are reproduced. The opposite side is closed and the rear uses twin doors. Cab/roof gaps discovered in the first render were closed. GLB reimport passed with no nonfinite vertices, no zero-area triangles and no studio objects.

Accuracy is moderate: recognisable arrangement and colour blocking, but the shell is more angular, the cab glazing is opaque smoky blue, the body lacks the reference's subtle sculpted panel contours, and the oak is flat-colour rather than grain-textured. Wheels, headlights and equipment are simplified; tiny menu text loses legibility at game scale. The interior is darker than the illustration and the rear/roof joins remain visibly assembled panels. This is a reviewable modelling comparison asset, not a claim of production optimisation or gameplay testing.
