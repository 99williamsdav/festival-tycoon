# Festival Tycoon — Initial Content and Balance Catalogue

Version 1.0 • These are explicit starting values for implementation and playtesting, not validated balance. All rates use festival time unless labelled otherwise. Full game behaviour is defined in [GAME_DESIGN_SPEC.md](GAME_DESIGN_SPEC.md).

## 1. Scale and units

One visible attendee = one person = one wallet = one admission entitlement. No revenue multiplier represents imaginary crowds. A mature 800-person event has the fictional cultural standing of a major festival; the game world compresses population and money consistently.

Money is game-scale GBP, stored in integer pennies. Dimensions are visual metres; they are not real-world festival safety specifications. Timings are festival seconds/minutes; simulation speed converts them to wall time. Services are exaggerated for readable queueing and the tiny opening crowd.

| Parameter | Initial value |
|---|---:|
| Opening licence | 50 concurrent people |
| Initial loan / initial cash | £800 / £800 |
| Starter loan term | Five editions, £160 principal per edition |
| Interest | 8% of opening outstanding principal per edition |
| Initial public reputation / independence | 35 / 90 |
| Initial scene credibility | 20 each; bookings establish differences |
| Initial licensing / environmental standing | 60 / 60 |
| Initial prestige / Know-how | 5 / 0 |
| Planning length | Eight manually advanced weeks |
| Initial doors / curfew | 12:00 / 21:00 |
| Default first stage set / changeover | 45 / 15 minutes |
| Fair one-day ticket | £15 before quality adjustment |
| Starting general field | 128 x 128 metres; only part initially usable |
| Walk speed, dry flat ground | 0.12 visual metres per festival second (2.4 metres per real second at 1x) |
| Deep mud speed floor | 35% of dry speed before crowd effects |
| Default live speed | 20 festival seconds per real second |

At end of edition one, interest is £64. Later interest follows remaining principal. Interest and principal are explicit final liabilities already shown in the planning forecast.

## 2. First-year worked economy

Assume 40 paid attendees, no children/discount bundles for this fixture, one vendor, inherited trailer stage, all payments settled. This fixture exists to catch double counting and explain the loan; it is not a required player outcome.

| Cash inflow | Amount |
|---|---:|
| Starter loan | £800 |
| 40 tickets at £15 | £600 |
| Vendor pitch fee | £50 |
| Festival bar sales | £240 |
| Festival merchandise sales | £60 |
| Total including loan | £1,750 |

| Cash outflow | Amount |
|---|---:|
| Six local acts | £120 |
| Event/trailer setup | £40 |
| Hospitality and entry coverage | £30 |
| Toilets hire | £45 |
| Security coverage | £60 |
| Basic medical coverage | £30 |
| Cleaning coverage | £30 |
| Contracted power/water | £40 |
| Promotion | £40 |
| Bar inventory | £120 |
| Merchandise inventory | £30 |
| Permit/admin/basic cover | £40 |
| Operating cash purchases | £625 |
| Interest | £64 |
| Principal repayment | £160 |
| Total cash outflow | £849 |

Closing cash = £1,750 - £849 = **£901**. Remaining loan principal = **£640**. Bar cost of sold items = £96, leaving £24 stock; merchandise cost of sold items = £30. Operating income excluding loan = £950. Expenses excluding purchased inventory = £475; cost of goods sold = £126. Operating profit = £349; net profit after interest = **£285**. Cash increase from opening loan-funded £800 = £101 = £285 profit - £160 principal - £24 extra inventory.

At only 20 guests with the same preparation, ticket/bar/merch income halves, vendor fee remains £50, producing £500 income and £451 closing cash after repayment. Unused stock is £87 and net profit is -£102. One poor year is survivable but materially reduces next year's options. Expanding stages, permits and capacity consumes the surplus from good editions.

