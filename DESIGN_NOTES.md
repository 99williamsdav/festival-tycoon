# Festival Tycoon — Living Design Notes

> Historical discovery record. The consolidated [GAME_DESIGN_SPEC.md](GAME_DESIGN_SPEC.md), [TECHNICAL_SPEC.md](TECHNICAL_SPEC.md), [CONTENT_AND_BALANCE.md](CONTENT_AND_BALANCE.md) and [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) now form the authoritative developer handoff. Earlier tentative statements below are retained as history; use the specification where decisions evolved.

## Final discovery decisions — 6 September 2026

- Selected the left-hand visual study: low-poly 3D.
- Tactile festival paperwork for the management interface, with persistent overlay elements on the site view.
- The user delegated remaining design decisions and requested a complete specification without further interactive questions.
- The consolidated specification resolves remaining conflicts and separates full-game scope from the first playable. No production code was requested or written during this design handoff.

## Project premise

An original festival-management simulation. The player develops a rural festival site, places and operates infrastructure such as stages and camping areas, books artists, sets policies, and manages attendees whose movement, spending, satisfaction, and mischief produce emergent stories. The player inherits a farm but has no interest in farming, and starts a festival on its land; this is an origin and site-ownership premise, not a cosy farming-game direction.

## Product principles

- A readable, lively graphical site simulation paired with deep, paper-style management.
- Decisions should have clear trade-offs and visible consequences on the site.
- Strong systems foundations before content scale or visual polish.
- The player is the creative director and operator, rather than an individual attendee.
- Begin as a grassroots independent event and grow into a credible Glastonbury-scale rival (represented visually by a few hundred individual attendees, not 200,000 agents).
- The tone is recognisably British festival culture with light exaggeration, warmth, and chaos; it is not a cosy game.
- The game must remain accessible and playful, in the spirit of Theme Hospital / RollerCoaster Tycoon, while producing emergent management stories reminiscent of Prison Architect and RimWorld.

## High-level progression

- The game advances one festival weekend at a time, followed by assessment and preparation for the following year.
- The default early planning phase is freeform and untimed. A calendar/turn structure may be considered later, but is not a foundation requirement.
- Early festivals can only secure local artists and food vendors.
- Growth unlocks larger acts, vendors, capacities, infrastructure, and site ambitions.
- Prestige is the overarching measure of success, but cashflow must remain tight enough to make survival a persistent concern.
- There is no hard campaign length. A capable player would likely reach the endgame in roughly 8–15 festivals.
- The game remains open-ended. A festival-prestige league/table culminates in surpassing a Glastonbury-like leading festival, functioning as a soft win state.
- Completing contextual in-event objectives yields a progression currency/experience. It can unlock beneficial or cosmetic upgrades; exact presentation (spendable currency, levels, or hybrid) remains to be designed.
- The campaign opens with a loan. Exact starting capital/debt is a balancing decision, but loan pressure should make first-year profitability meaningful.
- The first event is a one-day festival, expanding into multi-day/camping events through progression.
- The opening licence is approximately 50 people, with roughly 20–50 actual attendees depending on ticket demand. This deliberately stylised/abstract scale makes every visible individual meaningful.

## Strategic identity systems

### Independence vs commercialisation

- Sponsorship and partnership offers provide meaningful funding or operational advantages.
- Accepting them erodes independent credentials / coolness.
- Examples: exclusive beer partnerships and affiliation with a large promoter such as Festival Republic.
- This is a strategic tension, not a single correct path.

### Festival vibe and music identity

- The player can establish a festival personality, such as family-friendly, debauched, mainstream, or hippy.
- Music programming can establish genre identity: mixed, indie, pop, punk, metal, etc.
- The player does not permanently select a genre identity at campaign start. After programming a festival, they can market it toward an appropriate genre/scene to concentrate appeal and credibility.
- Identity should attract different attendee cohorts. Multiple identities can lead to success.
- A financial pull toward mainstream choices should exist, balanced against cultural credibility/coolness.
- Vibe is represented as multiple coexisting qualities/traits rather than one exclusive spectrum.
- Coolness/credibility may also be tracked by scene or faction (e.g. indie, punk, metal, hippie, hip-hop, rave), allowing an event to be respected by one scene but dismissed by another.

