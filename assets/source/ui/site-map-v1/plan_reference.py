# The festival site, drawn from the game's own layout (BuildLayout.StandardBuildLayout, FestivalStage, Main.Pond, FarmScene):
# world metres, x east, z south (north up). cell -> world: c * 0.5 - 63.75.
import math, sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter
sys.path.insert(0, "C:/Projects/festival-tycoon/assets/source/ui/steward-shift-hud-v1"); import hud_reference as H
for n in ("utensils", "door-closed", "trash-2", "shield", "beer", "music", "droplet", "heart-pulse", "user", "triangle-alert", "users", "wrench", "truck", "map-pin"): H.L.ICONS[n] = H.load_svg(n)
def C(cx, cz): return (cx * 0.5 - 63.75, cz * 0.5 - 63.75)
FD = "C:/Projects/festival-tycoon/game/assets/ui/fonts/"
def slab(px): return ImageFont.truetype(FD + "ZillaSlab-Bold.ttf", int(px))
def sans(px, w="SemiBold"):
    f = ImageFont.truetype(FD + "SourceSans3-Variable.ttf", int(px)); f.set_variation_by_name(w); return f
def marker(px): return ImageFont.truetype(FD + "ZillaSlab-SemiBold.ttf", int(px))
MARKER_SKEW = 0.22                                                          # Control's "marker": Zilla Slab SemiBold, slanted


def marker_text(im, xy, txt, px, fill, anchor="mm", multiline=False):
    """slanted Zilla Slab, drawn onto its own layer and sheared (Godot: draw_set_transform with a skew of -0.22 rad-ish x-shear)"""
    f = marker(px); tmp = Image.new("RGBA", (1, 1)); dd = ImageDraw.Draw(tmp)
    bb = (dd.multiline_textbbox if multiline else dd.textbbox)((0, 0), txt, font=f, anchor="la", **({"align": "center"} if multiline else {}))
    w, h = int(bb[2]) + 8, int(bb[3]) + 8; pad = int(h * MARKER_SKEW) + 4
    lay = Image.new("RGBA", (w + pad, h), (0, 0, 0, 0)); ld = ImageDraw.Draw(lay)
    (ld.multiline_text if multiline else ld.text)((pad, 2), txt, font=f, fill=fill, **({"align": "center"} if multiline else {}))
    lay = lay.transform(lay.size, Image.AFFINE, (1, MARKER_SKEW, 0, 0, 1, 0), resample=Image.BICUBIC)
    x, y = xy; ax, ay = anchor[0], anchor[1]
    ox = {"l": -pad, "m": -(w + pad) / 2 - pad / 2 + pad, "r": -(w + pad)}[ax]; oy = {"t": 0, "m": -h / 2, "b": -h}[ay]
    im.alpha_composite(lay, (int(x + ox), int(y + oy)))
hx = H.hx

T1 = [("tap", "Tap", C(103, 128)), ("loo", "", C(117, 121)), ("loo", "Loos", C(112, 121)), ("van", "Chips", C(140, 162)), ("bar", "Bar", C(140, 148)),
      ("aid", "First aid", C(139, 120)), ("post", "Steward post", C(119, 171)), ("bin", "", C(121, 148))]
T2 = [("tap", "Tap 2", C(159, 139)), ("loo", "", C(122, 121)), ("bar", "Bar 2", C(152, 146)), ("van", "Curry", C(170, 128)),
      ("bin", "", C(156, 152)), ("bin", "", C(178, 134)), ("marquee", "Marquee", (-11.0, 20.5))]
ICON = {"tap": ("droplet", "2b6e66"), "loo": ("door-closed", "2b6e66"), "van": ("utensils", "9c7424"), "bar": ("beer", "9c7424"), "aid": ("heart-pulse", "b8551e"),
        "post": ("shield", "1f2a26"), "bin": ("trash-2", "56615a"), "marquee": ("users", "1f2a26"), "stage": ("music", "1f2a26")}


