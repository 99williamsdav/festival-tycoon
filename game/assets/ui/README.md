# HUD fonts and icons

Used by the HUD redesign (`game/Hud/Ui.cs`), downloaded 1 October 2026.

| Files | Source | Licence |
| --- | --- | --- |
| `fonts/ZillaSlab-SemiBold.ttf`, `fonts/ZillaSlab-Bold.ttf` | github.com/google/fonts `ofl/zillaslab` | SIL OFL 1.1 (`fonts/OFL-ZillaSlab.txt`) |
| `fonts/SourceSans3-Variable.ttf` (from `SourceSans3[wght].ttf`) | github.com/google/fonts `ofl/sourcesans3` | SIL OFL 1.1 (`fonts/OFL-SourceSans3.txt`) |
| `icons/*.svg` | lucide-static 0.460.0 (unpkg) | ISC (`icons/LICENSE.txt`) |

The icons' `stroke="currentColor"` is replaced with white so code can tint them, and they import at 2× scale with mipmaps. `fonts/SourceSans3-Regular.tres` pins the variable font at weight 400; it is the project's default GUI font.