### Artist market and booking

- Artists are fictional, named acts with distinct identities to support immersion and memorable events.
- Artists may have genre, popularity, fee expectations, reliability, temperament/ego, and appropriate reputation expectations. Rider requirements are explicitly deferred.
- The player can bid above the festival's reputation tier, paying a higher fee for an aspirational booking.
- Acts charge a premium to appear at an uncool festival; high-coolness festivals can secure major artists for less.
- Endgame aspiration: a Glastonbury-like reputation where megastars want to play for prestige rather than money.
- Artist behaviour should respond to the event: e.g. a diva lead singer may abandon a set if a simultaneous booking leaves their stage nearly empty.
- Booking UI takes inspiration from a simplified Football Manager player search: filter/sort the act market, initially with limited information and later-unlockable scouting/insight criteria.
- For the MVP, acts should use a calculated fixed fee or simple offer range; players never type a bespoke fee value.
- The initial event has 5–7 programmed acts, a manageable abstraction of a real one-day line-up. Later licences/identities can permit late-night acts to support a more nocturnal vibe.
- The player controls running order. The planning interface is an original drag-and-drop timetable board: time axis vertically, stage columns horizontally, and movable act blocks. It must scale to multi-stage scheduling and conflicts.
- Each act has cohort/genre-specific draw as well as total popularity: a smaller respected scene act may increase faction credibility, while a more generic mainstream act may sell tickets but harm independent/cool credibility.
- Performance quality and crowd energy respond to audience match, stage quality, weather, schedule, artist morale, sound quality, crowd access, and incidents. Good sets lift festival satisfaction; poor ones may create complaints or artist drama.

### Vendor market and placement

- Food/vendor pitches are allocated by the festival.
- Vendors should bid/apply and be selected, then be placed by the player (exact flow to decide).
- Placement affects footfall and sales. Poorly placed vendors may complain, seek compensation, or decline to return next year.

## Failure and disruption

- Serious operational failures are an intended major component: for example weather and mud, artist cancellations, transport failures, crowd-safety risks, crime, and licensing problems.
- The desired experience is legible, entertaining crisis management rather than a dense Football Manager-style simulation.

## Site and live operations

- Core construction/placement includes terrain and paths; stages; campsites; toilets; bars; food traders; security; medical; entry gates; parking/shuttle areas; staff zones; backstage areas; and additional festival infrastructure to be defined.
- A festival can expand through rented neighbouring fields or relocate to a different site as it grows.
- During the weekend, players can pause/speed simulation and intervene operationally (e.g. staffing, access, response), but cannot make implausible structural changes such as moving stages.
- Live interventions include redeploying staff, changing access to gates/roads/areas, operational incident responses, limited emergency purchases/contractors, selected policies (such as alcohol service), and signs/announcements. Each should be independently configurable/enabled for tuning and future modes.
- Staffing is department/facility-level rather than individual-worker micromanagement: players set department capacity and assign levels to specific bars, guard posts, etc.; staff then act automatically.
- The intended representation is concrete, visibly individual festival-goer agents, not an entirely abstract crowd model.
- Players do not paint ordinary paths. Attendees make their own routes across fields; frequently used routes form visible desire paths. Rain turns worn ground into slowing, dirtying quagmires, while targeted upgrades (e.g. woodchip, wood or metal trackway/grates) harden critical routes.
- Opposing/heavy flows through a constrained area can create bottlenecks, delayed arrivals, missed acts, and frustration.

## Presentation and management interface

- The site is presented as an isometric, 3D-looking world, with a movable camera. Camera rotation is desirable but remains a technical scope decision.
- The camera uses fixed 90-degree rotations, plus pan/zoom, rather than free rotation.
- Stages visibly show simplified animated performers and audiences whose size and energy respond to the simulation.
- The administration layer takes inspiration from Football Manager's reports/dashboards but is intentionally less information-dense.
- An in-universe email/inbox drives communications, offers, notices, and reports.
- Initial audio uses generic ambient festival sound/music; original adaptive music is a later enhancement.
- The player character is an unseen manager; there is no playable/avatar character on the site.
- Avoid a conventional tutorial. Provide optional, dismissible, contextual notifications when the player first encounters or needs a system.
- Desired site mood: lush rural British summer with a bucolic atmosphere.
- Visual-rendering direction remains to be selected after an original three-way style study: chunky low-poly 3D, crisp illustrated 2D, and hand-painted storybook.
- The player can customise the festival name, colour palette, stage names, bar names, and campsite names. The system provides generated defaults with clear override controls; logo creation is deferred.
- Support multiple continuing campaign save slots.

