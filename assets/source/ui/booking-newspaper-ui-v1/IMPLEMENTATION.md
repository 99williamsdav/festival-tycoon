# Single-stage booking and local-paper results

Design handoff, 27 September 2026. Implementation authorized by user via coordinator; no further art approval gate. These are UI specifications, not gameplay changes. Native text, Godot controls and flat fills; no new image assets required. Reference: supplied Clashfinder screenshot (one stage header, vertical time axis, rectangular act blocks). Current code inspected: game/Main.Programme.cs, Main.HudWorkspace.cs, src/Festival.Simulation/FestivalProgramme.cs. Current UI colours: ink #293b38, paper #fff3d3, divider #aa9f78, hover #d3e8df, selected #e5d5aa. Keep the existing serif title font and sans-serif UI body. Mosaic accents optional as an existing small header asset only; avoid mosaic behind data.

## Booking layout

Inside Programme, replace dropdown rows with two columns: **Available bands** on the left, **Main stage** schedule on the right. Stage name sits directly above its single vertical lane; time labels live in a narrow gutter to its left. Do not use three horizontal cards or three stage-like columns. Begin with all three slots empty in a new current-mode plan. Restore loaded plans, never clear them just to show empty design.

Preferred content width 900–1040 px in the existing preparation workspace; do not redesign the whole HUD for this feature. At 620 px content width, use 250 px list + 16 px gap + 354 px schedule (54 px gutter, 300 px lane). Native 14 px body, 16 px names, 22–25 px section heading; 12–13 px secondary labels, no smaller. Keep start/status footer visible. Available list scrolls independently; schedule and stage header stay visible. At less than 620 px, stack list above schedule and allow page scroll; click-to-place remains available. At 1280×720 avoid a second permanently open detail panel. Six bands need no search/filter controls.

Schedule time is **Elapsed festival time · mm:ss**. Current slots from authoritative timetable are 00:15–01:30, 01:55–03:10, 03:35–04:50. Never label these 15:00 etc or add day/night mechanics. Linear vertical coordinate: y = laneTop + elapsedSeconds × scale, set height = 75 × scale. At 1 px/second the lane is 300 px through 05:00, with 75 px blocks and 25 px changeover gaps. Show slot start/end labels, not dense second ticks. Gap labels: “Changeover · 25s”; muted, non-droppable. A tiny pre-set area 00:00–00:15 is also non-droppable. Position from current programme data, not duplicated magic timing constants.

Band list card: line 1 band name and right-aligned price; line 2 genre; line 3 Popularity n/100; line 4 Ego n/100 · Professionalism n/100. At wider width, line 3 can contain all three labelled ratings; never unlabeled stars for these because newspaper stars mean a different measure. Popularity currently uses /100. Ego/professionalism unit/values must use approved band-attribute source once delivered, not fabricated design data. An unavailable field is “Not recorded” in diagnostics/mockup only; do not ship fake ratings. Price uses FestivalCurrency formatter. Descriptions of ego/professionalism effects come from gameplay contract, not assumptions such as “low ego always good”.

Retain assigned bands visibly in the list with “Set 1”, “Set 2”, “Set 3” and a move affordance; don't make their attributes disappear. In schedule, show band name (up to two lines), genre and set interval; keep remove button at least 32×32 px desktop target with accessible “Remove [band] from Set n”. A slot selection may repeat all attributes in a compact single-line footer, not an always-open inspector.

## Interaction contract

All edits are atomic three-element plan commands, saved only after command/autosave success. Visual drag preview never changes authoritative state. Current preparation-plan mode is unpaid until Start; DO NOT copy obsolete “Book three acts / pay once now” wording from old screenshots. Footer: “2 of 3 sets filled · Lineup £95 · Paid at Start”; if complete: “3 of 3 sets filled · Lineup £180 · Paid at Start”. Start remains the existing global action, not an extra local purchase button. Retain legacy already-paid programme rules for legacy saves (order only, no refund/rebooking); make restrictions explicit.

| Event/state | Visual feedback | Committed result |
| --- | --- | --- |
| Empty slot | Cream lane, subtle dashed inset, “Set 1 · Drop a band here”; fixed interval visible | None |
| Drag begins | Small name/genre/price ghost; original card remains; all valid target slots gain subtle green fill | None |
| Valid empty hover | Clear 2 px focus border and “Place in Set 2” | On drop, assign once |
| External available band over occupied slot | Amber fill, “Replace [old band] with [new band]”; ghost within same rectangle | Replace atomically; old band returns available; cost recomputes |
| Assigned band to empty slot | “Move from Set 1 to Set 3” | Clear source and fill target atomically |
| Assigned band over another assigned band | “Swap Set 1 and Set 2” | Swap atomically; no duplicate act |
| Same-slot drop | “Already in Set 1” | No-op, no save or payment |
| Gap/outside/phase-locked/invalid payload | No-entry feedback and text reason; never green target | Reject, original plan intact |
| Cancel/Escape/drop outside | Ghost disappears, focus returns to source | No change |
| Save/validation failure | Inline durable reason, focus near affected slot | Roll back optimistic preview; preserve previous valid plan |

