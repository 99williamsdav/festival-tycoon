# Rain forecast icons for the top bar's weather chip (approved heavy-rain board, chip B).
#   python make_icons.py -> out/<name>.svg (white stroke, like game/assets/ui/icons), out/png/<name>_<px>px_{white,gold}.png,
#                           out/chips_preview.png (the chip as it will look, at 3x)
# Lucide style: 24 grid, stroke 2, round caps and joins. The cloud outline is Lucide's own, from cloud-rain.svg (lucide-static
# v0.460.0, ISC), which the game already ships.
import os, sys
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import lucide_raster as L
OUT = os.path.join(HERE, "out"); os.makedirs(os.path.join(OUT, "png"), exist_ok=True)
CLOUD = L.CLOUD
ICONS = {
    "cloud-showers": [CLOUD, "M8 15v1", "M8 19v1", "M12 17v1", "M12 21v1", "M16 15v1", "M16 19v1"],
    "cloud-heavy-showers": [CLOUD, "M9 13l-2 8", "M13 14l-2 8", "M17 13l-2 8"],
    "cloud-downpour": [CLOUD, "M7.5 13l-2 8", "M11 13l-2.5 10", "M14.5 13l-2.5 10", "M18 13l-2 8"],
    "sun-dry-spell": ["M12 2v6", "m8.5 5 3.5-3.5L15.5 5", "M4.22 10.22l1.42 1.42", "M2 18h2", "M20 18h2", "M19.78 10.22l-1.42 1.42",
                      "M22 22H2", "M16 18a4 4 0 0 0-8 0"],
}
L.ICONS.update(ICONS)
for n, paths in ICONS.items():
    body = "\n".join(f'  <path d="{p}" />' for p in paths)
    open(os.path.join(OUT, n + ".svg"), "w").write(
        "<!-- Festival Tycoon rain forecast icon, Lucide style. Cloud outline from lucide-static v0.460.0 (ISC). -->\n"
        '<svg\n  xmlns="http://www.w3.org/2000/svg"\n  width="24"\n  height="24"\n  viewBox="0 0 24 24"\n  fill="none"\n  stroke="#ffffff"\n'
        '  stroke-width="2"\n  stroke-linecap="round"\n  stroke-linejoin="round"\n>\n' + body + "\n</svg>\n")
    for px in (18, 27, 36, 54):
        for tag, col in (("white", (255, 255, 255, 255)), ("gold", L.GOLD)):
            L.draw_icon(n, px, col).save(os.path.join(OUT, "png", f"{n}_{px}px_{tag}.png"))
chips = [L.chip("cloud-showers", "Showers", "till 3:40"), L.chip("cloud-heavy-showers", "Heavy showers", "till 4:15"),
         L.chip("cloud-downpour", "Downpour", "till 4:30"), L.chip("sun-dry-spell", "Dry spell", "in 20 min")]
from PIL import Image
sheet = Image.new("RGBA", (len(chips) * 340 * 3, 60 * 3), L.BAR)
for i, c in enumerate(chips): sheet.alpha_composite(c, (i * 340 * 3, 0))
sheet.save(os.path.join(OUT, "chips_preview.png")); print("ok")
