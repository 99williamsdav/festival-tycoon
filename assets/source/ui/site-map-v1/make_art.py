# Site map art (approved: B1 held laminated plan, A1 rotating corner minimap).
#   python make_art.py -> out/
# PLAN (printed, a 1400 x 1276 texture the game redraws per layout; px are texture pixels):
#   plan_base_t1_v1.png / plan_base_t2_v1.png   the fixed ground only: paper, header rule, field, hedges, lane, buildings, pond,
#                                               stages + crowd outlines, fixed labels (stages, GATE, buildings, Pond). No stands.
#   plan_icon_<kind>_v1.png                     66 px printed stand icons (bin 46 px): tap loo bar van aid post bin marquee
#   plan_sticker_{call,urgent}_v1.png           88 px round call stickers with a drop shadow (sticker r 39)
#   plan_you_v1.png                             hi-vis arrow, 112 px, pointing UP (north); rotate by the heading
#   plan_crew_{free,busy}_v1.png                40 px dots
#   plan_laminate_v1.png                        1400 x 1276 white sheen overlay (alpha), drawn last
# MINIMAP (HUD, 2x art: draw at Ui.S(px / 2)):
#   mini_base_t1_v1.png / mini_base_t2_v1.png   the dark field at 6.286 px/m (2x of 3.143 units/m), whole 72 m square, no stands
#   mini_icon_<kind>_v1.png                     32 px (16 units) dark-disc stand icons (bin 22 px)
#   mini_pin_{call,urgent}_v1.png               44 px (22 units) map pins, tip at the bottom centre
#   mini_you_v1.png  mini_crew_{free,busy}_v1.png  mini_north_v1.png  mini_frame_v1.png (352 px rim)  mini_mask_v1.png (352 px disc)
import os, math
from PIL import Image, ImageDraw, ImageFilter
import plan_reference as P_
from plan_reference import hx, H
HERE = os.path.dirname(os.path.abspath(__file__)); O = os.path.join(HERE, "out"); os.makedirs(O, exist_ok=True)
W, HT = 1400, 1276; VIEW = (-32, -13, 32, 35.6); K = (W - 80) / (VIEW[2] - VIEW[0])          # 20.625 px per metre
KINDS = ["tap", "loo", "bar", "van", "aid", "post", "bin", "marquee"]

# ---- plan base per tier
for tier in (1, 2):
    sh = Image.new("RGBA", (W, HT), hx("fbf7ec")); d = ImageDraw.Draw(sh)
    d.line([(40, 130), (W - 40, 130)], fill=hx("1f2a26"), width=4)
    full, P0, k = P_.draw_site(tier, int(72 * K), "printed", stands=False)
    x0, y0 = P0(VIEW[0], VIEW[1]); x1, y1 = P0(VIEW[2], VIEW[3]); sh.alpha_composite(full.crop((int(x0), int(y0), int(x1), int(y1))), (40, 150))
    ly = 150 + int(y1 - y0) + 18; ImageDraw.Draw(sh).line([(40, ly - 8), (W - 40, ly - 8)], fill=hx("e2d3b3"), width=2)
    sh.save(os.path.join(O, f"plan_base_t{tier}_v1.png"))
gl = Image.new("L", (W, HT), 0); ImageDraw.Draw(gl).polygon([(W * 0.55, 0), (W * 0.75, 0), (W * 0.2, HT), (0, HT), (0, HT * 0.9)], fill=40)
lam = Image.new("RGBA", (W, HT), (255, 255, 255, 255)); lam.putalpha(gl.filter(ImageFilter.GaussianBlur(60))); lam.save(os.path.join(O, "plan_laminate_v1.png"))

# ---- printed sprites, at plan scale (k = 20.625)
def printed_icon(kind):
    ic, c = P_.ICON[kind]; r = K * 1.6 * (0.7 if kind == "bin" else 1.0); S4 = 4; sz = int(2 * r + 6)
    im = Image.new("RGBA", (sz * S4, sz * S4), (0, 0, 0, 0)); d = ImageDraw.Draw(im); cc = sz * S4 / 2
    d.ellipse([cc - r * S4, cc - r * S4, cc + r * S4, cc + r * S4], fill=hx("ffffff"), outline=hx(c), width=int(K * 0.25 * S4))
    im.alpha_composite(H.L.draw_icon(ic, int(r * 1.3 * S4), hx(c)), (int(cc - r * 0.65 * S4), int(cc - r * 0.65 * S4)))
    return im.resize((sz, sz), Image.LANCZOS)
