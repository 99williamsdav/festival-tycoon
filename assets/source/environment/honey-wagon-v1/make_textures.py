# Dav's Lav-Sucker textures (approved livery B "Cheeky", sign A):
#   python make_textures.py -> out/lwf_honey_wagon_name_board_v1.png, lwf_honey_wagon_rear_face_v1.png,
#                              lwf_honey_wagon_rear_plate_v1.png, lwf_portaloo_out_of_order_sign_v1.png
# Name board, rear face and rear plate use the game's OFL fonts (Zilla Slab, Source Sans 3). The sign's marker lettering is
# drawn here stroke by stroke (no font), so it can ship.
from PIL import Image, ImageDraw, ImageFont
import os, math, random
HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
FD = "C:/Projects/festival-tycoon/game/assets/ui/fonts/"
def slab(s): return ImageFont.truetype(FD + "ZillaSlab-Bold.ttf", s)
def sans(s, w="Black"):
    f = ImageFont.truetype(FD + "SourceSans3-Variable.ttf", s); f.set_variation_by_name(w); return f
def hx(h, a=255): return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (a,)
INK, WHITE_TANK, GREEN, LIME = hx("1d1f21"), hx("f4f2ea"), hx("3d7f2a"), hx("8bbd3c")

# ---- name board (1024 x 300): Dav's Lav-Sucker, jaunty, "No jobbie too big"
W, H = 1024, 300
im = Image.new("RGBA", (W, H), WHITE_TANK); d = ImageDraw.Draw(im)
d.rectangle([0, 0, W, 14], fill=LIME); d.rectangle([0, H - 14, W, H], fill=LIME)
t = "Dav's Lav-Sucker"; s = 150
while d.textlength(t, font=slab(s)) > 900: s -= 2
lay = Image.new("RGBA", (W, H), (0, 0, 0, 0)); ld = ImageDraw.Draw(lay)
ld.text((W / 2 + 4, 116), t, font=slab(s), fill=LIME, anchor="mm"); ld.text((W / 2, 112), t, font=slab(s), fill=GREEN, anchor="mm")
lay = lay.rotate(3, resample=Image.BICUBIC, center=(W / 2, 112)); im.alpha_composite(lay)
d.text((W / 2, H - 52), "No jobbie too big", font=sans(46, "Bold"), fill=GREEN, anchor="mm")
im.convert("RGB").save(os.path.join(OUT, "lwf_honey_wagon_name_board_v1.png"))

# ---- rear face (512 x 512), planar on the rear dish: u = 0.5 + x / 1.6, v = 0.5 + (y - 1.75) / 1.6 (truck local, Godot)
# The rear valve is the nose at (0, 1.45) -> pixel (256, 352). Our own friendly face: no hat, no Henry livery.
S = 512; im = Image.new("RGBA", (S, S), WHITE_TANK); d = ImageDraw.Draw(im)
def px(x, y): return (S * (0.5 + x / 1.6), S * (1 - (0.5 + (y - 1.75) / 1.6)))
for sx in (-1, 1):
    cx, cy = px(sx * 0.26, 1.98)
    d.ellipse([cx - 66, cy - 76, cx + 66, cy + 76], fill=hx("ffffff"), outline=INK, width=12)              # eyes
    d.ellipse([cx - 30 + sx * 8, cy - 18, cx + 30 + sx * 8, cy + 42], fill=INK)                             # pupils, looking slightly in
    d.ellipse([cx - 12 + sx * 8, cy - 6, cx + 6 + sx * 8, cy + 12], fill=hx("ffffff"))                      # highlights
    d.arc([cx - 70, cy - 132, cx + 70, cy - 40], 205, 335, fill=GREEN, width=16)                            # eyebrows (raised, cheerful)
    ck = px(sx * 0.5, 1.42); d.ellipse([ck[0] - 46, ck[1] - 26, ck[0] + 46, ck[1] + 26], fill=hx("eba3ac"))  # rosy cheeks
sm = [px(-0.42, 1.36), px(0.42, 1.36)]
d.arc([sm[0][0], sm[0][1] - 120, sm[1][0], sm[1][1] + 90], 20, 160, fill=INK, width=16)                     # the big smile
for sx in (-1, 1):
    e = px(sx * 0.41, 1.40); d.line([e[0] - sx * 6, e[1] - 16, e[0] + sx * 10, e[1] + 8], fill=INK, width=14)  # smile corners
im.convert("RGB").save(os.path.join(OUT, "lwf_honey_wagon_rear_face_v1.png"))

