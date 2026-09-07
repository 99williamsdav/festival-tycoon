# Festival Tycoon — Developer Handoff and Acceptance Plan

Version 1.0 • 6 September 2026

## 1. Starting brief for the developer / coding agent

Build a Windows desktop festival-management simulation using [GAME_DESIGN_SPEC.md](GAME_DESIGN_SPEC.md), [TECHNICAL_SPEC.md](TECHNICAL_SPEC.md) and [CONTENT_AND_BALANCE.md](CONTENT_AND_BALANCE.md). The user is creative director and QA; the implementing agent supplies engineering, art, design implementation and tests. The design handoff authorizes a coherent proposed architecture; this document does not mean implementation has already begun.

Begin with M0. Prove the simulation, money, save, rendering and crowd foundations before implementing the full catalogue. Ship each milestone as a reproducible runnable build with its test results, known limits and short playtest instructions. Keep new tuning separate from rule changes. Do not silently replace the design because a different prototype is easier.

Implementation decisions that do not change user experience can be made autonomously. If testing disproves a numerical assumption, adjust the data and record why. If it disproves a pillar (such as 800 individually simulated guests or the selected art style), document measured alternatives before changing it.

## 2. Design decisions versus empirical questions

The design is sufficiently resolved to begin. Exact cost balance, hazard frequency, view clarity, comfortable event length and supported hardware require prototypes/playtests. Treat these as validation tasks with starting values, not open product questions blocking work.

| Risk / assumption | Test that resolves it | Earliest gate |
|---|---|---|
| Godot + C# workflow is reproducible | Clean build/export and headless scenario on Windows | M0 |
| Fixed ticks at accelerated festival time are affordable | 50/200/500/1,200-agent movement benchmark, measured speed | M0 then M3 |
| Low-poly guests remain readable | Four camera orientations, action silhouettes, rain/night | M0/M1 |
| One-day 50-person event makes economic sense | Ledger fixture, automated demand/economy sweeps | M1 |
| Desired paths emerge and produce controllable mud | Identical layouts/seeds with protected/unprotected routes | M1 |
| Untimed weekly planning feels responsive | One full preparation cycle, incremental booking reaction | M2 |
| Faction identity produces viable choices | Independent specialist vs commercial broad route sweeps | M4/M5 |
| Social stories emerge without bespoke chains | Primitive fixtures plus unscripted varied seeds | M4 |
| Know-how offers choices without blocking essentials | Unlock reachability and 15-edition simulation | M3/M5 |
| Persistent HUD/paper UI remain legible | Scaled UI, busy field, keyboard timetable and saved settings | Every visual gate |

## 3. M0 — Foundation and technical proof

Deliver a minimal project with pinned toolchain, deterministic simulation runner, versioned content loader, Windows export, camera, selection and save/reload. Represent a small farm with primitive meshes matching the planned proportions and material palette. This is an engineering/visual proof, not a complete festival.

Implement IDs, command validation, integer money/time, separate PRNG streams, tick loop, static grid navigation, simple multi-agent separation, a single service queue, terrain-cell state and basic transaction log. Render 50 agents walking between a gate and a service point. Provide a benchmark map with multiple destinations and narrow/wide passage variants.

Required checks:

- Build from documented commands with no editor-only hidden setup.
- Same seed + commands -> same checksum; save halfway -> same final checksum after resume.
- Pausing prevents needs, movement, weather and service progress; camera still works.
- 1x versus 4x yields identical results for commands scheduled at identical simulation ticks.
- Wallet-to-business transfer conserves money; queue reservation survives save/load.
- Camera rotates in 90-degree increments; selection tracks entity identity across all four views.
- Measure 50/200/500/1,200-agent performance; note bottlenecks and proposed capacity ceiling. No claim of pass without recorded hardware/results.

Exit: reproducible foundation with no Godot references in the simulation library, valid save header, no duplicated reservations, and evidence that the intended scale is plausible. Fix core failures before layering content.

## 4. M1 — First playable one-day festival

Deliver an end-to-end playable event: name/palette, inherited farm, opening loan, one stage, 5–7 selectable local acts, default ticket price overrides, basic placement, one independent food van, bar, toilets, water, medical/security/cleaning, small merch point, stock, pause/speed, persistent HUD and simple debrief.

Planning uses the final phase structure with eight week advances, initially simple fixed offers and demand. Booking data and timetable support an array of stages but expose one. Music acts animate simply and use licensed placeholder ambience. One day only; no camping, families, complex romance, full skill tree, sponsorship or relocation yet.

Agent scope: adults with genre affinity, spending funds/cash, hunger/thirst/bladder, mood, dirt and basic impatience/aggression. Walk, queue, buy, watch, leave. Simple frustration -> argument -> security-response chain proves primitives. Rain/wear creates mud and the woodchip countermeasure. First-aid response uses generic injury. Full backstage/social dependencies wait.

Playable incidents: stockout, toilet breakdown, missed act, mud delay and simple argument/brawl. Configured weather forecasts use saved truth and errors. Other incidents are not enabled or advertised yet. One simplified named competitor affects demand/date choice.

Acceptance:

