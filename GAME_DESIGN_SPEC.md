# Festival Tycoon — Complete Game Design Specification

Version 1.0 • 6 September 2026 • Working title, not final branding

## 0. Authority and reading order

This document consolidates the discovery discussion into an implementable full-game design. User decisions are preserved; previously unanswered questions are resolved using the creative discretion granted on 6 September. Numeric values are initial tuning values, not claims of playtested balance or real festival economics.

Read this document, then [TECHNICAL_SPEC.md](TECHNICAL_SPEC.md), [CONTENT_AND_BALANCE.md](CONTENT_AND_BALANCE.md), and [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md). Together these are the developer handoff. The original [DESIGN_NOTES.md](DESIGN_NOTES.md) remains a historical record. In conflicts, this specification supersedes those notes. Implementation-plan milestone boundaries determine when a feature ships; its presence in this full-game document does not put it in the first build.

### Decisions resolved in this specification

| Question / previous tension | Decision |
|---|---|
| Untimed planning vs weekly ticket sales | Manual Advance Week button. Unlimited editing time between advances; eight planning weeks. |
| Hundreds of agents vs headline mega-festival scale | One attendee always equals one person, one ticket and one wallet. Entire world uses compressed capacities, with a 50-person opening and approximately 800-person endgame. No invisible paying population. |
| One-day opening vs initial camping MVP | First playable is one day; camping follows in M3 after the annual management milestone. |
| One stage vs artist clash story | First playable supports a configurable list of stages, initially one; second-stage prototype follows before large-scale content. |
| Licence hard maximum vs overselling | Sales may exceed a displayed admission limit only through an explicit later risky policy; gates still enforce maximum concurrent occupancy. Rejected/waiting ticket holders cost refunds and goodwill. |
| Show must go on vs danger | No elective event cancellation/postponement. Individual sets, facilities, or areas can close; an enforced evacuation can end the event. |
| Cash pressure vs mandatory opening | Purchases cannot create unauthorized negative cash. Limited, costly emergency credit; insolvency resolved at settlement. |
| Freeform wishes vs developer build scope | Small vertical slice, then campaign systems, then emergent social depth and endgame. |
| Graphics | Low-poly 3D, orthographic angled camera, four orientations. Lush British countryside; silly grown-up behaviour. |
| Interface | Tactile paperwork with persistent HUD; legibility takes precedence over paper decoration. |
| Setting | Fictional British county, bands, brands, councils, media and rival festivals. Real names from discussion are inspiration only. |
| Platform | Windows desktop first. Godot .NET with a separate C# simulation. Browser is a future port, not a promised export. |
| Progression reward | Spendable Know-how earned from meaningful annual objectives; branching unlock tree. Purchased unlocks permit cash-funded facilities and options. |

## 1. Game identity

You inherit a farm and have no interest in farming. A tractor trailer, a field and an ill-advised loan become the beginnings of a music festival. Across successive summers, turn this into the most prestigious festival in the country while surviving weather, unreliable artists, money shortages and the decisions of the people you invited.

The central tension is ambition versus solvency, complicated by independence and scene credibility. A beer exclusivity deal may finance a covered stage. It also alienates some regular audience segments, changes bar prices and wins over an artist who values comfort more than authenticity. Neither corporate nor independent routes are prescribed as morally correct or mechanically dominant.

The intended appeal combines RollerCoaster Tycoon's observable service economy, Theme Hospital's humour, and the individual-driven incidents of Prison Architect and RimWorld. The administrative depth is presented through concise reports and inbox decisions. No playable manager character, farming loop, or dense personnel administration.

### Design pillars

1. Decisions become visible on the field: a bad pitch, clash, staffing cut or rain forecast has observable consequences.
2. Individuals make stories: incidents arise from needs, traits, relationships and physical proximity.
3. A festival has an identity: different scenes and guest expectations create viable distinct campaigns.
4. Success has a price: borrowing, booking, sponsorship and expanding demand trade-offs.
5. Countryside first, chaos second: a beautiful sunny field makes its later deterioration and absurdities more effective.
6. Explain the consequences: every major complaint and incident is traceable to contributing causes.

## 2. Product, audience and boundaries

Single-player, offline, mouse and keyboard, Windows x64. Multiple campaigns with manual saves and autosaves. No login, backend, live AI generation, subscriptions, multiplayer, premium currency or microtransactions. Design for a conventional paid game; storefront and price are later publishing decisions.

Adults are the intended audience. Alcohol, drugs, consensual adult flirting, infidelity, toilet mishaps, corruption and comic brawls can appear. Injuries are generic, with no blood, dismemberment or simulated deaths. Medical treatment or leaving the event resolves severe harm. Children are distinct dependants excluded from alcohol, drugs and sexual/social attraction systems. Wardrobe mishaps use an obscuring visual gag; family reactions are embarrassment, never sexual content involving minors. Content toggles can disable sexual jokes, drug references and profanity without breaking incident systems.

The game need not deliver an environmental or political sermon. Cleaning, field restoration, neighbour goodwill and licensing are practical management pressures with comic writing.