Use inventory equations rather than these fixed example totals in normal simulation. An attendee cannot spend £6 if their wallet/action choices cannot support it. Vendor pitch willingness must be evaluated against expected gross sales and costs; the £50 example must remain economically plausible for that vendor's model.

## 3. Demand model seed

MVP: four prospect segments, 100–160 total possible local buyers, differentiated by music interest, comfort and spending. All are adults until the family systems milestone. More nuanced groups extend this data model later.

For segment s and week w, values below are normalized 0–1:

```text
appeal = clamp(0.40*lineupFit + 0.20*publicTrust + 0.15*vibeFit
             + 0.15*practicalAccess + 0.10*socialProof, 0, 1)
pricePenalty = clamp((price / segmentFairPrice - 1) * elasticity, -0.15, 0.50)
purchaseChance = clamp(weekBase * (0.25 + 1.5*appeal)
                       - pricePenalty - competitionPenalty - forecastPenalty, 0.01, 0.65)
newBuyers = seeded binomial(awareUnsoldProspects, purchaseChance)
```

Here `weekBase` starts at 0.08 and grows toward 0.22 nearer the event. Competition penalty starts at 0–0.08; uncomfortable forecast penalty 0–0.12 depending on cohort and shelter. Price penalty is applied only to aware interested prospects; negative values represent a modest bargain boost. Actual sales are capped by remaining inventory, allocated in a stable seeded prospect order so earlier segment iteration cannot monopolise capacity.

Act `lineupFit` combines strongest desired act plus diminishing benefit from the rest (not the sum of fame). For example, normalize `bestFit + 0.25*secondFit + 0.10*thirdFit` against 1.35. Use skill/quality promise and genre affinity in each fit. Keep a separate programme-completeness penalty for grossly underfilled advertised days.

Advertising exposure: `uniqueReach = remainingUnaware * (1 - exp(-spend / segmentChannelScale)) * channelRelevance`. Cap within pool and apply all same-week spend together. Social proof from bots is separate from unique reach and decays quickly. MVP has no bot option.

Initial default ticket quote = £15 * day-equivalent duration * (0.8 + 0.2*offerQuality + 0.004*prestige), with configurable min/max for tier. `offerQuality` is 0–1. Display rounded whole-pound fair price; the ledger remains exact. Use overrides for authored first-year scenario (£15) so fixture maths stays stable.

## 4. Facilities and initial service rates

Values below are initial game balance, not real staffing/safety capacity guidance. Final catalogue should expose cost, annual cost, load and staffing separately. `Baseline` means available without Know-how; it does not always mean free.

