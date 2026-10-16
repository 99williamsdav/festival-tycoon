# Approved food van bodies — game integration

The frozen production package is archived here unchanged, with this integration note added. Its original HANDOFF, production review, manifest, editable Blender files, artwork references and reproducible scripts describe the asset-production handoff. Current game-integration verification is in `briefs/results/food-vans-integration-handoff.md`.

`game/assets/environment/food-vans-production-v1/` contains the exact approved GLBs and Godot import resources. Product mapping selects Chips → rounded cream/blue trailer, Pizza → timber/terracotta wagon, Curry → ribbed plum/mustard cab van. The complete curated model replaces the old chassis/fascia/name/roof-icon/kitchen assembly. Legacy livery palette substitution is bypassed. Existing separate curry chalk menu, actual server rigs and held-food animations remain separate.

Approved GLB SHA256:

- Chips: `E3BFE879F1422E825C10B46C509865C5C3613CDD34F66C7A0522FA3827202F98`.
- Pizza: `540361A894E103D6C7C5CC878B9F47AFA2502C17B4C7FAC844BB9C674D0B762D`.
- Curry: `63F9FD9489441A509DD726AC6E606A14F60F76A5560F92C54948160657F880CF`.

The existing assembly offset stays Godot (−2.15,0,0); server anchor (0,.47,.88), yaw PI, scale one; service point (0,0,2). Original GLB node transforms are preserved. Curry's two existing simulation-driven steam effects attach to the delivered named vent markers. No legacy kitchen or duplicate signs are added.

All models fit the original 13×7 half-metre-cell reservation. Curry extends .21 m beyond the old physical hitch but retains .13 m reservation margin; rear steps extend .01 m beyond the former physical rear with .30 m reservation margin. Navigation, service and queues are unchanged. The ray-picking target alone is 6.5×4×3 m, centred at vendor X−.5/Y2: it covers the new cab/roof inside the existing reserved X/Z bounds. It is not authoritative collision or a footprint expansion.

Native diagnostics select isolated save roots before creating any host, check all models/orientations/materials/picking and both server rigs, then exercise actual normal simulation offers, purchases, customer-held food, pause and file roundtrips. All ordinary/development save files and directory names are pinned before/after. Final source and copied-EXE acceptance is required before the playtest handoff.

No new art design, coffee/tractor change, pricing, queue anchor, food economics or simulation rule is included. Staff doors remain static geometry; enlarged inspection camera views are labelled separately from normal minimum18/default62 views. Existing roof/fascia occlusion and performance limitations remain.
