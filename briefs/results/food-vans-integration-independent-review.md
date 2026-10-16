# Independent food-van integration review

Status: PASS — independent source/native integration review complete, with the explicit existing-lighting shadow-banding limitation below. Earlier sections retain the review history; final verdict supersedes their pending statuses.

Scope: approved chips, pizza and curry production models, assembly replacement, existing gameplay/server contracts and isolated integration diagnostics. Read docs/WORKING-AGREEMENTS.md and the production HANDOFF.md, INTEGRATION-NOTES.md, REVIEW.md and FINAL-CHECKPOINT.md. Reviewer has made no product changes and has run no build, test or native process; the builder owns the exclusive execution lane.

## Source precheck

No material issue found in the initial Main.ImmersionAssets.cs and Main.Immersion.cs changes. The product-to-resource mapping matches the three actual trader products. Each replacement retains ApprovedFoodVanAssembly at (-2.15,0,0), removes legacy fascia/name/roof/kitchen additions and palette substitution, preserves the external chalk menu and attaches curry steam at the delivered markers. The ray-target box occupies local x [-3.75,2.75], y [0,4], z [-1.5,1.5], covering all delivered body bounds inside the existing reservation. Simulation reservation, queue/service cells, server rig anchors, clips and held-prop source remain untouched.

Existing SyncImmersionWorld rebuilds on trader art changes, and ProcessServiceServers detects the changed vendor parent and recreates its server view. Runtime confirmation is still required, including pick registry cleanup. The two curry marker casts rely on the frozen package contract; import evidence must confirm both markers exist as Node3D.

## Evidence requirements communicated to builder

- Verify actual imported models, one production model per van, no legacy decoration or material overrides, embedded artwork, and two correctly placed curry emitters only.
- Cover all three variants and all four placement quarter-turns, actual physics ray picking of roof/cab, unchanged reservation/service cell, placement/move acceptance, trader rebuild and removed body/pick/server cleanup.
- Verify unchanged server local position (0,.47,.88), yaw PI and unit scale, both sexes, every food clip and transitions including retract/reset, prop tilt/clearance and no footprint drift.
- Use real authoritative queue/service progression and purchases to demonstrate customer approach, product hand-off and exactly one recorded transaction. Staged offer frames cannot establish these behaviours.
- Verify paused simulation/server poses/held props and curry particles freeze, and resume without accumulated animation jumps.
- Route diagnostic saves before the first SessionHost constructor and any normal setup. Pin all player-save files and exact directory names before/after. The existing standalone --capture-servers command currently does not isolate SaveDirectory despite its comment, so it must not be used as-is. Its reusable pose checks only progress through offer plus a skipped transition and require additional retract/reset coverage.

This is a preliminary source review, not native acceptance, visual sign-off or approval for a playtest handoff. Final evidence and the resulting implementation will be reviewed before changing this status.

## Isolated native harness source audit

Reviewed Main.FoodVanEvidence.cs and its Main.cs wiring before final native execution. The capture flag sets the diagnostic architecture root during ConfigureCommandLine, before the initial SessionHost; subsequent SwitchToFestival uses the isolated save-root architecture. The dedicated process branch avoids normal process-loop mixing. The external player-save guard compares all pinned file hashes, the full file set and exact directory set against the original baseline.

The new harness exercises three actual trader choices, all four real move rotations, placement ghosts, physics ray hits with pick-registry identity, body/server rebuilds, imported mesh/material invariants, six rig/variant combinations and all imported clips. Its live path prepares a normal tier-two campaign, advances authoritative simulation ticks, observes an actual changed purchase receipt, checks correct vendor/product, empty server retract and customer held transaction/native prop, and verifies repeated presentation does not duplicate a purchase. This is materially stronger than staged pose evidence.

Two narrow evidence improvements requested before freeze: strengthen owner-at-counter from navigation Destination alone to actual arrived position and queue-head membership, retaining an explicit queue/approach observation; include _architectureChecks in the final report because it contains readiness and actual-sale save-roundtrip assertions. Existing roundtrip failure already throws, but the recorded results should expose those assertions. No production-code defect identified. Native results and final source still pending; no execution performed by reviewer.