| Facility | Unlock / role | Starter service model | Consequence when inadequate |
|---|---|---|---|
| Tractor-trailer stage | Baseline inherited | One performance; authored 50-person listening area | Poor access, limited production ceiling |
| Barn indoor stage | Production | 100-person listening area, sheltered | Sound overlap, doorway crowding |
| Field stage | Production | 150-person area | Exposure, larger staff/utility needs |
| Main stage | Advanced production | 350-person area | High hire/production cost |
| Small festival bar | Baseline | 1 till, 90-second service with one worker | Queue, lost sales, thirsty frustration |
| Independent food van | Baseline | 1 slot, 120-second service | Hunger, unhappy vendor if hidden |
| Merchandise stall | Baseline | 1 slot, 60-second service | Missed sales; optional year one |
| Toilet pair | Baseline | 2 cubicles, 120-second occupancy | Queues, dirt, desperate accidents |
| Free water point | Baseline essential | 2 slots, 30-second refill | Heat discomfort, service alerts |
| Security post | Baseline | Observation/dispatch anchor | Slower response, more escalation |
| First-aid point | Baseline essential | 1 slot, 5-minute generic treatment | Treatment queue, earlier departures |
| Waste bins | Baseline | Area coverage plus fill state | Litter, cleaning load |
| Gate | Baseline inherited | 10-second basic entry check per lane | Arrival queue; checks slow admission |
| Parking field | Baseline zone | 1 reservation per vehicle party | Delays and lost goodwill |
| Staff/backstage zone | Baseline designation | Entitlement access | Trespass, artist comfort loss |
| Camping area | Comfort | Pitches reserve sleeping spots | Fatigue, late-night disturbance |
| Shower block | Comfort | 2 slots, 4-minute service | Dirt complaints and queues |
| ATM | Commerce | 1 slot, 75-second service, finite notes | Guests have funds but cannot spend |
| Card terminals | Commerce | Per till upgrade; 60-second sale service | Terminal faults, recurring cost |
| Glamping / caravan | Advanced comfort | Limited reserved pitches | Premium expectations, access needs |
| Guest meeting point | Family milestone | Guardian reunions and support | Lost guests, guest-services backlog |
| Weather shelter | Comfort | Area occupancy slots | Cold/rain discomfort |
| Shade / cooling area | Comfort | Sheltered rest slots, water-adjacent | Heat exposure, dissatisfied guests |
| Sunscreen kiosk | Comfort / commerce | Low-cost sunscreen sales | Sunburn risk for unprepared guests |
| Stock barn | Inherited | Shared limited dry inventory | Replenishment delays when empty |
| Farmhouse office | Inherited | Administrative home | Cosmetic anchor; no forced office walking |
| Farmhouse guest rooms | Later comfort | Small premium accommodation inventory | More demanding VIPs |

Dry travel time across 60 visual metres is 500 festival seconds (25 real seconds at 1x). Movement is deliberately authored for readable on-screen pace relative to the accelerated event clock, rather than real-world walking speed. A toilet trip plus short queue can consume meaningful programme time and create missed-act decisions. Initial need decay should produce roughly 1–2 food visits, 2–4 drinks, 2–3 toilet visits per eight-hour attendee depending on traits and alcohol. Tune rates to meet these outcomes, not to display constantly maxed needs.

Cleanliness service initially reduces facility dirt by 35/100 over a two-minute job. Toilets accumulate dirt per use; muddy guests contribute extra. Broken facilities stop taking reservations, allow existing users to finish unless the incident specifically traps them, and route new guests elsewhere.

## 5. Reputation and pricing formulas

Public-trust update: `next = clamp(old + 0.35*(editionExperience - old) - promiseBreachPenalty, 0, 100)`. Promise breach is bounded 0–10 and applied once per distinct category/affected share.

Scene delta: capped -12 to +12 per edition, derived from satisfied scene attendees, relevant artist respect and commercial fit. Require a minimum audience share/sample for large positive gain. Tiny token bookings cannot raise every scene equally. New artists may carry multiple scene weights.

Artist fee quote (rounded to integer pounds for presentation):

```text
gapPremium = 1 + max(0, actFame - festivalPrestige) / 60
credibilityFactor = clamp(1.25 - relevantCredibility / 125, 0.45, 1.25)
relationshipFactor = clamp(1 - relationship / 250, 0.80, 1.20)
fee = baseFee * gapPremium * credibilityFactor * relationshipFactor
             * competitionFactor * commercialFitFactor
```

Relationship is -50 to +50. Competition factor 1–1.4; commercial fit 0.85–1.3 from artist preference. Local starting acts have authored quotes around £12–£30, overriding formula until opening scenario tuning is stable. Fame/prestige are 0–100. Prestige appearance waiver is a separate explicit contract, not fee underflow.

No automatic indie credibility penalty merely for the word 'pop'. Apply audience-expectation and authenticity mismatch. A well-delivered mixed festival can have both respected pop and indie programming.

## 6. Know-how tree and licence gates

Codes are stable definition IDs. Each row is a meaningful option, not a flat percentage buff. Full catalogue has 25 nodes; only the milestone subset should appear in early builds. Each prerequisite is AND unless labelled OR. Each node also requires all underlying implementation features to be enabled.

