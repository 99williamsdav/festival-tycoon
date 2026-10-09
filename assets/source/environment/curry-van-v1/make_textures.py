# "Korma Chameleon" curry van (third food trader, Tier 2): The Naan Stop's aubergine and gold livery with the Korma Chameleon name.
#   python make_textures.py -> out/lwf_food_van_palette_korma_v1.png   (96 x 8)
#                              out/lwf_food_van_name_panel_korma_v1.png (1024 x 128)
#                              out/lwf_food_van_sign_curry_v1.png       (640 x 640, roof sign picture, hard alpha)
#                              out/lwf_food_van_menu_board_korma_v1.png (512 x 720, chalk A-board)
# Fonts: Zilla Slab and Source Sans 3 from game/assets/ui/fonts (OFL). Music notes are drawn, not font glyphs.
from PIL import Image, ImageDraw, ImageFont, ImageFilter
import os, math
HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
FD = "C:/Projects/festival-tycoon/game/assets/ui/fonts/"
def hx(h, a=255): return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (a,)
def slab(s): return ImageFont.truetype(FD + "ZillaSlab-Bold.ttf", s)
def sans(s, w="Black"):
    f = ImageFont.truetype(FD + "SourceSans3-Variable.ttf", s); f.set_variation_by_name(w); return f

AUBERGINE, PLUM, GOLD, DARK, GOLD_INK, GOLD_DEEP = "5b2a4f", "3a1a33", "d9a43b", "2a1426", "e8bf5a", "9c6a2a"

# ---- palette: the van's own 12-swatch strip with the five livery slots changed (as chip_block and pizza)
LIVERY = {0: AUBERGINE,   # body
          3: PLUM,        # roof and skirts
          4: GOLD,        # trim and door
          11: GOLD,       # fascia frame
          1: DARK}        # fascia panel (behind the name decal)
pal = Image.open("C:/Projects/festival-tycoon/game/assets/environment/lwf_food_van_chassis_v1_food_van_palette.png").convert("RGBA")
d = ImageDraw.Draw(pal)
for slot, c in LIVERY.items(): d.rectangle([slot * 8, 0, slot * 8 + 7, pal.height - 1], fill=hx(c))
pal.save(os.path.join(OUT, "lwf_food_van_palette_korma_v1.png"))


def note(d, x, y, s, col, double=False):
    """a quaver (or a pair of beamed quavers) with its head's centre at (x, y)"""
    heads = [(x, y)] + ([(x + s * 1.3, y - s * 0.3)] if double else [])
    for hx_, hy in heads:
        d.ellipse([hx_ - s * 0.5, hy - s * 0.36, hx_ + s * 0.5, hy + s * 0.36], fill=col)
        d.line([hx_ + s * 0.42, hy, hx_ + s * 0.42, hy - s * 1.9], fill=col, width=max(2, int(s * 0.16)))
    if double:
        x0, x1 = x + s * 0.42, x + s * 1.72
        d.polygon([(x0, y - s * 1.9), (x1, y - s * 2.2), (x1, y - s * 1.9), (x0, y - s * 1.6)], fill=col)
    else:
        d.line([x + s * 0.42, y - s * 1.9, x + s * 1.0, y - s * 1.2], fill=col, width=max(2, int(s * 0.16)))


