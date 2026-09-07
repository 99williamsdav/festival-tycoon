# Festival Tycoon — Technical Specification

Version 1.0 • Read after [GAME_DESIGN_SPEC.md](GAME_DESIGN_SPEC.md)

## 1. Technology decision

Use the stable Godot 4 .NET edition, C# for game code, and a plain .NET simulation library without Godot dependencies. Use Godot for 3D scenes, materials, audio, input and UI. Use a small console runner and ordinary .NET tests for simulation verification. This is a project design choice: the separation makes economic and behavioural rules testable without launching the graphical game.

At milestone M0, select and pin the exact stable engine version, its supported .NET SDK, export templates and test-package versions in the repository. Use the official compatibility requirements for that selected release; do not assume a version from these notes is the latest. Do not auto-upgrade engines mid-milestone. Record a reproducible Windows build command and toolchain manifest.

Godot's documentation currently states that Godot 4 C# projects cannot export to web. Therefore a browser edition is explicitly a separate future engineering decision. Keeping the simulation engine-independent helps reuse but does not make the Godot presentation portable to the browser. Desktop is the accepted design recommendation; browser support must not be advertised as ready.

Source checks, 6 September 2026:

- [Godot C# basics](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html): .NET editor/SDK requirements and platform limitations.
- [Godot web export](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_web.html): web export restrictions.
- [Godot command-line workflow](https://docs.godotengine.org/en/stable/tutorials/editor/command_line_tutorial.html): automated editor/export workflow.
- [Godot Windows export](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_windows.html): desktop export configuration.

Use Godot's Compatibility renderer initially with simple directional lighting, low-cost shadows and conventional materials. Profile the actual look and crowd benchmark before committing to heavier effects. The renderer choice is reversible because simulation and content must not depend on a rendering feature.

## 2. Repository and ownership boundaries

Suggested layout (create during implementation, not during design):

```text
src/
  Festival.Simulation/       pure state, commands, tick systems, data definitions
  Festival.Persistence/      save DTOs, validation, migrations, atomic storage
  Festival.Runner/           headless scenarios, replay and balance output
game/                       Godot project, presenters, UI, scenes, rendering
content/
  definitions/              facilities, acts, policies, incidents, unlocks
  scenarios/                farm layout, stress maps, deterministic fixtures
  tuning/                   costs, demand, needs, movement, weather
  localisation/             stable keys and English text
tests/
  Festival.Tests/           simulation and persistence contracts
  fixtures/                 versioned saves and scenario command logs
assets/
  source/                   editable mesh/texture/audio source
  runtime/                  imported game assets
  provenance/               author, licence and usage records
tools/                      build, content validation, screenshot harness
```

Dependency rule: simulation references no scene node, frame clock, UI control, file path, global random API or audio system. Persistence maps simulation snapshots to versioned records. Godot invokes commands and consumes read models/events. Headless runner invokes the exact same simulation, never a simplified duplicate.

Avoid a custom engine, generic scripting language, all-purpose ECS framework, microservices, runtime language model, or full event-sourcing infrastructure. Start with explicit typed records, indexed entity collections and ordered systems. Refactor storage only after a measured bottleneck.

## 3. Core contracts

Conceptual interfaces (names may change, responsibility may not):

```text
GameSession.Execute(CommandEnvelope) -> CommandResult
GameSession.AdvanceTicks(count) -> ReadSnapshot + events
GameSession.AdvancePlanningWeek() -> WeeklyResult
GameSession.CaptureSave() -> CampaignSnapshot
CampaignLoader.Load(snapshot, contentCatalog) -> validated GameSession
QueryService.GetInspector(entityId, visibility) -> InspectorModel
```

Each command has command ID, campaign ID, expected phase, target entity IDs, requested parameters and ordered submission sequence. Validate phase, entitlements, references, feature flag, capacity, funds and spatial constraints. Return accepted/rejected with a stable reason code and understandable explanation. Apply accepted state changes atomically; a failed placement must not consume cash or release the old reservation.

UI buttons use the same availability queries as command validation. Never rely on hiding a button for enforcement. Examples: BookAct, PlaceFacility, ConfirmHire, SetProgrammeSlot, ChangePricePreset, AdvanceWeek, ChangeDepartmentAllocation, CloseArea, RequestSupply, SetAlcoholPolicy, AcceptContract, PurchaseUnlock.

Commands accepted while paused execute at the current deterministic boundary in submission order; movement and service timers do not progress. A completed command is included in the next save even when no tick has elapsed. Record commands for debugging/replay; snapshots remain the primary save format.

## 4. Time and deterministic execution

Authoritative tick duration: 0.25 festival seconds. Four ticks represent one festival second. At 1x (20 festival seconds per real second), run 80 ticks per real second; 4x runs 320. Movement uses this fixed interval. This intentionally preserves service and travel behaviour across speed changes. Godot renders interpolated positions at its own frame rate.

Do not feed wall-clock delta to financial, movement or hazard calculations. If processing falls behind, reduce effective displayed simulation speed and show a subtle performance indicator; never skip gameplay ticks. Limit work per rendered frame to avoid freezing input, preserving accumulated simulation debt within a bounded speed fallback.

Use integer pennies for money; integer millimetres for authoritative X/Z positions and distances; integer ticks for durations. Terrain heights may be quantized. Need/trait values use 0–10,000 fixed-scale integers. Use stable entity iteration and explicit rounding. Cosmetic animation can use floats freely. Avoid claiming cross-version bit-identical replay; guarantee same build/content/settings replay on supported Windows.

Use a specified, versioned seeded PRNG with independently saved streams for demand, weather, forecast error, artist decisions, individual behaviour and incidents. Derive agent streams from stable campaign/entity IDs, not object hashes. Cosmetic randomness is separate. A weather query or UI tooltip never consumes gameplay randomness.

### Tick order

1. Apply ordered accepted commands; validate phase transitions.
2. Scheduled global transitions: set start/end, deliveries, weather changes.
3. Resolve scheduled high-level agent decisions (staggered by entity ID).
4. Resolve route requests and movement from prior occupancy; commit positions together.
5. Update occupancy, perception candidates and queue arrival reservations.
6. Progress service/jobs with resource/payment checks; complete transactions.
7. Update needs/exposure and scheduled social interactions.
8. Evaluate eligible hazards, incident escalation/resolution and dispatch.
9. Update metrics/objectives, emit reports and publish read snapshot.

Weather/terrain diffusion runs once per festival second, financial/resource events on transaction boundaries, ordinary needs/decisions every 2–5 festival seconds, social checks every 5–15 seconds, crowd safety every second. Critical triggers wake relevant decision immediately at the next tick. Stage attendance is recalculated from current membership each second, not every frame.

System events may schedule next-tick reactions, never recursively run an unbounded incident chain inside one update. Explicit per-system work queues spread route searches and ordinary reconsideration across ticks.

## 5. Domain model and relationships

All IDs are stable, serializable and independent of scene paths. Runtime references may be cached but never used as save identities.

| Entity / record | Required state |
|---|---|
| Campaign | ID, schema/ruleset version, seed streams, settings, year, prestige/ratings, Know-how, unlocks, history, save metadata |
| Edition | phase/week, selected date, schedule, permits, weather truth/forecast, current tick, tickets, objectives, budgets |
| Site / Parcel | bounds, road access, ownership/rental, terrain cells, receptor locations, plot reservations |
| Facility | definition ID, transform, footprint, doors, slots, state, stock references, staff allocations, condition/dirt, opening policy |
| Agent | ID/type, position, route/action, needs, traits, wallet, party/relationships, perceptions, personal memories, consumed-item exposure timers, stream state |
| Ticket | holder, product/day entitlement, paid amount, refund status, admission/re-entry state, upgrade references |
| Party / Vehicle | members, guardian links, transport booking, arrival/departure target, vehicle slot |
| Act | persistent identity, genres, skill/fame, traits, relationships, availability; annual booking and performer IDs separate |
| Performance | booking, stage, advertised/current start, duration, state, attendance/quality metrics, morale |
| Vendor | persistent identity/relationship; bid/contract, pitch, edition sales and costs |
| Department / Job | capacity/coverage, staff agents, assigned facilities, prioritised work, reserved resources and target |
| Queue / Slot | ordered members, arrival sequence, physical positions, admission criteria, owner reservations |
| Transaction | immutable ID, tick/week, debit/credit entries, payer/payee, reason, stock/contract references |
| Contract / Loan | terms, schedule, liabilities, exclusivities, payments, remaining term, breach state; includes off-season livestock leases and emergency staff coverage contracts |
| Incident | type, subjects, causal event IDs, location, observable state, severity, phase, response job, cooldown |
| Objective | trigger window, sample rule, accumulated metric, completed/claimed ID |
| Notification | template ID, variables, severity, source, action link, dismissed/seen state |

Definitions are immutable content records; runtime state references definition IDs. A facility's name can change without changing its identity. Artist booking and performer agent state must not be conflated: a performer trapped in a toilet affects the booked performance through availability.

Households and adult relationships are typed edges. Avoid all-pairs relationship matrices. Stored edges exist only for meaningful contacts, with bounded memory per agent. Children have guardian references and are filtered out before adult interaction candidate scoring.

## 6. Navigation and crowds

Use a game-owned 2D traversal grid over the terrain, with 0.5-metre cells and elevation/slope metadata. Initial full map 128 x 128 metres (256 x 256 cells); later land uses chunks. Fine-grained agent positions lie within cells. Godot colliders help selection/rendering, not authoritative guest physics. Avoid one physics rigid body per attendee.

Walkability derives from buildings, hedges, fences, closures and entitlement-specific gates. Eight-neighbour A* disallows diagonal corner cutting. A cost includes distance, terrain/mud, slope, crowding and agent preferences. Cache routes/corridors by target region and movement profile; invalidate affected chunks on layout/closure changes. Do not rebuild the entire route world for every footprint change.

Movement uses bounded local steering/separation over a spatial hash. A destination step respects impassable boundaries and legal gate crossing. Small body overlap at very high pressure is a rendered compression effect, not permission to cross fences or enter closed areas. Ground volume/occupancy caps prevent arbitrary stacking. Local preference for keeping left reduces opposing flow friction; pressure still grows in narrow openings.

For broad destinations such as stages, choose distributed listening slots/regions, not a single centre point. Stage exits remain traversable. Queues use ordered service reservations and generated nearby queue slots. Upon abandoning, closure, injury or departure, release reservation. Do not let a visual queue jump change service order.

Crowd risk is a fictional normalized model, not an engineering safety predictor. Measure local occupied fraction, average desired/actual movement ratio, opposing flow and sustained exposure. A brief busy cell does not cause an injury. Sustained red zones increase hazard, trigger alerts, slow newcomers and prompt self-preserving reroutes.

Detect failed progress over 10 festival seconds -> repath; over 30 -> mark route problem and alternative exit/destination; over 60 -> surface stuck-access alert and use controlled evacuation assistance if necessary. Do not teleport through walls during normal play. If the end-of-event resolver must recover an agent due to a confirmed navigation failure, log a developer error and explicit assisted exit with no extra sale/reward. Regression tests must fail on unexpected recovery in valid layouts.

First implement 50 guests accurately; then benchmark 200/500/1,200 agents. Shared flow fields are an optional measured optimization for heavily shared destinations, not an initial mandatory rewrite. Off-camera guests use identical logic; lower animation/mesh LOD only.

## 7. Ground, sound and exposure calculations

Terrain update uses previous-state buffers so iteration order cannot make rain flow differently. Per cell, add rainfall, subtract drainage/evaporation, apply traffic wear and hardening resistance, then derive mud severity. Cap normalized values. Aggregate footprints each second; avoid scanning every agent for every terrain cell.

Route cost uses a slowly refreshed crowd/mud snapshot with thresholds, reducing route oscillation. Patches change ground type only when a crew job completes. Closures reroute before work begins. Cleaning affects contamination/dirt, not rainfall itself.

Sound uses one sample per relevant guest/area per scheduled interval with range checks. Start with squared-distance falloff, directional stage factor and simple occlusion category; add richer terrain tests only when required. Store enjoyment interference separately from council receptor nuisance.

Cold risk integrates wetness, temperature/wind and shelter over time. Slips depend on ground, pace and balance traits. Health states are configurable fiction with generic recovery; do not embed real clinical assertions in content.

## 8. Financial and inventory integrity

Use a balanced internal ledger. Entries identify cash, inventory, loan principal, income, expenses, payables and receivables. All transfers sum to zero within the accounting scope; outside parties are explicit accounts. UI is simplified; internal integrity is not.

Example: a sale reduces the guest's cash/account and increases their recorded spending; the festival books a debit to its cash asset and a credit to sales revenue. Inventory consumption debits cost of goods and credits inventory value. A vendor sale belongs to the vendor's books; the festival receives only pitch/revenue-share transfers. An ATM withdrawal moves guest bank funds to guest cash and consumes machine notes; the machine owner's bank settlement balances the other side. Every participating account has an explicit owner and accounting sign convention.

Inventory quantities are integer units; costs retain integer pennies. Financial commits and corresponding item/reservation changes occur in one transaction. Reject insufficient cash/stock rather than clamping a negative balance after the fact. Rounding policy is specified once and tested (round half away from zero for presentation; monetary calculation uses integer rational arithmetic where possible).

Stable transaction IDs make replayed notifications, jobs and save resume idempotent. A completed transaction never happens again because its completion animation restarts. Reconcile annual opening cash + cash inflows - cash outflows = closing cash, separately from profit.

## 9. Behaviour, hazards and causal history

A behaviour definition lists eligible agent categories, prerequisites, utility contributions, duration, effects, cooldown and animation signal. The implementation supplies typed primitives; data selects values/combinations. Avoid user-authored executable expressions in data initially.

Use hazard rates per festival minute. For a hazard rate lambda and elapsed duration dt in minutes, event probability is `1 - exp(-lambda * dt)` (precomputed/quantized lookup where useful for deterministic builds). Do not roll a constant chance per rendered frame. Rates multiply only documented condition factors, with hard eligibility checks and caps.

An event such as KissObserved carries observer, participants, time/location and initiating event ID. Jealousy checks only eligible observed events. Fight escalation links to confrontation, which links to observation. Injury, treatment, complaint and reputation summaries reference this chain. The UI may condense it, but debug inspection must expose exact state and contribution weights.

Food-borne illness follows the same rule: a completed service transaction references vendor and item batch; a later seeded exposure check may create illness for that actual guest; toilet/medical actions and any complaint point back to the exposure. Heat exposure records weather interval, shelter/water history and relevant facility state. Do not use an unexplained global "illness chance" or a direct financial deduction.

Maintain bounded detailed recent history during live play and immutable aggregate edition results at settlement. Preserve a small number of notable story chains; discard unused tick-by-tick noise. Serialization never depends on unbounded event history to rebuild the world.

## 10. Presentation and UI integration

Godot presenters map entity IDs to reusable visual instances. Agent meshes share materials/rigs; batch static decoration and distant crowd visuals when measured. Selection uses an ID registry; UI does not infer identity from object names.

World-to-screen labels aggregate at distance. Selected agent labels take priority. Stage music, gestures and particles respond to events/state but cannot complete simulation actions. Opening the finance screen cannot alter random state or stop a department job unless the session is visibly paused.

Create reusable paper panel, metric chip, alert, table/filter, amount control, confirmation preview and tooltip components before duplicating screens. Use localization keys from day one. Display only player-visible read models, keeping debug exact traits behind a developer flag.

Programme edits use a draft validated against current bookings; commit command is atomic. Drag previews never reserve money or mutate the schedule. Building placement is similar. Show errors at the relevant footprint/block, with text for keyboard users.

## 11. Save format and recovery

Versioned compressed JSON snapshots are sufficient initially; no database needed. Header: schema version, build/content/ruleset hash, campaign ID, timestamp, phase/year, checksum and save purpose. Payload: complete authoritative state including PRNG states, next IDs, current tick, job progress, wallets, reservations and claimed objectives.

Serialize sorted ID collections. Save to a sibling temporary file, flush and validate, then atomically replace slot while preserving a prior known-good backup. On failure keep existing save and show an actionable error. Do not erase a campaign because a file is unreadable.

Migration pipeline transforms supported older schemas before runtime construction. A future unknown schema refuses to load with explanation. Ruleset mismatches require an explicit supported migration or matching bundled catalog; never silently swap costs underneath ongoing contracts. First implementation can support only v1, but the version/migration interface must exist.

Autosaves rotate three files per campaign. Manual named slots are separate. Live autosave interval: five real minutes at a safe boundary, plus phase boundaries. Player can save paused or mid-queue. Validate that all references resolve, queues have no duplicate members and money/stock constraints hold after loading.

## 12. Content and feature configuration

Content records require schema version, stable ID, localized name/description, prerequisite IDs, phase availability and tuning values. Load catalogs once, validate all foreign references, unlock graph cycles, negative capacities, missing presentation keys and contradictory exclusivities. Invalid content stops at a clear load report, not an exception halfway through an event.

Feature flags initially include camping, multipleStages, socialRomance, advancedWeather, livestockIncidents, sponsors, cardPayments, overselling, relocation and each emergency intervention. Flags belong to scenario/ruleset definitions. Disabled feature means no offered unlock/objective/contract requiring it. Mandatory safety exits/water/treatment remain available in all scenarios.

Configuration changes during development apply to a new scenario or documented migrated save. Do not permit arbitrary hot changes to capacities, prices or tick timing mid-session. UI-only preferences can change immediately.

## 13. Performance and operational targets

Initial benchmark hardware target: Windows x64, six-core mainstream desktop CPU, 16 GB RAM, GTX 1660-class discrete graphics at 1080p. This is a development target, not a verified minimum requirement. Record actual test hardware and release requirements after profiling.

- 50-agent first playable: stable 60 FPS target at 1x, no input stalls during save or reports.
- 1,200 total agents benchmark: at least 30 FPS at 1x; 4x must sustain useful acceleration without skipping authoritative ticks. Measure actual speed attained.
- Simulation CPU target at the ceiling: under 8 ms per rendered frame on average at 1x; report 95th/99th percentiles separately.
- Planning commands ordinarily complete within 100 ms; weekly resolution under two seconds for normal campaigns; larger jobs show progress and remain deterministic.
- Campaign save target under two seconds, load under five seconds on the benchmark SSD; peak working memory under 2 GB initial target.

Track tick cost, decision count, path searches/cache hits, blocked-route age, agent/queue/job counts, draw calls, allocation rate and actual simulation speed. Optimize measured bottlenecks. No promise of 800 attendees is validated until the M0/M3 benchmarks pass.

## 14. Verification contract

Required automated invariants: no negative stock/cash without explicit debt; conserved wallet transfers; no over-admission; no duplicated occupancy/queue/service reservations; all ticket holders and jobs resolve; paused state does not progress; rendering/speed changes do not change final simulation checksum; saved/resumed run matches uninterrupted run for the same build.

Required integration scenarios: hungry guest purchases; insufficient cash then ATM then purchase; card debit bounded by bank balance; rain/wear detour; accessible exit after closure; actual clash reduces one crowd; abandoned queue releases slot; severe incident creates reachable response job; inappropriate social candidate rejected before scoring; objective award idempotent; settlement ledger reconciles.

Run seed sweeps to inspect distributions and impossible states, not to assert one arbitrary festival must have exactly three fights. Use curated forced-condition fixtures for rare incidents. Visual QA uses repeatable camera/seed snapshots at all four rotations, rain/night, crowded queues and scaled UI. Automated checks cannot certify humour, atmosphere or whether decisions feel fair; those require play sessions.

## 15. Architectural changes that require a written decision

Changes to money units, attendee-to-ticket ratio, tick duration, deterministic ordering, save identity, graph ownership, world-to-render dependency, or supported platforms must update the spec and fixtures before broad implementation. Routine UI and tuning iteration does not require repeated user approval. Do not migrate the game to a different engine merely because an isolated prototype is easier there.
