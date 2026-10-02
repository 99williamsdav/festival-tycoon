# -*- coding: utf-8 -*-
"""Gate apron + gate sign tiers v1: palette and text-free board art.
Run with system Python (Pillow): python make_textures.py   -> writes ./tex/*.png
No lettering is baked except the tier-3 'WELCOME TO' plate; festival names are drawn at runtime
over each board's LetteringArea."""
from PIL import Image, ImageDraw, ImageFilter, ImageFont
import math, random, os
HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "tex"); os.makedirs(OUT, exist_ok=True)

# 96x8 palette, 12 swatches of 8 px (u = (8*slot+4)/96). Slots 0-5 reuse the field/track palette exactly.
PALETTE = ["6A7F49", "5E733F", "728751", "927451", "7D5F43", "4E4035",
           "7A5B3C", "4E3826", "2E4D3F", "C69E46", "A55A3A", "E8DEC4"]
pal = Image.new("RGB", (96, 8))
for i, h in enumerate(PALETTE):
    pal.paste(tuple(int(h[k:k + 2], 16) for k in (0, 2, 4)), (8 * i, 0, 8 * i + 8, 8))
pal.save(os.path.join(OUT, "gate_sign_palette.png"))

def sun(d, cx, cy, r, col, rays=12, w=None):
    w = w or max(4, r // 5)
    for k in range(rays):
        a = k * 2 * math.pi / rays
        d.line([(cx + r * 1.25 * math.cos(a), cy + r * 1.25 * math.sin(a)), (cx + r * 1.85 * math.cos(a), cy + r * 1.85 * math.sin(a))], fill=col, width=w)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=col)

# Tier 1: an old two-panel door laid on its side (2.5 x 1.0 m), flaking paint. Blank: names painted at runtime.
rng = random.Random(4); W, H = 1024, 410
im = Image.new("RGB", (W, H), (146, 178, 166)); d = ImageDraw.Draw(im)
for _ in range(200):
    x, y = rng.randrange(W), rng.randrange(H); d.ellipse([x, y, x + rng.randint(5, 30), y + rng.randint(2, 10)], fill=(168, 140, 104))
for x0, x1 in ((44, 468), (556, 980)):
    d.rectangle([x0, 44, x1, H - 44], outline=(110, 140, 128), width=8)
d.ellipse([W - 52, H // 2 - 16, W - 20, H // 2 + 16], fill=(120, 96, 50))
im.filter(ImageFilter.GaussianBlur(0.7)).save(os.path.join(OUT, "t1_door.png"))
# Tier 1 cardboard arrow (0.9 x 0.39 m): a painted arrow, no words.
W, H = 512, 222
im = Image.new("RGB", (W, H), (176, 140, 96)); d = ImageDraw.Draw(im)
for _ in range(30):
    x = rng.randrange(W); d.line([(x, 0), (x + rng.randint(-15, 15), H)], fill=(160, 126, 84), width=2)
d.rectangle([0, 0, 26, H], fill=(205, 196, 170))
pts = [(70, 92), (330, 86), (326, 46), (452, 112), (322, 180), (328, 140), (72, 136)]
d.polygon([(x + rng.uniform(-3, 3), y + rng.uniform(-3, 3)) for x, y in pts], fill=(36, 34, 32))
im.save(os.path.join(OUT, "t1_arrow.png"))

# Tier 2: painted timber board (3.3 x 1.01 m), teal border, sun motif on the left, blank lettering area on the right.
rng = random.Random(3); W, H = 1024, 314
im = Image.new("RGB", (W, H), (232, 222, 196)); d = ImageDraw.Draw(im)
for y in range(0, H, 4):
    c = 230 + rng.randint(-8, 6); d.line([(0, y), (W, y)], fill=(c, c - 10, c - 36), width=2)
for y in (104, 209):
    d.line([(0, y), (W, y)], fill=(150, 128, 95), width=3)
d.rounded_rectangle([14, 14, W - 14, H - 14], radius=17, outline=(46, 104, 98), width=10)
sun(d, 118, 157, 32, (222, 160, 52))
im.filter(ImageFilter.GaussianBlur(0.5)).save(os.path.join(OUT, "t2_board.png"))

# Tier 3: sign-written arch board (6.8 x 1.47 m): green, gold and cream rules, emblem roundel at each end, blank centre.
def emblem(size):
    e = Image.new("RGBA", (size, size), (0, 0, 0, 0)); d = ImageDraw.Draw(e); c = size / 2
    d.ellipse([4, 4, size - 4, size - 4], fill=(240, 228, 196, 255), outline=(198, 158, 70, 255), width=size // 22)
    d.ellipse([size * .12, size * .12, size * .88, size * .88], outline=(46, 77, 63, 255), width=size // 40)
    sun(d, c, c * 0.92, int(size * 0.13), (214, 150, 46, 255), rays=14, w=size // 40)
    d.polygon([(size * .25, size * .74), (c, size * .55), (size * .75, size * .74)], fill=(46, 104, 98, 255))
    d.rectangle([size * .3, size * .74, size * .7, size * .79], fill=(120, 80, 50, 255))
    for k in range(5):
        x = size * (.22 + k * .14); d.ellipse([x - size * .07, size * .76, x + size * .07, size * .86], fill=(84, 120, 64, 255))
    return e
W, H = 2048, 443
im = Image.new("RGB", (W, H), (46, 77, 63)); d = ImageDraw.Draw(im)
d.rounded_rectangle([15, 15, W - 15, H - 15], radius=26, outline=(198, 158, 70), width=10)
d.rounded_rectangle([38, 38, W - 38, H - 38], radius=17, outline=(232, 222, 196), width=3)
e = emblem(340)
for ex in (52, W - 52 - 340):
    im.paste(e, (ex, 52), e)
im.save(os.path.join(OUT, "t3_board.png"))
# Tier 3 'WELCOME TO' plate (2.7 x 0.54 m): this text stays baked.
W, H = 1024, 205
im = Image.new("RGB", (W, H), (232, 222, 196)); d = ImageDraw.Draw(im)
d.rounded_rectangle([9, 9, W - 9, H - 9], radius=24, outline=(46, 77, 63), width=9)
f = ImageFont.truetype("C:/Windows/Fonts/georgiab.ttf", 90); t = "WELCOME  TO"; w = d.textlength(t, font=f)
d.text((W / 2 - w / 2, 48), t, font=f, fill=(46, 77, 63))
im.save(os.path.join(OUT, "t3_plate.png"))
print("textures written to", OUT)
