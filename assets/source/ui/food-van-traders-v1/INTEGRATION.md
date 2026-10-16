# Food van traders v1: integration (the trader flyer board)

Approved: **direction A, the trader flyers, opened B's way.** Clicking a placed van on the field, or its line in the Build
drawer, opens the flyer board on that van's tab. This replaces `BuildDrawer.TraderPicker()` / `RefreshTraderPicker()`.
No code has been changed.

**Conventions:**
- **Units:** all sizes are **1280-wide mockup units** for `Ui.S`/`Ui.Px`, and positions are on the 1280 × 720 screen.
- **Rotations:** Godot `RotationDegrees` (positive = clockwise).
- **Colours:** `Ui.cs` names; trader colours are listed below.

`reference_render.py` draws everything at these numbers; `verification/` holds its output.

## Traders

| Trader | Art | Cuisine | Strapline | Card colour | Pitch fee | Portion | From tier |
|---|---|---|---|---|---:|---:|---:|
| Chip Off The Old Block | `trader_art_chip_block_v1.png` | Chips | "Fry Me to the Moon" | `4fa3d9` | £40 | £3.00 | 1 |
| Pizza the Action | `trader_art_pizza_v1.png` | Pizza | "That's Amore" | `c4573a` | £60 | £4.50 | 1 |
| Korma Chameleon | `trader_art_korma_v1.png` | Curry | "Karma karma korma" | `5b2a4f` | £30 | £5.00 | 2 |
| **Brew Fighters** | `trader_art_coffee_concept_v1.png` (concept art until the van is integrated) | Coffee | **"The Best of Brew"** | `1f5f5a` | n/a | n/a | Classic |

- **Art:** in `game/assets/ui/traders/`, 2×, each fitted to 324 × 160 px.
- **Data:** the strapline and card colour are new per-trader data (e.g. `Strapline` and `CardColour` on `FoodTrader`,
  or a small presentation table keyed by `Art`).
- **Brew Fighters strapline:** "The Best of Brew" (Foo Fighters, "Best of You"), which works in a British accent.
  Alternatives: "Learn to Pour", "Times Like Tea's".

## Pips (0–5), from the FoodTraders.cs values

`pip(v) = clamp(round(v), 0, 5)`

| Meter | Formula | Chips | Pizza | Curry |
|---|---|---:|---:|---:|
| **Speed** | `pip(5000 / ServicePermille)` | 5 | 2 | 3 |
| **Filling** | `pip((FillingPercent − 50) / 25)` | 2 | 3 | 5 |
| **Treat** | `pip((EnjoymentPercent − 70) / 14)` | 2 | 5 | 4 |
| **Pull** | `pip(1 + AppealPennies / 400)` | 1 | 4 | 2 |

A trader without stats yet (coffee) shows all five pips as outlines.

## Comparison chips ("vs current")

Each card shows the difference from the trader **currently booked at this van**. There are no chips on the booked card
itself, on coffee while it's coming soon, or when the van has no trader.

**The chips, in order, at most 3:**
1. **Pitch fee**, if different: "£20 more pitch" (good) or "£20 less pitch" (bad).
2. **Then each meter that differs**, in the order speed, filling, treat, pull:

| Meter | Higher (good) | Lower (bad) |
|---|---|---|
| Speed | "quicker" | "slower" |
| Filling | "more filling" | "less filling" |
| Treat | "bigger treat" | "smaller treat" |
| Pull | "pulls more" | "pulls fewer" |

**Colours:** a good chip is `Teal` text on `TealWash`; a bad one is `Alert` text on `AlertWash`.

## Opening and closing

**Opens from:**
- clicking a placed food van in the world (in preparation, its pick volume);
- clicking a van line in the drawer;
- after placing a new van, on that van's tab.

**Closes:** with Done, the × or Esc.

**Booking:**
- Clicking an available flyer commits `ChooseFoodTraderCommand(traderId, vanId)` straight away. The stamp thumps in:
  scale 1.4 to 1, α 0 to 1, 0.18 s.
- The board stays open, so you can check another van.

**Dim:** a `BarDeep` α 0.59 rectangle over the map (x 456–1280, y 60–612), so the drawer stays readable.

## Board layout (1280 × 720)

