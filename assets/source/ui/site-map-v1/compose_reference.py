# The reference implementation of INTEGRATION.md: builds the printed plan texture and the minimap ONLY from out/ sprites,
# with the documented mapping, label rule, note rule and live layer. The game redraws the same thing (SubViewport / Control).
import os, math
from PIL import Image, ImageDraw, ImageFont
import plan_reference as P_
from plan_reference import hx, sans, slab, marker_text
HERE = os.path.dirname(os.path.abspath(__file__)); O = os.path.join(HERE, "out")
def art(n): return Image.open(os.path.join(O, n)).convert("RGBA")
W, HT = 1400, 1276; X0, Z0, K, MAPX, MAPY = -32.0, -13.0, 20.625, 40, 150
def to_plan(x, z): return (MAPX + (x - X0) * K, MAPY + (z - Z0) * K)
INK, MUTED, MARK = hx("1f2a26"), hx("56615a"), hx("2b4a8c")
LEGEND = [("tap", "Water"), ("loo", "Loos"), ("bar", "Bar"), ("van", "Food"), ("aid", "First aid"), ("bin", "Bin"), ("post", "Post")]


def stands_for(tier):
    """(kind, label, x, z) from the default layout; in game, from CaptureBuildPlacements() + the marquee build"""
    out = []
    for kind, lab, (x, z) in P_.T1 + (P_.T2 if tier >= 2 else []): out.append((kind, lab, x, z))
    return out


def notes_for(tier, stands):
    """Control's marker notes: fixed text, anchored to things, so they move with the layout"""
    van = next(((x, z) for k, l, x, z in stands if k == "van"), None)
    n = [((0 - 6.5, 32 - 2.0), "IN / OUT"), ((-2 - 7.5, -9), "keep the lane\nclear for Henry")]
    if tier >= 2: n.append(((14.25 + 12.75, 18 - 7.0), "generator -\nno guests!"))
    elif van: n.append(((van[0] + 6.75, van[1] + 6.75), "chips queue\ngets long here"))
    return n


def place_label(d, X, Y, r, text, font, taken):
    tw = d.textlength(text, font=font); th = font.size
    for (ax, ay, anchor) in ((X, Y + r + 2, "mt"), (X - r - 4, Y, "rm"), (X + r + 4, Y, "lm"), (X, Y - r - 2, "mb")):
        if anchor == "mt": box = (ax - tw / 2, ay, ax + tw / 2, ay + th)
        elif anchor == "rm": box = (ax - tw, ay - th / 2, ax, ay + th / 2)
        elif anchor == "lm": box = (ax, ay - th / 2, ax + tw, ay + th / 2)
        else: box = (ax - tw / 2, ay - th, ax + tw / 2, ay)
        if not any(box[0] < b[2] and box[2] > b[0] and box[1] < b[3] and box[3] > b[1] for b in taken):
            taken.append(box); return ax, ay, anchor
    return X, Y + r + 2, "mt"