### Experience targets

- First festival: 20–50 guests, 5–7 acts, one afternoon/evening, roughly 20–35 minutes live at default speed including pauses.
- Normal annual cycle: 15–30 minutes preparation, 20–45 minutes live operation, 5 minutes debrief. These are playtest targets.
- Endgame: approximately 8–15 well-played festivals, then indefinite continuation.
- Mature site: up to eight stages, approximately 800 concurrent attendees plus staff/artists, and three event days. Performance test ceiling: 1,200 total active agents.
- Every displayed guest participates in the simulation even off-camera. Zoom never changes revenue or identity fidelity.

## 3. Campaign and annual state machine

Campaign creation generates a farm/festival name, colour palette and site seed. All venue names have an obvious rename action; no logo editor. The player can change names without losing history. Genre follows programming and marketing, rather than an irreversible creation choice.

Annual states:

`Debrief / Off-season -> Planning W8 ... W1 -> Opening check -> Live -> Egress -> Settlement -> Debrief`

### Off-season

Review results, repay debt, spend Know-how, restore land, renew partners and vendors, rent adjoining fields, or relocate if unlocked. The farm itself is inherited; only expansion land costs rent. Existing permanent works remain. Temporary hire and staffing contracts expire. Artist and vendor relationships persist, attendee identities do not. Attendee history becomes aggregate cohort feedback plus notable incident summaries.

The off-season can also rent unused fields to livestock operators. Each offer states income, duration, animal type, expected ground wear, manure/cleanup burden, and risk to nearby permanent structures. Livestock income helps a cash-poor festival, but compacts/wears the grass and can damage unfenced or poorly maintained permanent assets. The player can decline, select a lower-impact tenant, restrict them to a field, or leave land fallow to recover. This is a financial trade-off, not a separate farming game.

### Preparation

Eight manual weeks precede the date. While a week is open, time is stopped: place/move structures, set budgets, browse acts, plan a running order, select contracts, inspect forecasts and read mail without pressure. Advance Week resolves outstanding offers, contracted spending, marketing exposure and ticket sales; updates forecasts; then presents a concise weekly digest.

Advance Week always previews known outgoing cash, changes that will commit, and outstanding scheduling problems. Purchases/accepted contracts take effect according to their explicit terms. Unaccepted quotes are not binding. Save/load does not reroll the same week's outcomes.

Choose one of four calendar windows each year. Bank holidays improve availability of leisure time and demand, but increase competition and fees. Weather depends on seasonal distributions, not the holiday label. Rival overlap modifies available acts, audience and advertising cost. MVP competitors are named data records, not full off-screen simulations.

Sales start once a date, price and at least one confirmed act exist. Guests remember advertised commitments. Removing a sold headliner can increase refund requests and reduce trust. At the final week advance, the game opens with the commitments achieved; it does not silently fill missing acts or services.

### Opening check

Show financial exposure, missing services, uncovered stock demand, crowd pinch points and the forecast. Warnings permit a bad event to proceed. Structural impossibilities require a narrow correction: at least one usable entrance/exit, a legal boundary and no unreachable occupied area. Allow correction of those blockers even at the deadline, with a clearly priced emergency contractor if necessary. A gate/exit in the inherited boundary is supplied from the start.

Essential water and basic first-aid provision are available from year one. Failing to buy optional comfort/security upgrades is allowed; critical life-support services must not be locked behind Know-how.

### Live and egress

Time controls: pause, 1x, 2x, 4x. At 1x, one real second advances 20 festival seconds. An eight-hour programme lasts 24 real minutes without pauses. Movement and service times use festival seconds; tune site distances for this scale. Simulation ticks remain fixed at every speed.

Planning buildings cannot move once doors open. Operation commands, repairs and emergency deliveries remain available. At curfew, acts finish or overrun according to policy; guests leave, campers settle if another day remains. Egress continues until everyone leaves or sleeps appropriately. End-of-event departures have a bounded recovery rule for stuck agents (see technical specification), not silent disappearance.

### Settlement, failure and continuation

Report actual income, expenses, profit, cash movement, loan principal, stock carryover, reputation changes and field repair costs separately. Loan principal is not an operating expense. Stock purchases consume cash; sold stock consumes inventory cost. Unused nonperishables can carry over; food waste does not become imaginary income.

Pay liabilities from cash, then approved credit. A one-time rescue offer may cover a modest shortfall in exchange for a multiyear commercial commitment and loss of independence. Declining or exceeding rescue capacity ends the campaign in receivership. Keep the save and final report; offer load earlier save or new campaign. No endless automatic loans.

There is no voluntary cancel/postpone button. Dangerous areas can close and an authority can end an unsafe event. This is a failure outcome, with financial consequences; player safety responses remain possible throughout.

## 4. Prestige, identity and long-term success

All rating calculations store 0–100 values; small HUD labels communicate trends and explanations. Money/debt is separate. No single universal coolness meter substitutes for audience identities.

