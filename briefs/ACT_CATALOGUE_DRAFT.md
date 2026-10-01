# Act catalogue and festival reputation: draft for review

*Drafted and implemented 1 October 2026 (`src/Festival.Simulation/ActCatalogue.cs`). Names are working titles for a tone pass; check them against real bands before they are final.*

**Decided with the user:** a fixed catalogue; six genres with indie replacing rock; 5 greyed acts just out of reach in the table; a shortlist of about 10 with at least one available act per genre. **Expectations come from the ticket price** (fixed per tier for now: Tier 1 sells 20 advance tickets at £10, so guests expect popularity around 30). A lineup above that raises arriving guests' satisfaction by up to 8 points and makes sets up to 30% more enjoyable; one below it does the reverse, by at most 8 points and 30%. Local acts therefore suit Tier 1. The six original acts keep their existing fees rather than the curve.

## How booking would work

- **Festival reputation** runs 0–100 and starts at 0. It is kept for the whole campaign. After each completed festival it moves 35% of the way toward that festival's result (stars × 20).
- **Who will play for you** depends on an act's popularity compared with your *effective reputation* (below):
  - popularity up to effective reputation + 30: **available** at the listed fee;
  - up to 20 points beyond that: a **stretch booking** at 1.5× the fee;
  - further still: **won't play for you yet**, with the reason shown.
- **Scene credibility** runs 0–100 per genre and also starts at 0. Booking a genre and satisfying its audience raises it. An act's effective reputation is your reputation, **plus** half your credibility in its own genre, **minus** a quarter of your credibility in each genre it clashes with.
- **Each run's shortlist:** the seed picks about 10 acts from those who would play for you, so replays differ. A real band keeps its name, character and stats across runs.

At reputation 0 the reachable acts are the 20 locals (popularity 30 or less) and 10 stretch acts (31–50). Nothing near today's popularity-90 headliner is in reach.

## Genres

The six genres are **folk, indie, pop, electronic, punk and metal**. Indie replaces rock. Early acts lean folk and indie because they are local.

| Genre | Gets on with | Clashes with |
|---|---|---|
| Folk | Indie | Electronic |
| Indie | Folk, punk | Pop |
| Pop | Electronic | Punk, metal |
| Electronic | Pop | Folk |
| Punk | Metal, indie | Pop |
| Metal | Punk | Pop |

*"Gets on with" has no effect in this draft. It is recorded in case friendly scenes should give a small bonus later.*

## Fees

Fees follow one curve: **fee ≈ popularity × (0.6 + popularity ÷ 100)**, in pounds. That gives £7 at popularity 10, £27 at 30, £55 at 50, £91 at 70, £135 at 90 and £160 at 100. The fees below are the plain curve; character adjustments (a big ego costing more, a shambolic band less) can come with the tone pass. Tier 1 has £800 for everything, so a lineup of three locals costs well under £100.

## The catalogue: 53 acts

Ego is how demanding the act is. Professionalism is how reliably they turn up and play. Both run 0–100, and an ego of 70 or more means the act expects to headline (as now). ★ marks the six existing acts.

### Folk (10)

| Act | Popularity | Fee | Ego | Prof. | Note |
|---|---:|---:|---:|---:|---|
| Two Men and a Harmonium | 8 | £5 | 10 | 40 | The harmonium is borrowed from the church. Nobody asked. |
| The Parish Council Ceilidh Band | 12 | £9 | 15 | 85 | Meet monthly. Minutes available on request. |
| Margaret and the Damp Cardigans | 15 | £11 | 25 | 60 | Margaret is 81 and has played every pub within six miles. |
| Stile & Gate | 22 | £18 | 20 | 70 | Duo. Sing exclusively about public footpaths. |
| The Muddy Wellies | 26 | £22 | 30 | 45 | Bring their own hay bale to sit on. |
| Meadow Lanterns ★ | 40 | £40 | 20 | 80 | Earnest, lovely, slightly too long. |
| The Hay Fever Collective | 48 | £52 | 35 | 55 | Fourteen members, nine handkerchiefs. |
| The Whittled Spoons | 62 | £76 | 45 | 75 | Their spoon solo has its own fan club. |
| Orchard Chorus ★ | 70 | £91 | 45 | 90 | Harmonies so tight the cows stop chewing. |
| Bramble & Thorne | 78 | £108 | 65 | 80 | Folk royalty. Their tour van has a wood burner. |

### Indie (10)

| Act | Popularity | Fee | Ego | Prof. | Note |
|---|---:|---:|---:|---:|---|
| Bus Replacement Service | 6 | £4 | 20 | 15 | Will arrive eventually. Possibly. |
| The Allotments | 10 | £7 | 25 | 50 | Every song is about marrows. Every song. |
| Sixth Form Poetry | 14 | £10 | 55 | 30 | Have a manifesto. It is laminated. |
| Gap Year | 19 | £15 | 50 | 35 | Back from Thailand and want you to know it. |
| Lukewarm Tea | 24 | £20 | 30 | 60 | Mild. Pleasant. Forgettable in the best way. |
| The Overdue Library Books | 35 | £33 | 40 | 50 | Owe the county library £14.60. |
| Planning Permission | 45 | £47 | 50 | 65 | Took four years to get their first gig approved. |
| Barnstorm Circuit ★ | 55 | £63 | 35 | 65 | Was rock; now indie. Same jackets. |
| Velvet Bypass | 66 | £83 | 60 | 70 | Two NME mentions and they will bring both up. |
| The Cathedral Cities | 84 | £121 | 75 | 75 | Stadium indie. Wear coats in July. |

