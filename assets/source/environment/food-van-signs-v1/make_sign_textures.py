# Roof sign pictures for the food vans (cut-out "sticker" boards):
#   python make_sign_textures.py -> out/lwf_food_van_sign_chips_v1.png (512 x 640) and out/lwf_food_van_sign_pizza_v1.png (640 x 640)
# Drawn at 4x and reduced. Alpha is a hard cut-out (the GLB uses alpha clip), with a cream border and an ink outline so the
# shape holds against grass, sky and the van roof at zoom 62.
from PIL import Image, ImageDraw, ImageFilter, ImageChops
import os, math, random
HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
def hx(h, a=255): return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (a,)
INK, CREAM = hx("1d2a25"), hx("f6f1e6")
K = 4


def sticker(art, border, outline):
    """cream border and ink outline around the art's silhouette, then a hard alpha"""
    a = art.split()[3].point(lambda v: 255 if v > 40 else 0)
    grow = lambda m, r: m.filter(ImageFilter.MaxFilter(r * 2 + 1)) if r < 12 else grow(m.filter(ImageFilter.MaxFilter(23)), r - 11)
    b = grow(a, border); o = grow(b, outline)
    out = Image.new("RGBA", art.size, (0, 0, 0, 0))
    out.paste(INK, (0, 0), o); out.paste(CREAM, (0, 0), b); out.alpha_composite(art)
    return out


def finish(im, size, name):
    im = im.resize(size, Image.LANCZOS)
    r, g, b, a = im.split(); a = a.point(lambda v: 255 if v >= 128 else 0)
    Image.merge("RGBA", (r, g, b, a)).save(os.path.join(OUT, name))