| Rating | Meaning | Main effects |
|---|---|---|
| Public reputation | Trust that the event delivers what it sells | Conversion, refunds, repeat aggregate demand |
| Independence | Autonomy from commercial control | Scene/artist reactions, deal availability; not automatically goodness |
| Scene credibility, one per scene | Respect earned within indie, punk, metal, folk/hippie, hip-hop, electronic/rave, pop, classic rock | Scene demand, artist fees, specialist acclaim |
| Licensing standing | Council/authority trust | Attendance tiers, hours, inspection intensity, bonds |
| Environmental standing | Field care, litter, sanitation and stewardship reputation | Restoration costs, neighbours, relevant cohorts/partners |
| Prestige | Overall national standing | Table position, larger opportunities, soft victory |

Festival vibe is a vector of family-friendly, bohemian, rowdy, luxury, mainstream and alternative traits. Traits can coexist; guest compatibility determines friction. Family and late-night party appeal can both work if spatial separation, quiet camping and schedules support it. A metal crowd is not intrinsically unsafe: genre shifts composition weakly; actual behaviour depends on individuals, conditions and policy.

After each festival, compute an edition score: 30% experience, 25% artistic impact, 20% scene acclaim, 15% operational delivery, 10% reach. Each component is 0–100; scene acclaim uses the top two meaningfully served scenes, allowing a specialist to compete. Artistic impact considers performance quality, discovery and artist calibre rather than fee alone. Reach uses a saturating logarithm of attended tickets across days and never allows raw scale to dominate.

Prestige moves 30% toward edition score each year; early performance receives a minimum meaningful movement. Severe reportable incidents impose a bounded additional penalty based on affected share and outcome. Avoid counting the same incident at full weight again in every component. Public reputation moves 35% toward experience/trust results; scene changes follow actual music, audience feedback and credible/inauthentic deals.

Show a fictional prestige table led initially by **Midsummer Common**, the established legendary rural festival. Rivals have stable identities and bounded seeded annual score changes. Surpassing the leader after settlement triggers a commemorative front page and a soft victory; keep playing. All major viable identities must be capable of this result.

## 5. Tickets, demand and advertising

### Products and capacity

- Day pass for a specified day; weekend pass includes ordinary camping and all event days.
- Glamping and caravan pitches are reserved upgrades attached to a valid pass, not extra attendees.
- Family tickets bundle explicit adult and child admissions. Family-only camping is a zone policy, not a demographic ban on the whole event.
- VIP upgrade includes guest-access backstage hospitality, not unrestricted production/security access.
- Parking is a vehicle booking associated with a party; one car is not charged once per occupant.

A ticket has one attendee identity for its valid dates. No double counting weekend admission, re-entry or upgrades. Total sold, today's eligible tickets, arrived, on-site and waiting are distinct UI figures. Licensing limits people concurrently present, and capacities of campsites/vehicles/upgrades use their own units.

For this fictional game's permit model, the displayed licence is guest capacity: every adult and child counts, including sleeping campers and VIPs. Staff, performers and vendors use a separate authorised workforce roster and still contribute to physical crowd occupancy. This abstraction preserves a 50-ticket opening without pretending its crew takes no physical space.

Default sales cap equals licensed concurrent capacity per eligible day, reserving all weekend places on every covered day. A later oversell policy raises sales ceiling up to 110%, with a warning and explicit choice. Gates never automatically admit above the licence. Queue, denied admission and refunds make this risky even if some guests fail to arrive. The first playable omits overselling.

### Prices and audience demand

Default ticket price derives from event duration, stage/act offer and prestige, within a local purchasing-power ceiling. Show Bargain / Fair / Premium / Steep presets (0.8 / 1.0 / 1.2 / 1.4 times default). Bar/merch screens also offer unit-price increments. Default is selected automatically. Partnership-fixed prices visibly lock the relevant control.

Maintain a finite prospect pool per audience segment each year. A segment combines age/life stage, party purpose, geography, visit format and genre preferences. Marketing does not create infinite people. For each planning week:

1. Resolve bookings and commitments; generate truthful advertised offer.
2. Add unique exposure using reach and saturation curves, subject to remaining segment population.
3. Estimate attraction from artist preference, compatibility, reputation and practical travel suitability.
4. Reduce purchase propensity for price, competing events, weak promises and forecast discomfort.
5. Draw unique sales from aware nonbuyers, capped by ticket/product availability; instantiate buyers and spending budgets.
6. Process valid refund requests and release inventory. Refunded guests remain tracked to prevent immediate repeated conversion exploits.

MVP uses bounded segment scores and seeded draws (formula and seed values in the balance document). Booking several similar acts has diminishing ticket-sales benefit; it can still improve the programme for existing fans. UI shows estimated incremental draw as a range, not guaranteed sales.

### Channels

Posters and flyers are cheap/local with saturation; local radio has broader reach; social ads permit age/interest/personality tendency and geography targeting. Influencers unlock broad but identity-sensitive exposure. Bot farms create apparent interest and a short-lived social-proof boost but few direct buyers and discovery risk. The dashboard distinguishes impressions, interested people and actual sales.

