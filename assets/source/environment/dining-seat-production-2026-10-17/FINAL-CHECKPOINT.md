# Builder checkpoint — ready for independent review

Geometry and review sources are frozen. No production acceptance or runtime handoff is claimed until the coordinator receives the independent fresh Astra medium review.

- Approved scope: H1 plain tied bale and P1 classic A-frame only.
- Production: H1 566 triangles / P1 1196 triangles; one embedded palette material each; 2 / 4 local seat anchors; grounded identity mesh transforms; no people, props, skin or animation in either GLB.
- Current visual evidence: `renders/` and `review.html`; all hair palettes repaired and packed, both hay twine bands continuous, no rectangular side apron/tile surfaces.
- Final evaluated evidence: `final-check.log`, `verification/*-metrics.json`, neighbor checks, per-seat supported contact coordinates and per-seat furniture intersection details.
- Six real native-scale guest poses: pelvis/seat gap1mm, both shoe minima0–.012mm, zero body/furniture, food/furniture, food/body or neighboring actor/food crossings, no evaluated body/food vertices contained inside solid furniture.
- P1 clipped evaluated triangle underside/thigh clearance: .13988m male / .14424m female. Opposing body vertex/surface minimum .15832m.
- Pure-Python structural/export assertions: PASS in `verification/export-verification.json`.
- Review-only seated/eating poses. Traversal, sit/stand transitions, game integration, runtime animation and EXE are outside this package and not tested.

Authoritative package/referenced-source hashes are in `PROVENANCE-sha256.json`; runtime hashes are also in each `manifest-*.json` and export verification. `HANDOFF.md` and `SEATING-AND-AXES.md` describe reproduction, dimensions, anchors and proposed access reservations.

FINAL ACCEPTANCE: Fresh independent GPT-6 Astra medium review passed; see REVIEW.md and verification/independent-* evidence. Parent accepts this asset-production delivery. Runtime integration remains outside scope.
