# Three new HUD icons for the steward shift, in the game's Lucide style (24 grid, stroke 2, round caps, white stroke):
#   hand        the calm action (Lucide's own 'hand' outline, lucide-static, ISC)
#   radio       a walkie-talkie, for the radio strip
#   thermometer heat, for the needs bars (Lucide's own 'thermometer' outline, ISC)
#   python make_icons.py -> out/<name>.svg and out/png/<name>_<px>px_{white,gold}.png at 14, 18, 22, 26 px (x1) and 2x
# The others the HUD uses already ship in game/assets/ui/icons: droplet, heart-pulse, map-pin, music, beer, door-closed,
# trash-2, check, x, minus, shield, users, clock, triangle-alert.
import os, sys
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import hud_reference as H
OUT = os.path.join(HERE, "out")
NEW = {k: H.L.ICONS[k] for k in ("hand", "radio", "thermometer")}
for n, paths in NEW.items():
    body = "\n".join(f'  <path d="{p}" />' for p in paths)
    open(os.path.join(OUT, n + ".svg"), "w").write(
        "<!-- Festival Tycoon steward shift HUD icon, Lucide style" + (" (Lucide's own outline, lucide-static, ISC)" if n != "radio" else "") + ". -->\n"
        '<svg\n  xmlns="http://www.w3.org/2000/svg"\n  width="24"\n  height="24"\n  viewBox="0 0 24 24"\n  fill="none"\n  stroke="#ffffff"\n'
        '  stroke-width="2"\n  stroke-linecap="round"\n  stroke-linejoin="round"\n>\n' + body + "\n</svg>\n")
    for px in (14, 18, 22, 26, 28, 36, 44, 52):
        for tag, c in (("white", (255, 255, 255, 255)), ("gold", H.col("gold"))):
            H.L.draw_icon(n, px, c).save(os.path.join(OUT, "png", f"{n}_{px}px_{tag}.png"))
print("ok")
