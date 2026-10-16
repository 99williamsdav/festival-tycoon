# Reference renderer for the approved trader flyer board (direction A, opened B's way). Every number is a 1280-unit mockup
# measurement for Ui.S/Ui.Px; INTEGRATION.md lists them. Stats come from the raw FoodTraders.cs values through the pip rules.
import os, sys, math
from PIL import Image, ImageDraw, ImageFilter
sys.path.insert(0, "C:/Projects/festival-tycoon/assets/source/ui/steward-shift-hud-v1"); import hud_reference as H
from hud_reference import Shot, R, text, tw, col, slab, sans, icon, shadowed, S
D = os.path.dirname(os.path.abspath(__file__)) + "/"; M = D + "verification/"; os.makedirs(M, exist_ok=True)
CAP = "C:/Projects/festival-tycoon/reports/evidence/classic-stress/builder-native-final2/15-food-traders-page-one.png"
for n in ("lock", "check", "chevron-down", "chevron-right", "x", "pound-sterling", "truck", "star", "utensils", "clock", "users"): H.L.ICONS[n] = H.load_svg(n)
hx = H.hx
import re as _re
def _rects(name):
    """Lucide <rect> with attributes in any order -> a path (corners square; fine at icon size)"""
    t = open(f"C:/Projects/festival-tycoon/game/assets/ui/icons/{name}.svg").read(); out = []
    for r in _re.findall(r"<rect([^>]*)/?>", t):
        a = dict(_re.findall(r'(\w+)="([\d.]+)"', r)); x, y, w, h = (float(a.get(k, 0)) for k in ("x", "y", "width", "height"))
        out.append(f"M{x} {y}h{w}v{h}h{-w}Z")
    return out
H.L.ICONS["lock"] = H.load_svg("lock") + _rects("lock")


VAN = {k: Image.open(D + f"out/trader_art_{k}_v1.png").convert("RGBA") for k in ("chip_block", "pizza", "korma")}
VAN["coffee"] = Image.open(D + "out/trader_art_coffee_concept_v1.png").convert("RGBA")


def pip(v): return max(0, min(5, int(v + 0.5)))
def stats(service_permille, filling_pct, enjoyment_pct, appeal_pennies):
    """the pip rules (INTEGRATION.md): FoodTraders.cs values -> 0..5"""
    return dict(speed=pip(5000 / service_permille), fill=pip((filling_pct - 50) / 25), treat=pip((enjoyment_pct - 70) / 14), pull=pip(1 + appeal_pennies / 400))


#   FoodTraders.cs:       service  filling  enjoyment  appeal
T = [dict(key="chip_block", name="Chip Off The Old Block", cuisine="Chips", pun="Fry Me to the Moon", fee=40, price="£3.00", tier=1, c="4fa3d9", **stats(1000, 100, 100, 0)),
     dict(key="pizza", name="Pizza the Action", cuisine="Pizza", pun="That's Amore", fee=60, price="£4.50", tier=1, c="c4573a", **stats(2500, 115, 140, 1200)),
     dict(key="korma", name="Korma Chameleon", cuisine="Curry", pun="Karma karma korma", fee=30, price="£5.00", tier=2, c="5b2a4f", **stats(1750, 175, 130, 400)),
     dict(key="coffee", name="Brew Fighters", cuisine="Coffee", pun="The Best of Brew", fee=None, price="—", speed=None, fill=None, treat=None, pull=None, tier=1, c="1f5f5a", soon=True)]
STATS = [("Speed", "speed"), ("Filling", "fill"), ("Treat", "treat"), ("Pull", "pull")]
TIER = 1


def pips(d, x, y, v, c, n=5, size=9, gap=3):
    for i in range(n):
        box = R(x + i * (size + gap), y, x + i * (size + gap) + size, y + size)
        if v is None: d.rounded_rectangle(box, 2 * S, outline=col("rule"), width=S)
        else: d.rounded_rectangle(box, 2 * S, fill=hx(c) if i < v else col("rule"))


def van_img(s, key, x, y, w, h, grey=False):
    im = VAN[key].copy(); r = min(w / im.width, h / im.height); im = im.resize((int(im.width * r * S), int(im.height * r * S)), Image.LANCZOS)
    if grey:
        g = im.convert("LA").convert("RGBA"); a = im.split()[3].point(lambda v: int(v * 0.55)); g.putalpha(a); im = g
    s.im.alpha_composite(im, (int((x + w / 2) * S - im.width / 2), int((y + h) * S - im.height))); s.d = ImageDraw.Draw(s.im)