# ---------------------------------------------------------------- chips: a striped paper cone of chips with a wooden fork
W, H = 512 * K, 640 * K
art = Image.new("RGBA", (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(art)
cx = W / 2; top = H * 0.40; tip = H * 0.93; hw = W * 0.31
rng = random.Random(4)
chips = []
for i in range(11):                                    # fanned chips behind the cone's mouth
    x = cx + (i - 5) * W * 0.040 + rng.uniform(-10, 10) * K / 4; ang = math.radians((i - 5) * 4.5 + rng.uniform(-3, 3))
    L = H * rng.uniform(0.22, 0.30); w = W * 0.066
    chips.append((x, ang, L, w))
for x, ang, L, w in sorted(chips, key=lambda c: -c[2]):
    bx, by = x, top + H * 0.06; tx, ty = bx + L * math.sin(ang), by - L * math.cos(ang)
    nx, ny = math.cos(ang) * w / 2, math.sin(ang) * w / 2
    poly = [(bx - nx, by - ny), (tx - nx, ty - ny), (tx + nx, ty + ny), (bx + nx, by + ny)]
    d.polygon(poly, fill=hx("e8b84a"), outline=hx("a8661c"), width=3 * K)
    d.line([(tx - nx * 0.9, ty - ny * 0.9), (tx + nx * 0.9, ty + ny * 0.9)], fill=hx("c98a2a"), width=4 * K)   # fried tips
fx, fy = cx + W * 0.13, top - H * 0.24                 # little wooden fork stuck in
d.line([(fx, fy), (fx - W * 0.05, top + H * 0.02)], fill=hx("c9a06a"), width=9 * K)
for k in (-1, 0, 1): d.line([(fx + k * 9 * K, fy), (fx + k * 9 * K + W * 0.008, fy - H * 0.05)], fill=hx("c9a06a"), width=5 * K)
cone = [(cx - hw, top), (cx + hw, top), (cx, tip)]
lay = Image.new("RGBA", (W, H), (0, 0, 0, 0)); ld = ImageDraw.Draw(lay)
ld.polygon(cone, fill=hx("f6f1e6"))
stripes = Image.new("RGBA", (W, H), (0, 0, 0, 0)); sd = ImageDraw.Draw(stripes)
for k in range(-12, 14):                               # diagonal red stripes, clipped to the cone
    x0 = cx + k * W * 0.085
    sd.polygon([(x0, top - 10), (x0 + W * 0.045, top - 10), (x0 - W * 0.30, tip + 10), (x0 - W * 0.345, tip + 10)], fill=hx("c8352b"))
lay.paste(stripes, (0, 0), ImageChops.multiply(stripes.split()[3], lay.split()[3]))
ImageDraw.Draw(lay).polygon(cone, outline=hx("7a1f17"), width=5 * K)
ImageDraw.Draw(lay).rectangle([cx - hw - 6 * K, top - 4 * K, cx + hw + 6 * K, top + 22 * K], fill=hx("f6f1e6"), outline=hx("7a1f17"), width=4 * K)   # rolled rim
art.alpha_composite(lay)
finish(sticker(art, 22 * K // 2, 7 * K // 2), (512, 640), "lwf_food_van_sign_chips_v1.png")

# ---------------------------------------------------------------- pizza: a whole pizza with one slice pulled out
W = H = 640 * K
art = Image.new("RGBA", (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(art)
c = (W * 0.48, H * 0.50); R = W * 0.40; gap = 0.0
def pie(dc, cc, r, a0, a1, fill, outline=None, width=0):
    dc.pieslice([cc[0] - r, cc[1] - r, cc[0] + r, cc[1] + r], a0, a1, fill=fill, outline=outline, width=width)
def toppings(dc, cc, r, a0, a1, seed):
    rg = random.Random(seed)
    def inside(px, py):
        dx, dy = px - cc[0], py - cc[1]; rr = math.hypot(dx, dy); a = math.degrees(math.atan2(dy, dx)) % 360
        lo, hi = (a0 + 6) % 360, (a1 - 6) % 360
        ok = (lo <= a <= hi) if lo < hi else (a >= lo or a <= hi)
        return ok and rr < r * 0.80
    for _ in range(40):                                # mozzarella
        px, py = cc[0] + rg.uniform(-r, r), cc[1] + rg.uniform(-r, r)
        if inside(px, py): rr = rg.uniform(0.07, 0.11) * r; dc.ellipse([px - rr, py - rr * 0.8, px + rr, py + rr * 0.8], fill=hx("f6e3a0"))
    for _ in range(26):                                # pepperoni
        px, py = cc[0] + rg.uniform(-r, r), cc[1] + rg.uniform(-r, r)
        if inside(px, py): rr = 0.09 * r; dc.ellipse([px - rr, py - rr, px + rr, py + rr], fill=hx("a32a1f"), outline=hx("7a1f17"), width=2 * K)
    for _ in range(9):                                 # basil
        px, py = cc[0] + rg.uniform(-r, r), cc[1] + rg.uniform(-r, r)
        if inside(px, py):
            a = rg.uniform(0, math.pi); L = 0.10 * r
            dc.ellipse([px - L, py - L * 0.5, px + L, py + L * 0.5], fill=hx("2f7d3e"))
A0, A1 = 300, 345                                      # the pulled slice
off = (math.cos(math.radians((A0 + A1) / 2)) * R * 0.16, math.sin(math.radians((A0 + A1) / 2)) * R * 0.16)
for (cc, a0, a1, seed) in ((c, A1, A0 + 360, 1), ((c[0] + off[0], c[1] + off[1]), A0, A1, 2)):
    pie(d, cc, R, a0, a1, hx("d79a52"), hx("8a5426"), 5 * K)        # crust
    pie(d, cc, R * 0.84, a0, a1, hx("c8352b"))                       # tomato
    toppings(d, cc, R, a0, a1, seed)
    for a in (a0, a1):                                               # crust edge lines on the cut
        d.line([cc, (cc[0] + R * math.cos(math.radians(a)), cc[1] + R * math.sin(math.radians(a)))], fill=hx("8a5426"), width=5 * K)
finish(sticker(art, 22 * K // 2, 7 * K // 2), (640, 640), "lwf_food_van_sign_pizza_v1.png")
print("ok")
