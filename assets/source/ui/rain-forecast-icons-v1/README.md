# Rain forecast icons v1: the top bar's weather chip

From the approved heavy-rain board (`Documents/Festival Tycoon concepts/heavy-rain/`), item 7, **chip B**: icon and
word, plus a muted "till 3:40" or "in 20 min". A downpour icon is added for the rare rain C.

**Rebuild:** `python make_icons.py`. Copy `out/*.svg` to `game/assets/ui/icons/`, then run the Godot import. The SVG
import settings match the existing Lucide icons (`svg/scale=2.0`, mipmaps on).

## Icons

The style is Lucide's: a 24 × 24 grid, stroke 2, round caps and joins, white stroke (the game tints them, e.g.
`Ui.IconRect(icon, 18, Ui.Gold)`). The cloud outline is Lucide's own, from the shipped `cloud-rain.svg`
(lucide-static v0.460.0, ISC).

| Name (`Ui.Icon("…")`) | Use |
|---|---|
| `cloud-showers` | Showers, light rain |
| `cloud-heavy-showers` | Heavy showers (rain B, the norm) |
| `cloud-downpour` | Downpour (rain C, rare) |
| `sun-dry-spell` | A dry spell is coming, or has come |

- **PNGs:** `out/png/<name>_<px>px_{white,gold}.png` at 18, 27, 36 and 54 px, which is the chip's 18 px icon at
  `Ui.S` scales 1, 1.5, 2 and 3. They're for anywhere an SVG can't be used. The game itself should use the SVGs.
- **Preview:** `out/chips_preview.png` shows all four in the chip at 3×.

## Integration (`Hud/TopBar.cs`)

The chip already has everything this needs. `Stat(row, "Weather", glyph: null, icon: "sun", …)` returns its `Value`
label and its `Line` `HBoxContainer`.

1. Keep the line it returns (today it's discarded with `.Value`), and add a muted second label to it, as the guests stat
   does for `/ 25`: Source Sans 3 SemiBold about 14, `Ui.BarMuted`.
2. Swap the disc's icon by name as the weather changes. Keep a reference to the `TextureRect` created in `Stat`, or
   rebuild it.
3. Text by state, using in-game clock times:

| Weather | Icon | Value | Muted suffix |
|---|---|---|---|
| Showers | `cloud-showers` | Showers | `till 3:40`: when this shower ends |
| Heavy showers | `cloud-heavy-showers` | Heavy showers | `till 4:15` |
| Downpour | `cloud-downpour` | Downpour | `till 4:30` |
| Dry now, a shower forecast | `sun-dry-spell`, or today's `sun` if it's hot | Dry spell | `rain in 20 min` |
| Raining, a dry spell forecast within 30 min | the rain icon | as above | `dry in 20 min` |
| Hot (Tier 1, unchanged) | `sun` | Hot | none |

The suffix is what lets the player plan: lay straw or put the marquee up before the next shower lands.