def compare(t, cur):
    """the one-line 'what you gain or lose' vs the van's current trader"""
    if cur is None or t["key"] == cur["key"] or t.get("soon"): return []
    out = []
    if t["fee"] != cur["fee"]: out.append((f"£{abs(t['fee'] - cur['fee'])} {'more' if t['fee'] > cur['fee'] else 'less'} pitch", t["fee"] > cur["fee"]))
    for lab, k in (("quicker", "speed"), ("more filling", "fill"), ("more treat", "treat"), ("more pull", "pull")):
        dv = t[k] - cur[k]
        if dv: out.append((({"quicker": "quicker", "more filling": "more filling", "more treat": "bigger treat", "more pull": "pulls more"}[lab] if dv > 0 else {"quicker": "slower", "more filling": "less filling", "more treat": "smaller treat", "more pull": "pulls fewer"}[lab]), dv > 0))
    return out[:3]


def card(s, x, y, w, h, t, state="normal", cur=None, van_label=None, note=None):
    """the trader flyer: paper card, van art, name, pun, cuisine tag, pays-you chip, four pip meters, compare line"""
    lift = -4 if state == "hover" else 0; y += lift
    locked = state in ("locked", "elsewhere"); soon = t.get("soon")
    edge = {"hover": col("gold"), "selected": col("teal")}.get(state)
    shadowed(s, R(x, y, x + w, y + h), 8, col("bright") if not locked else hx("eee6d3"), outline=edge, width=3 if edge else 0, shadow=8 if state == "hover" else 5, salpha=120 if state == "hover" else 70)
    d = s.d
    d.rounded_rectangle(R(x + 6, y + 6, x + w - 6, y + 92), 6 * S, fill=hx(t["c"], 40 if not locked else 18))
    van_img(s, t["key"], x + 10, y + 10, w - 20, 80, grey=locked or soon); d = s.d
    d.rounded_rectangle(R(x + 12, y + 12, x + 12 + tw(d, t["cuisine"].upper(), sans(9.5, "Bold")) + 14, y + 28), 8 * S, fill=hx(t["c"]))
    text(d, (x + 19, y + 20), t["cuisine"].upper(), sans(9.5, "Bold"), col("bright"), "lm")
    text(d, (x + 12, y + 100), t["name"], slab(15 if len(t["name"]) < 20 else 13.5, True), col("ink") if not locked else col("inkm"))
    text(d, (x + 12, y + 121), "\u201c" + t["pun"] + "\u201d", H.sans(11.5, "Italic") if False else sans(11.5, "SemiBold"), hx(t["c"]) if not locked else col("inkm"))
    # pays-you chip + price to guests
    fee = "pays you —" if t["fee"] is None else f"pays you £{t['fee']}"
    d.rounded_rectangle(R(x + 12, y + 142, x + 12 + tw(d, fee, sans(11, "Bold")) + 16, y + 160), 9 * S, fill=col("tealwash") if not locked else col("rule"))
    text(d, (x + 20, y + 151), fee, sans(11, "Bold"), col("teal") if not locked else col("inkm"), "lm")
    text(d, (x + w - 12, y + 151), f"{t['price']} a portion", sans(10.5, "SemiBold"), col("inkm"), "rm")
    for i, (lab, k) in enumerate(STATS):
        yy = y + 170 + i * 15
        text(d, (x + 12, yy + 4), lab, sans(10.5, "SemiBold"), col("inkm"), "lm"); pips(d, x + w - 72, yy, t[k], t["c"] if not locked else "b9c4b8")
    cy = y + 232
    for lab, good in compare(t, cur):
        tw_ = tw(d, lab, sans(10, "Bold")) + 12
        if cy > y + h - 30: break
        d.rounded_rectangle(R(x + 12, cy, x + 12 + tw_, cy + 15), 7 * S, fill=col("tealwash") if good else col("alertwash"))
        text(d, (x + 18, cy + 7.5), lab, sans(10, "Bold"), col("teal") if good else col("alert"), "lm"); cy += 18
    if state == "selected":
        st = f"BOOKED · {van_label}" if van_label else "BOOKED"
        lay = s.layer(); ld = ImageDraw.Draw(lay); bw = tw(d, st, slab(13, True)) + 20
        sy = y + 248
        ld.rounded_rectangle(R(x + w / 2 - bw / 2, sy, x + w / 2 + bw / 2, sy + 26), 4 * S, outline=col("teal"), width=2 * S); ld.text(((x + w / 2) * S, (sy + 13) * S), st, font=slab(13, True), fill=col("teal"), anchor="mm")
        lay = lay.rotate(5, center=((x + w / 2) * S, (sy + 13) * S), resample=Image.BICUBIC); s.put(lay); d = s.d
    if state == "locked":                                                     # tier lock: a strip of hazard tape across the art
        lay = s.layer(); ld = ImageDraw.Draw(lay)
        ld.rectangle(R(x - 6, y + 42, x + w + 6, y + 62), fill=col("ink", 235)); ld.text(((x + w / 2) * S, (y + 52) * S), f"FROM TIER {t['tier']}", font=sans(11, "Bold"), fill=col("gold"), anchor="mm")
        lay = lay.rotate(-8, center=((x + w / 2) * S, (y + 52) * S), resample=Image.BICUBIC); s.put(lay); d = s.d
        icon(s.im, "lock", x + w - 26, y + 100, 14, col("inkm")); d = s.d
    if state == "elsewhere":
        tag = note or "At van 1 · change van 1 first"; d.rounded_rectangle(R(x + 12, y + 56, x + 12 + tw(d, tag, sans(10.5, "Bold")) + 16, y + 74), 9 * S, fill=col("ink"))
        text(d, (x + 20, y + 65), tag, sans(10.5, "Bold"), col("gold"), "lm")
    if soon:
        lay = s.layer(); ld = ImageDraw.Draw(lay)
        ld.rectangle(R(x - 6, y + 42, x + w + 6, y + 62), fill=col("gold", 240)); ld.text(((x + w / 2) * S, (y + 52) * S), "CLASSIC · COMING SOON", font=sans(10.5, "Bold"), fill=col("bar"), anchor="mm")
        lay = lay.rotate(-8, center=((x + w / 2) * S, (y + 52) * S), resample=Image.BICUBIC); s.put(lay); d = s.d
    if note and state not in ("elsewhere",):
        text(d, (x + 12, y + h - 18), note, sans(10, "SemiBold"), col("inkm"))
    return s


