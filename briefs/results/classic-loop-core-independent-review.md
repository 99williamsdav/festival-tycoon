# Classic core independent review

Verdict: approved for the reviewed Classic core source scope after bounded repairs. P1 and P2 are closed; the original findings and repair verification remain below as review history. Reviewed the core diff against cfc24719cbc552433d7cdd757a94c48b41b551ad and all 30 file hashes in reports/evidence/classic-loop/slice1/review-checkpoint.json matched at review time. UI/game work and packages.lock.json are excluded. No implementation or baseline reference files were edited.

## Findings

1. **P1 — deceased maintenance worker can be dispatched again.** ClassicCasualties.cs:50-51 clears the deceased maintenance worker's job to None but retains WorkerId, and marks the person Departed while leaving Admitted true. EquipmentIncident.cs:86 only checks the assigned worker is admitted, so the ordinary DispatchMaintenance command accepts them. ApplyEquipmentCommand then gives them a maintenance route and active job. Reproduced with normal paid Classic preparation, maintenance.worker hired, opening and 8,000 ticks, the existing fatal medical entry invoked for that worker, then EquipmentCommand(DispatchMaintenance). The command returns IsAccepted=True; immediate Restore returns “A deceased Classic person retains navigation, performance or work ownership.” This can break the durable command/save flow after a supported casualty. Reject dispatch for a deceased/departed worker without changing Survival behavior; add a command-after-death regression rather than only ticking after death.

2. **P2 — new Classic cross-validation dereferences malformed state before shape validation.** ClassicCampaign.cs:107 reads PerformanceRecords elements before ValidatePersistedPerformanceRecords; ClassicCasualties.cs:127 enumerates live.Performers before ValidatePersistedLivePerformances. Reproductions: a fresh Classic snapshot with PerformanceRecords=[null] throws NullReferenceException from ValidateClassicHistory; the observed closed repeated-death snapshot with LivePerformances[*].Performers=null throws ArgumentNullException from ValidateClassicLifecycle. Neither returns SessionRestoreResult.Failure. SaveFileAdapter.Load's catch filter also does not include these exceptions. Guard the shapes consumed by the new cross-validation before enumeration, or safely reorder structural validation, and add negative tests for null elements and nested arrays. Audit the other newly early-read collections, including equipment evidence, against their later validators.

## Independent checks

- dotnet test tests/Festival.Tests/Festival.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -nr:false -- --filter 'FullyQualifiedName~ClassicCampaignTests': **11 passed, 0 failed**, duration 58.465 seconds. Workspace TEMP/TMP used; no recording or evidence-generation flags set.
- Separate small console probes under reports/evidence/classic-loop/slice1/review-probes reproduced both findings using the compiled Simulation assembly. The malformed pond-trial probe was rejected normally.
- Reviewed the builder's four-paid-festival evidence, causal injection disclosure, Survival immutable-boundary report, and bounded-regression report. Those wider suites were not independently rerun. Existing immutable references were not recorded or replaced.
- Source inspection found the intended ordinal/Tier separation, parked-debt and canonical cash carry, optional Classic hash/JSON extension, mild weather, pressure scaling, doubled timing profile, casualty reputation floor, live cleanup, and conditional Survival branches consistent with the scoped design, subject to the findings above.

## Limits

This is core source review, not native UI/playtest acceptance. Death fixtures intentionally enter causal fatal boundaries and do not demonstrate naturally occurring fatalities in an ordinary player session. No claims of ownership acquisition, expansion, rental placement, or loan changes are made. This core review does not authorize UI/native acceptance or a final playtest handoff.

## Bounded re-review 1

Repair checkpoint review-repair-checkpoint.json verified, with all unchanged files still matching the original checkpoint. Independent targeted tests passed **5/5 in 16.816 seconds**. Re-ran the original standalone probes: deceased maintenance dispatch is rejected and the resulting snapshot restores; null performance records and null live performer arrays now return failure. **P1 closed.** The specific original P2 reproductions are repaired.

**P2 remains open for the same structural-validation class:** on the observed second-festival `next` snapshot, set `Preparation.CarriedIn.OwnedEquipment=null`. Restore throws ArgumentNullException at Preparation.cs:593 (`p.CarriedIn?.OwnedEquipment.Contains`). The new Classic branch bypasses CarryIssue at Preparation.cs:540, including its existing null-owned-kit guard, and ClassicHistory validation runs too late. Add early Classic carried-kit shape validation before preparation reads this collection and a later-festival negative save test. This was reproduced independently with the unmodified observed next snapshot except that one null field. No immutable references or implementation files changed.


## Bounded re-review 2 — final core verdict

**P2 closed; scoped core review approved.** Verified the cumulative original, repair, and carry-repair checkpoint hashes, with no unexpected changes. The early Classic-only CarriedIn.OwnedEquipment null check now runs before preparation validation and leaves the Survival path unchanged.

Independently ran the updated FourPaidFestivalsCarryActualHoldingsRelationshipsAndResetOnlyTheDay test: **1 passed, 0 failed, 44.644 seconds**. This exercises four paid festivals, valid restoration and all three continuations, and rejects the null carried-kit mutation on each later festival. Re-ran the standalone probes: null carried kit, null performance record and null live performer arrays all return validation failure; deceased maintenance dispatch remains rejected and its snapshot restores successfully. All builds/tests finished.

No outstanding findings in this reviewed core scope. The wider Survival evidence remains builder-run evidence as identified above; no baseline was recorded or replaced. UI/native behavior remains a separate review slice.
