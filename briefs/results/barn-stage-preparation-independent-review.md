# Barn Stage preparation — independent review

**PASS — preparation acceptance only.** 13 October 2026. Fresh independent reviewer, GPT-6 Astra with Medium reasoning, separate from the plan writer. No blocking findings in the commissioned preparation scope.

Reviewed `docs/WORKING-AGREEMENTS.md`, `briefs/results/barn-stage-integration-plan.md` and `briefs/results/barn-stage-preparation-checkpoint.json`, the complete concept `HANDOFF.txt` including its B correction, the corrected `02-end-on-hall-v2-gable-facing.png`, manifest, floor plans, farm context and four-camera diagrams. The current explicit approval of B supersedes the concept package's older awaiting-selection wording.

## Checkpoint and evidence

- HEAD: `4b70adf8927b172d9b71b0c7930bf372a024e246`.
- Reviewed plan SHA256: `6311FE6FBCF12657169A97E3B3F7971F8A133C4C4CB88F3D9A9EC63C6C9074DC`.
- Reviewed checkpoint SHA256: `80D3D93E74A30431D1B536948375C43C36C44E37D022B0C7F523217CDC6B85F8`.
- Independently recalculated all 14 source hashes and all six design-input hashes listed in the checkpoint: all match.
- Git status shows no changed implementation source. The plan/checkpoint are untracked preparation documents; existing dirty character import metadata, simulation package lock, lead-outfit directory and UID files remain outside this commission. This reviewer changed only this review document.
- `barn-stage-asset-v1/HANDOFF.txt` is absent at the inspected design workspace location. No completed asset, native Barn implementation, passing Barn tests or exported executable is asserted.

## Independent source findings

Direct source reads confirm the important integration obstacles described in the plan:

- `FarmScene.cs` retains `farm.large-barn` at (0, -23), while `NavigationFixture.cs:43` blocks the whole old rectangle. Opening an actual measured interior requires authoritative terrain changes, not only presentation changes.
- `FestivalStage.cs` exposes anchors, deck/stair geometry, crowd and reserve contracts. Its current `All` list is also the two-stage normal-play list, and `Find` searches that list. `MultiStage.cs:87-89` uses `For` for current and saved stage sets; Tier 2 opens Pond. Distinct Barn lookup must therefore be separated from normal allocation. The plan correctly forbids casually appending Barn to `All`/`For` or redefining Pond.
- `Main.LivePerformance.cs:264-269` hardcodes the non-main stair rise to Pond world X, and its light selection chooses Pond for every non-main stage. Barn needs explicit ownership and height behavior. `FestivalAudio.cs:240` falls back to Trailer when lookup fails.
- `RoofFade.cs` selects material suffixes `_Canvas`/`_Roof`, uses .30/.25 s/1 s/.5 s, duplicates instance materials, retains shadow twins and restores opaque rendering. `Main.Marquee.cs:63` supplies marquee occupancy or selection. Neither establishes all-role Barn occupancy or wall fading.
- `Main.cs:372-375` adds the full farm-object pick box; `Main.Hover.cs:60-66` uses one layer-1 ray hit. Sparse Barn targets and actual occupant click/hover assertions are necessary.
- `Shelter.cs` currently derives coverage from placed services and caches it against placement identity. Permanent measured interior coverage and initial/rebuilt/restored cache checks are appropriate. `Weather.cs:311` consumes coverage for people.

## Acceptance assessment

The plan faithfully carries approved B: band backs to the short east gable, facing west along the hall, with separate reused Duck/Pond kit instances and the proposed rotated stair module. Dimensions remain handoff-dependent targets. It preserves the existing barn, front entrances, clear cross-aisle, track and neighbouring geometry; it does not mistake illustrative transparency for runtime proof.

Planned verification is adequate for this preparation stage: real swept guest/band/staff routes and reservations, stair-only deck access, measured shelter boundaries, distinct programme/audio/light ownership, each occupancy role and selection, timing/re-entry/material isolation, four camera directions at two zooms and lighting conditions, and native visible-person picking. Normal stage allocation, roster, economics, timings, unlocks/trial and accepted fisticuffs are explicitly protected.

The authorized activation default is precise: physical interior/shelter integration after coordinator asset handoff, with programmed Barn behavior exercised in a labelled controlled fixture until a separate venue-allocation commission. Fixture inputs must explicitly provide roster, equipment and power. Any diagnostic stage cards/booking/audience presentation must receive the fixture configuration without broadening normal `All`/`For` consumers. The plan's factory/persistence boundary appropriately prevents fabricated campaign-save claims; a real continuation limitation must be reported.

No tests were rerun for these documentation-only changes. This PASS accepts the integration map and future acceptance cases; it does not establish that Barn works. Coordinator asset handoff remains required before integration. Fresh independent review of the actual implementation and a separate review of evidence from the copied Windows EXE plus adjacent data remain mandatory. The plan correctly requires no ZIP and no routine manual-QA gate.

## Final status addendum — 13 October 2026

**PASS retained, preparation only.** Verified the final plan at commit `fa42420`; SHA256 is `020221C77413582BEA78FACDA6C8D532EF63DCB27B5B26F5CA01DC5366659AE3`. Reversing only the Current outcome replacement (review pending to actual PASS, report link, matching-hash result and explicit future review requirements) reproduces the original reviewed SHA256 `6311FE6FBCF12657169A97E3B3F7971F8A133C4C4CB88F3D9A9EC63C6C9074DC`. Thus the substantive plan is byte-for-byte unchanged. The earlier hash remains historical evidence, not the current plan hash. No tests rerun; no implementation or asset edits. Actual source and Windows EXE reviews remain required after coordinator asset handoff and implementation.