def base(dim=False):
    s = Shot(CAP)
    if dim:
        lay = s.layer(); ImageDraw.Draw(lay).rectangle(R(456, 60, 1280, 612), fill=col("bardeep", 150)); s.put(lay)
    return s


def drawer_clear(s):
    d = s.d; d.rectangle(R(34, 184, 438, 600), fill=H.hx("f6efdd")); return d


VANS = [("Van 1", "chip_block"), ("Van 2", "pizza"), ("Van 3", "chip_block"), ("Van 4", "chip_block"), ("Van 5", "chip_block")]
def tk(key): return next(t for t in T if t["key"] == key)


def pitch_lines(s, y0, sel=None, compact=False):
    """the drawer's van list: one line per placed van, with its trader's thumbnail"""
    d = s.d
    text(d, (46, y0), "FOOD VAN TRADERS · THEY PAY YOU TO PITCH", sans(9.5, "Bold"), col("inkm")); y = y0 + 18
    for i, (lab, key) in enumerate(VANS):
        t = tk(key); on = sel == i
        d.rounded_rectangle(R(44, y, 426, y + 44), 6 * S, fill=col("bright") if not on else hx("fbf0d4"), outline=col("gold") if on else col("rule"), width=2 * S if on else S)
        d.rounded_rectangle(R(50, y + 5, 104, y + 39), 4 * S, fill=hx(t["c"], 40)); van_img(s, key, 52, y + 6, 50, 32); d = s.d
        text(d, (112, y + 6), lab.upper(), sans(9, "Bold"), col("inkm")); text(d, (112, y + 18), t["name"], slab(13.5, True), col("ink"))
        text(d, (420 - 26, y + 22), f"£{t['fee']}", sans(12, "Bold"), col("teal"), "rm")
        icon(s.im, "chevron-right", 402, y + 15, 14, col("inkm")); d = s.d; y += 50
    return y


