# Rustic water tower prototype v1 — approved R0.05a asset

This exact small farmyard water-tower prototype was visually approved by the coordinator and user on 25 September 2026. It represents the foundation/perk that increases drinking-water throughput at the existing taps. The standpipe asset is reused for additional taps; this asset adds no tap or visible pipe network. Gameplay integration is a separate builder step.

## Review this exact version

- `01-water-tower-three-quarter.png`: isolated game-style view.
- `02-farmhouse-scale-context.png`: approved scale/placement direction beside the existing farmhouse. The farmhouse, grass, and track are review-only context, not exported tower parts.
- `technical.json`: measured mesh and bounds data.

## Source, export, and scale

- Editable Blender 4.1.1 source: `assets/source/environment/lwf_water_tower_prototype_v1.blend`.
- Godot-compatible glTF 2.0 export: `assets/runtime/environment/lwf_water_tower_prototype_v1.glb`.
- Four mesh nodes, 1,068 triangles, matte flat-colour materials, no external textures. Ground-centred origin; Blender metre units; GLB Y-up. Front is Blender `-Y` / Godot `+Z`.
- Measured visual bounds: **2.96 m wide × 2.9875 m deep × 5.525 m high**, including roof, feet and maintenance ladder. Suggested simulation reservation: **3.5 × 3.5 m**.
- Coordinated fixed-placement proposal for R0.05a: centre **9.7 m east of the farmhouse centre**, leaving approximately **1.9 m** between the measured farmhouse east bound and tower west edge. Simulation owns the final placement/obstacle data; the review render alone is not that authority.

The low-poly twelve-sided tank has alternating aged timber staves, muted blue hoops and a small water-drop plaque. A shallow charcoal slate cap, four braced oak posts, individual stone shoes and a narrow maintenance ladder keep it recognisable without becoming a municipal landmark. Height stays below the farmhouse roof ridge.

## Verification, provenance, and limits

Blender source reopen and GLB reimport matched all four mesh nodes, 1,068 triangles and measured bounds. Godot 4.7.2 completed an isolated headless editor import of the GLB. Host certificate-store/user-settings warnings did not block that import. In-game lighting, picking, navigation and crowd performance remain untested.

Original project-authored procedural geometry and flat-colour materials, informed by the existing farmhouse/standpipe palette. No downloaded model or texture. The source includes review-only imported farmhouse context, which is excluded from the GLB. Exact approved Blender-source and GLB SHA-256 hashes are in `../ASSET_APPROVALS.md`.

No collision, navmesh, pipe network, tap, water animation, interaction or gameplay-effect data is embedded. The simulation owns tower persistence and throughput. Approval of this exact asset does not authorize another asset or a redesign.
