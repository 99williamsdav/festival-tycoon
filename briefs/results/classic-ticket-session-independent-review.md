# Classic booking session independent review

15 October 2026. **Initial checkpoint: changes required. One blocking save defect.** Review covers the economic/session integration against base `113f5b03`, using the exact 22-file checkpoint in `reports/evidence/classic-tickets/session/review-checkpoint.json`. All 22 pins matched before and after independent verification. No product or test source was edited by this reviewer.

## Blocking finding

**[P1] Validate paid contracts with opening scene credibility.** `src/Festival.Simulation/ClassicBooking.cs:223` overwrites the factory's payment standing with `p.SceneCredibility`, including credibility earned at settlement. A legitimate stretch booking can therefore be repriced as an available booking during validation, and its completed save rejected.

Reproduced using real commands and simulation, with no injected cash, reputation or results: Classic seed 14; first offered perk; Bus Replacement Service, Two Men and a Harmonium and Glitter Rota; default £10 price; default site; 24 soft drinks/16 beers; cheapest staff candidate for each role. Glitter Rota is booked for 4800p with opening reputation/scene zero. Opening save restores successfully. The festival finishes with 22 guests, three stars and reconciled accounts; Pop credibility rises to 15. Completed restore fails with `Classic paid contract quote is invalid: Invalid, ineligible or mispriced act contracts.` Replaying reputation zero alongside the new scene reclassifies Glitter Rota as Available and expects 3200p.

Use the scene at booking/opening (the existing `StandingBefore` boundary after settlement), while retaining each journal action's cancellation-adjusted reputation. Add the exact played stretch regression, including completed save/hash restoration and subsequent carry. Probe source is `artifacts/classic-ticket-session-review-probe/Program.cs`; original output is `reports/evidence/classic-tickets/session/reviewer-stretch-probe.txt`.

## Other reviewed behavior

The reviewed commands begin with actual loan/carried cash, charge ticket receipts and normal catalogue quotes at confirmation, and leave site/staff/stock charges for opening. No additional borrowing or programme cap was found. Empty post-sale slots release paid fees, apply original-announcement loss severity, and block opening. Repeated/restored lineups retain the maximum refund/disappointment; cancellation reverses the attempt's net cash, preserves festival/market/weather identity and applies the five-point reputation floor exactly once per confirmed cancellation.

Roster reconstruction keeps actual holders across person, wallet, medical and disorder collections and preserves announcement-based fan genres. Accounts expose sale/refund attempts and signed band releases. Historical ticket facts feed relationships and popularity replay. Inspected save validation covers actual cash, phases, quote identity, roster, history and carry; the completed quote-standing issue above is the demonstrated exception.

Both retained actual four-festival histories reconcile, including cancellation after reputation earned in Festival 1. The negative example starts Festival 2 at -385p, reaches -16285p through actual confirmation, restores and cancels to exactly -385p before playing that festival. These are bounded recovery examples, not a universal balancing guarantee.

## Verification

Independently executed the existing binaries:

`dotnet test tests/Festival.Tests/Festival.Tests.csproj --no-build --no-restore -- --filter 'FullyQualifiedName~ClassicBookingIntegrationTests|FullyQualifiedName~ClassicBookingLedgerTests|FullyQualifiedName~ClassicTicketDemandTests|FullyQualifiedName~ClassicBookingTermsTests|FullyQualifiedName~ClassicCampaignTests|FullyQualifiedName~ClassicWeatherTests'`

**53 passed, 0 failed, 0 skipped, 1m16.251s.** Atomic save tests used the authorized workspace TEMP/TMP directory. Output is `reports/evidence/classic-tickets/session/reviewer-focused-tests.txt`. This is independent execution of supplied assemblies, not an independent product rebuild. The separate bounded probe references the same simulation assembly.

All ten immutable Survival reference/fixture pins independently match the pre-booking checkpoint. The builder's final two-fixture log reports **2 passed, 2m29.001s**; I inspected that output and the unchanged pins but did not rerun those long fixtures. Hash evidence and executed assembly hashes are in `reviewer-verification.json` and `reviewer-survival-pins.json` beside the test logs.

## Boundary

This is an economic/session review only. The pinned `game/Main.Classic.cs` change moves new saves from v100 to v101; previous saves remain preserved without silent migration. Concurrent unpinned UI work, diagnostic changes, native playtesting, visual handoff and Windows EXE delivery are outside this review. No reference files were rewritten or repinned, and unrelated import/lock/outfit/UID files were not changed.

## Narrow repair re-review — 15 October 2026

**PASS for the repaired, pinned economic/session scope. The blocking completed-save finding is resolved; no remaining blocking findings found in this scope.** This final disposition supersedes the initial changes-required disposition above, whose reproduction and original evidence are retained.

Verified every pin in `reports/evidence/classic-tickets/session/repair-checkpoint.json` before and after re-review. Only `ClassicBooking.cs` and `ClassicBookingIntegrationTests.cs` differ from the initial 22-file checkpoint. The repair uses `p.StandingBefore?.SceneCredibility ?? p.SceneCredibility` for quote validation, preserving booking/opening scene credibility after settlement while continuing to replay cancellation-adjusted reputation from each journal action. Before settlement and in unrated zero-attendance results, the existing current scene remains appropriate. Historical validation already uses the record's opening scene together with historical relationship/popularity replay and action reputation.

Independently reran the original bounded played probe against the repaired simulation assembly. The same 22-guest, three-star result still reconciles and now restores successfully. Evidence: `reviewer-stretch-repaired-probe.txt`; the original failing `reviewer-stretch-probe.txt` was preserved.

Independently executed with `--no-build --no-restore` the new `CompletedStretchContractKeepsOpeningFeeWhenEarnedSceneStandingImproves` regression and both parameter rows of `FourPlayedFestivalsReconcileActualTicketsBandFeesAndCashCarry`: **3 passed, 0 failed, 0 skipped, 53.744s**. The regression proves the actual paid 4800p stretch fee, earned scene improvement, completed-state hash round-trip and next-festival/history round-trip. The two four-festival cases also retain actual earned-reputation cancellation and cash/carry coverage. Output: `reviewer-repair-tests.txt`.

Source/assembly verification is in `reviewer-repair-verification.json`; final source and unchanged immutable Survival pins are in `reviewer-repair-after.json`. No independent product rebuild or second long Survival fixture run was performed for this narrow Classic-only validation repair; the earlier 53-case independent run and builder final immutable two-fixture run remain documented above.

The user has now approved the designer layout and separate UI implementation is in progress. This review neither evaluates that concurrent unpinned UI nor accepts native playtesting, accessibility, a copied Windows EXE or complete feature delivery.
