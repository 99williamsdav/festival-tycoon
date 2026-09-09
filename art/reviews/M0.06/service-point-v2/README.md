# Generic service point v2

Freshly rebuilt approval candidate. Awaiting separate coordinator and user approval. V1 source/runtime and current review history are preserved; no v1 geometry was patched or recoloured for this rebuild.

The kiosk is an enclosed timber cabin with a wide serving hatch and counter, folded green shutters, a small cream canvas awning, blank framed sign and closed rear staff door with latch and step. Solid wall backing and tight weatherboards keep a coherent silhouette from every side. Warm wood, agricultural green, dark roof/iron and cream canvas match the approved rural palette. A few broad rub marks and slight board-colour variation provide restrained wear.

The kiosk is generic: no words, logos, products, menu, taps or service-specific equipment. The open hatch and counter define the customer side. Rain-cover experiments for the trailer are unrelated to this kiosk's modest removable awning.

## Files and views

- Source: `assets/source/environment/lwf_service_point_v2.blend`
- Runtime: `assets/runtime/environment/lwf_service_point_v2.glb`
- `01-south.png`: customer front; `02-east.png`: right side; `03-north.png`: staff door/rear; `04-west.png`: left side.
- `05-context.png`: lit countryside with review-only hedge and track.
- `06-scale-comparison.png`: 1.75m human proxy and approved trailer stage v2, both at actual scale.
- Every image is 1920 x 1080, orthographic, 35 degrees downward; Blender Cycles with matte materials and soft daylight.

## Dimensions and modular contract

Cabin footprint 3.8 x 2.5m, approximately the requested 4 x 3m kiosk. Roof, awning and rear staff step bring total visual extents to 4.13 x 3.7925m; full height is 3.08892m. Counter top is 1.165m above ground; the serving hatch is approximately 2.46m wide and 1.01m high. Wall backing is 0.14m thick. Front and rear access clearances remain a simulation/integration decision; the mesh extents are not authoritative queue or placement metadata.

Metres, glTF Y-up, common ground-centred placement origin, applied source transforms. Customer side Blender -Y / Godot +Z; rear staff door Blender +Y / Godot -Z.

| Mesh node | Contents | Triangles |
| --- | --- | --- |
| LWF_ServicePoint_Shell | Enclosed cabin, roof, floor, rear staff door and step | 1,600 |
| LWF_ServicePoint_Counter | Serving ledge, opening trim, folded shutters and brackets | 288 |
| LWF_ServicePoint_Awning | Canvas, rods and brackets | 66 |
| LWF_ServicePoint_Signage | Blank removable sign and frame | 112 |

Total 2,066 triangles, four mesh nodes, four surfaces, one matte palette material and one original packed 64 x 64 texture embedded in the GLB. Parts can be replaced independently for specialised skins/upgrades. Source contains review context, camera and lights; export contains only the four asset nodes.

Identity, service type, queue slots, operational footprint, customer/staff access and upgrade state remain simulation-owned. No gameplay code, collision/navigation geometry or service logic is supplied.

## Provenance, verification and limits

Fresh original Blender primitive and custom-polygon kiosk geometry; shared primitive/palette helpers and palette from the authored trailer workflow. No downloaded models/textures. Approved trailer reused only for scale review. V1 was not used as geometry input.

Blender 4.1.1 source reopen and GLB reimport passed: four mesh parts, applied source transforms, single UV layer and embedded 64 x 64 image. Standard Godot 4.7.2 editor import and subsequent instantiation passed: four meshes/surfaces, one textured material, 2,066 triangles and expected bounds. Sandbox log/certificate-store warnings did not prevent verification.

All six final renders visually inspected for coherent enclosure, roof clearance, material separation, front/rear orientation, readable sign/counter, scale and lighting. These are Blender art-review renders; in-game lighting and placement have not been integrated. The interior is only the enclosed shell/floor visible through the opening, with no fit-out. Shutters and door are fixed in the depicted state; no animation or alternate state is included. Thin canvas is two-sided.

STOP for coordinator and user approval. No later assets, commit or push.
