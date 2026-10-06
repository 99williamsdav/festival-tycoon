# Drawing kit for the field notes art (shared by build_field_notes.py and the concept board): HUD palette, fonts, doodles, pins, marker.
from PIL import Image, ImageDraw, ImageFont, ImageFilter
import math, random

FD = "C:/Projects/festival-tycoon/game/assets/ui/fonts/"
def hx(h, a=255): return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (a,)
BAR, BAR_RAISED, BAR_TEXT, BAR_MUTED = hx("17302a"), hx("22453a"), hx("f5ebd6"), hx("b9c4b8")
GOLD, GOLD_SHADOW, GOLD_INK, GOLD_WASH = hx("d8a43b"), hx("9c7424"), hx("6b4f16"), hx("fbf4e4")
PAPER, PAPER_BRIGHT, PAPER_RULE, PAPER_EDGE = hx("f5ebd6"), hx("fff8e9"), hx("e2d3b3"), hx("d5c39e")
INK, INK_MUTED, TEAL, ALERT, WARN = hx("1f2a26"), hx("56615a"), hx("2b6e66"), hx("b8551e"), hx("f2a65a")
OUTLINE = hx("1d2a25"); WASP = hx("f2b233")

_fc = {}
def font(kind, size):
    key = (kind, int(size))
    if key not in _fc:
        if kind in ("slab", "slabb"):
            _fc[key] = ImageFont.truetype(FD + ("ZillaSlab-SemiBold.ttf" if kind == "slab" else "ZillaSlab-Bold.ttf"), int(size))
        else:
            f = ImageFont.truetype(FD + "SourceSans3-Variable.ttf", int(size))
            f.set_variation_by_name({"body": "Regular", "semi": "SemiBold", "bold": "Bold"}[kind]); _fc[key] = f
    return _fc[key]


def caps(d, xy, text, size, col, kind="bold", spacing=1.0):
    x, y = xy
    for ch in text.upper():
        d.text((x, y), ch, font=font(kind, size), fill=col); x += d.textlength(ch, font=font(kind, size)) + spacing
    return x


def wrap_lines(d, text, f, width):
    lines, line = [], ""
    for w in text.split():
        t = (line + " " + w).strip()
        if d.textlength(t, font=f) > width and line: lines.append(line); line = w
        else: line = t
    return lines + [line]


# ---------------------------------------------------------------- hand-drawn strokes
def wob(pts, rng, amp):
    out = []
    for a, b in zip(pts, pts[1:]):
        n = max(2, int(math.dist(a, b) / 4))
        for i in range(n):
            t = i / n; out.append((a[0] + (b[0] - a[0]) * t + rng.uniform(-amp, amp), a[1] + (b[1] - a[1]) * t + rng.uniform(-amp, amp)))
    out.append(pts[-1]); return out


def stroke(d, pts, col, w, rng, amp=0.5):
    d.line(wob(pts, rng, amp), fill=col, width=max(1, int(w)), joint="curve")


def circ(cx, cy, r, a0=0, a1=360, n=28, sq=1.0):
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / n)), cy + sq * r * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]