# ---- name panel: dark plum board, gold line frame, palace arches at both ends, gold slab capitals, music-pun strapline
W, H = 1024, 128; K = 4
im = Image.new("RGBA", (W * K, H * K), hx(DARK)); d = ImageDraw.Draw(im)
d.rectangle([8 * K, 8 * K, (W - 8) * K, (H - 8) * K], outline=hx(GOLD_INK), width=3 * K)
d.rectangle([14 * K, 14 * K, (W - 14) * K, (H - 14) * K], outline=hx(GOLD_DEEP), width=1 * K)
for x in (44, W - 44):                                                   # onion-dome arches
    x *= K; top, mid, foot, hw = 22 * K, 54 * K, 102 * K, 22 * K
    pts = []
    for j in range(25):
        t = j / 24; a = math.pi * t
        px = x - hw * math.cos(a); py = mid - (mid - top - 8 * K) * math.sin(a) ** 1.4
        pts.append((px, py))
    d.line(pts, fill=hx(GOLD_INK), width=4 * K, joint="curve")
    d.line([(x, top), (x, top + 10 * K)], fill=hx(GOLD_INK), width=3 * K)
    d.line([(x - hw, mid), (x - hw, foot)], fill=hx(GOLD_INK), width=4 * K); d.line([(x + hw, mid), (x + hw, foot)], fill=hx(GOLD_INK), width=4 * K)
    d.ellipse([x - 5 * K, 64 * K, x + 5 * K, 74 * K], fill=hx(GOLD_DEEP))
t = "KORMA CHAMELEON"; s = 74 * K
while d.textlength(t, font=slab(s)) > 790 * K: s -= 2
for k in (3, 2, 1): d.text((W * K / 2 + k * K, 50 * K + k * K), t, font=slab(s), fill=hx("140910"), anchor="mm")
d.text((W * K / 2, 50 * K), t, font=slab(s), fill=hx(GOLD_INK), anchor="mm")
tag = "Karma karma karma karma korma"; f = sans(24 * K, "Bold")
d.text((W * K / 2, 104 * K), tag, font=f, fill=hx(GOLD), anchor="mm")
tw = d.textlength(tag, font=f) / 2
note(d, W * K / 2 - tw - 30 * K, 110 * K, 9 * K, hx(GOLD), double=True)
note(d, W * K / 2 + tw + 18 * K, 110 * K, 9 * K, hx(GOLD))
im.resize((W, H), Image.LANCZOS).convert("RGB").save(os.path.join(OUT, "lwf_food_van_name_panel_korma_v1.png"))

# ---- roof sign picture: a bowl of curry with a naan dipped in and steam, sticker style like the chips and pizza signs
S = 640 * K
art = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(art)
cx, cy = S * 0.5, S * 0.60
d.ellipse([cx - S * 0.36, cy - S * 0.13, cx + S * 0.36, cy + S * 0.13], fill=hx("b5651d"))                       # curry surface
for i in range(14):                                                                                           # chunks
    a = i * 2.4; r = S * (0.05 + 0.25 * ((i * 37) % 10) / 10)
    x, y = cx + r * math.cos(a), cy + r * 0.32 * math.sin(a); d.ellipse([x - 26 * K, y - 16 * K, x + 26 * K, y + 16 * K], fill=hx("e08a3a"))
d.ellipse([cx - S * 0.06, cy - S * 0.04, cx + S * 0.10, cy + S * 0.02], fill=hx("f4ede0"))                    # yoghurt swirl
d.ellipse([cx + S * 0.02, cy - S * 0.07, cx + S * 0.07, cy - S * 0.045], fill=hx("3d7f2a"))                     # coriander
bowl = [cx - S * 0.40, cy - S * 0.42, cx + S * 0.40, cy + S * 0.34]
d.pieslice(bowl, 0, 180, fill=hx("e8ddc4")); d.pieslice(bowl, 0, 180, outline=hx(AUBERGINE), width=10 * K)     # bowl in the van's colours
d.rectangle([cx - S * 0.40, cy - 4 * K, cx + S * 0.40, cy + 4 * K], fill=hx(AUBERGINE))
for x in range(-3, 4): d.ellipse([cx + x * S * 0.09 - 9 * K, cy + S * 0.18 - 9 * K, cx + x * S * 0.09 + 9 * K, cy + S * 0.18 + 9 * K], fill=hx(GOLD))
nl = Image.new("RGBA", (S, S), (0, 0, 0, 0)); nd = ImageDraw.Draw(nl); nx, ny = S * 0.5, S * 0.5               # the naan, leaning in
nd.ellipse([nx - S * 0.2, ny - S * 0.11, nx + S * 0.2, ny + S * 0.11], fill=hx("e7b864"), outline=hx(GOLD_DEEP), width=6 * K)
for j in range(7):
    x = nx - S * 0.13 + j * S * 0.045; y = ny - S * 0.04 + ((j * 13) % 5) * S * 0.018; nd.ellipse([x - 10 * K, y - 7 * K, x + 10 * K, y + 7 * K], fill=hx("a0682a"))
