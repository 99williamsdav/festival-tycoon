# Inherited trailer stage v1

Status: awaiting separate coordinator review and user approval. Only the basic operational version is authored.

An inherited agricultural flatbed with a modest setup: worn green chassis and A-frame towing eye, tandem agricultural wheels, leaf springs, drop supports, aged timber deck, cream canvas weather canopy, timber steps, salvaged side/rear rails, two small speaker cabinets and two basic lamp fittings. The open long side is the audience-facing side. The original warm farm palette and simple faceted geometry follow the approved rural art direction.

## Deliverables

- Source: `assets/source/environment/lwf_trailer_stage_v1.blend`
- Runtime: `assets/runtime/environment/lwf_trailer_stage_v1.glb`
- All six PNG renders are 1920 x 1080, orthographic, 35 degrees downward.
- 01 south/front, 02 east/hitch end, 03 north/rear, 04 west/access end.
- 05 countryside context; 06 true-scale composition with the approved small barn and a 1.75m neutral human proxy on the audience side.

## Runtime contract

Metres; glTF Y-up. All five mesh nodes have applied transforms and a shared ground-centred placement origin. Overall geometry is approximately 9.905m wide, 4.915m deep and 3.970m high, within a nominal 10m x 5m operational footprint. Deck surface is approximately 1.20m above ground. Audience direction is Blender -Y / Godot +Z; hitch points Blender/Godot +X.

Five separable mesh nodes: `LWF_TrailerStage_Base`, `LWF_TrailerStage_Canopy`, `LWF_TrailerStage_Access`, `LWF_TrailerStage_Rails`, `LWF_TrailerStage_Fittings`. Base includes wheels, towing gear, deck and supports; canopy includes cover and supports; fittings include cabinets and lamps. 4,368 triangles, five surfaces, one matte material and one original packed 64 x 64 palette image embedded in the GLB. No external texture dependency.

Gameplay identity, operational footprint, audience orientation and upgrade state remain simulation-owned. No state logic, collision, navigation, animation or functional lights are included. Future variants can replace the five parts; no bare or upgraded variants have been authored.

The Blender source also contains explicitly named REVIEW objects for lighting, camera, ground, countryside, the approved barn and scale proxy. Those are excluded from the runtime GLB. Hide REVIEW objects when working on the model alone.

## Provenance and verification

Original meshes authored from primitives and custom polygons in Blender 4.1.1; original procedural palette, no downloaded model or texture. The approved small barn is reused only in the review composition.

Blender source reopen and GLB reimport passed: five mesh parts, applied source transforms, shared UV layer, embedded 64 x 64 palette. Standard Godot 4.7.2 editor import in an isolated verification project passed, followed by instantiation and checks of the five meshes, five surfaces, one textured material, triangle total and bounds. Sandbox log/certificate-store warnings occurred in the verification process; they did not prevent import or instantiation.

All six final renders were visually inspected for framing, canopy clearance, palette, farm-trailer silhouette, front access and scale. These are Blender art-review renders; in-game lighting and integration are outside this asset assignment. Canvas is intentionally thin and two-sided. Wear is restrained palette variation and a few broad marks, not photoreal surface detail.

STOP: separate coordinator and user approval are required before any later asset. No commit or push was made for this candidate.