| ID | Node / cost | Prerequisite | Unlock |
|---|---|---|---|
| P1 | Second billing / 2 | None | Second stage booking and placement |
| P2 | Under cover / 2 | P1 | Barn stage conversion |
| P3 | Proper production / 2 | P1 | Field stage and better sound package |
| P4 | Artist haven / 2 | P2 or P3 | Backstage hospitality/guest rooms |
| P5 | Headline operation / 3 | P3 + P4 | Main stage package, prestige appearance opportunities |
| C1 | Make a weekend / 2 | Completed one-day edition | Camping and two-day permit application |
| C2 | Wash the weekend off / 1 | C1 | Shower hire |
| C3 | Dry feet / 1 | None | Timber walkway; woodchip is baseline |
| C4 | Creature comforts / 2 | C1 + C2 | Glamping/caravan products |
| C5 | Room for everyone / 2 | C1 | Quiet/family camping, extra viewing amenities |
| O1 | Radios that work / 1 | None | Coordinated dispatch/patrol coverage view |
| O2 | Weather desk / 2 | O1 | Nowcast detail, shelters and storm-protection packages |
| O3 | All-night crew / 2 | O1 | Night coverage presets and late-hours staffing |
| O4 | Firm ground / 2 | C3 | Reusable metal trackway and drainage work |
| O5 | Control room / 3 | O2 + O3 | Advanced area-flow plans and operation presets |
| M1 | Cash in a field / 1 | None | ATM hire |
| M2 | Tap and go / 2 | M1 | Card terminals and mixed-payment policy |
| M3 | Trader relations / 1 | One completed vendor contract | Revenue-share contracts and better bid forecasts |
| M4 | Deal maker / 2 | M3 | Larger commercial partnerships and comparisons |
| M5 | Merch culture / 2 | M2 | Artist merchandise contracts and premium stock |
| R1 | Know your crowd / 1 | None | More segment reports and targeting presets |
| R2 | On the circuit / 2 | R1 | More accurate act draw/reliability insights |
| R3 | Social reach / 2 | R1 | Influencers and risky bot promotion |
| R4 | Guest insight / 2 | R1 + O1 | Limited entry/feedback personality estimates |
| R5 | Beyond the county / 3 | R2 + R3 | Foreign campaigns and relocation search |

All-branch purchase cost is about 48 Know-how. At 2–5 points per edition, this supports roughly 10–15 editions with genuine choices. Skill progression and prestige need not finish together. These costs must be simulated for dead ends before content expansion.

Licence tiers: 50 -> 100 -> 200 -> 400 -> 800. Each application requires adequate land boundary/egress, contracted essential services, a fee/bond and minimum standing (initial candidates 50, 55, 60, 65 for successive increases). Serious breaches may impose a smaller next-year limit, never despawn already admitted people. The application screen explains exact deficiencies. No arbitrary toilet-per-head lock substitutes for actual queue simulation; service budget/essential provision is a coarse licence requirement, throughput remains a practical player risk.

## 7. Objective catalogue

Awards are once per edition/objective ID, with minimum denominator and active time window. Generate only feasible objectives from enabled content and actual event products. No requirement to purchase an unavailable facility.

| Objective | Exact starter evaluation | Reward |
|---|---|---:|
| A drink before the chorus | 15+ completed bar services, p90 wait <=5 minutes during named set window | 1 |
| The audience found them | 50%+ on-site guests attend at least 15 minutes of headliner; minimum 20 guests | 1 |
| Still on their feet | Operate 4+ hours, no untreated injury remaining over 10 minutes; minimum 20 guests | 1 |
| Trading places | Vendor sales cover contracted estimated operating cost including pitch fee | 1 |
| Paid the band | Complete event, pay all act fees and annual debt instalment without rescue | 1 |
| Dry-ish passage | During at least 30 minutes rain, keep critical-route average speed >=60% dry baseline | 1 |
| Scene favourite | 15+ relevant fans, scene experience >=75/100 | 1 |
| Money in the pocket | 10+ ATM withdrawals, no empty machine period over 10 minutes | 1 |
| Second stage, first impression | Two stages each have 10+ listeners for 20 minutes in at least one set | 1 |
| Peaceful neighbours | No sustained curfew/volume breach across full licensed programme | 1 |

