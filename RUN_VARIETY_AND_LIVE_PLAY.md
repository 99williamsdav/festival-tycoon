# Festival Tycoon — run variety and live play

**Status: proposal/discussion, 30 September 2026.** Nothing here is approved or authorized for implementation. It records design discussion on two open questions: *what makes attempt 5 different from attempt 1?* and *what does the player do during the live day?* Each idea needs its own product decision, concept (where graphics are involved) and bounded brief. Read alongside the [design philosophies](DESIGN_PHILOSOPHIES.md), [current prototype](CURRENT_DESIGN.md) and [roguelike authority](ROGUELIKE_DESIGN.md).

Numbers below (pool sizes, pacing) are illustrative starting points for discussion, not tuning.

---

## Part 1 — Run variety

### Guiding test

Randomness is only useful if it changes the plan. For every source of variety, ask: **would a different seed change what I build, book or hire?** If not, it is flavour, which is welcome but does not create replay value.

### 1. Weekend contracts: the campaign backbone

Before each tier, offer a choice of 2–3 **weekend contracts**. Each bundles:

- a **crowd type**, which shapes needs and behaviour,
- a **condition**, which constrains or pressures the plan,
- a **reward**, such as cash, a Favour, a contact or a perk draft.

Examples:

| Contract | Crowd | Condition | Reward |
|---|---|---|---|
| Folk & real ale weekend | Calm, heavy drinkers | Beer demand ×2 | Low fee, steady bar income |
| Rave in the barn | Rowdy, late-staying | Disorder pressure up, longer closing | High fee |
| Council jubilee | Mixed, older | Noise limit; inspector walks the site | Earns a Council Favour |

This turns the campaign into a string of self-chosen risk/reward decisions (the FTL / Slay the Spire map choice) and gives each run a different shape. It also gives Favours a natural, authored source that fits the "visible opportunity cost" rule.

### 2. Larger offer pools

Draw each weekend's offers from larger pools, so the market differs every time.

- **Bands:** draw about 6 from a pool of about 30. Give them traits that act on the simulation, not just stats, building on the existing Ego/Professionalism system:
  - "brings 5 superfans who push to the front" (crush risk, high enjoyment),
  - "crowd drinks double during their set",
  - "late arrival" (the changeover may overrun),
  - "pyro" (a fire risk near the generator),
  - "demands an encore".
- **Staff:** individuals with one quirk and a price, for example:
  - a cheap steward who wanders off,
  - an ex-paramedic who is slow behind the bar,
  - the farmer's nephew, who works for free but is useless.
- **Equipment deals:** a second-hand generator at half price but less reliable, or a borrowed PA with a return deadline.

### 3. Weekend modifiers

These reshape needs, which is where the existing systems already interact:

- **Heatwave:** thirst rises, so water becomes the bottleneck.
- **Rain:** mud slows movement, and toilets and the tent get busier.
- **Rival event nearby:** guests arrive late and all at once.
- **Council conditions:** no glass, a curfew, a licensing officer on site.

### 4. Guests with traits (Philosophy 1: people, not meters)

Each weekend, include a few **named guests** with a visible trait or goal:

- a critic whose rating counts double,
- the councillor's nephew,
- a stag party,
- someone with a known medical condition who needs shade and water.

They give each run a cast and turn failures into stories ("the stag do drank the bar dry and started the fight").

### 5. Perks that combine

Eight uniform perks cannot carry a roguelike. Aim for about 25–40, with deliberate **combinations that suggest a strategy**. Example: *Real Ale Licence* (beer pays more) plus *Composting Loos* (toilets fill more slowly) together invite a "beer festival" strategy. Perks should push the player toward a festival identity, not just add small percentages.

### 6. Progress on the persistent farm

The persistent farm is the most distinctive asset, so progression should live in the story rather than in a global stat menu:

- **Band memory:** bands you treated well return cheaper, and bands you treated badly badmouth you.
- **Contacts book:** staff you have hired become known contacts with known traits.
- **Scarred land:** a field churned to mud one weekend starts worse the next.
- **Casualty history:** already recorded, and the natural home for the deferred zombie/ghost returns.

Everything here must respect the existing anti-grind rule: repetition must never be the only route to the basic counters needed to survive.

---

## Part 2 — The live day

### Framing

Preparation is **strategy**; the live day is **tactics plus payoff**.

| Model | Player activity during the live day | Risk |
|---|---|---|
| Pure watch (autobattler) | Speed controls, inspecting people | Passive; failure feels unfair because the player couldn't react |
| Micromanager | Directing individuals | Breaks Philosophy 3 |
| **Festival director (recommended)** | A small set of high-impact calls, each with a cost | Needs tuning so it's neither idle nor frantic |

### Director calls

- **Radio your staff.** Staff are the player's hands: "medic to the stage", "steward to the bar queue". Staff take time to walk there and leave their post uncovered while away, so the cost is physical rather than a click budget. This fits the physical simulation and Philosophy 3.
- **PA announcements.** Crowd-wide levers with trade-offs:
  - *Last orders:* less beer income, less drunkenness.
  - *Move back from the barrier:* eases the crush, but the crowd enjoys the set less.
  - *Free water at the farmhouse:* costs stock, but cools the heat.
- **Stage calls.** Extend or cut a set, delay the headliner, allow an encore. Each has a clear payoff and risk. For example, delaying the headliner buys time to fix a problem but makes the crowd angrier.
- **Service switches.** Close or open the bar, take a tap out of service, shed generator load. The existing equipment chain already expects "shed load / isolate".
- **Dilemma moments.** Two or three times a day, a short paused decision: the band wants more money or won't go on; a guest offers to "fix" the generator; rain is coming. These are a good home for trait-driven band and guest behaviour, and for the approved-direction high-ego storm-off ([B-012](briefs/BACKLOG.md)).

### Pacing rules of thumb

- Aim for roughly **one meaningful decision every 30–60 seconds**, and let players fast-forward through calm stretches.
- **Every call costs something:** time, money, happiness or staff coverage. A free intervention is a chore, not a decision.
- **A good plan should survive a normal day on its own.** Live calls are for surprises and optimisation, so a strong plan can be watched working (Philosophy 3).
- **Every lethal-chain warning should point to one or more director calls as its counter.** This is the practical home of the causal-safety rule.

### Tone: farce, not dread (Philosophy 6)

- **Pressure ladder:** nuisance → problem → emergency → death. Most live chaos sits on the funny lower rungs (someone stuck in the portaloo, a cow in the field, the drummer gone missing). Emergencies are fewer and unmistakable; death happens only when an emergency is ignored.
- **Near-misses carry the drama.** A last-second rescue is the highlight of a day. Celebrate it with a sting, a camera moment and a newspaper line ("Heroic steward saves man from own dancing").
- **Use the timetable's rhythm.** Changeovers bring bar and toilet rushes; headliners bring crush. Chaos peaks there and eases during good sets, so the player gets breathing space.
- **Serious signals, cheerful copy.** "Dave has climbed the speaker stack. Dave is not qualified."

---

## Part 3 — Tier 1 red herrings

*Idea recorded 30 September 2026 at the user's suggestion.*

### The idea

In Tier 1, some threats look alarming but cannot become critical. Players who give them too much attention get distracted from the real threat. The same threats come back in later tiers, where they *can* escalate, so the first encounter works as an early warning. The player's growing knowledge becomes the progression, which suits the roguelike structure and the farce tone: the generator coughs smoke while the real problem builds quietly at the water tap.

### The main risk: trust

If the game ever seems to lie about danger, players learn to ignore warnings, and a later death feels unfair. That would break the causal-safety contract and Philosophy 2 (consistent, learnable, trustworthy rules). Red herrings must therefore come from **conditions, not scripting**.

