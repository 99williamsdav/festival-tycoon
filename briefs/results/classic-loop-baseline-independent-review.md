# Independent pre-Classic baseline and plan review

Verdict: PASS. No blocking findings in the baseline capture, immutable comparison tests, or approved bounded plan. This approves the pre-production baseline gate only; it does not approve Classic implementation or an EXE.

Reviewed 14 October 2026 against HEAD `6fed0e2504f9166ad212755d75827d2fae3a928c`, following `docs/WORKING-AGREEMENTS.md`. Reviewer task: `/root/classic_baseline_review`, separate from the implementation writer. Requested spawn metadata supplied with the assignment: `model=gpt-6-astra`, `reasoning_effort=medium`, `fork_turns=none`. These are requested settings, not runtime model attestation.

## Verified evidence

- Checkpoint SHA-256 is `09C1031E21DA6E77F390D462B0D829702C91E5B482C29F9EC47117967E7F3B73`. All 141 source entries, 10 evidence entries and five original-reference/test entries match their pinned hashes before and after the independent run. Every non-generated Simulation/Persistence C# file is pinned, together with the seven named critical UI files.
- HEAD matches the checkpoint. No tracked production C# changes exist. The pre-existing package lock and character imports, outfit assets, UID files, diagnostic saves and review temporary directories were left untouched. This review wrote only this report.
- Compared both new test sources directly with their accepted originals. Fixture seeds, setup, commands, advance counts, injected hazard state and observation ordering are equivalent. Differences are class/method names, evidence paths, recording guards, inclusion of all serialized snapshot fields, and removal of historical restore-failure exceptions. The now-unused old retry-gap argument does not bypass any assertion.
- References contain 21 main plus 17 extra state boundaries and 25 incident boundaries: 63 total. All 38 main/extra observations include Identity and AuthoritativeHash field digests and the full serialized-state digest. Incident digests hash the entire serialized snapshot, including those fields. No fields are removed. Every boundary requires successful same-version restore and an equal authoritative hash.
- Companion event arrays preserve order across 38 named accumulated `AdvanceWithoutSnapshot` return lists; incident observations preserve the accepted fixture event lists, including the natural medical-death tick. Persisted incident evidence is also inside full-state digests. This is exact coverage of those fixtures, not exhaustive coverage of every possible command or seed.
- Recorder flags were absent throughout. No recorder flag was set and no baseline was rewritten. Existing-file guards protect the primary references. Both save-limitation manifests are empty.
- Original pinned logs show the accepted original pair passing (2m 37s 745ms) and fresh recording pair passing (3m 20s 382ms). Independently executed the requested comparison without a build:

  `dotnet test tests/Festival.Tests/Festival.Tests.csproj --no-build -- --filter 'FullyQualifiedName~ClassicSurvivalParityTests|FullyQualifiedName~ClassicSurvivalIncidentParityTests'`

  Result: exit 0, passed 2, failed 0, skipped 0, duration **2m 36s 395ms**. The existing test assembly postdates both recorder sources. No rebuild was performed.

## Plan review and implementation checks

The plan matches the supplied approval: management-first genuine Classic, repeated Trailer/farm festivals, canonical carry, no new funding or fines, actual nonterminal causal deaths, minus 10 reputation per unique death with floor zero, fixed mild/dry weather, 50% disorder pressure buildup and doubled specified hazard windows. Excluded progression, art, placement, weather forecast and monitoring work remains excluded. Survival controls, tiers, finances, events, RNG and hashes remain protected by the existing and fresh references.

The following are concrete implementation risks already within the plan, not baseline defects:

1. Keep festival ordinal separate from Trailer definition tier in history replay, seeds, carry and validation. `Preparation` currently requires Tier 1 exactly when no carry exists; `CarryIssue` only admits Tier 1 into Tier 2; relationship validation groups gigs by tier and checks that each tier precedes the current one; previous-layout validation also infers the prior tier. Repeated Trailer festivals require explicit Classic facts and reconciliation, not looser Survival validation.
2. Carry actual closing cash and outstanding principal, then credit the next festival's advance tickets once. Existing `OpeningCashFor`, planning ledger, loan validation and relationship replay must agree on the repeated-festival transition. Test save/resume at closure and the next preparation to catch duplicate transition income or history.
3. Existing lifecycle validation couples one casualty to one failed attempt and hearing. Existing medical Fatal and equipment Terminal flags assume a stopped edition. Classic must keep living response and hazard processing active, suppress duplicate deaths by person identity, and remove deceased people from claims/queues/work without releasing unrelated staff. Validate multiple causes and multiple victims before and after save/restore.
4. The medical validator still derives hot weather from Survival tier, and the fallback medical death description says fixed Hot conditions. Classic setup, validators and causal copy must agree on mild/dry, non-hot behavior. Apply the 50% multiplier to pressure buildup, preserving the existing recovery and incident thresholds unless separately approved.
5. New Classic persistence facts must not alter serialized Survival snapshots or canonical hashes, including by introducing serialized null/default fields. Continue the exact tests after schema and policy slices without repinning. Keep Survival validators strict and add malformed-Classic-save cases rather than globally relaxing checks.

Acceptance still requires the planned four starts/three paid continuations, current-version restores, casualty cleanup and completion tests, natural-seed samples, separate native save/host flows and fresh independent source/EXE reviews. This baseline review makes no playability, native export, manual-QA or performance claim.