## Platform (provisional recommendation)

- Target a downloadable Windows-first desktop game for the initial release: it best supports the live individual-agent simulation, offline saves, and desktop interaction. Keep future browser portability in mind, but do not let it constrain the first foundation.

## Setting

- Preferred direction: an invented British county, towns/councils, festivals, artists, and companies, written with recognisably British place-name humour. This preserves comic flavour and avoids making the game feel like a direct real-festival simulator.
- Use of specific real British locations remains a possible later decision.

## Weather

- British weather is central to the simulation.
- Forecasts inform planning; weather should create actionable uncertainty rather than arbitrary surprise.
- Forecasts begin as broad seasonal signals and become more specific nearer the event, but retain a meaningful possibility of being wrong.
- Rain/mud/heat/darkness and the day-night cycle are candidate systems. Better paths and infrastructure mitigate weather effects.
- Mud lowers attendee happiness, slows movement, and soils facilities, increasing cleaning requirements and dissatisfaction.
- Attendees can have a (potentially hidden) filth tolerance trait; easy-going/hippie attendees may be less affected.
- Mud increases demand and queues for showers, which may be an unlockable facility. Health consequences are reserved for sustained severe exposure, such as hypothermia, rather than routine mud.

## Attendees and tickets

- Attendees are represented on two axes: visit format (day-tripper or weekender/camper) and a cohort such as local visitor, family, student, party group, dedicated music fan, affluent older guest, or troublemaker.
- Cohorts can engage with any musical scene, while genre and identity shift their likelihood (e.g. punk may bring a higher troublemaker share; classic rock more older guests).
- Candidate ticket products: day pass, weekend pass with camping, glamping, family-only offering, caravan parking, and VIP/backstage access.
- Capacity management/possible overselling is a candidate risk/reward mechanic, subject to later safety and licensing design.
- Attendees have individual spending funds. Early events are cash-only: cash is finite unless ATM capacity is supplied, which can itself produce queues. Later upgrades require payment but enable card terminals/payment.
- Player-visible attendee labels/statuses appear when needs or states cross thresholds (e.g. thirsty, muddy, angry, lost, drunk). Personality traits remain hidden and emerge through behaviour.
- A later guest-screening entrance upgrade may reveal attendee personality traits at arrival.
- Day-trippers primarily arrive in pre-opening/early-event waves. Future weekenders/campers arrive earlier and remain overnight, making gates and parking/camping layout consequential.

## Date and competitor pressure

- Date selection is strategically meaningful: a summer bank holiday can improve demand and perhaps weather but has more competing festivals; a quieter late-summer date risks poorer weather.
- Competitor festivals will exist in the MVP at a basic level, with deeper booking/date competition phased in later.

## Site progression

- Begin with one rural site with varied terrain and constraints. Add site relocation later, alongside expansion into neighbouring fields.
- The inherited farm begins with a road around it, rough track, designated car-park field, several barns, a farmhouse, and a tractor trailer that can serve as the initial stage.
- Treat water/electricity capacity as financial outlay initially rather than a detailed network-construction simulation.
- Barns and farmhouse should have possible festival uses; exact functions remain to be designed.
- Farmhouse functions include festival office and VIP accommodation. Barns provide storage and an indoor stage.
- Livestock remains on-site by default. Players can pay to relocate it for an event; some guests may enjoy its presence, while livestock (especially cows and manure) can create operational issues.

## In-event objectives

- Add small, contextual side objectives during a festival (e.g. operational, artist, or audience targets) to create focus beyond general satisfaction.
- Rewards are player/festival progression currency or experience, not literal individual staff/attendee XP.
- Prefer a skill-tree-like festival-know-how progression if there are enough meaningful unlocks; otherwise use a more linear unlock structure.
- Candidate unlock categories: additional and specialised stages, advertising, new facilities, cashless/card infrastructure, and operational upgrades.