Targeting operates on audience tendencies, not knowledge of individual hidden traits. National/foreign marketing is available when transport, duration and draw make it plausible. No national TV/radio channel. Repeat expenditure within the same week has one aggregate diminishing-return calculation to prevent click splitting exploits.

## 6. Money, borrowing and contracts

All commercial numbers are fictional game-scale pounds. Fifty people must economically support a tiny event: compress the entire cost structure consistently. Do not apply real headline fees to toy attendance. See the worked first-year ledger in CONTENT_AND_BALANCE.md.

The finance screen distinguishes spendable cash, committed future outgoings, forecast surplus range and debt service. Accepted obligations reserve available funds or approved credit. Speculative ticket revenue cannot count as cash for purchase validation.

Starter loan: one fixed repayment schedule over several editions, interest per edition, early repayment permitted without exploit. Further credit depends on assets, repayment history and stable revenue; a displayed ceiling prevents snowball borrowing. No real-time interest accumulates while the player thinks in a planning week.

Acts in MVP charge fixed fees at confirmation. Later booking negotiation uses three offers around a quote, immediate seeded response, and cooldown before re-offer. Hire, wages and services are generally prepaid per event; emergency responses are paid when authorized. Later deposit contracts explicitly schedule remaining payments before opening and reserve funds.

Festival owns bar/merch revenue and stock. Independent vendors own their food takings, pay agreed pitch fees and optionally a revenue share. Never credit the festival with vendor gross sales. Vendor sales still appear in their performance report. Refund compensation is a distinct payment.

Artist-specific merchandise adds a royalty contract later; generic festival merchandise has no artist royalty. Insurance is an optional prepaid policy with explicit covered incidents, excess, limits and exclusions; it cannot refund costs of deliberate policy breaches. Claims settle after the event using event facts, not rerolled outcomes.

## 7. Artist market and scheduling

Artists persist across years with stable name, genre vector, appeal, fame, performance skill, reliability, ego, independence preference, crowd expectations and relationship to the festival. Hidden values have broad public descriptors; unlocked insights tighten confidence. Random annual growth/decline is seeded and bounded. No rider management in version 1.

Offer price is derived from base fee, relevant credibility, relationship, availability/competition, commercial fit and the gap between artist fame and festival prestige. Being above the player's usual tier is expensive, not an arbitrary booking lock. Specialist authenticity can attract a prestigious niche act early if affordable.

At legendary prestige, compatible major acts can occasionally waive their performance fee. Production/hospitality costs remain; prestige appearances have a cooldown and annual cap so the entire line-up never becomes free. No named real artist appears.

Search filters: available date, genre, price band, public draw, booking status. Sort by any visible column. Unlockable insights show reliability estimates and likely audience overlap. Hidden exact fields remain available only in developer diagnostics.

Timetable: stage columns, downward time axis, draggable blocks; five-minute snapping, keyboard move alternative, undo before commitment. Default set length 45 minutes, changeover 15; later act types vary. Within-stage overlap and curfew violations show explicit errors/warnings. Similar simultaneous acts across stages are permitted and highlighted as an estimated audience clash.

Guests maintain a ranked programme of wanted acts and can abandon a need queue to reach a favourite. Route time, toilet need and crowd access can prevent attendance. Audience counts include people actually in the listening area; distant movement toward the stage is not counted as attendance.

Per set, aggregate guest enjoyment from affinity, skill, production, sound interference, weather comfort and access. Artists react to actual turnout versus a reasonable expected range. Diva walk-offs require low turnout sustained for a fraction of the set, sufficient ego/frustration, and a decision check. Never trigger because zero guests are present during the first arrival second. Stage morale/hospitality interventions can help, but do not guarantee recovery.

Operational options: hold doors to a stage area, announce delay, shorten an upcoming set, move an act to an existing compatible stage, invite an available reserve act. All require valid time/capacity; late changes disappoint affected fans and are logged. Stage structures remain fixed.

## 8. Site, construction and rural assets

Start at fictional **Lower Wittering Farm** in **Merefordshire** (generated alternatives allowed). Map includes hedged fields, a perimeter road, rough vehicle track, gentle slopes, low wet ground, mature trees, farmhouse, two barns and cattle paddock. Land parcels have rental price, playable boundary, slope, drainage and neighbour sensitivity.

Farmhouse starts as office; later conversion adds a small VIP/artist accommodation wing. One barn stores stock/equipment; the other can convert into a small covered stage. Functions are slot-based refurbishments purchased off-season, not full building-interior construction. Trailer stage is inherited and usable after a small setup expense.

Players place facilities on a fine hidden grid; ghosts show footprint, doors, service access, orientation, listening/queue area and conflicts. Rotate placed assets in 90-degree steps. Move freely before hire confirmation; after commitment show any cancellation/refit cost. No charge exploitation through repeated buy/sell; undo restores the original transaction only within the same uncommitted edit.

Facilities require traversable entrances. Fences/barriers define access, but normal grass remains walkable. Do not require painted paths. Queues reserve nearby ground and cannot pass through buildings, hedges or other queues without a visible capacity problem. Shade/shelter, viewing obstruction, noise and terrain matter.

