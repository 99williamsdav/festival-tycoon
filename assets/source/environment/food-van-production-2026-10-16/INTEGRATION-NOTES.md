# Food van production — integration reference

This is a read-only source audit for the later integration owner, not authorization to change game code in this asset-production assignment. Current assignment: build the three approved concepts, verify and deliver reviewable assets. No coffee van or tractor work.

## Existing coordinate contract

- Blender assembly coordinates convert to Godot as `(x,z,-y)`.
- `ApprovedFoodVanAssembly` is offset `(-2.15,0,0)` under the vendor root.
- Food server is a sibling under vendor root at Godot `(0,0.47,0.88)`, yaw PI, scale 1. In Blender assembly coordinates its foot anchor is `(2.15,-0.88,0.47)`.
- Existing service aperture: Blender x 0.90–3.40, z 1.21–2.23, front plane y -1.10.
- Existing counter top: z 1.235; y -1.50 to -1.10.
- Service customer cell is vendor `(0,0,2.0)` in Godot; queues begin there. Preserve authoritative service/queue logic.

## Placement reservation

`ImmersionFootprint` reserves 13 by 7 half-metre cells, with centre offsets x -7 through 5 and z -3 through 3. Including cell half-widths, the continuous vendor-local reservation is x [-3.75,2.75], z [-1.75,1.75] metres. In Blender assembly coordinates this is x [-1.60,4.90], y [-1.75,1.75]. The service cell is separately reserved. These bounds are wider than the old physical body/hitch bounds; report both comparisons when judging the curry cab.

## Assembly replacement hazards

Current `game/Main.ImmersionAssets.cs` separately adds chassis, fascia, name panel, roof pictogram, curry kitchen dressing, steam emitters and chalk menu. A new complete van GLB must replace the appropriate complete assembly rather than receive duplicated fascia, sign and kitchen parts. Preserve the chalk menu separately if not included. Inspect the package manifest for what each new GLB contains.

The old `ApplyFoodVanLivery` targets materials whose names begin `LWF_FoodVan_MattePalette`; it swaps an old palette texture. New curated palettes must not be indiscriminately overwritten. The existing server assets keep their own materials and are not embedded in van exports.

Current curry steam uses named nodes `LWF_FoodVan_SteamVent_L` and `LWF_FoodVan_SteamVent_R`; any new named markers must sit at the actual new vent outlets. Continue using the existing simulation-driven emission/pause behaviour. No steam or smoke geometry belongs in a static production export.

## Camera reference and scope

Actual game camera elevation is 38.853 degrees. At 16:9, default Godot vertical orthographic size 62 corresponds to horizontal 110.222m; closest size 18 corresponds to horizontal 32m. Large inspection renders are useful but should be labelled separately from these normal gameplay views.

Later integration validation should cover all orientations, existing server clips and held props, placement picking, queue/customer approach, pause and transaction behaviour. This asset package cannot claim those game behaviours have been integrated or verified.

## Sources inspected 16 October 2026

- `C:/Projects/festival-tycoon/game/Main.ImmersionAssets.cs`
- `C:/Projects/festival-tycoon/game/Main.ServiceServers.cs`
- `C:/Projects/festival-tycoon/src/Festival.Simulation/Immersion.cs`
- `C:/Projects/festival-tycoon/src/Festival.Simulation/Navigation.cs`
- `C:/Projects/festival-tycoon/src/Festival.Simulation/BuildLayout.cs`
- `C:/Projects/festival-tycoon/assets/source/characters/service-server-assets-v1/HANDOFF.txt`

Production results, measured dimensions and final review status are recorded separately by the builder/reviewer. This note does not pre-approve unverified output.
