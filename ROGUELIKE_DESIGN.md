# Festival Tycoon — roguelike campaign pivot

Status: **Active product authority; implementation not yet authorized.** Approved product decisions below replace conflicting campaign rules in `GAME_DESIGN_SPEC.md`, `CONTENT_AND_BALANCE.md` and `IMPLEMENTATION_PLAN.md`. `TECHNICAL_SPEC.md` remains authoritative where it does not conflict. Values and rules marked **PROPOSED** require user approval before their owning R0 brief starts.

## The game now

Festival Tycoon is a short-attempt survival-management campaign played on one persistent inherited farm. Each edition has a fixed mandatory audience roster and runs across a festival weekend. Keep every guest, staff member and performer alive through it to advance. A safe but scruffy festival advances with modest resources; a delightful festival earns more. The final tier's safe completion wins the campaign. The exact compressed day lengths are not approved.

The first death ends that edition immediately. A council hearing may spend one scarce collectible **Council Favour** to retain the licence and retry the same tier. With none available, the campaign ends. Favours come from explicit community/council choices with visible opportunity costs, never cash-purchased bribes.

The farm persists through good editions and failed ones. Casualty history retains the person's identity, role and attributable cause; future zombie returns may use it, but zombies and other absurd endless events are not R0 scope. Short, legible attempts and replay are the main appeal. Do not build the old annual tycoon beside this campaign, a sandbox, or a generic mode framework now.

## Authority and fixed decisions

| Topic | Approved contract |
|---|---|
| Progress | Complete every fixed tier in order. A poor-but-safe edition advances; quality changes rewards, not victory eligibility. |
| Audience | Each tier admits its entire mandatory attendee roster. The player cannot downsize, refuse the crowd, sell fewer tickets or close the gate to manufacture a win. |
| Survival | Guests, staff and performers are protected people. The first death among them ends the edition. |
| Expectations | Needs and music expectations scale with tier. Weak music and unmet needs create visible, causal risks; there is no separate lineup/service promise checklist for victory. |
| Failure | A death is recorded once, operation stops, and the council hearing is resolved once. Spending one Favour retries the same tier; no Favour means campaign loss. |
| Favour | Earned only through explicit authored choices with visible opportunity costs. It is not bought for cash and cannot be repeatedly farmed. |
| Safety fairness | Every lethal chain has observable evidence and warning, an understandable cause, and at least one available intervention before death. Its basic counter is available before the hazard can become eligible. Knowledge and money improve outcomes; unavoidable first deaths and required grinding are forbidden. |
| Persistence | The same farm and campaign history continue across successes and failures. The exact cash/stock/contract settlement on a failed edition is still proposed below. |
| Finish | Safely complete the final tier to win. Optional endless continuation starts only after victory, banks the victory/rewards first, and later adds escalating absurdity days. |
| Scope | No 100-death spectacle, zombies, camping, families, sponsors, rivals or annual economy in R0. Traditional tycoon/sandbox may be reconsidered later, not built in parallel. |

## Proposed campaign and edition lifecycle

The authoritative campaign owns the farm, tier, money, owned objects, stock, contacts, Favour balance, one-time choice claims, attempts, rewards, casualty history and victory state. An edition owns its roster, offers/contracts, rentals, schedule, incident chains and live people.

The following ordering is a recommended engineering/design contract, not yet user-approved. At every transition, validation and save should complete before visible state advances. The proposed death path is:

1. A causal chain emits one terminal `DeathOccurred` fact for one named person and role.
2. The edition atomically freezes: no later service, sale, hazard or second death can resolve at that tick or after it.
3. One immutable casualty record and one failed-attempt record are appended. Failure costs are settled by the approved policy; no success reward is created.
4. One hearing is created and autosaved. The latest autosave returns to that hearing. Older manual saves remain loadable unless a different save policy is approved; repeated terminal processing from any loaded state must still be internally exactly once.
5. `Spend Favour` debits exactly one and creates exactly one same-tier retry; it cannot be repeated after reload. `Concede` or a zero balance ends the campaign.

The exact success/endless boundary and whether every protected person must physically arrive/depart are proposals. No implementation may teleport, double-admit or silently despawn a protected person.

Safe early closure/evacuation is not a win and never advances the tier. Its retry and cost treatment are **PROPOSED**, not approved.

## R0 vertical slice

R0 proves one complete campaign slice, not content breadth:

- a persistent farm and two fixed tiers within the measured 50-person boundary;
- deterministic, seed-stable offers for acts, staff, equipment and contracts, with bounded choices and guaranteed access to every essential counter;
- a complete failure → hearing → Favour spend → same-tier retry;
- a complete safe edition → reward → larger-tier transition;
- equipment, medical and disorder chains, each with trigger, evidence, warning, intervention, escalation and attributable death;
- save/reload safety at live warnings, the terminal death boundary, hearing, retry creation, success settlement and tier transition.

The smallest first build is a fixture-only full outcome loop in R0.00: explicit death, hearing, fixture-Favour spend, same-tier retry, then explicit safe outcome and tier advance. It is not playable fun and gives no authority to real economy/reward rules. The integrated user route must not expose debug forced-death/success controls; counters must actually interrupt normal eligible chains.

### Incident contracts

These are bounded design targets; exact rates and timings remain data tuning.

| Chain | Trigger and evidence | Warning and intervention | Escalation and terminal attribution |
|---|---|---|---|
| Equipment | A loaded temporary power unit exceeds its safe configured load; load, condition and nearby occupancy are visible. | Flicker/alarm and a named overload alert; shed load, isolate the unit or dispatch maintenance. A baseline isolator and maintenance response exist before eligibility. | Degraded unit → dangerous equipment fault → named nearby guest/worker/performer death if unresolved. The record cites load, condition, warnings and missed/late response. |
| Medical | A person accumulates a plainly shown exposure from unmet water/rest needs and conditions. | Need trend, distress thought and medical alert; use free water/rest, dispatch first aid or safely remove the person. Water and basic medical coverage are baseline. | Distress → collapse → critical untreated state → death. Use generic fictional language, not clinical claims or graphic detail. |
| Disorder | Local frustration and crowd pressure combine around identifiable people; an argument is visible. | Escalation meter/alert; reduce the pressure, close the relevant service/area, dispatch security and then medical support if needed. Baseline security and medical responses exist first. | Argument → fight → serious generic injury → death only if the chain remains unresolved. The record cites participants, pressure, response and treatment delay. |

No chain rolls directly from calm to death. Seed sweeps must show that correct timely intervention prevents each terminal outcome.

## Existing work to reuse, not overclaim

- Reuse the approved farm, four-view camera, low-poly attendee and environment assets, paper UI direction, build/export tooling and provenance.
- Reuse `GameSession` command ordering, deterministic 0.25-festival-second ticks, saved PRNG/state hashing, atomic rotating saves, integer finance/ledger, traversal, occupancy, physical queue reservations and transaction ownership.
- Rework the M1.01 planning/campaign data rather than discarding it: campaign identity, farm seed, £800 loan example, eight-week shell and finance/save code exist at `0abd629`. Repository status remains **Implemented — verification pending** even though an independent review was accepted in conversation.
- Reuse the M1.00 shared-world fixture only as evidence for the current ceiling. At 50 people it measured about 61.45 mean FPS, but p50/p95/p99 exceeded the strict 16.667 ms budget; attained speed was about 0.99×/3.95× and OS input latency remained **Unverified**. The written exception remains in force. Do not add simulation LOD/population multipliers, call 50 a general capacity, or omit proportional remeasurement as behaviours grow and at the final gate.
- Old numeric balance is historical input, not silent approval for the new campaign economy.

## Decisions required before their owning brief

Recommended choices are deliberately proposals.