- A complete event with 20–50 attendees can be funded, operated and settled without debug commands.
- Worked ledger in CONTENT_AND_BALANCE.md reconciles exactly in a deterministic fixture; normal runs use actual guest transactions.
- Moving a vendor/bar to a poor position changes reachable footfall and sales; do not implement an arbitrary 'bad location' coin flip.
- Heavy traffic plus rain creates a visibly worse route; woodchip produces a measurable speed/cleanliness improvement.
- Guests miss actual acts when queued/delayed and cite the correct act/reason.
- Auto-pause alert appears once for a serious incident and supports a valid response.
- Placement cannot obstruct emergency egress; no customers walk through fences; no valid-layout fixture invokes stuck recovery.
- Save mid-queue/mid-service and resume without extra transactions or lost guests.
- Debrief identifies three material drivers of result and can continue to a reset second edition retaining cash/debt/land (even before richer campaign features).
- No forced tutorial. Contextual tips dismiss and remain dismissed after load.

Exit: an enjoyable small event whose money, movement, needs, weather and programme connect. Visual polish can be modest; the farm, people and decisions must already be readable.

## 5. M2 — Planning and annual management

Expand weekly sales, audience segments, ticket promises/refunds, marketing, act search/filtering, artist persistence, vendor bids/renewals, financial forecasts, contracts, annual reputation and table. Add second-stage experiment and clash detection early in this milestone, retaining the one-stage campaign start.

Add cancellations, reserve acts, schedule edits, power capacity/faults, noise/curfew, vendor complaints, food-safety/illness clusters, heatwaves with heat-exposure/sunburn response, loans/rescue/receivership and full settlement/carryover. Add a small initial Know-how branch set: second stage, ATM, guest reports and dispatch. Only exposed nodes can be purchased.

M2 rescue uses one fixed, fully visible contract template with the required revenue-share obligation. M4 expands this existing contract mechanism into the general sponsorship market; do not build two separate obligation systems.

Acceptance:

- Additional appropriate act booked after weak sales produces a plausible incremental sales response with diminishing returns.
- Artist availability/quotes do not change merely because a filter or tooltip is used.
- Same-stage illegal overlap is rejected; cross-stage genre clash is allowed and affects actual crowd allocation.
- Unsold products remain distinct from denied admissions; refund releases inventory once.
- A cancelled advertised act creates feedback/refund risk with causal attribution.
- Vendor gross sales never appear as festival income; stock carries over correctly.
- Debt and optional rescue terms are shown before acceptance; insolvency ends at a coherent settlement.
- At least three consecutive editions work with persisted artists/vendors, renamed assets and fresh attendee identities.
- Weekly advance/save/resume never rerolls forecast or duplicates spending/rewards.

Exit: a meaningful three-edition management game with clear revenue and reputation feedback.

## 6. M3 — Weekends, land and operational breadth

Add camping/two-day passes, sleep, night, showers, family parties/guardians, quiet/family zones, vehicle reservations, glamping/caravans, late-hours licences, extra staffing coverage including costly mid-festival contractors, barn conversions, neighbouring fields, wet-ground improvements, livestock and card payments. Activate relevant Know-how branches.

Add off-season livestock leases as a farm-income decision, with parcel-bound grass/manure effects and eligible permanent-asset damage/repair. The player must be able to choose between leaving fields fallow, low-impact grazing and higher-income/riskier leases.

Weekend pass includes camping and once-per-person daily entry rights. Implement gate re-entry, parking party ownership, separate backstage guest/production entitlements, and stock/cleaning across days. Add diva reactions, cow breaches, lost child/reunion and exposure conditions. Basic optional cosmetics and generated stage/bar/campsite names become available.

Acceptance:

- Weekend guest retains one wallet/identity across days; no extra ticket charge or duplicated campsite reservation.
- Day guest without second-day entitlement cannot enter; sleeping campers remain counted in licence occupancy.
- Shared vehicle books/charges once; departed vehicle slot becomes available.
- Cash/ATM/card flows reconcile; ATM cash depletion and card affordability matter.
- Children never enter adult attraction/consumption candidate sets; reunions resolve guardian links.
- Demand for showers increases under relevant mud/camping conditions; there is a valid baseline first-aid alternative for exposure.
- Counterflow bottleneck increases journey delay; widening the passage or rerouting reduces it under the same seed.
- Eight stage destinations and 1,200 total agents pass recorded performance thresholds or a documented scope adjustment is made before endgame content.

Exit: a weekend festival with tangible site growth and manageable larger crowds.

## 7. M4 — Identity, partnerships and emergent stories

Add all scene credibility/vibe consequences, sponsorship and promoter affiliation, advanced marketing, artist fee discounts/waivers, nuanced traits, adult social relationships, backstage door faults, drugs/alcohol distress, storm safety and fictional corruption. Existing typed primitives should compose into the discovery examples.

Use curated fixtures to make rare prerequisites occur; do not add a hardcoded 'jealous lover storyline' action. For normal play, varied seeds and crowd conditions determine what happens. Add causal incident inspection and newspaper summaries driven by actual event facts.

Acceptance:

