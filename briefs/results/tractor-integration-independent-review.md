# Tractor B integration — provisional architecture review

Read-only source audit on 16 October 2026 against the integration starting point c06a9d5c. No implementation acceptance is implied. No build, test, native runtime or export was run; Builder owns the exclusive execution lane.

Inspected WORKING-AGREEMENTS.md and the approved package HANDOFF.txt, placement.json, manifest.json and verification/glb-reimport.json. They agree on unchanged GLB placement at barn-local (0,0,2.1), world (0,0,-20.9), yaw zero, scale one; raw bounds X ±1.127 m and local Z [-1.860,2.190] m. The package records 14,962 triangles, 12 materials, seven mesh nodes and glass as the only alpha material. Package assertions have been read, not independently reproduced.

## Recommended bounded compatibility model

Use an explicit session-owned geometry revision, for example BarnGeometryVersion=0 for legacy and 1 for Tractor B. Serialize the field only when nonzero and append its canonical hash discriminator only when nonzero. Missing old fields must mean zero. Reject unsupported values. This preserves exact historical serialized field sets and hashes while marking the current geometry change honestly.

Set current revision before campaign setup builds or validates geometry. New public campaign/development starts select current revision; historical parity fixture setup explicitly selects legacy revision without changing its immutable reference outputs. Prefer a narrow internal constructor parameter or test helper over a global mutable override. A global override can bleed between parallel tests and runtime sessions.

One derived presence predicate must drive visual presence, terrain construction, movement sweeps and placement checks: current geometry revision AND !BarnStageOpen. Stage membership already accommodates optional Classic stage masks; do not infer conversion from tier or festival number. A missing saved blocker must not independently cause the renderer to show a tractor.

Restore legacy snapshots exactly, retaining their original saved grids and version zero. Do not inject obstacles into a live restored grid: its routes, positions, destinations and authoritative hash were created against the legacy geometry. Preserve version across current-version save/restore, candidate-session coordinators, rollback and retry. If upgrading old saves is required, that is a distinct migration needing a safe lifecycle boundary and route/layout repair; it must not happen incidentally in Restore.

## Source dependencies and complications

- GameSession.Restore restores the serialized grid and compares the exact canonical hash. A conditional new hash discriminator and omitted zero field are necessary to preserve legacy resources. New-version validation should check the required blocked cells when a grid exists, and prevent visual/continuous collision from disagreeing with a saved grid.
- Preparation.ApplyStartPreparedEdition builds terrain, then overlays stage cells and other objects. Check the final grid, not just BarnLayout.Terrain output. RetryPreparedWeekend recreates a baseline and resets the grid. Preserve the originating geometry revision through retry rather than adopting a constructor default.
- BuildLayout.ValidateBuildLayout and another terrain-based placement check construct terrain from stages. Propagate geometry through those checks and saved-layout validation. LastFestivalLayoutFit caches by LayoutFitKey; if geometry affects validation, include geometry in that key. Also inspect fallback grids in Immersion, Toilets and response-post placement rather than assuming every call is current gameplay.
- ClassicBooking creates a baseline; ClassicCampaign and NextFestival create fresh sessions. Pass geometry deliberately through these paths. Choosing preservation across an existing campaign is the least surprising legacy policy. New starts can opt in explicitly. Do not accidentally upgrade a restored legacy campaign on retry or a host clone.
- ClassicDevelopment restores embedded recorded-classic-first-clear.json both to derive RecordedClassicHistory and to make a development start. Keep that resource restorable under legacy geometry. A newly requested diagnostic development start may explicitly choose current geometry after extracting the historical carry; its immutable recorded history remains historical.
- RecordedDevelopmentStart restores recorded-tier-one-clear.json and calls CreateNextFestival. Existing tests require the menu start to match the ordinary transition exactly. If a development wrapper changes revision independently, this equality changes even though Tier 2 has an active Barn and no tractor. Define and test that policy explicitly; do not modify the resource or repin its baseline.
- SurvivalArchitectureParityTests and ClassicSurvivalParityTests construct new campaigns before observing all persisted fields. Merely making Restore backward-compatible does not preserve their baseline runs. They need explicit legacy construction while current tractor tests exercise revision one. Their observation projection must not broadly omit new state to conceal behavioral changes.

## Clearance and acceptance points