def draw_site(tier=2, size=1200, style="printed", calls=(), crew=(), you=None, labels=True, icon_px=None, label_k=1.45, stands=True):
    """returns an RGBA image of the whole 64 x 64 m field (plus a margin), north up"""
    m = 4; span = 64 + 2 * m; k = size / span
    def P(x, z): return ((x + 32 + m) * k, (z + 32 + m) * k)
    printed = style == "printed"
    paper = hx("f7f1e3") if printed else (0, 0, 0, 0)
    im = Image.new("RGBA", (size, size), paper); d = ImageDraw.Draw(im)
    grass, hedge, track, bldg, water, ink = ((hx("dfe6c6"), hx("7f9a63"), hx("e8cfa3"), hx("cfc6b2"), hx("a9cfd8"), hx("1f2a26")) if printed else
                                            (hx("3d5a3a", 235), hx("1f3524"), hx("8a7351"), hx("56615a"), hx("4f7d8c"), hx("f5ebd6")))
    d.rectangle([*P(-32, -32), *P(32, 32)], fill=grass)
    for a, b in (((-32, -32), (32, -32)), ((-32, -32), (-32, 32)), ((32, -32), (32, 32)), ((-32, 32), (-3, 32)), ((3, 32), (32, 32))):   # hedges, gate gap
        d.line([P(*a), P(*b)], fill=hedge, width=int(1.2 * k))
    d.rectangle([*P(-2, -26), *P(2, 34)], fill=track)                               # the vehicle track, gate to yard
    for (x, z, w, h, lab) in ((-22, -14, 11, 8, "Farmhouse"), (19, -17, 10, 8, "Barn"), (0, -23, 14, 9, "Big barn")):
        d.rectangle([*P(x - w / 2, z - h / 2), *P(x + w / 2, z + h / 2)], fill=bldg, outline=ink if printed else None, width=2)
        if labels: d.text(P(x, z), lab, font=sans(k * 1.6), fill=hx("56615a") if printed else hx("b9c4b8"), anchor="mm")
    # the pond: an irregular blob round (24, 24.5)
    pts = [P(24 + (5.6 + 0.8 * math.sin(3 * t)) * math.cos(t), 24.5 + (4.6 + 0.6 * math.sin(2 * t + 1)) * math.sin(t)) for t in [i / 40 * 2 * math.pi for i in range(40)]]
    d.polygon(pts, fill=water, outline=hx("6f9fb0") if printed else None)
    if labels: d.text(P(24, 24.5), "Pond", font=sans(k * 1.6), fill=hx("2e5d6b") if printed else hx("cfe6ec"), anchor="mm")
    # stages
    def stage(x, z, w, h, lab, crowd):
        d.rectangle([*P(x - w / 2, z - h / 2), *P(x + w / 2, z + h / 2)], fill=hx("1f2a26") if printed else hx("e8bf5a"))
        cx, cz, cw, ch = crowd
        d.rounded_rectangle([*P(cx - cw / 2, cz - ch / 2), *P(cx + cw / 2, cz + ch / 2)], int(k * 1.5), outline=hx("1f2a26", 90) if printed else hx("e8bf5a", 90), width=2)
        if labels:
            if lab.startswith("Pond"): d.text(P(x + 3.4, z), lab.replace(" ", "\n"), font=slab(k * 2.0), fill=ink, anchor="lm")
            else: d.text(P(x, z - h / 2 - 1.6), lab, font=slab(k * 2.0), fill=ink, anchor="mb")
    stage(-16.5, 11, 4.5, 9, "Trailer Stage", (-9.5, 11, 9, 12))
    if tier >= 2: stage(20, 17, 4, 6, "Pond Stage", (13.5, 16.5, 8, 9))
    if labels:
        d.text(P(0, 33.6), "GATE", font=sans(k * 1.7, "Bold"), fill=ink, anchor="mt")
    items = (T1 + (T2 if tier >= 2 else [])) if stands else []
    for kind, lab, (x, z) in items:
        ic, c = ICON[kind]; r = (icon_px if icon_px else k * 1.6) * (1.0 if kind != "bin" else 0.7)
        X, Z = P(x, z)
        if printed:
            d.ellipse([X - r, Z - r, X + r, Z + r], fill=hx("ffffff"), outline=hx(c), width=max(2, int(k * 0.25)))
            im.alpha_composite(H.L.draw_icon(ic, int(r * 1.3), hx(c)), (int(X - r * 0.65), int(Z - r * 0.65)))
        else:
            d.ellipse([X - r * 0.8, Z - r * 0.8, X + r * 0.8, Z + r * 0.8], fill=hx("0e1f1a", 220))
            im.alpha_composite(H.L.draw_icon(ic, int(r * 1.15), hx("f5ebd6", 230)), (int(X - r * 0.575), int(Z - r * 0.575)))
        d = ImageDraw.Draw(im)
        if labels and lab:
            if lab == "Bar 2": d.text((X + r + 3, Z), lab, font=sans(k * label_k, "Bold"), fill=ink, anchor="lm")
            elif lab == "Tap 2": d.text((X - r - 3, Z), lab, font=sans(k * label_k, "Bold"), fill=ink, anchor="rm")
            else: d.text((X, Z + r + 2), lab, font=sans(k * label_k, "Bold"), fill=ink, anchor="mt")
    return im, P, k