### Pop (8)

| Act | Popularity | Fee | Ego | Prof. | Note |
|---|---:|---:|---:|---:|---|
| Kerry from the Co-op | 9 | £6 | 45 | 70 | Does covers on her lunch break. Genuinely good. |
| Daisy & the Diaries | 18 | £14 | 50 | 55 | Every song is a voice note to an ex. |
| Glitter Rota | 34 | £32 | 55 | 60 | Leave glitter in the field until next spring. |
| The Bunting | 47 | £50 | 40 | 80 | Wholesome. Suspiciously so. |
| Sugar Tax | 63 | £78 | 70 | 65 | Expect a headline slot and a fruit bowl. |
| Matching Tracksuits | 76 | £103 | 75 | 70 | Synchronised dance routine; one member always late. |
| Neon Postcards ★ | 90 | £135 | 90 | 85 | Proper pop stars. Have a stylist for the stylist. |
| Heartbreak Hotline | 97 | £152 | 95 | 80 | Chart-toppers. Their rider requests a swan. |

### Electronic (9)

| Act | Popularity | Fee | Ego | Prof. | Note |
|---|---:|---:|---:|---:|---|
| DJ Spreadsheet | 7 | £5 | 30 | 90 | An accountant by day. Mixes in cell references. |
| Village Hall Disco | 16 | £12 | 20 | 75 | Mirror ball, smoke machine, one fire exit. |
| Strobe Warning | 21 | £17 | 45 | 40 | Fair warning given. |
| Low Battery | 39 | £39 | 35 | 25 | Set ends when the laptop dies. |
| Modular Compost | 53 | £60 | 50 | 60 | Synths made from garden waste. Sound like it. |
| Afterparty at Nan's | 60 | £72 | 40 | 70 | Nan is the drummer. Nan is 74. |
| Field Frequency ★ | 75 | £101 | 60 | 75 | Make the generator sound like a choice. |
| Sub-Bass Badger | 86 | £126 | 80 | 65 | Masked. Nobody has seen the badger's face. |
| Daybreak Protocol | 99 | £158 | 90 | 90 | Festival-closing headliners. Bring their own sunrise. |

### Punk (8)

| Act | Popularity | Fee | Ego | Prof. | Note |
|---|---:|---:|---:|---:|---|
| Neighbourhood Watch | 11 | £8 | 40 | 35 | Retired. Furious. Three chords and a petition. |
| The Hosepipe Ban | 20 | £16 | 45 | 30 | Banned from two counties for spraying the crowd. |
| Spat at a Swan | 28 | £25 | 60 | 20 | The swan won. |
| Unlicensed Bouncy Castle | 38 | £37 | 55 | 30 | Bring a bouncy castle. Unlicensed. |
| The Bin Strike | 52 | £58 | 60 | 40 | Leave the stage dirtier than they found it. |
| Council Tax | 68 | £87 | 70 | 45 | Everybody hates them; everybody turns up. |
| Copper Static ★ | 80 | £112 | 80 | 70 | Was rock; now punk. Swear more. |
| Riot at the Garden Centre | 91 | £137 | 90 | 50 | Legendary. Insurers refuse to cover them. |

### Metal (8)

| Act | Popularity | Fee | Ego | Prof. | Note |
|---|---:|---:|---:|---:|---|
| Doom Fete | 14 | £10 | 35 | 60 | Play the church fete every year. Vicar loves them. |
| Tractor Pull | 30 | £27 | 50 | 55 | Their drummer is a farmer. Their drum is a trailer. |
| The Septic Tank | 42 | £43 | 55 | 50 | Exactly as heavy as advertised. |
| Pitchfork Uprising | 50 | £55 | 60 | 60 | Agrarian black metal. The pitchforks are real. |
| Grimfarrow | 58 | £68 | 65 | 70 | Corpse paint, Barbour jackets. |
| Slurry Pit | 71 | £93 | 70 | 55 | The mosh pit is a slurry pit. Bring spare socks. |
| Combine Harvester of Souls | 83 | £119 | 85 | 75 | Twenty-minute songs about the harvest. |
| Thrashing Machine | 94 | £145 | 90 | 85 | Headliners. Genuinely terrifying. Lovely backstage. |

## How the catalogue is spread

| Popularity | Folk | Indie | Pop | Electronic | Punk | Metal | Total |
|---|---:|---:|---:|---:|---:|---:|---:|
| Local (0–30) | 5 | 5 | 2 | 3 | 3 | 2 | 20 |
| Regional (31–55) | 2 | 3 | 2 | 2 | 2 | 2 | 13 |
| National (56–80) | 3 | 1 | 2 | 2 | 2 | 2 | 12 |
| Headline (81+) | 0 | 1 | 2 | 2 | 1 | 2 | 8 |
