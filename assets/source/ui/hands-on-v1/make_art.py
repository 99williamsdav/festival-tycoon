# Hands-on campaign art (approved picks: 1B radial radio menu, 2A "Your shift" results tab, 3A title buttons + view cues + YOU marker).
#   python make_art.py -> out/  (all 2x art: draw each at half its pixel size in 1280 mockup units, i.e. Ui.S(px / 2))
#
#   Radial (inner ring r 58..150, three 112 deg wedges with 8 deg gaps; sprites point UP, rotate by 0 / 120 / 240 deg about the centre):
#     lwf_radial_wedge3_{normal,hover,busy,disabled}_v1.png   640 x 640  (hover is drawn out to r 156: it "pops")
#     lwf_radial_hub_v1.png                                    216 x 216  dark centre disc, r 54
#     lwf_radial_backdrop_v1.png                               640 x 640  soft dark disc behind the ring, r 158
#   Crew ring (outer r 162..236, a 180 deg fan centred on the crew wedge), N = 1..6 crew:
#     lwf_radial_crew{N}_{normal,hover,busy}_v1.png            1000 x 1000  one wedge of the N-fan, pointing UP; rotate to slot i
#   Results tab:
#     lwf_results_mini_clipboard_v1.png                        clipboard + sheet + clip at 0.62 scale, ruled, no stamp (stamp drawn on top)
#   Markers:
#     lwf_you_tag_v1.png        "YOU" pill with pointer, hi-vis      120 x 76
#     lwf_you_ring_v1.png       hi-vis ground ring (decal, top-down) 256 x 256
#     lwf_you_edge_disc_v1.png  off-screen edge badge disc with the person icon, upright                100 x 100
#     lwf_you_edge_arrow_v1.png the badge's arrow, pointing RIGHT, centred on the disc's centre: rotate it  168 x 168
from PIL import Image, ImageDraw, ImageFilter
import os, math, sys
HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
sys.path.insert(0, "C:/Projects/festival-tycoon/assets/source/ui/steward-shift-hud-v1"); import hud_reference as H
def hx(h, a=255): return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (a,)
SS = 4                                                                     # supersample