def doodle(d, name, cx, cy, s, col, w, seed=1):
    """a ~s px hand-drawn glyph centred on (cx, cy)"""
    rng = random.Random(seed); u = s / 24.0; P = lambda x, y: (cx + x * u, cy + y * u); St = lambda pts, ww=w: stroke(d, pts, col, ww, rng, 0.35 * u)
    if name == "wasps":
        d.ellipse([P(-3, -2), P(9, 6)], fill=col)                                         # abdomen
        for x in (1, 4.5): d.line([P(x, -2.6), P(x + 0.4, 6.4)], fill=hx("f2b233"), width=max(1, int(1.6 * u)))
        d.ellipse([P(-7, -1), P(-2, 4)], fill=col); d.ellipse([P(-10, 0), P(-6.5, 3.5)], fill=col)
        St(circ(*P(-2, -6), 4.5 * u, 200, 380)); St(circ(*P(3, -6.5), 4.5 * u, 160, 340))
        St([P(-10, 0.5), P(-12, -3)], w * 0.7); St([P(9, 2), P(11.5, 3)], w * 0.8)
    elif name == "heat":
        St(circ(*P(0, 0), 5.5 * u));
        for k in range(8):
            a = math.radians(k * 45 + 10); St([P(8 * math.cos(a), 8 * math.sin(a)), P(11 * math.cos(a), 11 * math.sin(a))])
    elif name == "drink":
        St([P(-6, -8), P(-5, 9), P(5, 9), P(6, -8)]); St(circ(*P(0, -8), 6 * u, 180, 360, sq=0.35)); St(circ(*P(0, -8), 6 * u, 0, 180, sq=0.35))
        St(circ(*P(8.5, 0), 3.5 * u, -90, 90)); St([P(-4, -3), P(4, -3)], w * 0.6)
    elif name == "stuck":
        St([P(-7, 10), P(-7, -10), P(7, -10), P(7, 10)]); St([P(-9, 10), P(9, 10)])
        d.ellipse([P(3, -1), P(5.5, 1.5)], fill=col); St([P(-3, -4), P(1, 0), P(-3, 4)], w * 0.7); St([P(-1, -4), P(3, 0)], w * 0.0 + 0.01)
    elif name == "toxic":
        St([P(-6, 10), P(-6, -4), P(6, -4), P(6, 10)]); St(circ(*P(0, -4), 6 * u, 180, 360, sq=0.6))
        for x in (-5, 0, 5): St([P(x, -9), P(x + 2, -12), P(x - 1, -15), P(x + 1.5, -18)], w * 0.7)
    elif name == "power":
        pts = [P(2, -11), P(-6, 2), P(0, 2), P(-3, 12), P(7, -3), P(1, -3), P(2, -11)]; d.polygon(wob(pts, rng, 0.2 * u), fill=col)
    elif name == "tempers":
        St(circ(*P(-4, -3), 7 * u, 40, 330, sq=0.75)); St([P(-8, 2), P(-10, 7), P(-4, 3.5)])
        St(circ(*P(6, 3), 6 * u, 210, 500, sq=0.75)); St([P(-2, -4), P(0, -1), P(-3, 1), P(-1, 4)], w * 0.8)
    elif name == "late":
        St(circ(*P(0, 1), 9 * u)); St([P(0, 1), P(0, -5)]); St([P(0, 1), P(4.5, 3)]); St([P(-4, -10), P(4, -10)], w * 0.8)
    elif name == "litter":
        St([P(-5, -8), P(-3, 8), P(4, 8), P(6, -8)]); St([P(-6, -8), P(7, -8)]); St([P(-4, -2), P(5, 0)], w * 0.6)
        St([P(-11, 9), P(-6, 10)], w * 0.7); St([P(8, 10), P(12, 8.5)], w * 0.7)
    elif name == "dusk":
        St(circ(*P(0, -3), 7 * u, 140, 400)); St([P(-4, 3), P(-3, 7), P(3, 7), P(4, 3)]); St([P(-3, 9.5), P(3, 9.5)])
        for a in (200, 270, 340): St([P(10 * math.cos(math.radians(a)), -3 + 10 * math.sin(math.radians(a))),
                                      P(13 * math.cos(math.radians(a)), -3 + 13 * math.sin(math.radians(a)))], w * 0.7)
    elif name == "queue":                            # three people in a line; the one at the back taps a watch
        for i, (x, sc_) in enumerate(((-8.5, 1.0), (-0.5, 1.0), (8, 1.0))):
            d.ellipse([P(x - 2.6, -9), P(x + 2.6, -3.8)], fill=col)                       # head
            St([P(x - 3.6, 10), P(x - 3.4, 0.5), P(x - 1.6, -1.8), P(x + 1.6, -1.8), P(x + 3.4, 0.5), P(x + 3.6, 10)], w * 0.85)
        St([P(5.2, 3.5), P(8.5, 2.2)], w * 0.8); d.ellipse([P(7.6, 1.2), P(10.2, 3.6)], outline=col, width=max(1, int(w * 0.6)))   # wrist + watch
        St([P(11.5, -1.0), P(12.8, -2.4)], w * 0.6); St([P(12.2, 1.6), P(13.8, 1.2)], w * 0.6)                                    # tap marks
    elif name == "unknown":
        f = font("slabb", int(17 * u)); d.text((cx, cy + u), "?", font=f, fill=col, anchor="mm")