Click/keyboard alternative is essential: select a band, then activate a slot with click/Enter/Space; same validation and replacement/swap hints. Escape cancels selection. No forced drag precision. Focus ring distinct from hover. No hover-only essential price or rating. Disable edits once festival starts; show locked programme read-only. No automatic first-free assignment on merely selecting a band. Replacement needs no extra modal in unpaid plan mode because it is reversible; always preview the named replacement before release/activation. One status line announces final outcome, not every mouse movement.

Suggested Godot composition: HBoxContainer → list ScrollContainer/VBoxContainer + schedule VBoxContainer; schedule header + custom lane Control with child PanelContainers positioned from authoritative elapsed intervals. A thin dedicated drop target Control per slot routes payload act ID/source slot to one shared edit method. Use native drag APIs, typed/validated payload IDs, not node references that become stale on refresh. Avoid rebuilding all controls each frame or during active drag. Avoid input leaking through to world picking. Keyboard uses identical command path. Keep exact API details aligned with project's Godot version.

## Newspaper success result

Only after authoritative successful completion AND final guest departure. While guests depart, retain live world/HUD with “Festival finished · Guests leaving: n”; don't show results early, even if final set is finished. Death/casualty failure has precedence and routes to Council hearing, never this paper. A low satisfaction rating or financial loss is still a completed demo if no failure condition occurred.

Full-screen modal over dimmed scene, paper max width 1000 px with generous 28 px margins; centre at desktop, single vertical scroll if needed at 720 px height, sticky small action footer. Native serif masthead **The Lower Wittering Gazette** (proposed local name, cosmetic), kicker **LOCAL EDITION · FESTIVAL REVIEW**; no real date invented. Tier 1 stays implementation metadata or “Local edition”, not a fake national publication. Thin rules, ink text, warm cream paper, no decorative raster newspaper texture, no unrelated photograph requirement.

Order: masthead → large satisfaction headline → five-star row plus explicit “4 / 5 · Guest satisfaction 78%” (illustrative only) → short factual editorial sentence → two columns: “The crowd's verdict” and “Festival accounts” → full-width “Around the field” facts → “Demo complete” and final actions. Stars exclusively reflect the approved satisfaction metric; profit must never modify stars. Always state denominator/sample definition in small text (e.g. eligible guest count) from metric contract. Missing/invalid satisfaction does not silently become 1 star or zero percent; coordinate fallback with Builder. Do not invent star thresholds: use coordinator-approved thresholds.

Editorial tone: warm local-paper wit about performances/queues only, no fabricated quotes, crowds, sellouts or causality. Headline templates by approved rating: 5 “A day to remember”; 4 “Festival hits the right note”; 3 “A mixed reception in the field”; 2 “Festival leaves room for improvement”; 1 “A tough debut for the festival”. These are presentation strings, not extra outcomes. If this is not the first festival, avoid “debut”: use “A difficult day in the field”. Do not claim everyone loved the event from mean satisfaction.

Accounts box: **Festival profit +£n** or **Festival loss −£n**, then revenue/cost breakdown only if the same authoritative reporting metric supplies it. Cash balance is NOT profit. Don't use green stars or a cheerful profit headline to camouflage poor satisfaction, and don't brand a loss as a death/failure outcome. Net loss should be plain and legible, not a Council warning.

Facts must represent actual event metrics, not purchases or assumptions:

- “Pints consumed”: only if each beer serving is authoritatively one pint and completed consumed units are recorded. Otherwise use honest “Beers finished” (requires coordinator decision if label requirement is strict). Do not equate sales with consumption or full servings with partial consumption.
- “Fights”: distinct incident count, not involved people, punches or damage ticks.
- “Near-death collapses”: only if the medical metric explicitly marks near-death. Otherwise “Recovered collapses” with explanation, or omit until metric contract approved. Do not label every collapse near-death. Count incidents vs people consistently and label it. Safety facts are neutral, not a reward or joke.
- Zero is rendered only for a measured zero. Unavailable legacy metric shows “Not recorded”, not 0.

Primary terminal action **Return to menu**; optional **Play again** only if established demo-reset route exists and preserves saved-run policy. No “Next festival” or fake progression button. “Demo complete” stays visible; results view can't resume running simulation. Council hearing retains all its own choices. Snapshot/final report reopening on reload is Builder lifecycle responsibility.

## Decisions to resolve in implementation, not by invented art

1. Approved ego/professionalism numeric source and scale (not present in inspected FestivalAct record).
2. Exact satisfaction aggregation and 1–5 thresholds; neither inferred from profit nor mocked numbers.
3. Metric semantics and availability for pints, fights and specifically near-death collapses.
4. Success terminal predicate/departure denominator and failure precedence from simulation.

These are data/behaviour contract questions for coordinator/Builder, not requests for another aesthetic gate. Mockup fixture numbers are explicitly labelled and must not ship as defaults.

## Bounded visual QA

Capture new empty plan, one assigned, three assigned, replacement preview, swap, invalid changeover drop, keyboard placement, validation/save rollback, loaded plan, live-locked programme; 1280×720 and 1920×1080. Verify long band names and all five requested fields visible/readable. Check cost matches plan exactly and no drag changes cash. Newspaper: 1/3/5-star fixtures, negative profit, zero/missing metrics, final-departure gating, death bypass, reload/retry/menu lifecycle. Label fixtures vs actual completed gameplay. This designer handoff does not claim implemented or tested Godot UI.