MVP offers the first two and Paid the band only; award metric logic still generic. Annual completion point is separate. Do not make 'no injury' objectives unfair if unavoidable seeded harm exists: evaluate response, not luck.

## 8. Incident catalogue and implementation ordering

`M1` = first playable; later milestone names refer to IMPLEMENTATION_PLAN.md. Stochastic events require eligible conditions; a plain random newspaper story is flavour only and must not simulate invented causes.

| Incident | Preconditions / driver | Visible effect | Counterplay / resolution | Stage |
|---|---|---|---|---|
| Bar runs dry | Stock reaches zero | Closed sale slots, complaints | Resupply, alternatives | M1 |
| Toilet out of order | Condition/use hazard | Service lost, queue growth | Maintenance/extra hire | M1 |
| Missed favourite | Arrival after desired performance due to delay | Specific unhappy memory | Better flow/scheduling next time | M1 |
| Quagmire | Wear + rain + poor drainage | Slow/dirty guests | Woodchip, closure/reroute | M1 |
| Argument -> brawl | Frustration, aggression, observed provocation | Local abstract fight, injury | Security/de-escalation | M1 simple; M4 social |
| Artist cancellation | Reliability/availability hazard | Unfilled slot, refund risk | Reserve act, reprogramme | M2 |
| Vendor complaint | Sustained low trade relative to pitch promise | Inbox demand, renewal risk | Compensation or reasoned refusal | M2 |
| Power fault | Overload/condition/weather | Facility outage | Crew repair, capacity package | M2 |
| Noise complaint | Receptor nuisance over threshold | Warning/enforcement risk | Volume/hours change | M2 |
| Muddy slip | Wet surface + poor footing | Embarrassment, generic injury | Ground protection/first aid | M3 |
| Trench Foot | Prolonged wet feet + inadequate respite | Discomfort and reduced mobility | Treatment, dry facilities | M3 |
| Heat exhaustion | Heatwave + sustained exposure/dehydration | Generic medical need, possible departure | Free water, shade/cooling, medical response | M2 |
| Sunburn | Heat/UV exposure + no protection | Discomfort and satisfaction loss | Shade, sunscreen, warnings | M2 |
| Stomach-bug cluster | Unsafe vendor sale + delayed illness check | Toilet/medical surge, complaints | Vendor standards, medical/cleaning response | M3 |
| Cow escape | Open/broken paddock boundary | Manure, crowd reaction, damage | Containment/relocation/repair | M3 |
| Lost child | Guardian separation without reunion | Distressed party, service alert | Meeting point, staff reunite | M3 |
| Diva walk-off | Sustained low turnout + ego | Set ends early | Reassurance, programme design | M3 |
| Backstage toilet jam | Occupancy + door-condition fault | Artist unavailable | Maintenance rescue | M4 |
| Jealous partner | Observed consenting-adult romance + relationship/traits | Confrontation chain | Security/space/response | M4 |
| Drug/alcohol distress | Individual consumption + susceptibility | Generic medical need | Treatment/service policy | M4 |
| Stage storm hazard | Storm + operating exposure | Delay/fault/injury | Suspend activity/protect | M4 |
| Sponsor scandal | Dubious deal + seeded discovery | Credibility and contract trouble | Accept consequences/exit terms | M4 |
| Corrupt curfew bargain | Eligible fictional contact | Risky extra hours | Formal extension alternative | M4 |

