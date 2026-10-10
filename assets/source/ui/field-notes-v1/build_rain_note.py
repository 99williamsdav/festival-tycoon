# The rain field note's pin and doodle, added after the first set (Tier 2's heavy rain). Same kit and sizes as
# build_field_notes.py; writes only the two new files, so the existing art isn't rebuilt.
#   python build_rain_note.py   -> out/pin_rain.png, out/doodle_rain.png
import os
from fn_kit import *

HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
p, tx, ty = pin(2, "rain")
p.save(os.path.join(OUT, "pin_rain.png"))
im = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
doodle(ImageDraw.Draw(im), "rain", 64, 64, 88, INK, 6, seed=1)
im.save(os.path.join(OUT, "doodle_rain.png"))
print("pin tip", round(tx), round(ty))