def plan(tier, calls=(), crew=(), you=None, allrounder=True):
    im = art(f"plan_base_t{tier}_v1.png"); d = ImageDraw.Draw(im)
    d.text((40, 34), "LOWER WITTERING FESTIVAL", font=sans(26, "Bold"), fill=MUTED); d.text((40, 64), "Site plan", font=slab(54), fill=INK)
    d.text((W - 40, 82), f"TIER {tier} · STEWARDS' COPY", font=sans(22, "Bold"), fill=hx("b8551e"), anchor="ra")
    st = stands_for(tier); taken = []
    for kind, lab, x, z in st:                                                  # icons first, so labels avoid every icon
        X, Y = to_plan(x, z); ic = art(f"plan_icon_{kind}_v1.png"); im.alpha_composite(ic, (int(X - ic.width / 2), int(Y - ic.height / 2)))
        taken.append((X - ic.width / 2, Y - ic.height / 2, X + ic.width / 2, Y + ic.height / 2))
    d = ImageDraw.Draw(im); f = sans(32, "Bold")
    for kind, lab, x, z in st:
        if not lab: continue
        X, Y = to_plan(x, z); r = art(f"plan_icon_{kind}_v1.png").width / 2
        ax, ay, anchor = place_label(d, X, Y, r, lab, f, taken); d.text((ax, ay), lab, font=f, fill=INK, anchor=anchor)
    for (x, z), txt in notes_for(tier, st): marker_text(im, to_plan(x, z), txt, 31, MARK, "mm", multiline=True)
    # live layer
    for kind, x, z, lab in calls:
        X, Y = to_plan(x, z); s = art(f"plan_sticker_{kind}_v1.png"); im.alpha_composite(s, (int(X - 44), int(Y - 44)))
        marker_text(im, (X + 33, Y - 33), lab, 31, hx("b8551e") if kind == "urgent" else hx("d8a43b"), "lb")
    if allrounder:
        d = ImageDraw.Draw(im)
        for x, z, busy, name in crew:
            X, Y = to_plan(x, z); s = art(f"plan_crew_{'busy' if busy else 'free'}_v1.png"); im.alpha_composite(s, (int(X - 20), int(Y - 20)))
            d = ImageDraw.Draw(im); d.text((X + 24, Y), name, font=sans(27, "Bold"), fill=hx("2b6e66"), anchor="lm")
    if you:
        X, Y = to_plan(you[0], you[1]); a = art("plan_you_v1.png").rotate(-you[2], resample=Image.BICUBIC); im.alpha_composite(a, (int(X - 56), int(Y - 56)))
    # legend: row 1 at y 1170, row 2 at y 1220
    d = ImageDraw.Draw(im); x = 40
    for kind, lab in LEGEND:
        ic = art(f"plan_icon_{kind}_v1.png").resize((32, 32), Image.LANCZOS); im.alpha_composite(ic, (int(x), 1174)); d = ImageDraw.Draw(im)
        d.text((x + 38, 1190), lab, font=sans(22, "Bold"), fill=INK, anchor="lm"); x += 38 + d.textlength(lab, font=sans(22, "Bold")) + 22
    row2 = [("plan_sticker_call_v1.png", "radio call"), ("plan_sticker_urgent_v1.png", "urgent"), ("plan_you_v1.png", "you")] + ([("plan_crew_free_v1.png", "crew")] if allrounder else [])
    x = 40
    for n, lab in row2:
        s = art(n).resize((34, 34), Image.LANCZOS); im.alpha_composite(s, (int(x), 1222)); d = ImageDraw.Draw(im)
        d.text((x + 40, 1239), lab, font=sans(22, "Bold"), fill=INK, anchor="lm"); x += 40 + d.textlength(lab, font=sans(22, "Bold")) + 26
    d.text((W - 40, 1239), "M  put away", font=sans(22, "Bold"), fill=MUTED, anchor="rm")
    im.alpha_composite(art("plan_laminate_v1.png")); return im