# ===================================================== A · Flyer board (recommended)
def flyer_board(sel_van=1, hover=None, cur_key="pizza", tier=1, second_chips=False, name="A1"):
    s = base(dim=True); d = drawer_clear(s)
    text(d, (46, 192), "Food van", slab(15, True), col("ink")); y = pitch_lines(s, 216, sel=sel_van)
    x0, y0, x1, y1 = 466, 74, 1262, 576
    shadowed(s, R(x0, y0, x1, y1), 10, hx("f5ebd6"), shadow=16, salpha=140); d = s.d
    text(d, (x0 + 20, y0 + 16), "WHO'S PITCHING?", sans(10, "Bold"), col("inkm"))
    text(d, (x0 + 20, y0 + 30), f"Van {sel_van + 1} · by the Pond Stage bar", slab(22, True), col("ink"))
    d.rounded_rectangle(R(x1 - 40, y0 + 16, x1 - 16, y0 + 40), 12 * S, outline=col("rule"), width=S); icon(s.im, "x", x1 - 35, y0 + 21, 14, col("inkm")); d = s.d
    # van tabs
    tx = x0 + 20
    for i, (lab, key) in enumerate(VANS):
        t = tk(key) if not (second_chips and i == 1) else tk("chip_block"); on = i == sel_van
        w = tw(d, f"{lab} · {t['cuisine']}", sans(11, "Bold")) + 30
        d.rounded_rectangle(R(tx, y0 + 64, tx + w, y0 + 88), 12 * S, fill=col("ink") if on else col("bright"), outline=None if on else col("rule"), width=S)
        d.ellipse(R(tx + 8, y0 + 72, tx + 16, y0 + 80), fill=hx(t["c"]))
        text(d, (tx + 21, y0 + 76), f"{lab} · {t['cuisine']}", sans(11, "Bold"), col("bartext") if on else col("ink"), "lm"); tx += w + 6
    cur = tk(cur_key); cw = 182; gap = 12
    for i, t in enumerate(T):
        cx = x0 + 20 + i * (cw + gap)
        st = "normal"
        if t["key"] == cur_key: st = "selected"
        elif t.get("soon"): st = "soon"
        elif t["tier"] > tier: st = "locked"
        elif hover == i: st = "hover"
        note = None
        if second_chips and t["key"] == "chip_block": note = "2nd chips van: shares the chips crowd"
        card(s, cx, y0 + 104, cw, 292, t, st if st != "soon" else "normal", cur, van_label=f"VAN {sel_van + 1}", note=note)
    d = s.d
    text(d, (x0 + 20, y1 - 34), "Click a flyer to book it for this van · it pays you at Start · swap any time before then", sans(11.5, "SemiBold"), col("inkm"))
    d.rounded_rectangle(R(x1 - 140, y1 - 44, x1 - 20, y1 - 14), 8 * S, fill=col("gold")); text(d, (x1 - 80, y1 - 29), "Done", sans(14, "Bold"), col("bar"), "mm")
    s.save(M + f"{name}.png"); return s


flyer_board(1, hover=0, cur_key="pizza", name="board-tier1-hover-chips")
flyer_board(1, hover=None, cur_key="pizza", tier=2, name="board-tier2")


# ===================================================== states sheet (direction A's card) and the two-van / same-cuisine view
def states():
    W_, H_ = 1280, 380; s = Shot.__new__(Shot); s.im = Image.new("RGBA", (W_ * S, H_ * S), hx("f5ebd6")); s.d = ImageDraw.Draw(s.im)
    cur = tk("pizza"); cw = 196
    items = [(T[0], "normal", "Normal (vs pizza, now booked)"), (T[0], "hover", "Hover: lifts, gold edge"), (T[1], "selected", "Selected: stamped for this van"),
             (T[2], "locked", "Locked: not at this tier"), (T[0], "elsewhere", "Booked at another van (Survival)"), (T[3], "normal", "Coffee: parked, Classic later")]
    for i, (t, st, lab) in enumerate(items):
        x = 14 + i * (cw + 14)
        card(s, x, 30, cw, 300, t, st, cur, van_label="VAN 2")
        text(s.d, (x, 344), lab, sans(12, "Bold"), col("ink"))
    s.save(M + "card-states.png", size=(W_, H_))


states()
flyer_board(1, hover=None, cur_key="chip_block", second_chips=True, name="board-classic-second-chips-van")
print("ok")
