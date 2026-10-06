# "Pizza the Action" livery (second food trader):
#   python make_textures.py -> out/lwf_food_van_palette_pizza_v1.png (96 x 8) and out/lwf_food_van_name_panel_pizza_v1.png (1024 x 128)
from PIL import Image, ImageDraw, ImageFont
import os, math
HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
FD = "C:/Projects/festival-tycoon/game/assets/ui/fonts/"
def hx(h): return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))
def slab(s): return ImageFont.truetype(FD + "ZillaSlab-Bold.ttf", s)

# palette: the van's own 12-swatch strip with five livery slots changed (same slots as chip_block)
LIVERY = {0: "b5532e",   # body: wood-fired terracotta
          3: "5a2c1e",   # roof and skirts: dark roast brown
          4: "f1e3c4",   # trim and door: cream
          11: "2f7d3e",  # fascia frame: basil green
          1: "f6f1e6"}   # fascia panel (behind the name decal)
pal = Image.open("C:/Projects/festival-tycoon/game/assets/environment/lwf_food_van_chassis_v1_food_van_palette.png").convert("RGBA")
d = ImageDraw.Draw(pal)
for slot, c in LIVERY.items(): d.rectangle([slot * 8, 0, slot * 8 + 7, pal.height - 1], fill=hx(c) + (255,))
pal.save(os.path.join(OUT, "lwf_food_van_palette_pizza_v1.png"))

# name panel: cream board, tricolour strip, tomato-red slab lettering with a crust-brown shadow, basil leaves (Zilla Slab, OFL)
W, H = 1024, 128; t = "Pizza the Action"
im = Image.new("RGBA", (W, H), hx("f6f1e6") + (255,)); d = ImageDraw.Draw(im)
third = W / 3
for i, c in enumerate(("2f7d3e", "fbf7ee", "c8352b")): d.rectangle([i * third, H - 13, (i + 1) * third, H], fill=hx(c))
d.line([0, H - 14, W, H - 14], fill=hx("d9cdb0"), width=2)
s = 88
while d.textlength(t, font=slab(s)) > 820: s -= 2
f = slab(s)
for k in (5, 4, 3, 2, 1): d.text((W / 2 + k, 56 + k), t, font=f, fill=hx("6b3a22"), anchor="mm")
d.text((W / 2, 56), t, font=f, fill=hx("c8352b"), anchor="mm")
def leaf(cx, cy, ang):
    pts = []
    for j in range(21):
        u = j / 20; r = 22 * math.sin(math.pi * u)
        pts.append((u * 52 - 26, -r * 0.55))
    pts += [(x, -y) for x, y in reversed(pts)]
    a = math.radians(ang); pts = [(cx + x * math.cos(a) - y * math.sin(a), cy + x * math.sin(a) + y * math.cos(a)) for x, y in pts]
    d.polygon(pts, fill=hx("2f7d3e")); d.line([pts[0], pts[20]], fill=hx("1f5a2b"), width=2)
leaf(52, 56, -30); leaf(W - 52, 56, 210)
im.save(os.path.join(OUT, "lwf_food_van_name_panel_pizza_v1.png"))
print("ok")