KM = 2 * 88 / 28                                                            # minimap: px per metre at 2x (3.143 units/m)
def minimap(tier, you, heading, calls=(), crew=(), allrounder=True):
    """352 x 352 (176 units at 2x); heading in degrees clockwise from north; the map turns so 'ahead' is up"""
    base = art(f"mini_base_t{tier}_v1.png"); m = 4; c0 = (you[0] + 32 + m) * KM, (you[1] + 32 + m) * KM
    half = 264; crop = base.crop((int(c0[0] - half), int(c0[1] - half), int(c0[0] + half), int(c0[1] + half)))
    lay = Image.new("RGBA", crop.size, (0, 0, 0, 0)); lay.alpha_composite(crop)
    a = math.radians(heading)
    def local(x, z):                                                        # world -> minimap (centre 0,0, up = ahead)
        dx, dz = x - you[0], z - you[1]; return (dx * math.cos(a) + dz * math.sin(a)) * KM, (-dx * math.sin(a) + dz * math.cos(a)) * KM
    rot = lay.rotate(heading, resample=Image.BICUBIC)                       # PIL: positive = CCW; Godot RotationDegrees = -heading
    out = Image.new("RGBA", (352, 352), hx("0e1f1a", 200)); o = (rot.width - 352) // 2; out.alpha_composite(rot.crop((o, o, o + 352, o + 352)))
    def put(img, lx, ly, anchor="c"):
        X, Y = 176 + lx, 176 + ly; oy = img.height if anchor == "b" else img.height / 2
        out.alpha_composite(img, (int(X - img.width / 2), int(Y - oy)))
    for kind, lab, x, z in stands_for(tier):
        lx, ly = local(x, z)
        if math.hypot(lx, ly) < 168: put(art(f"mini_icon_{kind}_v1.png"), lx, ly)     # icons stay upright
    if allrounder:
        for x, z, busy, name in crew:
            lx, ly = local(x, z)
            if math.hypot(lx, ly) < 168: put(art(f"mini_crew_{'busy' if busy else 'free'}_v1.png"), lx, ly)
    d = ImageDraw.Draw(out)
    for kind, x, z, lab in calls:
        lx, ly = local(x, z); dist = math.hypot(lx, ly) / KM; rim = 150
        if math.hypot(lx, ly) > rim:
            ux, uy = lx / math.hypot(lx, ly), ly / math.hypot(lx, ly); lx, ly = ux * rim, uy * rim
            cx, cy = 176 + lx - ux * 48, 176 + ly - uy * 48 - 12; t = f"{round(dist)} m"; tw = d.textlength(t, font=sans(21, "Bold")) / 2 + 10
            d.rounded_rectangle([cx - tw, cy - 16, cx + tw, cy + 16], 16, fill=hx("0e1f1a", 210)); d.text((cx, cy), t, font=sans(21, "Bold"), fill=hx("b8551e") if kind == "urgent" else hx("d8a43b"), anchor="mm")
        put(art(f"mini_pin_{kind}_v1.png"), lx, ly, "b")
    put(art("mini_you_v1.png"), 0, 0)
    mask = Image.open(os.path.join(O, "mini_mask_v1.png")).convert("L"); res = Image.new("RGBA", (352, 352), (0, 0, 0, 0)); res.paste(out, (0, 0), mask)
    res.alpha_composite(art("mini_frame_v1.png"))
    na = math.radians(-heading); n = art("mini_north_v1.png"); res.alpha_composite(n, (int(176 + math.sin(na) * 156 - 18), int(176 - math.cos(na) * 156 - 18)))
    return res


if __name__ == "__main__":
    V = os.path.join(HERE, "verification")
    CALLS = (("urgent", 15, 15, "collapse!"), ("call", -7, -1, "loo 2 stuck?")); CREW = ((4, 4, False, "Clive"), (-8, 18, False, "Jo"), (14, 8, True, "Terry"))
    plan(2, CALLS, CREW, (-9, 15, 55)).convert("RGB").save(os.path.join(V, "plan-t2-allrounder.png"))
    plan(2, CALLS, (), (-9, 15, 55), allrounder=False).convert("RGB").save(os.path.join(V, "plan-t2-steward-only.png"))
    plan(1, (("call", -7, -1, "loo 2 stuck?"),), (), (-4, 6, 60), allrounder=False).convert("RGB").save(os.path.join(V, "plan-t1.png"))
    minimap(2, (-9, 15), 55, CALLS, CREW).save(os.path.join(V, "minimap-t2-allrounder.png"))
    minimap(2, (-9, 15), 55, CALLS, (), allrounder=False).save(os.path.join(V, "minimap-t2-steward-only.png"))
    minimap(1, (-4, 6), 200, (("call", -7, -1, "loo 2 stuck?"),), (), allrounder=False).save(os.path.join(V, "minimap-t1-facing-ssw.png"))
    print("ok")
