# Classic booking journal independent review

15 October 2026. **PASS for the pinned two-file pure pre-opening journal checkpoint only.** No blocking correctness findings within this scope.

Reviewed `src/Festival.Simulation/ClassicBookingLedger.cs` and `tests/Festival.Tests/ClassicBookingLedgerTests.cs` against base `546e132dcc6d2bd25dce99cdc475720a68e957b5`, the user-approved terms in `briefs/classic-booking-ticket-phases.md`, the prior model review and the factual implementation note. Both SHA-256 values in `reports/evidence/classic-tickets/ledger/review-checkpoint.json` matched independently before and after review. Product source and tests were not edited.

## Findings

- Confirmation resolves sales once using the supplied campaign seed and festival ordinal, records ticket receipts and actual paid band fees, and locks the announced price. There is no mutable cash balance, spending cap or invented borrowing source in this journal.
- Rebooking compares against the original announcement, retains the highest severity, refunds only the additional quantity required and creates no additional ticket sales. Released contracts return their recorded paid fees; newly added contracts charge their supplied fees; a retained identity cannot alter its existing quote, genre or popularity.
- Full cancellation releases current contracts and refunds remaining holders. Together with earlier rebooking movements, this reverses the attempt's ticket and band cash exactly. It applies the approved five-point reputation loss with a zero floor and advances the booking attempt without changing the caller-supplied festival key. It cannot be repeated without another confirmation.
- Opening adds only a phase transition. It does not create another band charge. Illegal post-opening mutations and price edits after confirmation are rejected. Every generated money fact has offsetting cash and revenue/expense entries; long arithmetic covers the validated quantities and fees.
- JSON round-trip replay and explicit clone isolation are exercised. The public snapshot/action arrays remain caller-mutable; callers must use the clone boundary and validated replay rather than treat a record declaration as deep immutability. This is an integration responsibility, not a demonstrated defect in the scoped functional journal.
- The implementation note accurately describes the tested scope and its exclusions. Eligibility and normal current fees are supplied externally: the optional evaluation callback is a hook, not proof of catalogue, licensing or discount validation. The future adapter must validate before committing cash and preserve historical paid facts during replay.

## Independent verification

Executed:

`dotnet test tests/Festival.Tests/Festival.Tests.csproj --no-build --no-restore -- --filter 'FullyQualifiedName~ClassicBookingLedgerTests|FullyQualifiedName~ClassicTicketDemandTests|FullyQualifiedName~ClassicBookingTermsTests'`

**18 passed, 0 failed, 0 skipped; 488 ms.** These are seven journal cases (including two parameter rows) and eleven previously reviewed model/terms cases. Coverage includes negative booking cash and cancellation recovery, partial refunds followed by full cancellation, repeated rebooking and restored lineups, no extra receipts, free entry, zero sales, locked prices/quotes, invalid phases, callback rejection and JSON/clone behavior. Static inspection additionally checked state transitions and money arithmetic.

This was an independent run of the supplied binaries with no rebuild. Executed assembly hashes are saved in `reports/evidence/classic-tickets/ledger/reviewer-assemblies.json`; test output is in `reviewer-tests.txt`, and source verification is in `reviewer-hashes-before.json` and `reviewer-hashes-after.json` in that directory.

## Acceptance boundary

This approves only the pure journal. Session cash application/idempotency, catalogue and discount validation, guest identity/rosters, actual save-host continuity, weather preservation by the caller, accounts and booking UI, licensed setup, three-plus played festival carry, immutable Survival parity and native/export playtests remain outside this review and require their own integration evidence. No new EXE or complete feature delivery is approved. Existing import, lockfile, outfit, UID and Survival-reference files were not modified by this review.
