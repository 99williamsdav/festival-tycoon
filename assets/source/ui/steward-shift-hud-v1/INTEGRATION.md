# Steward shift HUD v1: integration spec

These are the approved picks from the Steward shift board: **prompt A, compass A, radio A, needs A**. No code has been
changed.

**Units:** every measurement is in **1280-wide mockup units**, i.e. the values `Ui.S()` and `Ui.Px()` take. A 1280
capture matches these numbers exactly. The renderer that draws them is `hud_reference.py` (functions `prompt_A`,
`compass_A`, `radio_A`, `needs_A`, `urgent_vignette`); `verification/` holds its renders over the first-person captures
with the installed arms.

**Fonts:** `Ui.Slab` (Zilla Slab SemiBold) for titles, the body and caps fonts for the rest. Sizes are in mockup px.

**Colours:** `Ui.cs` names, hex in brackets. "α" is alpha.

## Icons

| Icon | Status | Use |
|---|---|---|
| `hand` | new | Calm action |
| `radio` | new | Radio strip |
| `thermometer` | new | Heat bar |
| `droplet` | existing | Thirst bar |
| `heart-pulse` | existing | Urgent call, first aid landmark |
| `map-pin` | existing | Objective |
| `music` | existing | Stage landmark |
| `beer` | existing | Bar landmark |
| `door-closed` | existing | Toilets landmark, open action |
| `trash-2` | existing | Litter |
| `check` | existing | Success |
| `x` | existing | Fail |
| `minus` | existing | Cancelled |

- **Installed:** the three new ones are in `game/assets/ui/icons/` (white stroke, Lucide style; the same import
  settings as `cloud-rain.svg`: svg scale 2, mipmaps), so `Ui.Icon("hand")` and `Ui.IconRect("radio", …)` work as they
  do for the rest.
- **PNGs:** each new icon is also in `out/png/` at 14/18/22/26 px and 2× (28/36/44/52), white and gold.

## Crosshair

Keep the prototype's crosshair. Every panel below is placed relative to the screen centre (640, 360).

## 1. Hold-to-act prompt (A): cream card under the crosshair

**Position:** card top at **y = 404** (44 below centre), centred on x.
- Width = max(title width, detail width) + **84**.
- Height **62**, corner radius **10**.

**Card:**
- Fill `Paper` (`f5ebd6`) α 0.96.
- Drop shadow black α 0.43, offset (0, 3), blur about 5.
- **State border:** 2 px in the state colour; none while holding.

**Key cap:**
- 24 × 24 at (x0 + 16, y + 19), radius 5, fill `Bar` (`17302a`).
- The letter in body bold 14, `BarText` (`f5ebd6`), centred.

**Progress ring:**
- Centred on the key cap, radius **19**, stroke **3.2**.
- Track `PaperRule` (`e2d3b3`), fill `Gold` (`d8a43b`).
- Starts at 12 o'clock and runs clockwise, filling linearly with held time.

**Text block** at x0 + 58:

| Row | y | Content | Font | Colour |
|---|---|---|---|---|
| Line 1 | y + 9 | Action icon (15 px) | n/a | `Teal` (`2b6e66`) |
| Line 1 | y + 8, at x0 + 78 | **HOLD E** | caps 10 | `Teal` |
| Title | y + 21 | "Calm Ollie Moorcroft" (verb + name) | `Ui.Slab` 17 | `Ink` (`1f2a26`) |
| Detail | y + 42 | "guest · 2 m · agitated" (kind · distance · state) | body semibold 12 | `InkMuted` (`56615a`) |

**Result states:** the ring and key cap are replaced by a solid **36 px disc** on the same centre, with a 22 px icon in
`PaperBright` (`fff8e9`). The title moves to y + 12 and the detail to y + 35.

| State | Disc and border | Icon | Title | Detail (example) |
|---|---|---|---|---|
| Success | `Teal` (`2b6e66`) | `check` | Past tense: "Calmed" | "Ollie's settled down" |
| Fail | `Alert` (`b8551e`) | `x` | "Shrugged off" | "Ollie's not having it. Try again or call it in" |
| Cancelled | `InkMuted` (`56615a`) | `minus` | "Cancelled" | "You let go too soon" |

**Verbs and icons:**

| Verb | Icon |
|---|---|
| Calm | `hand` |
| Break up | `shield` |
| Open | `door-closed` |
| Pick up | `trash-2` |
| Help up | `heart-pulse` |
| Escort | `users` |

**Timings:**
- **Appear:** when a target is within reach (2.5 m) and within about 8° of the crosshair. Fade in, rising 6 px, over
  0.12 s.
- **Leave:** fade out over 0.10 s when the target leaves.
- **Hold lengths** (suggested): calm 1.6 s, break up 2.2 s, open 0.8 s, pick up 0.6 s, help up 2.5 s, escort 1.2 s. The
  ring fills linearly over the hold.
- **Releasing early:** Cancelled shows for 0.8 s, with no penalty.
- **Success or fail:** shows for 1.2 s, then fades over 0.2 s.
- **Arms:** the right hand eases in when the hold starts (see `steward-fp-arms-v1`).

## 2. Compass (A): slim top strip

**Strip:**
- Centred on x, top y = **14**, **520 × 26**, radius 13.
- Fill `BarDeep` (`0e1f1a`) α 0.59.
- Shows **150°** across: **3.47 px per degree** (260 px per 75°).

**Contents:**

