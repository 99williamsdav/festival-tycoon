# R0 — survival-campaign prototype

Status: **R0.00–R0.03 accepted within their stated prototype bounds. R0.03 human playtest and broader pacing remain pending. R0.04 and later briefs are not started.** [ROGUELIKE_DESIGN.md](../../ROGUELIKE_DESIGN.md) is the active product authority. Do not implement a brief until the user approves every **PROPOSED** choice it owns.

Scale gate: R0.00 and the [S0.00 — population architecture diagnostic](S0.00-scale-diagnostic.md) are accepted. Representative 200 completed functionally but missed arrival mean/tail targets; congested 200 timed out after valid timed windows. [S0.01 — crowd architecture spike](S0.01-crowd-architecture-spike.md) and [S0.02 — contention ownership spike](S0.02-contention-ownership-spike.md) stopped after faster or ownership-based movement trials failed bounded completion; their authoritative source changes were reverted. S0.06/S0.07 isolated traffic-kernel proofs are stopped and P3 is parked, not accepted. R0.01 uses existing small-crowd production movement within 50 active people. S0.08's full exported 42-person run attained 0.999× speed but recorded a rare unexplained 604 ms hitch and save pauses; strict stable 60 FPS and 200 rendered support are not established. The user accepted these hitches provisionally on 23 September 2026 to continue to R0.02, not as a fix or capacity claim. The approved early-tier weekend playtest target is about 10 real minutes including ordinary preparation and pauses; exact day lengths remain tunable and human pacing is unverified.

R0 proves the smallest persistent failure/retry/success campaign using the existing farm, deterministic simulation and 50-person maximum. It does not promise fun at R0.00 and does not recreate the old first-playable roadmap.

| Order | Brief | Reviewable result | Depends on | User decision gate |
|---:|---|---|---|---|
| 1 | [R0.00](R0.00-lifecycle-kernel.md) | Fixture death → hearing → spend/retry → fixture safe outcome/advance | Existing M0/M1.01 foundations | exact terminal/settlement transaction and save semantics |
| 2 | [R0.01](R0.01-preparation-persistence.md) | Persistent farm, meaningful offers, fixed roster, music expectations and guaranteed counters | R0.00 | tier counts/time, owned/rented/contact and failed-carryover rules |
| 3 | [R0.02](R0.02-equipment-chain.md) | Equipment warning can be prevented or become attributable death | R0.01 | exact equipment chain; asset/proxy approval |
| 3a | [R0.02a](R0.02a-live-performance.md) | Booked band plays; autonomous audience gathers, listens and reacts | R0.02 | band/audio asset approval, measured 50-person-bound crowd route |
| 4 | [R0.03](R0.03-medical-chain.md) | Needs/exposure warning can be prevented or become attributable death | R0.02a | exact medical chain; asset/proxy approval |
| 5 | [R0.04](R0.04-disorder-chain.md) | Pressure/argument can be de-escalated or become attributable death | R0.03 | exact disorder chain; asset/proxy approval |
| 6 | [R0.05](R0.05-hearing-retry.md) | Failure → hearing → Favour spend → viable same-tier retry | R0.04 | retry floor, first/community Favour, safe-closure cost |
| 7 | [R0.06](R0.06-success-tier-gate.md) | Safe Tier 1 → reward → fixed Tier 2; final integrated gate | R0.05 | reward floor, quality, scoring, victory/endless boundary |

## Shared execution rules

- Read the active design and targeted source/evidence named by the brief. `TECHNICAL_SPEC.md` still governs deterministic ticks, commands, save integrity, finance, causal history and measurement where compatible.
- Inspect current code and evidence; do not replace working boundaries merely to match suggested names. M1.01 stays **Implemented — verification pending** until repository acceptance changes.
- Implement only the assigned brief. Use clearly labelled fixtures only where permitted. No user-facing forced death, unavoidable eligible death, or invisible counter.
- New art starts at the beginning of its owning brief, one asset at a time, only after coordinator and user approve that exact design/proxy. A code-native readable proxy may be explicitly approved; it is not an automatic substitute. Sol is the preferred design model. Stop after each asset for both approvals before the next.
- Every authoritative field enters hashing and complete save/restore. Terminal death, settlement, reward, Favour spend and claim IDs are exactly-once operations across reload.
- Add focused deterministic, failure-path and save-boundary tests. Run the smallest relevant checks first. Broaden only in proportion to risk; reserve expensive rendered/full-suite gates for owning briefs.
- A review pass is required after implementation. Keep it token-aware: review changed state transitions, persistence, invariants and tests first; request broad repository review only when evidence shows cross-cutting risk.
- Use the chosen Sol medium builder and a fresh Astra light/low reviewer for each brief; retain the assigned builder/reviewer through that brief's repairs. After the first full review, re-review targeted changes. If repairs and reviews repeat without significant improvement, stop and escalate with evidence and options instead of continuing the loop.
- Record actual commands/evidence in a result file only after work occurs, update [PROGRESS.md](PROGRESS.md), demonstrate through the specified route, report limitations, then stop. Do not start the next brief, assets, commit or publish unless separately authorized.

## Old M1 disposition

| Old brief | Disposition in pivot |
|---|---|
| M1.00 shared-world feasibility | **Retained evidence/foundation.** Its strict-60-FPS failure and **Unverified** OS input latency remain visible. |
| M1.01 campaign weeks/finance | **Reworked.** Reuse identity, farm, finance, planning and save code; the eight-week annual product loop is not the new campaign authority. |
| M1.02 offers/demand/tickets | **Reworked** into bounded seed-stable offers and fixed mandatory tier rosters; no voluntary attendance sizing. |
| M1.03 programme/stage | **Reworked** in R0.01 into small meaningful act choices; R0.02a is planned to add one fixed live set, spatial listening and basic positional audio. Full timetable and lineup systems remain deferred. |
| M1.04 placement/egress | **Retained later** where incident routes/closures need it; no broad construction slice in R0. |
| M1.05 services/vendors/stock | **Reworked** into persistent/rented counters and owned stock; broad vendor economy deferred. |
| M1.06 attendees/admission/needs | **Reworked** into fixed roster arrivals/departures and survival/quality. |
| M1.07 stage listening | **Planned rework** in R0.02a as one autonomous audience and live-set slice. Full missed-act attribution, queue abandonment, multi-act scheduling and rich sound propagation remain deferred. |
| M1.08 weather/ground | **Deferred** beyond R0 unless one approved incident explicitly owns a minimal condition. |
| M1.09 staff/incidents | **Split/reworked** across R0.02–R0.04 with lethal causal fairness requirements. |
| M1.10 lifecycle/debrief | **Replaced** by R0.00, R0.05 and R0.06. |
| M1.11 integration gate | **Replaced** by R0.06; its truthful deterministic/save/performance discipline remains. |

## Next decision checklist

R0.01 start decisions are recorded in its brief; keep P3 parked and do not infer a capacity claim from the 50-active-person prototype boundary. R0.00's fixture outcome ordering is already accepted. The ten-minute early-tier playtest target is approved; exact day lengths are tunable through playtesting. Real Favour economy and rewards belong to R0.05/R0.06.