Core service units are abstract contracted water/electricity provision with costs and capacity bands. Each operational facility declares load. Exceeding capacity yields degraded service and breakdown risk; no pipe/cable networks. Utility upgrades raise contracted capacity. Power failures still occur through overload/maintenance/weather rules. Lightning protection is a packaged engineering upgrade with a risk reduction; it never guarantees storm safety.

### Desires, wear and mud

Store each terrain cell's base surface, wear, moisture, drainage, slope, contamination and hardening. Repeated traffic increases wear; wet bare soil increases mud. High-traffic dry ground gradually becomes a visible desire path. Wet desire paths become slowing quagmires.

Agents seek routes trading distance, queue/flow cost and discomfort. A moderately worn dry path can be attractive; deep mud can cause a detour. Apply hysteresis so everyone does not alternate routes every tick. Camera rotation changes presentation only.

Woodchip, timber walkway and metal trackway can be laid over selected ground before opening or by an emergency crew in a temporarily closed patch. They improve passability with different price, durability and appearance. Natural grass and dirt are never forbidden merely because an upgrade exists.

Mud adds dirt, slip risk and wet exposure. Guests differ in filth tolerance; dirt alone does not automatically make them ill or reduce spending. Dirty guests soil serviced facilities; cleaning lowers it. Showers become useful under mud and camping conditions, with their own queues. Wet footwear can produce a comic Trench Foot status after prolonged exposure; represent as discomfort/reduced mobility and generic first-aid treatment. This is a deliberately simplified fictional condition model, not a medical simulation.

Plausible spending effects: demand for showers and emergency ponchos/boots if stocked. No blanket arbitrary mud multiplier on all buying. Heat instead raises water demand. Free drinking water remains available and never requires a profitable purchase.

## 9. Weather, lighting and neighbouring land

Generate a hidden event weather trajectory once per edition, with separate forecast errors. Seasonal outlook at W8, broad probabilities W4, daily range W2, hourly tendencies W1, live nowcast at opening. Weather and errors remain stable across reloading.

Rain increases soil moisture; drainage and evaporation decrease it; air temperature, wind, wetness and shelter determine cold exposure. Clouds, shadows, rain particles, ground shaders and sound communicate state. Do not obscure urgent overlays in bad weather.

Heatwaves create the opposite operational pressure. Heat raises thirst and free-water demand, increases bar/soft-drink sales, and makes shade, water points, sunscreen and cooling/rest areas valuable. Guests caught unprepared can acquire sunburn or heat-exposure states; severe heat exposure becomes a generic medical incident requiring rapid treatment, cooling and possibly departure. More drink spending is a commercial opportunity, never a substitute for adequate free water or medical provision. Forecasts should make heatwave preparation possible but retain uncertainty.

Storms can trigger interruption guidance, power faults or generic injuries if hazardous operation continues. Protective upgrades reduce risk; closing exposed activity is the strongest response. No graphic electrocution sequence or guaranteed lightning-rod immunity.

Sound uses stage volume, distance, orientation and obstacles to estimate interfering sound at listeners and nuisance at neighbour receptors. MVP can use simple distance falloff and stage direction. Later shelter/terrain obstruction refines it. Neighbour reports show time, source and recommended control, making noise management actionable.

Curfew and permitted sound vary by licence/date. A fictional corrupt official may offer a risky curfew indulgence later: cash, uncertain enforcement relief, discovery risk and council-standing penalty. Do not provide real-world bribery detail. A formal paid extension is the reliable alternative when available.

Animals use paddock containment, fence condition, noise stress and gate state. Leaving cattle on site gives some guests pleasure and generates manure/maintenance; escape requires a breach or open gate, not a random spawn in the crowd. Relocation pays to remove this risk for the edition. Animal incidents cause generic damage and comic disruption, not animal cruelty.

## 10. Attendees: data and decisions

Each attendee has age category, party ID, visit format, ticket entitlements, genre affinities, three priority acts, money, cash, account balance, traits, needs, memories and current action. Camping status is not a personality. Families and party groups contain individual people with different preferences.

Traits: patience, sociability, aggression, jealousy, fidelity, sensation-seeking, filth tolerance, price sensitivity, music devotion, risk tolerance and comfort preference. Use a few readable archetype tendencies plus continuous variation; avoid manually authoring every combination. Friendship/partner links exist within a year's party graph. New social links may form during the event and expire into aggregate history after settlement.

Needs/states (0–100): hunger, thirst, bladder, fatigue, dirt, wet/cold exposure, social desire, boredom, intoxication, anger and injury severity. Mood is a weighted interpretation of recent fulfilled/missed needs and experiences, not an independent random slider.

Public selection card: current action, important needs, short visible labels, favourite music, ticket, a few recent thoughts and spend summary. Personality stays hidden. Guest Insight upgrade reveals limited estimated tendencies from entry/guest feedback with uncertainty; screening does not magically reveal exact infidelity or jealousy scores.

### Action selection