### Rules

1. **Same rule, different conditions.** A threat always follows the same rule. The generator chain is always load × condition → fault. In Tier 1, 20 guests and a small rig keep the load too low to go past a scare. In later tiers, a bigger crowd and heavier rig can push it over the edge. The player learns a real rule, not "this one's fake".
2. **Honest warning levels.** A warning never overstates its rung on the pressure ladder (nuisance → problem → emergency → death). A herring can look dramatic (smoke, flicker, a worried steward) while clearly showing *Problem*, not *Emergency*. The skill being tested is triage: reading the ladder correctly under distraction.
3. **Explain after the fizzle.** When a herring fades, say why in the Newspaper or debrief. For example: *"Generator overheated briefly. Load was light tonight. With a bigger crowd, this could have been serious."* The early warning then becomes explicit.
4. **Attention has a real cost.** Under the festival-director model, fussing over a herring means a staff member walking away from their post, and that is how the real threat slips through. The distraction does its damage through the simulation, never through an invented penalty.

### Variation and replay

- Once a player knows which threat is the herring, it stops distracting them. That is acceptable: knowledge is the reward.
- Weekend contracts and modifiers (Part 1) can shift which threat is real and which is noise, for example a heatwave making water critical while the generator stays safe. Conditions must decide this, never a hidden random roll.
- Some herrings can be pure nuisances with no fatal version at all, such as a cow in the field or a lost drummer. These carry the comedy.

### Open questions

- Which existing chains (equipment, medical, disorder) make the best Tier 1 herring versus Tier 1 real threat?
- Should the debrief always explain fizzled threats, or only the first time the player sees each one?
- How are the escalation thresholds tuned so the Tier 1 cap holds for every valid Tier 1 layout, including extreme ones?

---

## Review against the design philosophies

| Philosophy | How this proposal serves it | Watch-out |
|---|---|---|
| 1. People, not meters | Trait bands, quirky staff and named guests make individuals the source of variety | Traits must show up in visible behaviour, not hidden modifiers |
| 2. Fun over realism | Contracts and modifiers are chosen for interesting decisions, not accuracy | Don't let modifiers become paperwork |
| 3. Organise, don't micromanage | Calls go through staff and crowd-wide levers, never direct control of guests | Too many calls per minute would break this; tune the pacing |
| 4. Failure creates stories | Named guests and trait-driven chaos produce memorable, attributable failures; farm memory carries consequences forward; red herrings foreshadow later threats | Farm scars must not create a death spiral that forces a restart; herrings must never mislabel severity |
| 5. Depth through interacting systems | Modifiers and traits act on existing needs (thirst, toilets, drink, crush) instead of adding new currencies | Resist adding a new meter for each modifier |
| 6. Farce, not dread | Pressure ladder, celebrated rescues, comic copy, chaos from excess | Keep death rare; frequent deaths would turn farce into grind |

## Decisions needed before any brief

1. Adopt **weekend contracts** as the tier-selection structure? If so, how many per choice, and do contracts replace fixed tiers or sit inside them?
2. Adopt the **festival director** model for the live day, and which call families come first (staff radio, PA, stage, service switches, dilemmas)?
3. Target pool sizes for bands, staff and perks for the next playable milestone.
4. Should farm memory (band memory, contacts book, scarred land) be part of R0.06 rewards, or later?
5. Should quality (rating) affect which contracts are offered? That would give quality a real role beyond rewards.
6. Adopt **Tier 1 red herrings** (Part 3), and which chain plays the herring?

## Suggested first slice (for discussion)

The smallest test of whether this direction is fun, using existing Tier 1 systems:

- **Two contracts** with different crowd types.
- **One modifier** (heatwave).
- **Three director calls**: radio a staff member, *last orders*, delay the headliner.

Then run a human playtest ([B-017](briefs/BACKLOG.md)) before building pools or meta progression.