NOTES = [("wasps", "Wasps", "A full bin is a wasp magnet, which can be more than just a nuisance for some..."),
         ("heat", "Heat", "It's a scorcher, and not everyone remembers to drink their water..."),
         ("drink", "Drink", "Beer makes everyone friendlier, up to a point..."),
         ("stuck", "Stuck", "Portaloo doors have a habit of sticking, and whoever's inside isn't getting out on their own..."),
         ("toxic", "Fumes", "A nearly full portaloo is grim at the best of times, and worse if you're stuck in it..."),
         ("power", "Power", "Everything plugged in leans on the generator, and it has its limits..."),
         ("tempers", "Tempers", "Long waits and short tempers don't mix, and words can turn into something worse..."),
         ("late", "Late", "The crowd came for the music, and their patience won't last forever..."),
         ("litter", "Litter", "Not everyone makes it to a bin, and nobody likes standing in rubbish..."),
         ("dusk", "Lights", "The lights are coming on, and they want their share of the power too..."),
         ("queue", "Queue", "A long queue for food is a long time away from the music...")]


# ---------------------------------------------------------------- the note card
def shadowed(img, blur, alpha, dy):
    pad = blur * 3
    out = Image.new("RGBA", (img.width + 2 * pad, img.height + 2 * pad + dy), (0, 0, 0, 0))
    sh = Image.new("RGBA", out.size, (0, 0, 0, 0)); m = img.split()[3].point(lambda v: int(v * alpha))
    sh.paste((10, 20, 16, 255), (pad, pad + dy), m); sh = sh.filter(ImageFilter.GaussianBlur(blur))
    out = Image.alpha_composite(out, sh); out.alpha_composite(img, (pad, pad)); return out, pad