| Element | Spec |
|---|---|
| **Panel** | x **466–1262**, y **74–576**. `Paper` fill (`f5ebd6`), radius 10; shadow black α 0.55, offset (0, 3), blur 8. |
| Caption | "WHO'S PITCHING?", caps 10, `InkMuted`, at (486, 90) |
| Title | `Ui.Slab` 22, `Ink`, at (486, 104): "Van 2 · by the Pond Stage bar" (van number · nearest landmark, using the game's relative naming) |
| Close | 24-unit circle at (1222, 90), `PaperRule` outline, `x` icon 14 `InkMuted` |
| **Tabs** | y **138–162**, from x 486, **6** apart. Each is a pill: width = text + 30, radius 12. 8-unit dot in the trader's colour at (+8, +8); text body bold 11 at +21, e.g. "Van 2 · Pizza". Active: `Ink` fill, `BarText` text. Others: `PaperBright` fill, `PaperRule` outline, `Ink` text. If the tabs overflow (many vans in Classic), scroll them horizontally with the mouse wheel. |
| **Cards** | Top **y 178**, from x **486**, **182 × 292**, **12** apart (four cards fill 764). With more traders, card width = max(150, (756 − 12·(n − 1)) / n); if they'd still be narrower than 150, scroll the row horizontally. |
| Footer | "Click a flyer to book it for this van · it pays you at Start · swap any time before then", body semibold 11.5, `InkMuted`, at (486, 542) |
| Done | (1122, 532)–(1242, 562), `Gold` fill, radius 8, "Done" bold 14 in `Bar` |

## The flyer card (card-local; card 182 × 292)

| Element | Spec |
|---|---|
| Card | `PaperBright` (`fff8e9`), radius 8; shadow black α 0.27, offset (0, 3), blur 5 |
| Art panel | (6, 6)–(176, 92), radius 6, the trader colour at α 0.16 |
| Van art | Fitted into (10, 10)–(172, 90), bottom-aligned, centred |
| Cuisine tag | At (12, 12), 16 tall, width = text + 14, radius 8, trader colour fill; caps 9.5 bold `PaperBright` at +7 |
| Name | `Ui.Slab` 15 (13.5 if 20 or more characters), `Ink`, at (12, 100) |
| Strapline | In curly quotes, body semibold 11.5 in the trader colour, at (12, 121) |
| "pays you £40" | Chip at (12, 142), 18 tall, width = text + 16, radius 9, `TealWash` fill, bold 11 `Teal` |
| Portion price | "£3.00 a portion", body semibold 10.5 `InkMuted`, right-aligned at (170, 151) |
| Meters | Four rows at y = 170 + 15·i (Speed, Filling, Treat, Pull). Label body semibold 10.5 `InkMuted` at x 12. Pips: 5 × 9-unit squares, 3 apart, radius 2, from x 110 (182 − 72). Filled in the trader colour; empty in `PaperRule`. |
| Compare chips | From y 232, 15 tall, 3 apart, radius 7, width = text + 12, bold 10 at +6 |
| Note line | Body semibold 10 `InkMuted` at (12, 274) |

## States

| State | When | Look |
|---|---|---|
| **Normal** | Available, not booked here | As above |
| **Hover** | Mouse over an available card | Lifts 4 units (y − 4), 3-unit `Gold` border, bigger shadow (α 0.47, blur 8); 0.08 s ease |
| **Booked here** | This van's trader | 3-unit `Teal` border and no compare chips. Stamp: a `Teal` 2-unit outline box, 26 tall, width = text + 20, centred on x 91, top at y 248, text `Ui.Slab` 13 `Teal` "BOOKED · VAN n", **`RotationDegrees = −5`**. |
| **Locked by tier** | `FromTier` > the current tier | Card `eee6d3`. Art greyscale at α 0.55 (a greyscale shader, or modulate (0.6, 0.6, 0.6, 0.55)). Name, strapline and pips in `InkMuted` / `b9c4b8`. **Tape:** a strip from x −6 to 188, y 42–62, `Ink` α 0.92, "FROM TIER n" body bold 11 in `Gold`, centred, **`RotationDegrees = +8`**. `lock` icon 14 at (156, 100). Not clickable; tooltip is the existing `TraderUnavailable` text. |
| **Booked at another van** | Survival's one-van-per-trader rule: `TraderUnavailable` says "already at van n" | Greyed as locked, with no tape. Tag at (12, 56), 18 tall, radius 9, `Ink` fill, body bold 10.5 `Gold`: **"At van n · change van n first"**. Clicking it switches to van n's tab. |
| **Coming soon** | Coffee, until it has stats | Art greyscale; pips as outlines; "pays you —", "— a portion". Tape as locked, but `Gold` α 0.94 with "CLASSIC · COMING SOON" in bold 10.5 `Bar`, `RotationDegrees = +8`. Not clickable. |
| **Second van, same cuisine** (Classic) | Another placed van already has the same `Product` | Note line: "2nd chips van: shares the chips crowd" (cuisine in lower case). Also on the booked card. |

## The drawer (replaces the trader rows in Food & drink › Food van)

- **Header:** "FOOD VAN TRADERS · THEY PAY YOU TO PITCH", caps 9.5 `InkMuted`, under the Food van row.
- **List:** one line per **placed** van, in van-number order. Unplaced catalogue vans aren't listed; placing one opens the
  board on its tab.

| Element | Spec |
|---|---|
| Line | **382 × 44**, 6 apart, radius 6, `PaperBright`, 1-unit `PaperRule` outline |
| Thumb | Box 54 × 34 at (6, 5), radius 4, trader colour α 0.16, with the trader art fitted to 50 × 32 |
| Text | "VAN n", caps 9 `InkMuted`, at (68, 6); the trader name, `Ui.Slab` 13.5 `Ink`, at (68, 18) |
| Fee | "£40", bold 12 `Teal`, right-aligned at (350, 22) |
| Chevron | `chevron-right` 14 `InkMuted` at (358, 15) |
| Open tab | While its board tab is open, the line has fill `fbf0d4` and a 2-unit `Gold` border |

With more vans the drawer scrolls as now; no paging is needed, since each van is a single line.

## Verification (`verification/`)

| Render | Shows |
|---|---|
| `board-tier1-hover-chips` | Tier 1: chips hovered against the booked pizza; curry taped; coffee coming soon |
| `board-tier2` | Curry unlocked |
| `board-classic-second-chips-van` | Two chips vans |
| `card-states` | Normal, hover, booked, tier-locked, booked at another van, coming soon |
