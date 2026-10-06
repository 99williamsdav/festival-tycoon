# Field notes production art (approved board: Documents/Festival Tycoon concepts/field-notes/).
#   python build_field_notes.py            -> out/*.png, plus out/preview_card.png and out/preview_guide.png
# Every sprite is drawn at 2x the 1280 x 720 mockup size (draw it at half its pixel size at 1280 x 720, i.e. Ui.S(px / 2)).
import os, math, random, json
from fn_kit import *

HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
SPEC = {}
def save(im, name, **meta):
    im.save(os.path.join(OUT, name)); SPEC[name] = dict(size=list(im.size), **meta)

KEYS = [k for k, _, _ in NOTES]

# ---------------------------------------------------------------- world sprites (Sprite3D: billboard, no depth test, fixed size)
for key in KEYS:
    p, tx, ty = pin(2, key)
    save(p, f"pin_{key}.png", tip_px=[round(tx), round(ty)], disc_centre_px=[round(tx), 26])
g = Image.new("RGBA", (96, 96), (0, 0, 0, 0)); ImageDraw.Draw(g).ellipse([18, 18, 78, 78], fill=hx("ffd27a", 150))
save(g.filter(ImageFilter.GaussianBlur(12)), "pin_glow.png", centre_px=[48, 48], note="centre on the pin's disc centre, behind the pin, dusk only")