def note_card(k, key, title, line, idx=1, total=10, style="notebook", width=460):
    """returns (RGBA image incl. shadow, shadow pad) at scale k (1 = 1280x720 mockup pixels)"""
    S = lambda v: int(round(v * k))
    tmp = ImageDraw.Draw(Image.new("RGBA", (1, 1)))
    if style == "plain":                                           # what Main.FieldNotes.cs builds today
        lf = font("semi", S(15)); lines = wrap_lines(tmp, line, lf, S(400))
        H = S(16) + S(14) + S(8) + len(lines) * S(20) + S(8) + S(30) + S(16)
        im = Image.new("RGBA", (S(width), H), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
        d.rounded_rectangle([0, 0, S(width) - 1, H - 1], S(8), fill=PAPER, outline=GOLD, width=max(1, S(2)))
        caps(d, (S(20), S(16)), title, S(11), GOLD_INK, spacing=S(1)); y = S(16) + S(14) + S(8)
        for l in lines: d.text((S(20), y), l, font=lf, fill=INK); y += S(20)
        y += S(8); bw = S(70); d.rounded_rectangle([S(width) - S(20) - bw, y, S(width) - S(20), y + S(30)], S(6), fill=GOLD)
        d.text((S(width) - S(20) - bw / 2, y + S(15)), "Got it", font=font("bold", S(13)), fill=BAR, anchor="mm")
        return shadowed(im, S(14) // 2 + 1, 0.38, S(4))
    rng = random.Random(idx * 7 + 3)
    IC = S(46); LX = S(22) + IC + S(16); TW = S(width) - LX - S(22)
    lf = font("slab", S(18)); lines = wrap_lines(tmp, line, lf, TW)
    top = S(20); H = top + S(16) + S(8) + len(lines) * S(23) + S(14) + S(30) + S(20)
    W = S(width)
    im = Image.new("RGBA", (W, H + S(6)), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    edge = [(0, S(6))]; x = 0                                       # page with a slightly torn bottom edge
    while x < W: edge.append((x, H - S(3) + rng.uniform(-S(2.2), S(2.2)))); x += S(5)
    poly = [(0, 0), (W - 1, 0), (W - 1, H - S(3))] + edge[::-1][:-1] + [(0, H - S(3))]
    d.polygon(poly, fill=PAPER)
    for yy in range(top + S(16) + S(8) + S(19), H - S(10), S(23)): d.line([(LX - S(6), yy), (W - S(14), yy)], fill=PAPER_RULE, width=max(1, S(1)))
    d.line([(LX - S(10), S(8)), (LX - S(10), H - S(8))], fill=hx("e7b7a0"), width=max(1, S(1.2)))          # notebook margin rule
    d.rectangle([0, 0, W - 1, S(3)], fill=GOLD)                                                         # gold head rule
    cx, cy = S(22) + IC / 2, top + S(6) + IC / 2                                                         # doodle in a hand-drawn gold ring
    stroke(d, circ(cx, cy, IC / 2 - S(1), -80, 285, n=36), GOLD, S(2.2), rng, S(0.6))
    doodle(d, key, cx, cy, S(26), INK, S(2), seed=idx)
    x = caps(d, (LX, top), "Field note", S(10.5), GOLD_INK, spacing=S(1.1)); x = caps(d, (x + S(6), top), "·  " + title, S(10.5), INK, spacing=S(1.1))
    d.text((W - S(22), top + S(6)), f"{idx} of {total}", font=font("semi", S(11)), fill=INK_MUTED, anchor="rm")
    y = top + S(16) + S(6)
    for l in lines: d.text((LX, y), l, font=lf, fill=INK); y += S(23)
    y += S(12)
    d.text((LX, y + S(15)), "Kept in your field guide", font=font("body", S(12)), fill=INK_MUTED, anchor="lm")
    bw = S(84); d.rounded_rectangle([W - S(22) - bw, y, W - S(22), y + S(30)], S(6), fill=GOLD)
    d.text((W - S(22) - bw / 2, y + S(15)), "Got it", font=font("bold", S(13.5)), fill=BAR, anchor="mm")
    tape = Image.new("RGBA", (S(78), S(22)), hx("d8a43b", 205)); td = ImageDraw.Draw(tape)            # washi tape over the head
    for xx in range(0, S(78), S(7)): td.line([(xx, 0), (xx + S(10), S(22))], fill=hx("e8bb5a", 120), width=max(1, S(2)))
    tape = tape.rotate(-7, expand=True, resample=Image.BICUBIC)
    out, pad = shadowed(im, S(9), 0.40, S(5))
    out.alpha_composite(tape, (pad + W - S(118), max(0, pad - S(12))))
    return out, pad


# ---------------------------------------------------------------- world pin and wasp marker (screen space, fixed size)
def pin(k, key, glow=False):
    """teardrop pin, tip at the bottom centre. Returns (img, tip_x, tip_y). ~30 px tall at k = 1"""
    S = lambda v: v * k; W, H = int(S(26)) + 8, int(S(34)) + 8
    big = 4; im = Image.new("RGBA", (W * big, H * big), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    B = lambda v: v * big; cx, cy, r = W * big / 2, B(4 + S(11)), B(S(10.5))
    tip = (cx, B(4 + S(31)))
    def drop(rr, col):
        a = math.asin(min(0.99, rr / (tip[1] - cy)))
        pts = [tip] + circ(cx, cy, rr, 90 + math.degrees(a) + 180 - 180, 360 + 90 - math.degrees(a) - 0, n=40)
        pts = [tip] + [(cx + rr * math.cos(t), cy + rr * math.sin(t)) for t in [math.radians(90 + math.degrees(a) + i * (360 - 2 * math.degrees(a)) / 40) for i in range(41)]]
        d.polygon(pts, fill=col)
    if glow:
        g = Image.new("RGBA", im.size, (0, 0, 0, 0)); gd_ = ImageDraw.Draw(g); gd_.ellipse([cx - r * 1.9, cy - r * 1.9, cx + r * 1.9, cy + r * 1.9], fill=hx("ffd27a", 120))
        im = Image.alpha_composite(im, g.filter(ImageFilter.GaussianBlur(B(4)))); d = ImageDraw.Draw(im)
    drop(r + B(S(2.4)), OUTLINE); drop(r, GOLD)
    d.ellipse([cx - r * 0.66, cy - r * 0.66, cx + r * 0.66, cy + r * 0.66], fill=PAPER_BRIGHT)
    doodle(d, key, cx, cy, r * 1.15, INK, max(2, B(S(1.5))), seed=3)
    im = im.resize((W, H), Image.LANCZOS)
    return im, W / 2, 4 + S(31)


def chevron(k=1.0):
    """what the code draws today: a gold ▼ with a dark outline (Label3D, FontSize 64 at PixelSize .0012, fixed size)"""
    W, H = int(28 * k), int(26 * k); big = 4; im = Image.new("RGBA", (W * big, H * big), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    o = 3.5 * k * big; pts = [(W * big * 0.12, H * big * 0.15), (W * big * 0.88, H * big * 0.15), (W * big / 2, H * big * 0.9)]
    d.polygon(pts, fill=OUTLINE); c = (sum(p[0] for p in pts) / 3, sum(p[1] for p in pts) / 3)
    d.polygon([(c[0] + (p[0] - c[0]) * 0.72, c[1] + (p[1] - c[1]) * 0.72) for p in pts], fill=GOLD)
    return im.resize((W, H), Image.LANCZOS), W / 2, H * 0.9


def wasp_marker(k=1.0, style="stripes", glow=False):
    """small badge; centre returned. ~14 px at k = 1"""
    D = 15 * k; W = int(D + 8); big = 6; im = Image.new("RGBA", (W * big, W * big), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    c = W * big / 2; r = D * big / 2
    if glow:
        g = Image.new("RGBA", im.size, (0, 0, 0, 0)); ImageDraw.Draw(g).ellipse([c - r * 1.7, c - r * 1.7, c + r * 1.7, c + r * 1.7], fill=hx("ffd27a", 110))
        im = Image.alpha_composite(im, g.filter(ImageFilter.GaussianBlur(r * 0.4))); d = ImageDraw.Draw(im)
    if style == "bang":
        f = font("slabb", int(D * big * 1.5)); d.text((c, c), "!", font=f, fill=WASP, anchor="mm", stroke_width=int(big * 2.3 * k), stroke_fill=OUTLINE)
    elif style == "stripes":
        d.ellipse([c - r - big * 1.6 * k, c - r - big * 1.6 * k, c + r + big * 1.6 * k, c + r + big * 1.6 * k], fill=OUTLINE)
        d.ellipse([c - r, c - r, c + r, c + r], fill=WASP)
        msk = Image.new("L", im.size, 0); ImageDraw.Draw(msk).ellipse([c - r, c - r, c + r, c + r], fill=255)
        st = Image.new("RGBA", im.size, (0, 0, 0, 0)); sd = ImageDraw.Draw(st)
        for t in (-0.36, 0.30):
            sd.polygon([(c - r * 1.2, c + r * (t - 0.17) - r * 0.25), (c + r * 1.2, c + r * (t - 0.17) + r * 0.25),
                        (c + r * 1.2, c + r * (t + 0.15) + r * 0.25), (c - r * 1.2, c + r * (t + 0.15) - r * 0.25)], fill=OUTLINE)
        im.paste(st, (0, 0), Image.composite(st.split()[3], Image.new("L", im.size, 0), msk))
    elif style == "glyph":
        d.ellipse([c - r - big * 1.4 * k, c - r - big * 1.4 * k, c + r + big * 1.4 * k, c + r + big * 1.4 * k], fill=OUTLINE)
        d.ellipse([c - r, c - r, c + r, c + r], fill=PAPER_BRIGHT)
        doodle(d, "wasps", c, c, r * 1.45, INK, max(2, int(big * 1.1 * k)), seed=4)
    im = im.resize((W, W), Image.LANCZOS); return im, W / 2, W / 2