Keep the raw mesh envelope as the source geometry and expand it by a documented actual actor clearance exactly once. The optional 150 mm asset margin is not that actor clearance. GameSession.SeparationRadiusMillimetres=300 is described as centre separation between people; it is not automatically a body radius. Existing BarnLayout comments use a 380 mm pick capsule/frame margin, while hands-on wall lookahead is 250 mm. Establish the selected physical clearance and test both autonomous and hands-on movement against the envelope.

Grid rasterization and continuous sweeps must agree, including movement proposals and separation nudges in GameSession.Navigation and manual movement in StewardShift. Test crossing segments, corners, the front ballast and rear tyres, destinations near edges, alternative routes and removal after conversion. Preserve existing entrance widths and whole-barn access rules.

Rendering must use the same presence predicate and preserve tractor material opacity independently of shell fading, leaving its glass alpha intact. Final review requires native four-angle ordinary/faded evidence, converted absence, honest default zoom and glass sorting evidence. Package Blender renders do not satisfy native evidence.

Current conclusion: explicit persisted geometry provenance is a viable bounded approach. Legacy/current geometry and migration policy must remain explicit. Final implementation, tests and native evidence remain unreviewed.

## Builder refinement accepted in principle

Builder proposes retaining existing simulation constructor defaults at legacy revision zero and keeping immutable parity test source unchanged. Production new-game/development launch boundaries explicitly opt fresh Preparing sessions into revision one before host construction or first save; a steward factory that starts immediately receives the revision before Start. This is an acceptable, narrower alternative to changing constructors and adapting fixture source.

The opt-in must be an authoritative persisted/hashed revision with a strict fresh-session guard, not a global default or arbitrary setter. No live grid, agents or started edition may be silently migrated. Since a normal constructor has already performed setup, opt-in must repeat any geometry-sensitive validation and invalidate layout-fit caches. Next festival and Classic rebooking must receive the originating revision before setup; retry retains it. Every actual fresh runtime mode needs evidence of revision one, including optional stage masks and hands-on routes. Legacy loaded sessions retain zero and therefore no tractor visual or blocker. Embedded recorded transitions remain reproducible; any current-runtime opt-in happens after the canonical recorded transition at the launch boundary, avoiding changes to the historical resource or its equality tests. Unknown revisions must be rejected.

## First implementation source review

Inspected the tracked tractor diff, new BarnTractor.cs and BarnTractorTests.cs without running builds/tests/native. Unrelated palette import and package-lock modifications were excluded. Independently read the integrated GLB SHA256: `2c501b42d2b440f8f5623281f4f3426fdd6261e3f1fa04e56db74a0b557a187d`, matching the package manifest.

One actionable P2 finding: Main.DevelopmentTierEvidence.cs line 86 still compares the production DevelopmentTier2 launch (now revision one) against CreateRecordedTierTwoStart() (revision zero). The canonical hashes necessarily differ. This current-runtime harness should compare against the explicitly opted-in reference and assert revision one. Historical fixture sources and recorded baselines need no change.

No simulation blocker found in this source pass. The implementation propagates version through capture/restore/hash, next festival and Classic rebuilding; retry retains the existing field. The private campaign factory sets version before SetUpBuild. Current layout checks and the fit-cache key include version. The fresh opt-in validates the existing layout and clears the cache. Geometry uses all mesh bounds plus a single 380 mm expansion; game/Main.cs confirms the upright actor pick capsule radius is 0.38 m. Both autonomous proposal/nudge paths and hands-on movement pass the shared presence predicate. Barn model fade excludes the tractor sibling. Navigation validation runs before the new geometry validator and rejects null/duplicate terrain cells before its dictionary conversion.

The first test file does not yet directly substantiate malformed missing/walkable-footprint rejection, current-version host rollback, retry/next continuity or actual hands-on approach movement. Its ten cases cover seven mask rows plus legacy/current provenance, footprint/pathfinding/pure sweeps, and steward/recorded Tier 2 starts. These remaining acceptance claims need suitable focused evidence. Native rendering and opacity/glass evidence remain pending. This is source review only, not final playtest/export acceptance.

## Narrow source re-review

The P2 Main.DevelopmentTierEvidence comparison is now corrected to the opted-in canonical transition and explicitly asserts revision one. Finding closed by source inspection.

Four additional test methods now exercise missing/walkable saved footprint rejection before hash validation, rejection of a formerly legal placement without mutating the legacy session, real Council retry and canonical next-festival propagation, and host rejection/save failure preservation. These directly address the earlier lifecycle/validator coverage gaps; execution results were not independently run in this lane.