def marker(px):
    big = 8; S_ = px * big; im = Image.new("RGBA", (S_, S_), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    c = S_ / 2; ro = S_ / 2 - big * 0.5; ri = ro - S_ * 0.085                      # ink ring ~8.5% of the size
    d.ellipse([c - ro, c - ro, c + ro, c + ro], fill=OUTLINE); d.ellipse([c - ri, c - ri, c + ri, c + ri], fill=WASP)
    msk = Image.new("L", im.size, 0); ImageDraw.Draw(msk).ellipse([c - ri, c - ri, c + ri, c + ri], fill=255)
    st = Image.new("RGBA", im.size, (0, 0, 0, 0)); sd = ImageDraw.Draw(st); r = ri
    for t in (-0.36, 0.30):
        sd.polygon([(c - r * 1.2, c + r * (t - 0.17) - r * 0.25), (c + r * 1.2, c + r * (t - 0.17) + r * 0.25),
                    (c + r * 1.2, c + r * (t + 0.15) + r * 0.25), (c - r * 1.2, c + r * (t + 0.15) - r * 0.25)], fill=OUTLINE)
    im.paste(st, (0, 0), Image.composite(st.split()[3], Image.new("L", im.size, 0), msk))
    return im.resize((px, px), Image.LANCZOS)
save(marker(32), "wasp_marker.png", note="2x; shows 16 px at 1280 x 720"); save(marker(16), "wasp_marker_16.png", note="1x, for small or 1x screens")

# ---------------------------------------------------------------- doodles (ink on transparent; the card and the guide)
for key in KEYS + ["unknown"]:
    im = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    doodle(ImageDraw.Draw(im), key, 64, 64, 88, INK if key != "unknown" else PAPER_EDGE, 6, seed=1)
    save(im, f"doodle_{key}.png", note="draw at 26 px in the card ring, 28 px in the guide ring (1280 x 720)")

# ---------------------------------------------------------------- card and guide pieces (2x)
rng = random.Random(7)
ring = Image.new("RGBA", (92, 92), (0, 0, 0, 0)); stroke(ImageDraw.Draw(ring), circ(46, 46, 43, -80, 285, n=40), GOLD, 4.4, rng, 1.2)
save(ring, "ring.png", note="hand-drawn gold ring with a small gap at the top right; 46 px card, 50 px guide (1280 x 720)")
rb = Image.new("RGBA", (100, 100), (0, 0, 0, 0)); rd = ImageDraw.Draw(rb)
for a0 in range(0, 360, 30): rd.arc([4, 4, 96, 96], a0, a0 + 16, fill=PAPER_EDGE, width=4)
save(rb, "ring_blank.png", note="dashed ring for unseen guide entries, 50 px (1280 x 720)")

TW, TH = 256, 14                                     # torn edge, tiles horizontally (period = width)
te = Image.new("RGBA", (TW, TH), (0, 0, 0, 0)); td = ImageDraw.Draw(te); rr = random.Random(3)
ys = [rr.uniform(4.0, 9.5) for _ in range(TW // 10)]; ys.append(ys[0])
pts = [(0, 0), (TW, 0)] + [(TW - i * 10, ys[-1 - i]) for i in range(len(ys))]
td.polygon(pts, fill=PAPER)
save(te, "torn_edge.png", note="paper colour with a torn lower edge; tile horizontally under the card body, 7 px tall at 1280 x 720")

tape = Image.new("RGBA", (156, 44), hx("d8a43b", 205)); tdd = ImageDraw.Draw(tape)
for xx in range(0, 156, 14): tdd.line([(xx, 0), (xx + 20, 44)], fill=hx("e8bb5a", 120), width=4)
for x in range(0, 156, 6):                           # slightly ragged short ends
    for yy in (0, 43): pass
for yy in range(0, 44, 4):
    tdd.rectangle([0, yy, rr.randint(0, 3), yy + 3], fill=(0, 0, 0, 0)); tdd.rectangle([155 - rr.randint(0, 3), yy, 155, yy + 3], fill=(0, 0, 0, 0))
save(tape, "tape.png", note="washi tape, 78 x 22 at 1280 x 720, rotated -7 degrees, alpha in the texture")

gut = Image.new("RGBA", (56, 8), hx("e9dcbf")); ImageDraw.Draw(gut).line([(28, 0), (28, 8)], fill=PAPER_EDGE, width=3)
save(gut, "guide_gutter.png", note="tiles vertically down the middle of the guide spread, 28 px wide at 1280 x 720")
json.dump(SPEC, open(os.path.join(OUT, "field_notes_sprites.json"), "w"), indent=1)

# ---------------------------------------------------------------- previews: the card and a guide row built only from the pieces + spec
def preview_card(line="A full bin is a wasp magnet, which can be more than just a nuisance for some...", key="wasps", k=2):
    S = lambda v: int(round(v * k)); Wc = S(460)
    lf = font("slab", S(18)); tmp = ImageDraw.Draw(Image.new("RGBA", (1, 1))); lines = wrap_lines(tmp, line, lf, Wc - S(22 + 46 + 16 + 22))
    Hb = S(20) + S(16) + S(6) + len(lines) * S(23) + S(12) + S(30) + S(14)
    im = Image.new("RGBA", (Wc + S(40), Hb + S(60)), (0, 0, 0, 0)); ox, oy = S(20), S(26)
    card = Image.new("RGBA", (Wc, Hb + S(7)), (0, 0, 0, 0)); d = ImageDraw.Draw(card)
    d.rectangle([0, 0, Wc, Hb], fill=PAPER); d.rectangle([0, 0, Wc, S(3) - 1], fill=GOLD)
    t = Image.open(os.path.join(OUT, "torn_edge.png"))
    for x in range(0, Wc, t.width): card.alpha_composite(t, (x, Hb)) if x + t.width <= Wc else card.alpha_composite(t.crop((0, 0, Wc - x, t.height)), (x, Hb))
    LX = S(22 + 46 + 16)
    for yy in range(S(20 + 16 + 6) + S(19), Hb - S(6), S(23)): d.line([(LX - S(6), yy), (Wc - S(14), yy)], fill=PAPER_RULE, width=S(1))
    d.line([(LX - S(10), S(8)), (LX - S(10), Hb)], fill=hx("e7b7a0"), width=S(1))
    card.alpha_composite(Image.open(os.path.join(OUT, "ring.png")).resize((S(46), S(46)), Image.LANCZOS), (S(22), S(26)))
    card.alpha_composite(Image.open(os.path.join(OUT, f"doodle_{key}.png")).resize((S(37), S(37)), Image.LANCZOS), (S(22 + 4.5), S(26 + 4.5)))
    x = caps(d, (LX, S(20)), "Field note", S(10.5), GOLD_INK, spacing=S(1.1)); caps(d, (x + S(6), S(20)), "·  Wasps", S(10.5), INK, spacing=S(1.1))
    d.text((Wc - S(22), S(26)), "1 of 10", font=font("semi", S(11)), fill=INK_MUTED, anchor="rm")
    yy = S(20 + 16 + 6)
    for l in lines: d.text((LX, yy), l, font=lf, fill=INK); yy += S(23)
    yy += S(12); d.text((LX, yy + S(15)), "Kept in your field guide", font=font("body", S(12)), fill=INK_MUTED, anchor="lm")
    d.rounded_rectangle([Wc - S(22 + 84), yy, Wc - S(22), yy + S(30)], S(6), fill=GOLD)
    d.text((Wc - S(22 + 42), yy + S(15)), "Got it", font=font("bold", S(13.5)), fill=BAR, anchor="mm")
    sh, pad = shadowed(card, S(9), 0.4, S(5)); im.alpha_composite(sh, (ox - pad, oy - pad))
    tp = Image.open(os.path.join(OUT, "tape.png")).rotate(-7, expand=True, resample=Image.BICUBIC); im.alpha_composite(tp, (ox + Wc - S(118), oy - S(12)))
    return im
bgc = Image.new("RGBA", (1000, 420), hx("7f9a63")); bgc.alpha_composite(preview_card(), (30, 30)); bgc.save(os.path.join(OUT, "preview_card.png"))
print(json.dumps({k: v["size"] for k, v in SPEC.items()}))