State machine: arriving -> travel -> queue -> service/watch/socialise/rest -> reconsider -> departing, with incident/treatment overrides. On a scheduled reconsideration or critical event, score feasible destinations/actions:

`utility = need relief + wanted-act value + group/social value + comfort - travel cost - wait cost - spending pain - risk`

Critical injury, dangerous crowd conditions and extreme physical needs override optional entertainment. Sticky intentions and minimum action duration prevent oscillation. Failed destination attempts get a temporary penalty. Small seeded individual variation prevents everyone making identical decisions.

Guests budget against real personal funds. Paying cash debits cash; card debits account; ATM transfers account to cash subject to remaining funds, machine cash and access. Never mint money at an ATM or allow card to imply infinite spending. Guests can retain a small reserved departure amount; food/drink trade-offs remain possible. Children use guardian-controlled budgets and appropriate services.

Arrival waves depend on opening time, wanted acts, transport and visit format. Parking allocates a vehicle slot per party; overflow delays entrance rather than producing free invisible parking. Lost child episodes derive from separation and failed reunion, alert staff, and conclude at a staffed meeting point or guardian contact.

## 11. Social behaviours and emergent incidents

Only local neighbours and known relationships are candidates for interaction. A social action needs proximity, availability and compatible context. Render short clear gestures/thought bubbles, avoiding constant text noise.

Reusable primitives: notice, approach, greet, enjoy conversation, flirt, accept/reject, kiss (adults only), observe, interpret, confront, de-escalate, shove, join argument, flee, request help, intervene, treat and escort. Each has preconditions, state changes, durations and cooldowns. Some stochasticity chooses among feasible responses; no outcome is guaranteed merely by demographic category.

Example A: partners separate because one seeks a toilet; the other enjoys conversation with a compatible adult; mutual attraction plus willing traits allow a kiss; partner returns within perception range and notices; jealousy creates anger; confrontation and aggression permit escalation; nearby friends decide whether to join/de-escalate; security response depends on coverage, workload and travel. If the partner never sees it, there is no omniscient jealousy trigger.

Example B: clashing similar bands split real listeners; a diva's audience falls below expectation; frustration grows; reassurance or better attendance may recover it; otherwise a walk-off affects those guests and later reputation.

Example C: a backstage toilet is neglected; door condition deteriorates; an occupant encounters a jam; a band member's availability prevents their set starting; a maintenance request queues behind other faults. There is no event script that directly deducts a performance because 'toilet joke rolled'.

Incidents retain causal IDs and participating entities, severity, observable location, response requirements and resolution. Player-facing summaries can say 'Queue delay -> missed set -> argument -> security response'. The game log must distinguish observed facts from flavour.

Incident variety uses cooldowns and exposure limits. Conditions drive hazards; do not spawn a brawl every five minutes to entertain the player. Cap simultaneous high-severity incidents for early editions through scenario tuning, not deletion of an already active hazard. Minor stories can continue. Clear, maintainable sites should materially reduce incidents.

## 12. Facilities, staffing, inventory and response

Every facility declares footprint, interaction slots, service duration, staff demand, stock/service resource demand, condition, dirt, maintenance need, utility load, access category and opening policy. Slot reservations are owned and released exactly once. Facilities can be closed, opening, operating, degraded, broken, repairing or retired.

Bars, merch and vendors use physical queues. Service finishes only if employee capacity, stock, entitlement and payment are valid. Failed payments redirect to ATM/other activities, with brief remembered frustration; they do not hold a till forever. Queues estimate waiting time using actual staff and current throughput.

Player hires department totals and assigns minimum staffing to facilities/posts. Departments: hospitality, security, medical, cleaning, maintenance and guest services. Workers move/act visibly where useful, but cannot be individually commanded. Automatic assignment prioritises life-threatening incidents, then stranded people/critical services, then routine jobs. Response time includes travel. Show insufficient staffing rather than teleporting responders.

Staff operate on paid shift coverage; long multi-day operation increases staffing expense and fatigue risk. MVP event staffing is prepaid sufficient-hour slots, with no individual biographies or rosters. Later shifts use department coverage presets. Unassigned workers form a response pool. Reassignments take travel/changeover time.

Additional staff can be hired live as emergency contractors. The player chooses an available department and coverage amount; the offer shows higher short-notice rate, arrival delay, shift duration, and any road/access constraint. Emergency hires cannot instantly appear at a facility or erase an active queue, but may prevent a deteriorating incident from becoming worse. Normal planned coverage remains materially cheaper.

Bars need beer/soft drinks; merch needs simple generic stock. Forecast purchases default to expected attendance and trade pattern. Players can override stock level using Lean / Expected / Buffer presets. Warehouse replenishment uses departmental logistics jobs and takes time. Emergency resupply is expensive, delayed, road-dependent and can sell out.

Food vendors submit bids for marked pitches; accept a vendor and assign their vehicle to a compatible pitch before opening. Bids display fee, menu, capacity, expected footfall range, price tier and food-safety/reliability descriptor. Low-fee, dodgy traders can offer tempting pitch fees or cheap food but carry a higher food-poisoning risk. Vendors simulate sales and operating costs. Low traffic prompts a complaint; compensation is optional unless the contract promised a minimum. Repeat disappointment reduces renewal willingness, not automatic unlimited refunds.

