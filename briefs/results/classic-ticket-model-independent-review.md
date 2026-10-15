# Classic ticket pure-model independent review

15 October 2026. **PASS for the pinned six-file pure-model/audit checkpoint only.** No blocking correctness findings in this scope. This does not approve the integrated booking feature, UI, recovery playthrough, save implementation, export or delivery.

Reviewed against `briefs/classic-booking-ticket-phases.md`, including the explicit 15 October approved contract terms, and `briefs/results/classic-ticket-audit-plan.md`. Base is `4c1f82cf267a2cf50bfc76fb7908715faeaa330d`. The six exact paths and SHA-256 values are recorded in `reports/evidence/classic-tickets/model/review-checkpoint.json`; all six independently matched. No source, test, checkpoint or reference file was changed by this review.

## Findings and behavior

- Demand uses a fresh local keyed random stream derived only from campaign seed and ordinal. It neither reads nor advances a session stream. Price is non-increasing; popularity and reputation are non-decreasing by inspection of the sorted nonnegative weighted popularity, reach and willingness expressions. Arithmetic uses long intermediates where needed, floors sales and caps at supplied capacity. The ±1000 estimate encloses the ±800 settlement reach. No talent, facilities or genre input creates a hidden sales bonus. Known Classic capacity is explicitly 25; the future caller must use it.
- Contract levels match the approved 5%/20%/40% ticket targets, rounded up, and 200/1000/2000 satisfaction basis points. Assessment compares original identities, treats retained-act reordering as free, matches genuinely added/removed acts across all possible assignments, and preserves the previous maximum severity. Comparable means same genre and no more than five popularity points lower; a non-comparable original headliner loss or loss of at least fifteen popularity points is substantial. This implements the declared transparent classification proposal.
- The product source search finds these types only in their new definition files: there is no live session, finance or UI integration yet. Full fee refunds, incremental cash refunds, cancellation reversal and reputation floor are requirements for the next slice, not implemented or proven by these helpers. The audit correctly distinguishes those obligations and calls out the existing fixed-ticket credit, already-paid fees, roster identity, strict persistence and cancellation-state hazards.

## Verification

Independently executed:

`dotnet test tests/Festival.Tests/Festival.Tests.csproj --no-build --no-restore -- --filter 'FullyQualifiedName~ClassicTicketDemandTests|FullyQualifiedName~ClassicBookingTermsTests'`

Result: **9 passed, 0 failed, 0 skipped, 493 ms**. This is an execution of the supplied current binaries, not an independent rebuild. Evidence records both executed assembly hashes. Tests meaningfully cover sampled price/popularity/reputation monotonicity, bounded guidance, capacity, genre/talent separation, stable market and session hash, negative conservative budgets, 65 sampled minimum-budget markets and contract thresholds/rounding.

All ten pins in `reports/evidence/classic-tickets/before/checkpoint.json` also independently matched, including original immutable Survival baseline files and fixture source. The checkpoint records no excluded fields or repinning. Its retained builder log reports two passing Survival fixtures in 3m19.450s. I verified those records and hashes; I did not rerun the long Survival fixtures or independently recreate the claimed pre-change chronology.

The retained initial calibration has ten examples and three failed lowest-carry budgets. Final calibration has ten examples, zero failed budgets and 17–21 sales. Independently checked final recorded budget arithmetic and estimate containment: zero errors. The 65-market test also passed independently. These remain sampled budgets, not actual recovery commands, multiple played festivals, universal balance proof or native playtesting.

Exact expected/actual source/reference hashes and hashes of reviewed evidence and executed assemblies are in `reports/evidence/classic-tickets/model/reviewer-verification.json`.

## Non-blocking test follow-up and next acceptance boundary

The existing reorder test covers moving a retained headliner but only one changed identity. Before relying on multi-act replacement in live commands, add a regression with two or three changed acts requiring a crossed assignment, plus the five/six and fourteen/fifteen popularity boundaries. Inspection finds the exhaustive assignment implementation sound; this is coverage hardening, not a demonstrated defect.

The repeated-refund test exercises a pure target calculation, not an idempotent transaction. Next-slice acceptance must demonstrate cumulative incremental refunds against original quantity, immutable actually-paid fee releases, no extra sales, repeated-command/load behavior, full cancellation inverses with once-only reputation loss, and the same ordinal/market/weather. Retain the planned real negative-cash recovery, strict save/roster validation, three-plus-festival carry and immutable Survival parity checks. No approval of those unimplemented behaviors is implied by this model PASS.

## Narrow test addendum acceptance — 15 October 2026

**PASS; the non-blocking regression-coverage follow-up above is addressed.** Inspected the two added tests in `ClassicBookingTermsTests.cs`: the two changed support acts require a crossed assignment to remain comparable, and reversing their lineup also moves the retained headliner; the boundary test distinguishes same-genre popularity losses of five/six and fourteen/fifteen, plus headliner losses of five/six. These are meaningful checks of the previously identified gaps.

The updated test file matches SHA-256 `76E3961A34BE862185C60618A133688647C9F261320D6AA3A4FE9D4211346687` in `reports/evidence/classic-tickets/model/review-test-addendum.json`. All other five original checkpoint pins independently still match, including both product source files. Verification is saved in `reports/evidence/classic-tickets/model/reviewer-addendum-verification.json`.

Independently reran the same focused command on existing binaries with `--no-build --no-restore`: **11 passed, 0 failed, 0 skipped, 478 ms**. No new product integration is approved; all integration and delivery boundaries of the original scoped review remain in force.