Main.TractorEvidence uses the native imported scene, real fade processing, timed frames, four ordinary solid/faded angles, default zoom, converted angles, mode host switching, file round trips and a full current-geometry three-stage festival. Its geometry assertion measures seven meshes, 34 surfaces, 14,962 triangles, bounds and placement. No additional source blocker found. Suggested evidence hardening: ensure material checks cannot pass over an empty StandardMaterial3D sequence and explicitly assert that Tractor_Glass is present and retains alpha. Glass sorting still requires inspecting the resulting native pixels, regardless of property assertions.

Final source/evidence acceptance remains pending completed build/test/native output and visual inspection. No reviewer execution lane was used.

## Native evidence inspection and test status

Independently inspected all 16 PNGs in reports/evidence/tractor/builder-native: four solid ordinary angles, four faded ordinary angles, four converted angles, both default-62 views, optional inactive Barn, and the 133-person three-stage live overview. The ordinary solid roof naturally hides the tractor from rear angles and exposes the bonnet at the entrance-facing angles. Faded views reveal the complete tractor; the opaque tractor surfaces remain distinct beneath the faded shell. No visible cab-glass sorting break was found at the supplied native resolution in these four headings. The converted views contain no tractor. Default zoom renders it honestly small. No blocking visual finding.

Read checks.json: 102 checks, zero failures, including non-vacuous 34-surface opaque material checks, retained cab glass, state/grid agreement, exact file round trips, parked-host state, completed three-stage festival, next-version retention and legacy absence. import.json matches approved mesh counts/bounds/placement. Read builder build log (zero warnings/errors), 44/44 regression log and 14/14 targeted log. These are inspected Builder results, not independent executions.

Latest test correction replaces an invalid assumption that bins were legal in the old barn with an atomic rejection test for an already-started legacy session. Latest added manual-input test uses a labelled initial-position fixture, then real production input/ticks, blocking and open-floor positive controls plus roundtrip; source inspection found it appropriate.

Reviewer initially attempted a targeted no-build run with the filter before `--`, which this MTP setup did not apply as intended. That overlong run was interrupted through its own session; no pass is claimed. Log preserved at artifacts/tractor-reviewer-targeted-tests.txt. Confirmed no remaining dotnet/Festival.Tests processes and released execution lane to Builder. Correct targeted syntax requires `-- --filter FullyQualifiedName~BarnTractorTests`. Independent corrected run is pending the latest 15-case build.

## Source/native verdict — accepted for export

Independent corrected targeted run completed successfully: 15 passed, zero failed/skipped, 36.463 seconds, process exit zero. Command: `dotnet test tests/Festival.Tests/Festival.Tests.csproj --no-build --no-restore -- --filter FullyQualifiedName~BarnTractorTests`. Log: artifacts/tractor-reviewer-targeted-final.txt. This includes the final real-input positive/negative movement test.

No open blocking findings in the reviewed implementation, tests or supplied native evidence. The earlier P2 harness reference mismatch is closed. The amended material checks are non-vacuous and explicitly preserve glass alpha. Source/native integration is accepted for the authorized export step. This does not certify a copied executable not yet produced; final actual-EXE review remains required. Execution lane released after the completed independent test run. No implementation edits were made by this reviewer.

## Bounded diagnostic-isolation repair review

The subsequent copied-EXE reviewer identified a P2 diagnostic-isolation defect missed by this source pass: Classic development saves in the tractor harness used the default user profile directory because ClassicDevelopmentSaveDirectory recognized only the stress output override. Four known diagnostic slots written outside the evidence directory have been retained untouched. There are no before-pins; neither profile preservation nor absence of overwritten prior content can be claimed.

Independently reviewed the two-line repair against game commit 0df96421. ClassicDevelopmentSaveDirectory now selects `_classicStressEvidenceOutput ?? _architectureEvidenceOutput` before the ordinary user directory fallback. `--capture-tractor` sets `_architectureEvidenceOutput` before hosts are created, and BeginClassicCampaign constructs the diagnostic development host using this helper, so tractor development autosaves and named file-roundtrip slots now use the requested output/development-saves directory. Existing stress precedence is preserved and normal non-diagnostic gameplay retains its existing default directory. The tractor harness additionally asserts the exact development host directory before its live roundtrip.

No source blocker found in this bounded repair. No build, test, native run or export was performed during this re-review; another reviewer held the execution lane. Repair accepted at source level only. The rebuilt/copied executable and its rerun isolation assertions still require fresh acceptance, and earlier native acceptance does not establish that repaired executable's behavior.