Food-borne illness is an aggregate causal chain, not a random guest penalty: an unsafe vendor must serve a guest, a delayed illness check must occur, and affected guests then seek toilets and/or medical care. A cluster raises toilet demand, queue pressure, complaints, vendor liability and festival reputation risk. Better vendors, inspection/standards policies and prompt medical/cleaning response reduce the chance or impact; no vendor is permitted to cause illness merely because they are cheap.

Security can monitor posts, de-escalate, separate, escort, search according to policy and enforce restricted access. Medical can assess, treat or arrange departure. Cleaning handles bins, toilets and muddy interiors. Maintenance handles condition faults and weather protection. No explicit police/legal-procedure simulation.

## 13. Live command and policy contract

All actions use a shared command system and data-driven availability. The UI must show cost, delay, eligibility and side effects before commitment. Config may disable an action for a mode or milestone without leaving broken buttons or stranded scenarios.

| Action | Allowed live | Limits / consequences |
|---|---|---|
| Set department/facility staffing | Yes | Available staff, travel delay, new contractors cost money |
| Close/open gate or area | Yes | Preserve emergency egress; entrants reroute; closure can strand demand outside |
| Dispatch response | Yes | Creates prioritised department job, not a teleport |
| Emergency stock / service capacity | Yes | Credit/cash and delivery ETA; roads matter |
| Hire emergency staff | Yes | Short-notice premium, arrival delay and limited contractor availability |
| Patch mud / repair | Yes | Crew job on temporarily secured patch; footprint cannot move |
| Alcohol service policy | Yes | Suspend/restrict service; demand, mood and safety consequences |
| Announcement / signage | Yes | Local/eligible recipients; persuasion has cooldown and imperfect compliance |
| Adjust act schedule | Limited | Existing compatible stage, changeover and curfew constraints |
| Move/build stage or permanent building | No | Off-season/planning only |
| Cancel/postpone whole event | No | Authority evacuation remains possible |

Policy groups: alcohol availability, entry checks, quiet hours, backstage access, waste provision, pricing, stage volume and livestock relocation. Most are presets with concise benefit/cost descriptions. Broad organisational policy should not require editing every facility unless the player chooses overrides.

Serious incidents auto-pause by default; less severe alerts do not. User can change alert classes. Deduplicate related alerts so one brawl does not pause ten times. Mouse camera and selections work while paused. Reports opened live pause by default with visible status and an option to keep time running.

## 14. Know-how, objectives and unlocks

Know-how is earned through successful experience and optional goals, never purchased with real money or harvested from repeatedly toggling policies. Award one edition-completion point plus up to three objective points, with one additional first-time milestone point. Failure still grants the completion point if the edition actually operated; restarting/reloading cannot duplicate awards.

Offer three objectives selected from feasible conditions each edition: one service, one artistic/identity, one stretch/commercial. Each states the exact metric, time window, minimum sample and reward. Example: 'Serve at least 15 bar customers; keep their 90th-percentile wait below five festival minutes between 16:00 and 20:00.' A closed bar cannot succeed through zero samples. Parent incident cause cannot be farmed for repeated rescue rewards.

Tree branches: Production, Comfort, Operations, Commerce and Reach. Nodes cost 1–3 Know-how, mostly require one earlier node, and unlock a choice of facilities/policies/contracts. No automatic stronger service simply for levelling. Cash, staffing and annual hire still apply. Licence expansion also requires trust and infrastructure, so Know-how alone cannot jump to the endgame.

Examples: second stage, covered stage conversion, dedicated backstage, camping, showers, glamping, trackway, operational radios, extra shifts, cash machines, card terminals, market insights, advanced marketing, accessible viewing platforms and improved acoustics. Basic accessibility of navigation and essential first-aid/water provision are baseline, not rewards.

Cosmetic purchases (bunting, flags, sign styles, stage skins) have clear no-stat labels and use cash after appropriate unlocks. Distinct identity milestones unlock decorative variants automatically, avoiding competition between a pretty flag and essential toilets for Know-how.

## 15. Sponsorship, partners and independence

Deals contain named fictional partner, upfront money, annual term, benefits, exclusivities, commitments, penalties and audience/artist reactions. All terms appear before acceptance. Conflicting category exclusivities are rejected. Bonuses credit once; annual obligations and renewal are visible.

Small local partners can be identity-positive without meaningful loss of control. Larger beer partners constrain drink prices/products; promoter affiliation funds growth and gives booking access while claiming future revenue and programming influence. Sponsor effect on each scene follows that partner's identity and conduct, not a universal coolness deduction for all money.

Independence reduces with actual control surrendered and recovers gradually after contracts end. Rebranding or dropping a logo does not instantly recover credibility. The independent route uses strong scene draw, direct sales, careful scope and cheaper prestige bookings; commercial route uses cash, infrastructure and broader reach. Balance both across multiple years rather than equalizing a single year's cash.