## Economy and commercial operations

- Planning is funded by startup capital and/or loans. Major costs are paid upfront: artist deposits/fees, temporary infrastructure, site rent, staffing, insurance, and so on.
- Ticket income is substantially upfront, with residual sales during the festival when not sold out.
- Bars and merchandise stalls are festival-operated; food vendors are independent and pay pitch fees. Revenue share is a possible negotiated alternative.
- The player should not need to micromanage every price. The game proposes prestige-aware defaults, with simple raise/lower overrides for tickets, vendors, and related charges.
- Bar and merchandise prices allow slightly more direct control. A sold drinks-rights partnership can constrain drink products and pricing.

## Planning, demand, and marketing

- Ticket sales progress week by week during the planning period. Demand responds to line-up, price, marketing, date, festival reputation/identity, and forecast as appropriate.
- Weak early sales can be addressed by booking additional acts, changing promotion, or related action, with corresponding cost and risk.
- The show cannot be cancelled or postponed: disruptions must be operated through.
- Baseline marketing channels: posters, local radio, flyers, and social media.
- Social-media promotion has scalable spend options (simple ads, sponsored influencers, bot farms) and can target chosen audience characteristics: age, personality type, and local/national/foreign reach.
- National television and radio advertising are out of scope for the intended festival realism.

## First playable festival (agreed MVP scope)

- One site with a stage, entry gate, campsite, festival bar, food vendor, toilets, and security post.
- Basic artist booking, ticket pricing, staffing levels, and weather forecast.
- Visible attendees with movement/needs; queueing, happiness, sales, safety, crowding, and rain/mud effects.
- A compact set of incidents, supported by reusable simulation rules.
- Early incident pool includes cancellations, rain/mud, gate queues, toilet failure, insufficient beer, power failure, noise complaints, medical incidents, fights, lost children, escaped cows, and food-vendor disputes.

## Capacity, risk, and alerts

- A licence provides the hard attendance maximum. Practical/soft limits emerge from gate throughput, camping area, toilets, stage access, parking, queues, terrain flow, and crowd density.
- Exceeding practical limits reduces enjoyment and raises safety/reputation risks; overselling is a possible high-risk tactic.
- Serious incidents trigger configurable alert-and-pause notifications; lower-severity events run without interruption unless the player intervenes.

## Tone, incidents, and content boundaries

- Comedy should arise from names, emails, fictional band/vendor personalities, animations, bad-practice consequences, and highly varied emergent incidents.
- Incidents should be absurd but plausibly festival-adjacent, and result from reusable conditions wherever possible.
- Examples: an artist misses a set after being trapped in a neglected backstage toilet; a muddy slip causes an embarrassing wardrobe mishap; cows enter the site and damage equipment; bribing a local council to overlook a noise curfew; an electrical storm threatens an unprotected act.
- Generate fictional band names as a prominent humour source.
- Do not use real brands.
- The game may depict drugs, sexual behaviour, bodily functions, politics, and environmental damage in a silly grown-up tone. Violence remains abstract: brawls and generic injuries needing treatment/departure, with no blood or graphic harm.

## Emergent-social simulation principles

- Memorable incidents must arise from composable individual rules rather than scripted story chains.
- Illustrative chain: compatible attendees meet and flirt; an existing partner observes a kiss; jealousy produces an argument; friends and inadequate security allow it to spread into a fight or brawl.
- Candidate agent relationships and states include: arriving with companions/partners, social affinity, attraction, separation, observation, jealousy, anger, intoxication, conflict, friend involvement, and security intervention.
- The implementation must prioritise legibility and a restricted initial behaviour set over attempting a fully general social simulator.
- Festival-goer identity/relationships do not persist between festival years in the initial design, avoiding a small-scale/cutesy tone and unnecessary simulation burden.

## Decisions pending

- Player fantasy, setting, scale, and time model.
- Winning/failing and campaign structure.
- Simulation detail, visual style, platform, engine, and business model.
- Core entities, economics, safety, artists, attendee behaviours, construction, and policies.

## Discovery log

### Session 1 — 2026-09-06

Established the core player fantasy, tone, representation scale, annual structure, success measures, strategic identity, desired reference games, and appetite for meaningful failures.
