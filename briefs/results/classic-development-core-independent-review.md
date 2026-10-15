# Classic development core independent review

Reviewer: independent GPT-6 Astra, medium reasoning. Date: 15 October 2026.

Scope: uncommitted simulation changes relative to d54224f7, ClassicDevelopmentTests.cs, recorded-clear resource and supplied test evidence. Game/UI, native execution and exports are excluded. Source inspection only so far: the builder explicitly reserved the build/test lane. No source edits, commits, builds or test runs were performed by this reviewer.

## Current disposition

Changes requested; core acceptance remains pending. One confirmed P2 finding is open. The builder also identified a failed next-festival relationship restore while this review was in progress. No UI or playtest handoff approval is implied.

## Finding

**P2 — Include every mandatory sound engineer in the booking setup minimum.** `src/Festival.Simulation/ClassicBooking.cs:34` computes `MinimumRequiredSitePennies` with one minimum wage per staff role. Development bookings now require one distinct sound engineer per active stage. A Barn/Pond or three-stage booking therefore understates mandatory setup costs by one or two engineers, and `Risky` can remain false with insufficient conservative setup cash. Sum the cheapest `Stages.Count` distinct sound candidate wages, keeping the existing single minima for other roles. Verify the estimate for one, two and three active stages and preserve ordinary one-stage behavior. Sent to builder during review.

## Evidence examined

- `artifacts/classic-development-core-tests-third.txt`: builder execution, 22 passed, zero failed. This precedes the newest admission repair and does not prove the complete later-festival path.
- `artifacts/classic-stress-two-festivals-second-details.txt`: builder execution, arrival-route test passed; two-festival test failed restoring the newly created festival 3 (`Act relationships invalid`, test line 116). Builder reports festival 2 finished with all 133 persons admitted/departed, 100 actual attendees and nine records; its results restore passed. Relationship-stage validation repair and repeat are still pending.
- Recorded resource SHA256 independently read: `80153452F7A5977E2D9A35F3705B4BCF8A77F9FAA24440D78B9029D112D4AB64`, matching the commissioned extraction.
- All seven nonempty subset tests exercise construction, bookings, hire requirements, start, save/restore and stage-specific mixing posts. They do not themselves exercise live generator cutoff at index zero, later live sets or two-festival completion for each subset.

## Source assessment

The diagnostic snapshot explicitly selects its stage mask and independent default-off ticket/cash flags. Current and historical booking journals must match that provenance; ordinary saves omit the added nullable fields. The starting carry is built by restoring the recorded normal first clear, while reputation 40 is an explicit separate diagnostic milestone. The grant adds £1,238.10 to the £261.90 carry for £1,500 total, is ledger-backed and applies only to festival 2. Current/historical validation reconciles the extra amount. Stage power lookup and mutation now use physical stage IDs, including a Barn/Pond at active index zero. Engineer candidates map to distinct role offers across all three slots; contracts span only active stages. The preparation destination repair is gated to the diagnostic and retains reachable legacy destinations while choosing deterministic distinct reachable neighbours for blocked ones.

No other confirmed correctness finding from the inspected diff at this point. Normal Survival branching and null-field serialization appear preserved in source; immutable Survival parity must still be established by execution against the final repaired source. Nine-slot rebooking/refund replay, defaults-factory creation, two-festival completion/carry, and the newest changes need final evidence before acceptance.

## Narrow re-review update

The P2 estimate finding is resolved in source: sound wages now sum the cheapest number of distinct candidates equal to the active-stage count. All seven subset cases assert the setup minimum. The relationship restore repair uses each historical booking's slot capacity and carries its actual stage count into expected-crowd replay. No new correctness finding from those repairs.

Read `artifacts/classic-stress-core-regression.txt` and its details: builder run passed 44/44, including original immutable Survival parity, ordinary booking/demand/contracts, all development subsets/default options, and the two-festival scenario. Independently read `reports/evidence/classic-stress/core/two-festivals.json`: festival 2 finished at tick 46957 and festival 3 at tick 46163; each has 100 guests, 133 admitted/departed persons, nine performance records and zero NoRoute agents. The passing test also verifies results/next-festival restore and exact cash carry without another grant.

Subsequent observational timing code in `PerformanceTimings.cs` and four scopes uses wall-clock observations only; it is not persisted, hashed or read by gameplay decisions. Source inspection found no new correctness issue. Three subsequent tests cover diagnostic provenance rejection, nine-slot rebooking/refunds and timing/hash neutrality, but have not yet run in the reviewed evidence. Final independent execution remains pending release of the builder's reserved native/build/test lane. The earlier changes-requested disposition is superseded by **source repairs cleared, final test verification pending**. UI/native/export remain outside this review.

## Final independent execution and disposition

**ACCEPTED — simulation core slice only.** No open findings. UI, native presentation, exports and broader performance acceptance are excluded.

After the builder released the lane and froze simulation source, this reviewer independently compiled and ran the exact five-class filter: ClassicDevelopmentTests, ClassicTicketDemandTests, ClassicBookingTermsTests, ClassicBookingIntegrationTests and ClassicSurvivalParityTests. The final build includes PerformanceTimings.cs and all four observation scopes.

- Initial execution: 47 total, 45 passed, two failed, duration 4m 06s. Both failures were existing file-host tests at their initial SaveMilestone, using new GUID directories under Path.GetTempPath(). Every development test, the three added provenance/rebooking/timing cases, both complete 100-attendee festivals, and immutable Survival parity passed.
- Exact two file-host tests rerun with approved filesystem escalation and `--no-build`: 2/2 passed in 1.733s. This resolves the initial sandbox temporary-directory restriction without changing or rebuilding source.
- Thus all 47 distinct requested cases independently passed against the same final compiled source. No FESTIVAL_RECORD flags were set, no immutable fixtures changed, and no source changes were made by the reviewer.

Preserved evidence: `artifacts/classic-stress-independent-core-tests.txt`, `artifacts/classic-stress-independent-core-tests-details.txt`, `artifacts/classic-stress-independent-filehost-retest.txt`, `artifacts/classic-stress-independent-filehost-retest-details.txt`.

The build/test lane is released. This final disposition supersedes earlier provisional entries while retaining their finding and repair history.

## Narrow follow-up: bounded work-duration p95 samples

Reviewed only the updated `PerformanceTimings.cs` after the previously accepted core build. **Source change accepted; no new correctness findings.** No build or test was run during this source-only follow-up because the builder retained the native lane.

Each enabled scope retains at most 65,536 exact duration samples; disabled scopes remain unobserved. Counts, accumulated duration and maximum continue across every observed call. The p95 uses the correct nearest-rank index, and a published dictionary entry has at least one duration sample before capture. These observations remain outside gameplay decisions, persistence and authoritative hashes.

Reporting limitation: p95 describes the first up-to-65,536 calls, not every call when `Calls > SampledCalls`. Present it as sampled p95 in that situation; do not describe it as full-run p95. The exported SampledCalls makes this distinction inspectable. The builder will execute the existing hash-neutrality test after its current native run; that new result is not included in the earlier 47-case evidence.

Final bounded-p95 verification: independently read `artifacts/classic-stress-p95-neutrality-test.txt`, showing a rebuild of simulation and tests followed by 1/1 passed, zero failed/skipped, in 3.171 seconds. The builder identifies this focused execution as `PerformanceObservationsDoNotChangeGameplayOrSavedHashes`. This is builder-run evidence independently inspected, not a second reviewer-run execution. Together with the narrow source review, it closes the pending observational-timing verification. Simulation core acceptance remains in force with the sampled-p95 reporting limitation above. No lane was used for this evidence read.