## 16. Interface, information and accessibility

World view always has a persistent top strip for cash/committed costs, current date/time, speed, attendance/licence, mood trend, forecast and alert count. Bottom toolbar exposes Site, Programme, Audience, Operations, Finance and Inbox. Selected objects open a right-hand inspector. Critical alert cards occupy a limited side rail; idle information does not cover the centre of the field.

Paper treatment: cream cards, printed grids, coloured tabs, stamped status labels, ticket-shaped counters, planner sheets and noticeboard accents. Use consistent aligned text and modern interaction; no handwritten body copy, random skew on tables or unreadably distressed surfaces. Inbox remains efficient with sender, subject, urgency, action deadline and one-click relevant screen.

Core screens: campaign selection; farm map/build catalogue; weekly planner; act search and comparison; timetable; ticket/marketing dashboard; vendor bids; staff/service coverage; stock; money/contracts; prestige/scene report; Know-how tree; annual newspaper/debrief; options and saves.

Overlays: crowd density/flow, queue length, mud/wear, service access, noise, cleanliness and staffing. One primary heatmap at a time plus selections. Tooltips identify units and whether a number is current, estimated or cumulative. Colour is backed by shapes/text. A stage selection reveals its audience and likely clashes; a bottleneck selection shows speed and missed-arrival counts.

Controls: WASD/arrows pan, wheel zoom, Q/E camera rotate, Space pause, 1/2/3 speeds, Escape cancel/close, Ctrl+Z uncommitted planning undo. Rebind controls; provide buttons for every key function. Drag-and-drop timetable supports click-select then click-slot and keyboard movement. UI scales from 1280x720 upward, target 1920x1080; test at 100/125/150% text scale. Optional edge pan, reduced motion, flash reduction, independent audio sliders and subtitles/event captions.

Context tips fire only on first meaningful need/encounter, with trigger IDs stored per campaign, cooldown, dismiss and 'show again in help'. No tutorial mode or forced camera. Example: first sold-out ATM queue prompts 'People have money in the bank, but not in their pockets' with a link to payments. No tip appears merely because a screen loads if irrelevant.

## 17. Art, animation and sound

Use actual 3D meshes with an orthographic camera around a 35-degree downward angle and four yaw orientations. Low-poly countryside, warm green fields, hedgerows, brick/wood buildings, cream canvas, colourful clothes and bunting. Avoid excessive tiny geometry, glossy plastic, flat grey prototypes as final art, or photorealistic textures. Outdoor lighting progresses from lush daylight to gold evening and stage-lit night.

Agents use shared rigs, clear body/clothing silhouettes and limited accessory variation. At normal zoom, a guest should be distinguishable from a guard, medic or artist. Animation vocabulary: walk, wait, cheer/dance, eat/drink, talk, seated/rest, slip/recover, gesture/argue, abstract dust-cloud brawl, escort and generic treatment. Toilet use is concealed; sleep is indicated by tents/poses. Cows have separate walk/idle/startle actions.

Building roofs and tall stage elements fade/cut away when concealing selected agents; four camera views must all support picking and access inspection. Mud changes ground shading and character dirt masks. Rain/lighting can use particles/material changes without altering simulation state through rendering.

Audio begins with properly licensed or original generic genre loops, crowd beds, rural ambience, rain, announcements and interaction sounds. Stage playback is spatial and mixed to avoid overlapping loops becoming unbearable. Visual stage indicators work muted. No real band's recordings or names. Adaptive original soundtrack and expanded genre performance loops are later production work.

The generated three-panel study in the conversation is an artistic direction reference, not a promise of identical in-engine fidelity or a set of production assets. The selected left panel establishes volume, palette and simplicity; game camera will sit higher to improve site readability.

## 18. Persistence and configuration

Save each campaign independently, including planning/live state, seed streams, all attendees currently attending, wallets, queues, injuries, relationships, reservations, jobs, weather, ground state, stock, liabilities, contracts, scores and claimed rewards. Between years, retire attendees and retain aggregate statistics/story summaries. Persist artists, vendors, rivals, land, improvements and relationships.

Autosave before week advance, before opening, at settlement and periodically during live operation. Save/load cannot award goals twice, reroll weather or reset service queues. Atomic saves, rolling backups and schema migrations are required from the first playable. Settings and input bindings are account-local, independent of campaign saves.

Tune costs, durations, utility weights, incident hazards, feature availability and content in versioned data. Save the selected ruleset version/hash with each campaign. Do not silently change a running campaign's settings when a content file changes. Internal developer diagnostics may expose hidden states; player UI may not.

## 19. Full-game completion criteria

The full design is implemented when a player can start on the farm, complete a one-day event, expand through multi-stage weekends, choose an economically viable cultural/commercial route, contend with meaningful weather and individual-driven incidents, reach the prestige leader and continue indefinitely. Every edition must have a reconcilable ledger and attributable audience feedback.

Performance, determinism, save recovery, accessibility and interface acceptance gates are specified in the companion documents. Content breadth is not a substitute for those gates. No production code has been written as part of this design handoff.