# ---- rear plate (1536 x 192, 1.6 x 0.2 m): the easter-egg sign on the left, HOW'S MY SMELLING? sticker on the right
PW, PH = 1536, 192
im = Image.new("RGBA", (PW, PH), hx("f4f2ea")); d = ImageDraw.Draw(im)
d.rectangle([0, 0, 1130, PH - 1], fill=hx("ffffff"), outline=INK, width=6)
for i, line in enumerate(("IF YOU CAN SMELL SHIT", "YOU'RE DRIVING TOO CLOSE")):
    f = sans(68, "Black"); d.text((565, 52 + i * 84), line, font=f, fill=INK, anchor="mm")
d.rectangle([1160, 10, PW - 10, PH - 10], fill=hx("f6d343"), outline=INK, width=6)
d.text((1348, 58), "HOW'S MY", font=sans(46, "Black"), fill=INK, anchor="mm")
d.text((1348, 108), "SMELLING?", font=sans(50, "Black"), fill=INK, anchor="mm")
d.text((1348, 152), "0800 POO POO", font=sans(26, "Bold"), fill=INK, anchor="mm")
im.convert("RGB").save(os.path.join(OUT, "lwf_honey_wagon_rear_plate_v1.png"))

# ---- OUT OF ORDER sign A (900 x 650): kraft card, marker strokes, sad face with a tear, duct tape
SW, SH = 900, 650; rng = random.Random(4)
im = Image.new("RGBA", (SW, SH), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
pts = [(16, 14), (SW - 14, 22), (SW - 12, SH - 16), (12, SH - 12)]
d.polygon(pts, fill=hx("c9a46c")); d.line(pts + [pts[0]], fill=hx("a8854f"), width=6)


def marker(strokes, ox, oy, sc, w=26):
    """hand-drawn letters: each stroke a polyline in a 0..1 letter box, with a little wobble"""
    for st in strokes:
        p = [(ox + (x + rng.uniform(-0.02, 0.02)) * sc, oy + (y + rng.uniform(-0.02, 0.02)) * sc) for x, y in st]
        d.line(p, fill=INK, width=w, joint="curve")
        for q in (p[0], p[-1]): d.ellipse([q[0] - w / 2, q[1] - w / 2, q[0] + w / 2, q[1] + w / 2], fill=INK)


def ring(cx, cy, rx, ry, n=14, a0=0, a1=360): return [(cx + rx * math.cos(math.radians(a0 + (a1 - a0) * i / n)), cy + ry * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]
L = {"O": [ring(0.42, 0.5, 0.38, 0.48)], "U": [[(0.08, 0.02), (0.08, 0.7)] + ring(0.42, 0.68, 0.34, 0.3, 8, 180, 0)[1:] + [(0.76, 0.02)]],
     "T": [[(0.02, 0.04), (0.82, 0.02)], [(0.42, 0.03), (0.44, 1.0)]], "F": [[(0.72, 0.03), (0.1, 0.04), (0.1, 1.0)], [(0.1, 0.5), (0.58, 0.5)]],
     "R": [[(0.1, 1.0), (0.1, 0.04), (0.5, 0.04)] + ring(0.5, 0.27, 0.26, 0.23, 8, -90, 90)[1:] + [(0.1, 0.5)], [(0.36, 0.5), (0.78, 1.0)]],
     "D": [[(0.1, 0.04), (0.1, 1.0)], [(0.1, 0.04)] + ring(0.32, 0.52, 0.44, 0.48, 10, -90, 90) + [(0.1, 1.0)]],
     "E": [[(0.72, 0.04), (0.1, 0.04), (0.1, 1.0), (0.74, 1.0)], [(0.1, 0.5), (0.6, 0.5)]], " ": []}
def word(text, ox, oy, sc, gap):
    x = ox
    for ch in text: marker(L[ch], x, oy, sc); x += sc * (0.95 if ch != " " else 0.5) + gap
def width(text, sc, gap): return sum(sc * (0.95 if ch != " " else 0.5) + gap for ch in text) - gap
for text, oy, sc in (("OUT OF", 62, 140), ("ORDER", 232, 150)):
    word(text, (SW - width(text, sc, 14)) / 2, oy, sc, 14)
cx, cy, r = SW / 2, 518, 92
d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=INK, width=14)
for sx in (-1, 1): d.ellipse([cx + sx * 35 - 13, cy - 42, cx + sx * 35 + 13, cy - 16], fill=INK)
d.arc([cx - 48, cy + 16, cx + 48, cy + 80], 200, 340, fill=INK, width=14)
d.line([cx + 40, cy - 10, cx + 44, cy + 22], fill=hx("3a7fc1"), width=12)                                    # the tear
for (x, y), a in zip(pts, (-35, 35, -35, 35)):
    tp = Image.new("RGBA", (160, 50), hx("9a9da2", 235)).rotate(a, expand=True)
    im.alpha_composite(tp, (int(x - tp.width / 2), int(y - tp.height / 2)))
im.save(os.path.join(OUT, "lwf_portaloo_out_of_order_sign_v1.png"))
print("ok")