| Decision | Recommended proposal | Why / validation |
|---|---|---|
| R0 tiers | **APPROVED:** Tier 1 = 20 mandatory attendees; Tier 2 = 40. Guests + on-site staff + performers must total no more than 50 active people in R0. | Creates a real size step without exceeding the only measured shared-world population. Re-profile the actual total with all new behaviour. A 25 → 50 guest plan would exceed current evidence once workforce is added and needs separate approval. |
| Weekend pace | **APPROVED TARGET:** An early-tier Friday–Sunday weekend should take about 10 real minutes in playtesting, including normal preparation and pauses; tune after observation. The exact festival-minute day lengths remain **PROPOSED** and must preserve the existing 4 ticks/festival-second and 20 festival-seconds/real-second unless separately changed. | The prior 90/120/90-minute suggestion would consume 15 real minutes of uninterrupted live play before preparation and pauses, so it does not meet the approved target. Measure complete attempts before fixing the schedule. |
| Persistent property | **APPROVED:** purchased equipment and permanent works persist; rentals expire after every edition outcome. Staff contacts persist as known people/options, while each work contract expires. | Makes the farm legible without turning contracts into permanent free labour. |
| Failed-edition carryover | **APPROVED FOR R0.01:** preserve current cash, owned property and remaining nonperishable stock after applying actual consumed stock and committed costs; no revenue/reward after the death tick. The retry keeps the tier seed and revealed offers. | Honest consequences and no reroll farming. Economy-sweep the concrete prices and costs during R0.01 review. |
| Retry floor | **PROPOSED:** recalculate a non-cash council safety loan package on every authorized Favour-backed retry, containing the cheapest essential counters still missing. Loaned items expire after that retry and cannot be sold, stockpiled or converted to rewards. | Every authorized retry remains viable, including a second retry, without death farming or invented cash. Test poor states and deliberate early-death abuse. |
| Initial Favour | **PROPOSED:** start with one Favour. One prototype community request may award one more once per campaign: reserve a premium staff-contact offer for council/community duty, leaving a visibly weaker or dearer choice for that edition; award only after the commitment is honoured. | Explicit opportunity cost, stable claim ID, no cash bribe or reload/repeat exploit. Exact contact effect needs approval. |
| Safe closure | **PROPOSED:** a warned evacuation freezes safely, gives no reward/advance and returns to same-tier preparation without a hearing or Favour cost; actual consumed/committed costs remain, rentals expire, and the same offer seed is retained. | Makes safety preferable to gambling while preserving a cost. This rule is pending; do not implement from implication. |
| Safe reward floor | **PROPOSED:** settlement first reserves the cheapest valid essential-counter package for the next tier from current owned items/stock, granting only the shortfall; quality then adds cash/choice rewards. | Guarantees a poor safe win can attempt the next tier without making quality irrelevant. Sweep the worst safe Tier 1 result. |
| Quality | **PROPOSED:** experience comes from needs met, music attended/enjoyed, waits and preventable warning duration. Survival is a gate, not most of the quality score. | Keeps weak music/unmet needs consequential without a promise checklist. |
| Score | **PROPOSED:** primary result is attempts to first final-tier victory; secondary results are cumulative experience, unused Favours and endless days. No global permanent stat boosts initially; only small option discoveries, if later approved. | Supports replay without pressuring unsafe play. |
| Saving | No hardcore restriction is approved. **PROPOSED:** ordinary manual/autosaves remain; terminal outcomes and rewards are transactionally idempotent rather than protected by deleting saves. | Avoids inventing a contentious save rule. |
| Final/endless | **PROPOSED:** reaching the final scheduled safe closing boundary records victory and rewards before a depart-or-continue branch. If departure remains simulated, it adds no new uncounted lethal risk; endless adds one day at a time and cannot revoke the win. Absurd incidents require separate future approval and telegraphing. | Reconciles banked victory with continuing the same event; exact closing/departure semantics need approval. |

## Deferred explicitly

Full camping/families, 100-person crowds, zombies, casualty return, cow stampedes, presidential visits, sponsors, rivals, yearly prestige economy, more tiers, traditional sandbox/tycoon, permanent global boosts, hardcore saves and a generic multi-mode architecture are not authorized. Endless eligibility fields may be reserved only when required for save compatibility; no absurd event is implemented in R0.

## Next gate

[R0.00](briefs/R0/R0.00-lifecycle-kernel.md), [R0.01](briefs/R0/R0.01-preparation-persistence.md) and the [S0.00 scale diagnostic](briefs/R0/S0.00-scale-diagnostic.md) are accepted within their recorded bounds. [S0.01](briefs/R0/S0.01-crowd-architecture-spike.md) and [S0.02](briefs/R0/S0.02-contention-ownership-spike.md) stopped after movement trials failed bounded completion; authoritative source changes were reverted. The isolated S0.06/S0.07 traffic-kernel proof stopped on crossing liveness; P3 is parked, not accepted. R0.01 uses existing small-crowd movement within the 50-active-person prototype boundary; a 42-person attempt completed at 0.916× speed with substantial movement-frame stalls, not a capacity or stable-60-FPS pass. The approved early-tier playtest target is about 10 real minutes including ordinary preparation and pauses; human pacing remains unverified and exact festival-minute day lengths remain tunable. Tier counts and failed-edition carryover are approved for R0.01. Real Favour/reward, safe closure, community-request and scoring proposals remain pending until their owning briefs.