def pin(im, X, Y, k, urgent=False, label=None, sticker=True):
    """a live radio-call pin: on the printed plan a round sticker with a marker number; on the minimap the HUD's map-pin"""
    d = ImageDraw.Draw(im); c = hx("b8551e") if urgent else hx("d8a43b")
    if sticker:
        r = k * 1.9
        sh = Image.new("RGBA", im.size, (0, 0, 0, 0)); ImageDraw.Draw(sh).ellipse([X - r + 2, Y - r + 3, X + r + 2, Y + r + 3], fill=(0, 0, 0, 70)); im.alpha_composite(sh.filter(ImageFilter.GaussianBlur(2)))
        d = ImageDraw.Draw(im); d.ellipse([X - r, Y - r, X + r, Y + r], fill=c, outline=hx("ffffff"), width=max(2, int(k * 0.3)))
        im.alpha_composite(H.L.draw_icon("heart-pulse" if urgent else "triangle-alert", int(r * 1.15), hx("ffffff")), (int(X - r * 0.575), int(Y - r * 0.575)))
    else:
        s = int(k * 3.4); im.alpha_composite(H.L.draw_icon("map-pin", s, c), (int(X - s / 2), int(Y - s * 0.95)))
    if label:
        marker_text(im, (X + k * 1.6, Y - k * 1.6), label, k * 1.5, c, "lb")


def you_arrow(im, X, Y, k, heading_deg, printed=True):
    """hi-vis arrow; heading 0 = north (up), clockwise"""
    d = ImageDraw.Draw(im); a = math.radians(heading_deg); r = k * 2.2
    def R(px, py): return (X + px * math.cos(a) - py * math.sin(a), Y + px * math.sin(a) + py * math.cos(a))
    pts = [R(0, -r * 1.25), R(r * 0.85, r * 0.85), R(0, r * 0.35), R(-r * 0.85, r * 0.85)]
    d.polygon(pts, fill=hx("d8f126"), outline=hx("0e1f1a")); d.line(pts + [pts[0]], fill=hx("0e1f1a"), width=max(2, int(k * 0.3)))


def crew_dot(im, X, Y, k, busy=False, name=None, printed=True):
    d = ImageDraw.Draw(im); r = k * 0.9
    d.ellipse([X - r, Y - r, X + r, Y + r], fill=hx("2b6e66") if not busy else hx("8fa59c"), outline=hx("ffffff"), width=max(1, int(k * 0.2)))
    if name: d.text((X + r + 3, Y), name, font=sans(k * 1.3, "Bold"), fill=hx("2b6e66") if printed else hx("cfe6df"), anchor="lm")