- Jealousy fixture: observe consenting-adult kiss -> confrontation -> possible fight; remove observation or aggression and chain changes appropriately.
- Backstage fixture: door jam affects an actual occupant's availability and therefore an act; repair restores availability.
- Social behaviour cannot query hidden remote actions without perception/message data.
- A well-run event materially reduces serious incident frequency across a seed set; no mandatory drama quota overrides this.
- Fixed-price beer partnership locks only affected products; exclusivity conflicts are rejected; payouts/penalties happen once.
- Music identity and execution matter: specialist and mainstream fixtures can both gain prestige; genre label alone does not assign misconduct.
- Persistent audience statistics replace old attendee instances after settlement, while artist/vendor relationships remain.

Exit: the distinctive festival identity and emergent comedy are observable and explainable.

## 8. M5 — Complete campaign, polish and release candidate

Add relocation, full 25-node tree, rival annual progression, 800-person licence tier, main-stage spectacle, mature three-day operation, endgame prestige victory, long-term contracts, optional risky overselling and final cosmetics/content variety. Polish sound, four-view assets, accessibility, paper interfaces and failure/recovery messages.

Campaign validation: automated 15-edition strategies and manual sessions for independent specialist, broad commercial, family/comfort, and party/nightlife routes. No route should need identical unlock ordering or automatic sponsor acceptance. A skilful player should plausibly reach the prestige leader in 8–15 editions; tune rival baseline and edition-score ramp to meet that target.

Release gates:

- Multiple complete campaigns with no unreconciled ledger, broken save migration, impossible prerequisite or unresolvable phase.
- Appropriate deterministic, integration, save, content and performance suites pass.
- Final HUD/timetable usable at supported resolutions/text scales and with alternate input controls.
- Every asset/audio track has provenance; generated names checked for obvious unintended real-entity matches.
- Stated system requirements measured on target hardware; installer/portable package launches offline on a clean test machine.
- No debug-only placeholders advertised as finished features. Player help matches actual rules.

## 9. QA scenario suite

These are reusable test cases, not tests that merely repeat formulas. Quantitative inequalities are compared under the same seed/controlled actions.

| ID | Scenario | Required observation |
|---|---|---|
| Q01 | Buy last drink with exact cash | One sale, zero remaining stock/cash, next guest cannot buy |
| Q02 | Buy with insufficient cash, sufficient account | Seeks ATM; funds transfer; purchase becomes possible |
| Q03 | Card with insufficient total funds | Fails without negative wallet or invented credit |
| Q04 | Close a queued facility | Reservations released, guests reconsider, no service payment |
| Q05 | Save during service completion boundary | Resume matches uninterrupted wallet/stock/queue |
| Q06 | Rain on busy route vs protected route | More mud/dirt/slowness in unprotected version |
| Q07 | Narrow opposite crowd flows vs wide opening | Higher sustained pressure/delay in narrow version |
| Q08 | Similar popular acts simultaneous vs staggered | Changed crowd allocation, no duplicate audience membership |
| Q09 | Observe adult romance vs out of sight | Observation-dependent jealousy only |
| Q10 | No medical staff available for injury | Backlog warning, no magical immediate treatment |
| Q11 | Sold-out gate plus oversold tickets | On-site never exceeds limit; waiting/refund consequences |
| Q12 | Weekend + glamping + parking | One attendee admission, one valid upgrade, party vehicle charging |
| Q13 | Refund/cancelled act weekly reload | Same outcome, one refund, no money duplication |
| Q14 | Zero service samples on wait objective | Objective does not pass |
| Q15 | Disabled action/feature | Hidden/unavailable in UI and rejected by command path |
| Q16 | Corrupt/truncated save | Last good backup retained, actionable error |
| Q17 | Rotate/render/inspect repeatedly | Simulation checksum unchanged at identical tick |
| Q18 | Equivalent commands at 1x and 4x | Same final authoritative state |
| Q19 | End festival with guest stranded by valid closure | Safe alternative/response exists; no silent despawn |
| Q20 | End year with remaining stock and loan | Profit, cash and principal reconcile independently |
| Q21 | Unfunded booking / impossible placement | Rejected atomically without partial payment/change |
| Q22 | Unlock/save/reload/claim repeated | Node/award acquired once, correct balance |
| Q23 | Sponsor annual renewal | Obligations and money applied once, correct remaining term |
| Q24 | Tiny token scene booking | Cannot farm maximum credibility in every scene |

## 10. Change discipline and handoff format

For each milestone, provide: build/run instructions; short description of playable behaviour; exact supported scope; automated results; recorded benchmark hardware/performance; screenshots from four orientations; known problems; and a five-minute QA route. Keep a changelog of design adjustments with measured rationale.

Keep committed user decisions intact unless the user changes them. Maintain a balance revision log for altered numerical defaults. Do not use the historical discovery notes as a task list or silently re-enable deferred features. A quality gate can justify iteration without adding unrelated scope.

The next concrete developer task is M0: pinned Godot/.NET scaffold, pure simulation contracts, the 50-agent queue/farm scene, atomic save round-trip and a recorded crowd benchmark. No further product clarification is required to begin that work when implementation is requested.
