# End-of-shift report card, option B (approved): the shift sheet on Control's clipboard with a rubber-stamped grade.
#   python make_report_art.py -> out/
#     lwf_report_clipboard_v1.png     1040 x 1388  the hardboard (2x of 520 x 694 mockup units), transparent outside its rounded corners
#     lwf_report_clip_v1.png           300 x 140   the steel clip (2x of 150 x 70), drawn over the sheet's top edge
#     lwf_report_sheet_v1.png          952 x 1268  the paper (2x of 476 x 634): PaperBright with a faint grain; the UI draws text on it
#     lwf_report_stamp_<grade>_v1.png  600 x 240   one per grade (2x of 300 x 120), worn ink, unrotated; the UI turns it ~14 deg
#     out/reference.png                the whole card at 1280 x 720, composed from these files exactly as INTEGRATION.md lays it out
from PIL import Image, ImageDraw, ImageFont, ImageFilter
import os, random, math
HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
FD = "C:/Projects/festival-tycoon/game/assets/ui/fonts/"
def hx(h, a=255): return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (a,)
def slab(px, semi=False): return ImageFont.truetype(FD + ("ZillaSlab-SemiBold.ttf" if semi else "ZillaSlab-Bold.ttf"), int(px))
def sans(px, w="SemiBold"):
    f = ImageFont.truetype(FD + "SourceSans3-Variable.ttf", int(px)); f.set_variation_by_name(w); return f
K = 2
GRADES = [("encore", "Encore!", "2b6e66"), ("headliner", "Headliner", "2b6e66"), ("crowd_pleaser", "Crowd-pleaser", "9c7424"),
          ("b_side", "B-side", "b8551e"), ("off_key", "Off-key", "b8551e")]

# ---- hardboard: warm brown, a few grain lines, rounded corners, a darker edge
W, H = 520 * K, 694 * K
b = Image.new("RGBA", (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(b)
d.rounded_rectangle([0, 0, W - 1, H - 1], 18 * K, fill=hx("8a6a45"))
g = random.Random(2)
for k in range(46):
    y = g.uniform(0, H); amp = g.uniform(2, 6) * K; ph = g.uniform(0, 6)
    pts = [(x, y + amp * math.sin(x / (W / g.uniform(1.5, 3)) + ph)) for x in range(0, W, 8)]
    d.line(pts, fill=hx("7d5f3d", g.randint(60, 140)), width=g.choice((1, 2)) * K // 2 + 1)
mask = Image.new("L", (W, H), 0); ImageDraw.Draw(mask).rounded_rectangle([0, 0, W - 1, H - 1], 18 * K, fill=255)
b.putalpha(mask); d = ImageDraw.Draw(b); d.rounded_rectangle([1, 1, W - 2, H - 2], 18 * K, outline=hx("6b4f30"), width=3 * K)
b.save(os.path.join(OUT, "lwf_report_clipboard_v1.png"))

# ---- clip: a steel bar with a rounded spring housing
CW, CH = 150 * K, 70 * K
c = Image.new("RGBA", (CW, CH), (0, 0, 0, 0)); d = ImageDraw.Draw(c)
d.rounded_rectangle([5 * K, 16 * K, CW - 5 * K, 66 * K], 10 * K, fill=hx("b9bdc2"), outline=hx("7f868c"), width=2 * K)
d.rounded_rectangle([CW / 2 - 22 * K, 4 * K, CW / 2 + 22 * K, 24 * K], 8 * K, fill=hx("9aa0a6"), outline=hx("7f868c"), width=2 * K)
d.line([(14 * K, 30 * K), (CW - 14 * K, 30 * K)], fill=hx("e4e7ea"), width=2 * K)
for x in (22 * K, CW - 22 * K): d.ellipse([x - 4 * K, 46 * K, x + 4 * K, 54 * K], fill=hx("7f868c"))
c.save(os.path.join(OUT, "lwf_report_clip_v1.png"))

# ---- sheet: PaperBright with grain
SW, SH = 476 * K, 634 * K
s = Image.new("RGB", (SW, SH), hx("fff8e9")[:3]); px = s.load(); g = random.Random(5)
for _ in range(SW * SH // 6):
    x, y = g.randrange(SW), g.randrange(SH); r, gg, bb = px[x, y]; v = g.randint(-7, 4); px[x, y] = (r + v, gg + v, bb + v)
s = s.filter(ImageFilter.GaussianBlur(0.6)); s.save(os.path.join(OUT, "lwf_report_sheet_v1.png"))

# ---- stamps: double border, the grade in Zilla Slab, worn ink
def stamp(label, colour, seed):
    st = Image.new("RGBA", (300 * K, 120 * K), (0, 0, 0, 0)); sd = ImageDraw.Draw(st); c = hx(colour, 230)
    sd.rounded_rectangle([6 * K, 6 * K, 294 * K, 114 * K], 12 * K, outline=c, width=5 * K); sd.rounded_rectangle([16 * K, 16 * K, 284 * K, 104 * K], 8 * K, outline=c, width=2 * K)
    t = label.upper(); f = 40
    while sd.textlength(t, font=slab(f * K)) > 250 * K: f -= 1
    sd.text((150 * K, 54 * K), t, font=slab(f * K), fill=c, anchor="mm")
    sd.text((150 * K, 90 * K), "STEWARD SHIFT  ·  CONTROL", font=sans(11 * K, "Bold"), fill=c, anchor="mm")
    g = random.Random(seed); p = st.load()
    for _ in range(26000):                                               # worn, uneven ink
        x, y = g.randrange(st.width), g.randrange(st.height); r, gg, bb, a = p[x, y]
        if a: p[x, y] = (r, gg, bb, int(a * g.uniform(0.15, 0.85)))
    blot = Image.new("L", st.size, 0); bd = ImageDraw.Draw(blot)
    for _ in range(10):
        x, y, rr = g.uniform(0, st.width), g.uniform(0, st.height), g.uniform(10, 40) * K; bd.ellipse([x - rr, y - rr, x + rr, y + rr], fill=g.randint(60, 140))
    blot = blot.filter(ImageFilter.GaussianBlur(12 * K)); a = st.split()[3]
    st.putalpha(Image.composite(a.point(lambda v: int(v * 0.55)), a, blot))      # blotchy pressure: some patches print fainter
    return st
for i, (key, label, colour) in enumerate(GRADES): stamp(label, colour, 10 + i).save(os.path.join(OUT, f"lwf_report_stamp_{key}_v1.png"))
print("art ok")