def sector(size, r0, r1, a0, a1, fill, outline=None, ow=0, hatch=None, dashed=False):
    """annular sector, angles in degrees clockwise from UP; size is the canvas side in 2x px; radii in mockup units"""
    K = 2 * SS; W = size * SS; c = W / 2
    im = Image.new("RGBA", (W, W), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    def P(r, a): t = math.radians(a - 90); return (c + r * K * math.cos(t), c + r * K * math.sin(t))
    n = max(8, int(abs(a1 - a0) / 2))
    pts = [P(r1, a0 + (a1 - a0) * i / n) for i in range(n + 1)] + [P(r0, a1 - (a1 - a0) * i / n) for i in range(n + 1)]
    d.polygon(pts, fill=fill)
    if hatch:                                                              # busy: fine diagonal hatching inside the wedge
        hl = Image.new("RGBA", (W, W), (0, 0, 0, 0)); hd = ImageDraw.Draw(hl)
        for k in range(-W, W, 14 * SS): hd.line([(k, 0), (k + W, W)], fill=hatch, width=3 * SS)
        m = Image.new("L", (W, W), 0); ImageDraw.Draw(m).polygon(pts, fill=255); im.paste(hl, (0, 0), Image.composite(hl.split()[3], Image.new("L", (W, W), 0), m))
    if outline:
        if dashed:
            seg = pts + [pts[0]]
            for i in range(len(seg) - 1):
                if i % 2 == 0: d.line([seg[i], seg[i + 1]], fill=outline, width=ow * SS)
        else: d.line(pts + [pts[0]], fill=outline, width=ow * SS, joint="curve")
    return im.resize((size, size), Image.LANCZOS)


PAPER, RULE, GOLD, BARDEEP, INKM, TEAL, ALERT = hx("f5ebd6", 238), hx("e2d3b3", 230), hx("d8a43b", 250), hx("0e1f1a", 150), hx("56615a"), hx("2b6e66"), hx("b8551e")
# ---- inner ring: three wedges, 112 deg each, centred UP
STATES = {"normal": dict(fill=PAPER, outline=hx("ffffff", 120), ow=2, r1=150),
          "hover": dict(fill=GOLD, outline=hx("6b4f16", 255), ow=3, r1=156),
          "busy": dict(fill=RULE, outline=hx("b8551e", 160), ow=2, r1=150, hatch=hx("b8551e", 40)),
          "disabled": dict(fill=hx("1f2a26", 150), outline=hx("b9c4b8", 150), ow=2, r1=150, dashed=True)}
for st, k in STATES.items():
    sector(640, 58, k["r1"], -56, 56, k["fill"], k["outline"], k["ow"], k.get("hatch"), k.get("dashed", False)).save(os.path.join(OUT, f"lwf_radial_wedge3_{st}_v1.png"))
hub = Image.new("RGBA", (216 * SS, 216 * SS), (0, 0, 0, 0)); d = ImageDraw.Draw(hub)
d.ellipse([0, 0, 216 * SS - 1, 216 * SS - 1], fill=hx("0e1f1a", 215)); d.ellipse([6 * SS, 6 * SS, 210 * SS, 210 * SS], outline=hx("d8f126", 150), width=3 * SS)
hub.resize((216, 216), Image.LANCZOS).save(os.path.join(OUT, "lwf_radial_hub_v1.png"))
bd = Image.new("L", (640, 640), 0); ImageDraw.Draw(bd).ellipse([10, 10, 630, 630], fill=255); bd = bd.filter(ImageFilter.GaussianBlur(12))
back = Image.new("RGBA", (640, 640), hx("0e1f1a")); back.putalpha(bd.point(lambda v: int(v * 0.47))); back.save(os.path.join(OUT, "lwf_radial_backdrop_v1.png"))
# ---- crew ring: a 180 deg fan of N wedges (r 162..236), 6 deg gaps; sprite = one wedge centred UP
for N in range(1, 7):
    span = (180 - 6 * (N - 1)) / N
    for st, k in (("normal", STATES["normal"]), ("hover", STATES["hover"]), ("busy", STATES["busy"])):
        r1 = 236 + (6 if st == "hover" else 0)
        sector(1000, 162, r1, -span / 2, span / 2, k["fill"], k["outline"], k["ow"], k.get("hatch")).save(os.path.join(OUT, f"lwf_radial_crew{N}_{st}_v1.png"))

# ---- results: the mini clipboard (from the steward report art) at 0.62, ruled sheet, no stamp
U = "C:/Projects/festival-tycoon/game/assets/ui/steward/"; sc = 0.62
bw, bh = int(520 * sc * 2), int(694 * sc * 2)
mini = Image.new("RGBA", (bw + 20, bh + 20), (0, 0, 0, 0))
mini.alpha_composite(Image.open(U + "lwf_report_clipboard_v1.png").resize((bw, bh), Image.LANCZOS), (10, 10))
sh = Image.open(U + "lwf_report_sheet_v1.png").convert("RGBA").resize((int(476 * sc * 2), int(634 * sc * 2)), Image.LANCZOS); sdd = ImageDraw.Draw(sh)
sdd.text((14 * 2, 16 * 2), "Steward shift report", font=H.slab(17 * 1, True).font_variant(size=34), fill=hx("1f2a26"))
for k in range(8): sdd.line([(14 * 2, (60 + k * 30) * 2), ((476 * sc - 14) * 2, (60 + k * 30) * 2)], fill=hx("e2d3b3"), width=2)
rs = sh.rotate(-2, expand=True, resample=Image.BICUBIC); mini.alpha_composite(rs, (10 + int(22 * sc * 2) - 4, 10 + int(44 * sc * 2) - 6))
clip = Image.open(U + "lwf_report_clip_v1.png").resize((int(150 * sc * 2), int(70 * sc * 2)), Image.LANCZOS); mini.alpha_composite(clip, (10 + (bw - clip.width) // 2, 12))
mini.save(os.path.join(OUT, "lwf_results_mini_clipboard_v1.png"))

# ---- markers
K = 2
tag = Image.new("RGBA", (60 * K * SS, 38 * K * SS), (0, 0, 0, 0)); d = ImageDraw.Draw(tag); s_ = K * SS
d.rounded_rectangle([2 * s_, 2 * s_, 58 * s_, 28 * s_], 13 * s_, fill=hx("d8f126"), outline=hx("0e1f1a"), width=2 * s_)
d.polygon([(23 * s_, 27 * s_), (37 * s_, 27 * s_), (30 * s_, 36 * s_)], fill=hx("d8f126"), outline=hx("0e1f1a"))
d.rectangle([24 * s_, 25 * s_, 36 * s_, 28 * s_], fill=hx("d8f126"))
d.text((30 * s_, 15 * s_), "YOU", font=H.sans(13 * SS, "Bold"), fill=hx("0e1f1a"), anchor="mm")
tag.resize((120, 76), Image.LANCZOS).save(os.path.join(OUT, "lwf_you_tag_v1.png"))
ring = Image.new("RGBA", (256 * SS, 256 * SS), (0, 0, 0, 0)); d = ImageDraw.Draw(ring); c = 128 * SS
for r, w, a in ((118, 10, 255), (96, 4, 150)): d.ellipse([c - r * SS, c - r * SS, c + r * SS, c + r * SS], outline=hx("d8f126", a), width=w * SS)
for k in range(4):                                                         # four ticks, so it reads as "you are here" not a selection
    t = math.radians(k * 90); d.line([(c + 100 * SS * math.cos(t), c + 100 * SS * math.sin(t)), (c + 126 * SS * math.cos(t), c + 126 * SS * math.sin(t))], fill=hx("0e1f1a", 200), width=6 * SS)
ring.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT, "lwf_you_ring_v1.png"))
# edge badge in two parts so the person stays upright: the disc (r 22) and an arrow that rotates about the disc's centre
H.L.ICONS["user"] = H.load_svg("user")
e = Image.new("RGBA", (50 * K * SS, 50 * K * SS), (0, 0, 0, 0)); d = ImageDraw.Draw(e); cx = cy = 25 * s_
d.ellipse([cx - 22 * s_, cy - 22 * s_, cx + 22 * s_, cy + 22 * s_], fill=hx("d8f126"), outline=hx("0e1f1a"), width=2 * s_)
e.alpha_composite(H.L.draw_icon("user", 22 * s_, hx("0e1f1a")), (int(cx - 11 * s_), int(cy - 11 * s_)))
e.resize((100, 100), Image.LANCZOS).save(os.path.join(OUT, "lwf_you_edge_disc_v1.png"))
a_ = Image.new("RGBA", (84 * K * SS, 84 * K * SS), (0, 0, 0, 0)); d = ImageDraw.Draw(a_); c = 42 * s_
d.polygon([(c + 40 * s_, c), (c + 23 * s_, c - 10 * s_), (c + 23 * s_, c + 10 * s_)], fill=hx("d8f126"), outline=hx("0e1f1a"))
a_.resize((168, 168), Image.LANCZOS).save(os.path.join(OUT, "lwf_you_edge_arrow_v1.png"))
print("ok", len(os.listdir(OUT)))