| Element | Spec |
|---|---|
| Ticks | Every 15° except cardinals: a 1 px line from y + 9 to y + 17, `BarMuted` (`b9c4b8`) α 0.63. Not drawn beyond ±74°. |
| Cardinals | N, E, S, W in body bold 13, `BarText`, centred at y + 13 |
| Landmark icons | 14 px, `BarMuted` α 0.78, at y + 6, drawn within ±72°: stage `music`, bar `beer`, toilets `door-closed`, first aid `heart-pulse`, gate (optional) `map-pin` muted |
| Centre notch | A triangle (cx − 5, y + 27), (cx + 5, y + 27), (cx, y + 22), in `BarText` |
| Objective pin | `map-pin`, 20 px, top at y + 2. **Clamped to ±72°**: a pin behind you sits at the strip's edge, pointing the way. |
| Distance chip | Under the pin, y + 30 to y + 48 (18 tall), radius 9, width = text + 16. Text "Bar 2 · 64 m" in body bold 12. |

| | Pin and chip fill | Chip text |
|---|---|---|
| Normal | `Gold` | `Bar` |
| Urgent call | `Alert` | `PaperBright` |

**Timings:** the pin glides to its new bearing (lerp at 12 per second). The distance updates every 0.25 s, rounded to
whole metres.

## 3. Radio call strip (A): bottom-left

| | Normal | Urgent (collapse, crush, fire) |
|---|---|---|
| Box | x **16** (`Ui.Gutter`), bottom **704**; **470 × 62**; radius 8 | **520 × 74**, same anchor |
| Fill | `BarDeep` α 0.80 | `Alert` α 0.93 |
| Left accent bar (4 wide, full height) | `#d8f126` (the vest's hi-vis) | `PaperBright` |
| Icon at (x0 + 16, y0 + 14) | `radio` 22, hi-vis | `heart-pulse` 26, `PaperBright` |
| Tag at (x0 + 52, y0 + 9), caps 10.5 | "RADIO · CONTROL", `BarMuted` | "URGENT · CONTROL", `PaperBright` |
| Message at (x0 + 52, y0 + 22) | `Ui.Slab` 18, `BarText` | `Ui.Slab` 21, `PaperBright` |
| Where line, body semibold 12 | y0 + 43, `BarMuted` | y0 + 49, `PaperBright` α 0.86 |
| Hint, right-aligned at x0 + w − 14, body semibold 10.5: "R  Answer   ·   Tab  Ignore" | y1 − 18 | y1 − 22 |
| Response timer | n/a | A 4 px bar along the bottom edge (from x0 + 4), `PaperBright`, width = time left; the countdown "0:42" right-aligned at (x0 + w − 14, y0 + 9), body bold 12 |

**Urgent edge pulse** (`urgent_vignette`), once per urgent call:
- A full-screen `Alert` vignette: transparent inside a rounded rect inset 3% (x) and 5% (y), blurred out to the edges.
- Peak α **0.32**, ramping up in 0.15 s and fading over 0.9 s.

**Timings and queueing:**
- **Arrive:** slide in from the left, 0.2 s ease-out, plus the radio arm's talk raise.
- **Normal calls:** stay until answered (R) or ignored (Tab), or for 20 s. Then they collapse to a one-line strip (tag
  plus message) until answered.
- **Urgent calls:** replace whatever is showing and can't be ignored. The timer runs, say, 45 s; at zero the strip
  reads "Control: sending someone else", and the call counts as missed.
- **Queue:** further calls queue. Show a small `BarText`-on-`Bar` badge, "+2", at the strip's top-right corner.
- **Prototype text:** the prototype's "you're on the second post" style messages fit as normal calls.

## 4. Own needs (A): thirst and heat bars

Two rows, stacked just above the radio strip: x0 = **24**, rows at **y = strip top − 60** (thirst) and **strip top −
38** (heat). With the normal strip (top at 642) that's 582 and 604.

| Element | Spec |
|---|---|
| Row pill | x0 − 6 to x0 + 118, y − 3 to y + 17 (124 × 20), radius 9, `BarDeep` α 0.47 |
| Icon | 14 px at (x0, y), `BarText` α 0.86: `droplet` (thirst), `thermometer` (heat) |
| Track | x0 + 22 to x0 + 108 (86 wide), y + 5 to y + 10 (5 tall), radius 3, `BarText` α 0.24 |
| Fill | value × 86 wide: `Good` (`8fd1b5`) up to 0.5, `Warn` (`f2a65a`) above 0.5 |

**Visibility:**
- **Hidden** while the value is below 0.3.
- **Appearing:** fade in over 0.4 s; fade out over 0.6 s once it's back below 0.25 (hysteresis).
- **Above 0.8:** the row's alpha pulses between 0.7 and 1.0 at 1 Hz.
- **Nothing is fine:** both rows hidden, which is the usual state.
- **Bar placement** (`verification/needs-hidden.png` vs `needs-shown.png`):
  - Each row hides on its own, so with only one need showing, it takes the lower slot.
  - If the radio strip has collapsed or is hidden, keep the rows where they were rather than letting them jump down.

## Verification (`verification/`, `render_hud.py`)

| Render | Shows |
|---|---|
| `prompt-hold`, `-success`, `-fail`, `-cancel` | The four prompt states, calming Ollie by the stage |
| `prompt-door`, `prompt-litter` | The other actions, with their hands |
| `compass-radio` | Compass A and a normal radio call |
| `urgent-clamped` | An urgent collapse call: orange strip, timer, edge pulse, orange pin clamped at the strip's left edge (the stage is behind you) |
| `needs-hidden`, `needs-shown` | Nothing to show, and thirst at 66% (amber) with heat at 42% |
