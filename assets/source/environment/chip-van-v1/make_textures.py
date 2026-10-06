# Chip Off The Old Block livery (approved from Documents/Festival Tycoon concepts/chip-vans/):
#   python make_textures.py -> out/lwf_food_van_palette_chip_block_v1.png (96 x 8) and out/lwf_food_van_name_panel_chip_block_v1.png (1024 x 128)
from PIL import Image, ImageDraw, ImageFont
import os
HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
FD = "C:/Projects/festival-tycoon/game/assets/ui/fonts/"
def hx(h): return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))
def black(s):
    f = ImageFont.truetype(FD + "SourceSans3-Variable.ttf", s); f.set_variation_by_name("Black"); return f

# palette: the van's own 12-swatch strip with five livery slots changed
LIVERY = {0: "4e9cc4",   # body
          3: "2f6f93",   # roof and skirts
          4: "f6f1e6",   # trim and door
          11: "f6f1e6",  # fascia frame
          1: "f6f1e6"}   # fascia panel (behind the name decal)
pal = Image.open("C:/Projects/festival-tycoon/game/assets/environment/lwf_food_van_chassis_v1_food_van_palette.png").convert("RGBA")
d = ImageDraw.Draw(pal)
for slot, c in LIVERY.items(): d.rectangle([slot * 8, 0, slot * 8 + 7, pal.height - 1], fill=hx(c) + (255,))
pal.save(os.path.join(OUT, "lwf_food_van_palette_chip_block_v1.png"))

# name panel: white board, blue chequer strip, extruded block capitals (Source Sans 3 Black, OFL)
W, H = 1024, 128; t = "CHIP OFF THE OLD BLOCK"
im = Image.new("RGBA", (W, H), hx("f6f1e6") + (255,)); d = ImageDraw.Draw(im)
for i in range(0, W, 24):
    d.rectangle([i, 0, i + 11, 7], fill=hx("2f6f93")); d.rectangle([i + 12, 8, i + 23, 15], fill=hx("2f6f93"))
s = 84
while d.textlength(t, font=black(s)) > 940: s -= 2
f = black(s)
for k in range(7, 0, -1): d.text((W / 2 + k, 72 + k), t, font=f, fill=hx("1b4a66"), anchor="mm")
d.text((W / 2, 72), t, font=f, fill=hx("4e9cc4"), anchor="mm", stroke_width=2, stroke_fill=hx("1b4a66"))
im.save(os.path.join(OUT, "lwf_food_van_name_panel_chip_block_v1.png"))
print("ok")
