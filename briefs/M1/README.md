# M1 — first playable planning index

Status: **Approved implementation plan — M1.00 measured; hard gate failed and M1.01 is blocked.** These briefs narrow `IMPLEMENTATION_PLAN.md` §4; they do not replace the product, technical or balance specifications. Use the shared execution/evidence rules in [`briefs/README.md`](../README.md).

## Foundation choice and carried-forward rules

**Approved 22 September 2026:** the [bounded M1 crowd exception](CROWD_DECISION.md) permits M1.00 to proceed with 20–50 full-fidelity people only if the early one-world 50-person feasibility gate meets the existing targets or returns to the user with measured options. It does not repair M0, lower future targets or approve a new crowd architecture.

The following are carried-forward specification rules rather than new approval questions: one person/ticket/wallet and identical off-camera logic; the 80-authoritative-ticks/real-second 1× clock; eight manual planning weeks; the existing £800 starter-loan worked example and inherited farm; and provisional reuse of approved art under [ASSET_GAPS.md](ASSET_GAPS.md). A separate settlement-insufficiency choice is intentionally deferred until before M1.10, where it first owns behaviour: simple terminal receivership with the final report/save preserved, or explicit extra scope for one fixed rescue contract. No earlier brief may invent credit/bailouts or block opening merely because of a financial warning.

## Sequence and dependencies

| Order | Brief | Stakeholder-visible outcome | Depends on |
|---:|---|---|---|
| 1 | [M1.00](M1.00-shared-world-feasibility.md) | One real 50-person shared-world crowd proof, or a hard-stop profiling report | Accepted M0.10 |
| 2 | [M1.01](M1.01-campaign-weeks-finance.md) | Create a named festival and advance eight planning weeks with real cash/loan state | M1.00 pass |
| 3 | [M1.02](M1.02-offers-demand-tickets.md) | Pick date/price/act offer and see seeded weekly ticket sales versus a named rival | M1.01 |
| 4 | [M1.03](M1.03-programme-stage.md) | Book from 5–7 candidate acts and build a valid one-stage timetable | M1.02 |
| 5 | [M1.04](M1.04-placement-egress.md) | Place/move festival facilities while preserving usable entrances and egress | M1.01, M1.00 |
| 6 | [M1.05](M1.05-services-vendors-stock.md) | Operate paid/free services, a food vendor and stock with correct ownership | M1.04, M1.02 |
| 7 | [M1.06](M1.06-attendees-admission-needs.md) | Sold adults arrive on their own, choose needs/actions, spend and leave | M1.05, M1.03 |
| 8 | [M1.07](M1.07-stage-listening.md) | Guests physically watch wanted acts or miss them for attributable reasons | M1.06, M1.03 |
| 9 | [M1.08](M1.08-weather-ground.md) | Saved forecasts become rain/wear/mud; woodchip visibly improves a route | M1.06, M1.04 |
| 10 | [M1.09](M1.09-staff-response-incidents.md) | Visible staffing resolves bounded, causally generated operational incidents | M1.05–M1.08 |
| 11 | [M1.10](M1.10-lifecycle-debrief-presentation.md) | Opening → live → egress → debrief → second edition, with usable HUD/tips/presentation | M1.01–M1.09; insolvency decision |
| 12 | [M1.11](M1.11-final-integration-gate.md) | A reviewable 20/50-person multi-seed first playable and honest performance/save gate | All prior M1 briefs |

Do not start a dependent brief until its prerequisite result is accepted. UI grows with each feature: each brief exposes its playable state through the normal HUD/screens, so M1.11 is integration rather than a debug-only UI retrofit.

## `IMPLEMENTATION_PLAN.md` §4 acceptance ownership

| Acceptance item | Primary owner | Integration proof |
|---|---|---|
| Complete 20–50 event funded/operated/settled without debug commands | M1.01, M1.02, M1.10 | M1.11 |
| Worked ledger reconciles; normal runs use actual transactions | M1.01, M1.05 | M1.11 |
| Poor vendor/bar position changes reachable footfall and sales physically | M1.04, M1.05 | M1.11 |
| Rain + traffic worsens route; woodchip improves speed/cleanliness | M1.08 | M1.11 |
| Guests miss actual acts and cite correct act/reason | M1.07 | M1.11 |
| Serious incident auto-pauses once and supports a valid response | M1.09 | M1.11 |
| Placement preserves emergency egress; no fence crossing/stuck recovery in valid fixture | M1.04 | M1.11 |
| Mid-queue/mid-service save resumes without duplication/loss | M1.05, M1.06 | M1.11 |
| Debrief names three material drivers; second edition retains cash/debt/land | M1.10 | M1.11 |
| Contextual tips are optional, dismissible and persist after load | M1.10 | M1.11 |

## M1 exit

M1 is accepted only after M1.11 passes functional, deterministic, save/load, rendered and measured performance checks at both 20 and 50 people across multiple declared seeds. No brief may call 50 a universal capacity: every added behaviour renews the shared-world measurement obligation. M1 acceptance does not repair the original 1,200 M0 failure or alter later scale targets.