## Sixth-run final evidence review (16 October 2026)

Source and behavioural evidence PASS. Independently read builder-native-sixth/checks.json: 146 food/server checks and 39 architecture checks, all true. Import reports identify exactly the three expected resources, 2,370 / 2,066 / 5,244 triangles, four surfaces, unchanged (-2.15,0,0) assembly and only the retained curry chalk menu. All three actual-sale roundtrips pass. Reviewed core test log: 41/41 pass. Native stdout identifies Godot 4.7.2 Compatibility on RTX3050Ti; stderr is empty. Player-save guard confirms all 233 file contents, file names and 52 directory names unchanged. These are independent reviews of builder execution evidence, not independently rerun processes.

Directly inspected all 24 sixth-run PNGs (each variant: four placed rotations, minimum18/default62, actual offer and actual sale). Approved silhouettes, artwork, proportions and serving relationships are retained. The live views now show the serving side; server props visibly disappear at sale and customer food appears. Roof/fascia occlusion and small default-scale details remain expected.

Visual finding awaiting classification before unconditional native visual acceptance: repeating diagonal/sawtooth shading appears on the broad side panels in chips-angle2-inspection6.png and curry-angle2-inspection6.png; pizza roof streaks are apparent in pizza-angle1/3-inspection6.png and pizza-minimum18.png. This resembles directional shadow-map artefacts, but the cause has not been established by this reviewer. The source and imported counts do not suggest duplicated legacy geometry. Builder was asked for bounded classification or a scoped repair; do not claim pristine visual acceptance while this remains unexplained. No model, light or product edits made by reviewer.

Exclusive execution lane released to builder. No additional test/native/export process launched by reviewer.

## Visual classification closed

Independently viewed all six builder-native-final shadow comparison images and audited the diagnostic toggle. The broad-panel/roof bands disappear when only _sun.ShadowEnabled is false; the same model instance and authoritative state are retained, and shadows are restored immediately afterward. Main.cs production light setup is unchanged. This establishes a shadow-dependent rendering artefact under the existing lighting, rather than duplicated legacy decorations or missing production artwork.

Visual scope accepted with a known limitation: directional-shadow bands remain at some angles in normal play, including the pizza roof at nearest normal zoom. Shadows-off images are explicitly diagnostic comparisons, not the shipped appearance. A global shadow/light redesign is outside this integration; no pristine-render claim is warranted. This closes the visual-classification hold. Final run count and player-save guard are the only pending checkpoint evidence.


## Final verdict — PASS (16 October 2026)

Independent source/native integration acceptance is complete for the approved three food vans. No unresolved material integration finding remains. Independently parsed builder-native-final/checks.json: 155 food/server checks plus 39 architecture checks, all 194 pass; all three actual-sale save roundtrips pass. Final output contains 30 PNGs, including the six inspected shadow diagnostics. Final stderr is empty. Independently read save-check-after-final.json: 233 player files and all 52 directory names unchanged, with no content or file-set differences. Core test evidence remains 41/41 passing.

Acceptance covers the bounded production assembly replacement, preserved server/service/reservation contract, all variants/rotations/picking, trader rebuilds, native rig clips/crossfades/props, real queue-head arrival and transaction/customer transfer, pause and isolated save roundtrips. No economy, navigation, coffee, tractor or crowd behaviour change was required. Review is based on source inspection and independent examination of builder-generated evidence; reviewer did not rerun the application or tests. Export/package execution is the builder's subsequent responsibility and is not claimed by this verdict.

Known visual limitation retained: the unchanged directional-shadow setup produces visible bands on some broad panels and the pizza roof at certain angles, including nearest normal zoom. The diagnostic comparison establishes shadow dependence and restores normal shadows; it is not a production fix. Accepted within the requested unchanged-lighting integration scope, without a claim of pristine shading.

Execution lane remains with the builder. Reviewer changed only this report.