Start incident budgets for a 50-guest day: aim for 2–4 minor problems and 0–1 serious problems in an ordinarily managed event; good management can avoid serious incidents completely. A deliberately neglected fixture should produce a clearly worse result. This is a target distribution across seeds, not a quota enforced by the storyteller.

## 9. Seed identities and writing voice

All names below are fictional working examples; check for accidental real-world matches before release. Generated names draw from curated word pools with duplicate checks and a profanity setting. No live text model is required.

Initial acts (authorable, then generative variation): **The Parish Notices** (indie, reliable), **Grievance Committee** (punk, energetic), **Velvet Roundabout** (classic rock, broad local draw), **DJ Damp Socket** (electronic, late preference), **The Hedgerow Situation** (folk/hippie, tolerant), **Lexi After Lunch** (pop, ambitious), **Concrete Teapot** (experimental indie, cult appeal), **Mild Inconvenience** (punk, high ego).

Initial traders: **Lord of the Fries**, **Wrap Committee**, **Beans of Production**. Fictional beer partner: **Big Barrel Leisure**; promoter: **Circuit & Co.**; local paper: **The Wittering Gazette**. Treat names as candidates pending clearance, not guarantees of uniqueness.

Sample contextual tip: 'The queue has money. The queue does not have cash. An ATM might help.' Link: payments panel.

Sample vendor complaint: 'You described the pitch as secluded. Our accountant has suggested a less positive word.' Body includes actual customers, sales and contract promise.

Sample incident headline: 'Singer freed from backstage toilet; encore now a technical possibility.' Only eligible when those facts happened.

Sample debrief: 'Forty people arrived. Thirty-six say they would return. The remaining four would like to discuss the toilets.' Underlying counts must match feedback, or the line is omitted.

Humour belongs in observations and personality. Controls, financial terms, licence requirements and failure reasons use unambiguous language.

## 10. Asset inventory by milestone

M0/M1: one rural terrain set, grass/dirt/mud materials, farmhouse, two barn exteriors, trailer stage, food van, bar canopy, toilet pair, gate/fence kit, water point, first aid/security markers, bins, stock props; one basic human rig with guest/security/medical/artist colour variants; four camera views; day/night/rain; paper UI components; simple music/crowd/rain/UI audio.

M2/M3: field and barn-stage interiors, tents/glamping/caravan, showers, ATM/card props, trackway, cattle, family/child silhouettes, more crew and guest variety, VIP rooms, clearer stage performance animations.

M4/full: expanded outfit/accessory and stage set dressing, larger production structures, visual social gestures, incident gags, site relocation variants, wider genre music loops, endgame spectacle. Reuse silhouettes/material families and modular components to keep production feasible.

Every asset needs provenance and an editable source or documented regeneration recipe. Concept imagery is a reference; usable animated meshes, UVs, materials and collisions require a separate production pipeline.

The original generated [style comparison](STYLE_STUDY.png) is preserved with this handoff. The selected direction is the left panel. It was generated using the built-in image tool during discovery; the other two panels are rejected alternatives. It is reference art, not an engine screenshot or licensed third-party game screenshot.

## 11. Simulation bootstrap values

These values remove blank implementation decisions. Store them in tuning data and adjust through controlled playtests. Gameplay values below use 0–100 presentation units (multiply by 100 for the technical fixed-scale representation).

### Needs, action utility and mood

| Need | Initial range | Growth per festival hour | Typical relief |
|---|---|---:|---:|
| Hunger | 5–30 | 12 | Food -65 |
| Thirst | 5–25 | 18 | Drink/refill -55 |
| Bladder | 0–20 | 8, plus 15 per substantial drink | Toilet -85 |
| Fatigue | 0–20 | 6; more late at night | Rest -15/hour; sleep -30/hour |
| Social desire | 10–40 | 8 while alone | Good conversation -25 |
| Boredom | 0–20 | 20 while idle, reduced during wanted acts | Enjoyed act -30/hour |
| Dirt | 0–10 | Terrain/service dependent | Shower -80 |