for kind in KINDS: printed_icon(kind).save(os.path.join(O, f"plan_icon_{kind}_v1.png"))
for urgent in (False, True):
    im = Image.new("RGBA", (88, 88), (0, 0, 0, 0)); P_.pin(im, 44, 44, K, urgent); im.save(os.path.join(O, f"plan_sticker_{'urgent' if urgent else 'call'}_v1.png"))
im = Image.new("RGBA", (112, 112), (0, 0, 0, 0)); P_.you_arrow(im, 56, 56, K, 0); im.save(os.path.join(O, "plan_you_v1.png"))
for busy in (False, True):
    im = Image.new("RGBA", (40, 40), (0, 0, 0, 0)); P_.crew_dot(im, 20, 20, K, busy); im.save(os.path.join(O, f"plan_crew_{'busy' if busy else 'free'}_v1.png"))

# ---- minimap (2x)
KM = 2 * 88 / 28                                                            # 6.286 px per metre at 2x
for tier in (1, 2):
    base, _, _ = P_.draw_site(tier, int(72 * KM), "mini", labels=False, stands=False); base.save(os.path.join(O, f"mini_base_t{tier}_v1.png"))
def mini_icon(kind):
    ic, c = P_.ICON[kind]; sz = 22 if kind == "bin" else 32; S4 = 4
    im = Image.new("RGBA", (sz * S4, sz * S4), (0, 0, 0, 0)); d = ImageDraw.Draw(im); d.ellipse([0, 0, sz * S4 - 1, sz * S4 - 1], fill=hx("0e1f1a", 225))
    im.alpha_composite(H.L.draw_icon(ic, int(sz * 0.72 * S4), hx("f5ebd6", 235)), (int(sz * 0.14 * S4), int(sz * 0.14 * S4)))
    return im.resize((sz, sz), Image.LANCZOS)
for kind in KINDS: mini_icon(kind).save(os.path.join(O, f"mini_icon_{kind}_v1.png"))
for urgent in (False, True): H.L.draw_icon("map-pin", 44, hx("b8551e") if urgent else hx("d8a43b")).save(os.path.join(O, f"mini_pin_{'urgent' if urgent else 'call'}_v1.png"))
im = Image.new("RGBA", (44, 44), (0, 0, 0, 0)); P_.you_arrow(im, 22, 23, 6.8, 0); im.save(os.path.join(O, "mini_you_v1.png"))
for busy in (False, True):
    im = Image.new("RGBA", (18, 18), (0, 0, 0, 0)); ImageDraw.Draw(im).ellipse([0, 0, 17, 17], fill=hx("8fa59c") if busy else hx("2b6e66"), outline=hx("ffffff"), width=2)
    im.save(os.path.join(O, f"mini_crew_{'busy' if busy else 'free'}_v1.png"))
S4 = 4; sz = 352
m = Image.new("L", (sz * S4, sz * S4), 0); ImageDraw.Draw(m).ellipse([0, 0, sz * S4 - 1, sz * S4 - 1], fill=255); m.resize((sz, sz), Image.LANCZOS).save(os.path.join(O, "mini_mask_v1.png"))
f = Image.new("RGBA", (sz * S4, sz * S4), (0, 0, 0, 0)); ImageDraw.Draw(f).ellipse([2 * S4, 2 * S4, sz * S4 - 2 * S4, sz * S4 - 2 * S4], outline=hx("f5ebd6", 110), width=4 * S4)
f.resize((sz, sz), Image.LANCZOS).save(os.path.join(O, "mini_frame_v1.png"))
n = Image.new("RGBA", (36 * S4, 36 * S4), (0, 0, 0, 0)); d = ImageDraw.Draw(n); d.ellipse([0, 0, 36 * S4 - 1, 36 * S4 - 1], fill=hx("0e1f1a"))
d.text((18 * S4, 18 * S4), "N", font=P_.sans(22 * S4, "Bold"), fill=hx("f5ebd6"), anchor="mm"); n.resize((36, 36), Image.LANCZOS).save(os.path.join(O, "mini_north_v1.png"))
print("ok", len(os.listdir(O)))