art.alpha_composite(nl.rotate(32, resample=Image.BICUBIC, center=(nx, ny)), (int(S * 0.2), int(-S * 0.13)))
for i, x in enumerate((cx - S * 0.2, cx - S * 0.06)):                                                         # steam
    d.line([(x + S * 0.03 * math.sin(t * 2.4 + i), cy - S * 0.16 - t * S * 0.11) for t in [k / 6 for k in range(7)]], fill=hx("ffffff"), width=16 * K, joint="curve")
def grow(m, r): return m.filter(ImageFilter.MaxFilter(r * 2 + 1)) if r < 12 else grow(m.filter(ImageFilter.MaxFilter(23)), r - 11)
a = art.split()[3].point(lambda v: 255 if v > 40 else 0); b = grow(a, 44); o = grow(b, 14)
sig = Image.new("RGBA", art.size, (0, 0, 0, 0)); sig.paste(hx("1d2a25"), (0, 0), o); sig.paste(hx("f6f1e6"), (0, 0), b); sig.alpha_composite(art)
sig = sig.resize((640, 640), Image.LANCZOS); r_, g_, b_, a_ = sig.split()
Image.merge("RGBA", (r_, g_, b_, a_.point(lambda v: 255 if v >= 128 else 0))).save(os.path.join(OUT, "lwf_food_van_sign_curry_v1.png"))

# ---- chalk A-board: today's "setlist" of music-pun dishes
MW, MH = 512, 720
im = Image.new("RGB", (MW * 2, MH * 2), (34, 40, 38)); d = ImageDraw.Draw(im); Z = 2
CHALK, YEL, PINK = (240, 236, 220), (246, 201, 69), (232, 120, 160)
d.rectangle([0, 0, MW * Z - 1, MH * Z - 1], outline=(138, 106, 69), width=22 * Z)
d.text((MW, 78 * Z), "TODAY'S SETLIST", font=slab(50 * Z), fill=CHALK, anchor="mm")
d.line([(70 * Z, 114 * Z), (MW * Z - 70 * Z, 114 * Z)], fill=CHALK, width=2 * Z)
DISHES = (("Smells Like Tikka Spirit", "£6"), ("Tikka Massala Massive", "£6"), ("Korma Chameleon", "£5"),
          ("Bhaji Ballad", "£3"), ("Naan Stop Party", "£1"))
for i, (t, p) in enumerate(DISHES):
    yy = (172 + i * 82) * Z
    s = 32 * Z
    while d.textlength(t, font=sans(s, "SemiBold")) > 330 * Z: s -= 2
    d.text((52 * Z, yy), t, font=sans(s, "SemiBold"), fill=CHALK, anchor="lm"); d.text((MW * Z - 52 * Z, yy), p, font=sans(32 * Z, "Bold"), fill=YEL, anchor="rm")
d.text((MW, 600 * Z), "Hold the line!", font=sans(40 * Z, "Bold"), fill=PINK, anchor="mm")
d.text((MW, 646 * Z), "it's worth the wait", font=sans(30 * Z, "SemiBold"), fill=PINK, anchor="mm")
note(d, 108 * Z, 606 * Z, 11 * Z, PINK, double=True); note(d, MW * Z - 120 * Z, 606 * Z, 11 * Z, PINK)
im.resize((MW, MH), Image.LANCZOS).save(os.path.join(OUT, "lwf_food_van_menu_board_korma_v1.png"))
print("ok")