Show a label above 60, severe at 85, clear label below 50 (hysteresis). Essential need utility ramps sharply above 80. Baseline candidate utility uses `1.5*needRelief + actValue + socialValue + comfortValue - 2*travelMinutes - 2*estimatedWaitMinutes - pricePain - dangerPenalty`, with all value terms in 0–100 ranges. Only relevant need terms are included. Normal desired act value 40, favourite 80, headliner devotion bonus up to 20. Ordinary intent persists for at least 30 festival seconds unless a critical event intervenes. Reconsider every five seconds, staggered; switching requires utility improvement of 15 to prevent oscillation.

Mood starts at 60. Positive memories (good act +10, pleasant social encounter +6, good meal +4) and negative memories (missed favourite -15, intolerable wait -8, filthy service -10, frightening incident -15) have capped category contributions and decay over approximately two festival hours. Continuous unmet-need discomfort adds a current penalty. Annual guest experience averages time-weighted mood plus exit impressions, with max 20% exit weighting to prevent one final good drink erasing a disastrous day.

Initial money after ticket purchase: cash £12–£35, account £10–£45, correlated weakly with cohort; ticket funds are accounted before these discretionary balances. Basic drinks £3, food £5, merch £6. Guest retains £2 departure reserve when transport requires it. Sale service durations are in the facilities table. Drinking water costs £0 and does not require cash. Alcohol adds intoxication and bladder pressure, but repeated refusal/service policy caps eligible consumption.

Conversation lasts 45–120 festival seconds (2.25–6 real seconds at 1x); visible confrontation lasts 60–180 seconds with early de-escalation opportunity. Brawls last at most five festival minutes without intervention before separation/exhaustion, and still leave injury/anger consequences. Global social candidate scan radius starts at three visual metres, perception radius six with simple occlusion checks. Developer scenario fixtures can set traits directly; player-facing code cannot.

### Performance, faults and incident exposure

Per-guest set quality uses 35% affinity, 25% artist skill, 15% production, 15% comfort/access and 10% performer morale, normalized to 0–100. Interference reduces the production component. A set's satisfaction contribution scales with minutes actually attended (minimum meaningful sample five minutes); a passer-by does not receive a whole-set reward.

Artist expectation starts from confirmed ticket affinities and competing stage forecasts, never raw stage maximum. Low turnout is less than 30% of reasonable expected attendance sustained for ten minutes. A high-ego act may then roll a walk-off hazard; baseline conditional hazard 0.05 per minute, multiplied by ego/frustration and reduced by a successful hospitality response. An empty stage during changeover is ineligible.

Starter facility breakdown hazard: 0.0005 per operating minute at healthy condition, increasing up to 0.02 below 20 condition with overload. Maintenance restores 40 condition over five minutes. Daily normal wear is authored by uses, with repair between editions; do not decay at wall-clock time. These values target rare well-maintained failures and visible deterioration under neglect.

Fight hazard applies only after confrontation with sufficient anger/aggression: baseline 0.1 per minute during an eligible confrontation, with de-escalation competing at a comparable or greater rate for calm participants. Do not roll this for all guests all the time. Generic injury hazard is eligible only during an active fight, serious slip or environmental incident. Early scenario caps constrain escalation candidates; they do not magically erase causes.

Sustained crowd-pressure warning begins after 30 festival seconds over a configured occupied-fraction threshold (initially 0.75), critical after 60 seconds over 0.9 with low movement and opposing flow. These normalized fractions refer to the game's authored cell body capacity and are deliberately not real crowd-density guidance. Show crowd pressure in categories rather than persons per square metre.

### Weather bootstrap

Four date windows: early summer, midsummer, summer bank holiday, late summer. For the first implementation choose seeded scenario classes Dry, Showery, Wet or Stormy, weighted per date; interpolate hourly rain, temperature and wind curves within the chosen class. Initial probabilities for midsummer: 50/30/18/2 percent; late summer: 35/35/25/5. Other dates interpolate, with bank holiday demand bonuses independent of weather.

Weather values are game distributions rather than meteorological claims. Scenario-class probabilities must sum to one. Initial humidity/soil moisture sets the field response. At W8 show only seasonal probabilities; W4 a noisy class tendency; W2 daily ranges; W1 hourly intervals. Forecast uncertainty shrinks without revealing the exact stored future. Storm details remain rare in the first playable and are enabled later.

Ground per minute: rain adds 0–0.03 moisture; ordinary drainage removes 0.005, improved drainage 0.015; dry evaporation up to 0.005. Each crossing adds 0.002 wear, capped at 1, with slower gain on hardened ground. Mud = moisture * wear * (1 - protection). Deep-mud label starts at 0.6; movement multiplier = max(0.35, 1 - 0.65*mud). These are normalized tuning units, not physical hydrology. Make visual changes gradual and measurable within a first festival.

### Growth, contracts and annual restoration

| Item | Initial full-game price rule |
|---|---|
| Licence application | £50 / £150 / £400 / £900 for next 100/200/400/800 tier; annual compliance/admin remains separate |
| Neighbouring field rental | £100–£300 per edition based on area, drainage and road access |
| Second small stage hire | £120 per edition plus staffing/utility load |
| Barn stage conversion | £300 permanent; £40 annual inspection/setup |
| Field / main stage hire | £300 / £1,200 per edition |
| Camp pitches | £2 setup per reserved person per edition; weekend ticket pays for access |
| Shower / ATM hire | £80 / £40 per edition, plus staff/utilities or machine cash float |
| Card terminal | £50 purchase per till, £10 event service cost |
| Woodchip / timber / metal | £1 / £4 / £10 per square metre; timber/metal are reusable with condition |
| Cattle relocation | £60 per edition for the initial herd |
| Emergency stock | 150% normal unit cost + £20 delivery; 30–90 minute ETA |
| Emergency staff | 175% of planned hourly equivalent; 20–60 minute arrival; department availability is finite |
| Emergency credit | Up to 25% of conservative expected ticket income, minimum £100, hard cap £500 initially; 20% fee at settlement |
| One-time rescue | Shortfall up to 50% previous/current actual ticket income, minimum eligible ceiling £150; three-year promoter deal with 15% festival gross-revenue share and 20 independence loss |

Rescue is offered only on insolvency; no upfront windfall beyond the exact shortfall. The equity/control cost is fictional and explicit. Gross revenue share excludes loans and tax-like pass-through refunds. Subsequent edition obligations remain visible.

Field restoration removes litter/manure, resets temporary pitch reservations, repairs worn grass slowly and charges by damaged area plus cleanup labour. Baseline off-season cleanup £15 plus up to £0.50 per severely damaged square metre; authored initial scenario can include routine cleanup within its £30 cleaning budget, but excess damage is additional and forecast during the event. Permanent hardening persists with annual condition loss. Hire items return automatically; owned stock/structures remain. Relocation retains cash, staff policies, portable owned equipment and artist/vendor relationships; permanently converted barns/drainage stay on the original land. Show all stranded assets and move costs before committing.

Off-season livestock contracts are authored offers: initial examples are low-impact sheep grazing (£35 income, low grass wear), cattle grazing (£90 income, high wear/manure and small building-damage risk), and a premium managed grazing agreement (£55 income, low wear, no building risk). Contracts apply their result once at the next planning phase using the already stored offer seed. Damage must target an eligible nearby asset and generate a repair quote; never deduct condition from a building outside the rented parcel.

Difficulty initially has one authored Standard ruleset. After campaign balance, optional Forgiving can lower debt/incident severity and Generous Sandbox can relax finances. Do not introduce these until the Standard route is playable; they must use the same simulation rules and explicit settings rather than forks of the game code.
