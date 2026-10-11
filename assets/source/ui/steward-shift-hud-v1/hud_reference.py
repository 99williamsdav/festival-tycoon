# Reference renderer for the steward shift HUD (approved picks: prompt A, compass A, radio A, needs A). Every number here is in
# 1280-wide mockup units, i.e. exactly what Ui.S()/Ui.Px() take; INTEGRATION.md lists them. Drawn at 2x for crispness.
import sys, os, re, math
from PIL import Image, ImageDraw, ImageFont, ImageFilter
sys.path.insert(0, "C:/Projects/festival-tycoon/assets/source/ui/rain-forecast-icons-v1"); import lucide_raster as L
FD = "C:/Projects/festival-tycoon/game/assets/ui/fonts/"; ICONS = "C:/Projects/festival-tycoon/game/assets/ui/icons/"
S = 2
def hx(h, a=255): return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (a,)
C = dict(paper="f5ebd6", bright="fff8e9", rule="e2d3b3", edge="d5c39e", ink="1f2a26", inkm="56615a", teal="2b6e66", tealwash="dceae5", gold="d8a43b",
         goldink="6b4f16", alert="b8551e", alertwash="f8dcc3", bar="17302a", bardeep="0e1f1a", bartext="f5ebd6", barmuted="b9c4b8", good="8fd1b5", warn="f2a65a", hivis="d8f126")
def col(k, a=255): return hx(C[k], a)
def slab(px, semi=False): return ImageFont.truetype(FD + ("ZillaSlab-SemiBold.ttf" if semi else "ZillaSlab-Bold.ttf"), int(px * S))
def sans(px, w="SemiBold"):
    f = ImageFont.truetype(FD + "SourceSans3-Variable.ttf", int(px * S)); f.set_variation_by_name(w); return f


# ---- icons: the game's Lucide SVGs, plus three Lucide-style ones for this mode
def load_svg(name):
    t = open(ICONS + name + ".svg").read(); paths = re.findall(r'<path d="([^"]+)"', t)
    for cx, cy, r in re.findall(r'<circle cx="([\d.]+)" cy="([\d.]+)" r="([\d.]+)"', t):
        cx, cy, r = float(cx), float(cy), float(r); paths.append(f"M{cx - r} {cy}a{r} {r} 0 1 0 {2 * r} 0a{r} {r} 0 1 0 {-2 * r} 0")
    for x, y, w, h, rx in re.findall(r'<rect x="([\d.]+)" y="([\d.]+)" width="([\d.]+)" height="([\d.]+)"(?: rx="([\d.]+)")?', t):
        x, y, w, h = float(x), float(y), float(w), float(h); paths.append(f"M{x} {y}h{w}v{h}h{-w}Z")
    return paths
for n in ("door-closed", "trash-2", "heart-pulse", "triangle-alert", "map-pin", "droplet", "check", "x", "music", "beer", "shield", "users", "clock", "star", "sun", "minus", "volume-2", "message-square"):
    try: L.ICONS[n] = load_svg(n)
    except Exception as e: print("icon", n, e)
L.ICONS["hand"] = ["M18 11V6a2 2 0 0 0-2-2a2 2 0 0 0-2 2", "M14 10V4a2 2 0 0 0-2-2a2 2 0 0 0-2 2v2", "M10 10.5V6a2 2 0 0 0-2-2a2 2 0 0 0-2 2v8",
                   "M18 8a2 2 0 1 1 4 0v6a8 8 0 0 1-8 8h-2c-2.8 0-4.5-.86-5.99-2.34l-3.6-3.6a2 2 0 0 1 2.83-2.82L7 15"]
L.ICONS["radio"] = ["M8 7h8a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2z", "M15 7V2", "M9 11h6", "M9 14.5h6", "M10 18h4"]
L.ICONS["thermometer"] = ["M14 4v10.54a4 4 0 1 1-4 0V4a2 2 0 0 1 4 0Z"]
L.ICONS["toilet"] = L.ICONS["door-closed"]


def _parse(d):
    """SVG path parser that reads arc flags as single digits (Lucide packs them: 'a2 2 0 011.5 1.5')"""
    i, n = 0, len(d); subs, cur = [], []; x = y = sx = sy = 0.0; cmd = None
    def skip():
        nonlocal i
        while i < n and d[i] in " ,\n\t": i += 1
    def num():
        nonlocal i
        skip(); m = re.match(r"-?(\d+\.?\d*|\.\d+)(e-?\d+)?", d[i:]); i += m.end(); return float(m.group(0))
    def flag():
        nonlocal i
        skip(); v = int(d[i]); i += 1; return v
    while True:
        skip()
        if i >= n: break
        if d[i].isalpha(): cmd = d[i]; i += 1
        if cmd in "Mm":
            nx, ny = num(), num(); x, y = (x + nx, y + ny) if cmd == "m" else (nx, ny)
            if cur: subs.append(cur)
            cur = [(x, y)]; sx, sy = x, y; cmd = "l" if cmd == "m" else "L"
        elif cmd in "Ll": nx, ny = num(), num(); x, y = (x + nx, y + ny) if cmd == "l" else (nx, ny); cur.append((x, y))
        elif cmd in "Hh": v = num(); x = x + v if cmd == "h" else v; cur.append((x, y))
        elif cmd in "Vv": v = num(); y = y + v if cmd == "v" else v; cur.append((x, y))
        elif cmd in "Cc":
            pts = [num() for _ in range(6)]
            if cmd == "c": pts = [pts[k] + (x if k % 2 == 0 else y) for k in range(6)]
            x0, y0 = x, y
            for t in [k / 12 for k in range(1, 13)]:
                mt = 1 - t; cur.append((mt ** 3 * x0 + 3 * mt * mt * t * pts[0] + 3 * mt * t * t * pts[2] + t ** 3 * pts[4], mt ** 3 * y0 + 3 * mt * mt * t * pts[1] + 3 * mt * t * t * pts[3] + t ** 3 * pts[5]))
            x, y = pts[4], pts[5]
        elif cmd in "Aa":
            rx, ry, ph = num(), num(), num(); fa, fs = flag(), flag(); nx, ny = num(), num()
            if cmd == "a": nx, ny = x + nx, y + ny
            if rx > 0 and ry > 0: cur += L.arc_pts(x, y, rx, ry, ph, fa, fs, nx, ny)
            else: cur.append((nx, ny))
            x, y = nx, ny
        elif cmd in "Zz": cur.append((sx, sy)); x, y = sx, sy; subs.append(cur); cur = []; cmd = None
        else: i += 1
    if cur: subs.append(cur)
    return subs
L.parse = _parse


def icon(img, name, x, y, px, c):
    ic = L.draw_icon(name, int(px * S), c); img.alpha_composite(ic, (int(x * S), int(y * S)))


class Shot:
    def __init__(self, bg, blur=0, dark=0.0):
        im = Image.open(bg).convert("RGB")
        if blur: im = im.filter(ImageFilter.GaussianBlur(blur))
        if dark: im = Image.blend(im, Image.new("RGB", im.size, (14, 31, 26)), dark)
        self.im = im.resize((im.width * S, im.height * S), Image.BICUBIC).convert("RGBA"); self.d = ImageDraw.Draw(self.im)
    def layer(self):
        return Image.new("RGBA", self.im.size, (0, 0, 0, 0))
    def put(self, lay): self.im.alpha_composite(lay); self.d = ImageDraw.Draw(self.im)
    def arms(self, png):
        a = Image.open(png).convert("RGBA").resize(self.im.size, Image.BICUBIC); self.put(a)
    def save(self, path, size=None):
        out = self.im.convert("RGB"); out = out.resize(size or (out.width // S, out.height // S), Image.LANCZOS); out.save(path); return out


def R(x0, y0, x1, y1): return [x0 * S, y0 * S, x1 * S, y1 * S]
def text(d, xy, t, f, c, anchor="la"): d.text((xy[0] * S, xy[1] * S), t, font=f, fill=c, anchor=anchor)
def tw(d, t, f): return d.textlength(t, font=f) / S
def shadowed(shot, box, radius, fill, outline=None, width=0, shadow=10, salpha=110):
    lay = shot.layer(); dd = ImageDraw.Draw(lay)
    dd.rounded_rectangle([box[0], box[1] + 3 * S, box[2], box[3] + 3 * S], radius * S, fill=(0, 0, 0, salpha))
    lay = lay.filter(ImageFilter.GaussianBlur(shadow)); shot.put(lay)
    shot.d.rounded_rectangle(box, radius * S, fill=fill, outline=outline, width=width * S)


def crosshair(shot, x=640, y=360):
    d = shot.d
    for (dx, dy) in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        for c, w in (((0, 0, 0, 140), 4), ((255, 255, 255, 230), 2)):
            d.line([((x + dx * 3) * S, (y + dy * 3) * S), ((x + dx * 10) * S, (y + dy * 10) * S)], fill=c, width=w * S // 2 + 1)


def ring(d, cx, cy, r, w, frac, c, track=None):
    box = [(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S]
    if track: d.ellipse(box, outline=track, width=int(w * S))
    if frac > 0: d.arc(box, -90, -90 + 360 * frac, fill=c, width=int(w * S))


def keycap(d, x, y, k, size=24, dark=True):
    d.rounded_rectangle(R(x, y, x + size, y + size), 5 * S, fill=col("bar") if dark else col("bright"), outline=col("edge") if not dark else None, width=S)
    text(d, (x + size / 2, y + size / 2 + 1), k, sans(14, "Bold"), col("bartext") if dark else col("ink"), "mm")


# ============================================================ 2. hold-to-act prompt
STATES = {
    "hold": None,
    "success": ("check", "teal", "Calmed", "Ollie's settled down"),
    "fail": ("x", "alert", "Shrugged off", "Ollie's not having it. Try again or call it in"),
    "cancel": ("minus", "inkm", "Cancelled", "You let go too soon"),
}


def prompt_A(shot, verb="Calm", target="Ollie Moorcroft", sub="guest · 2 m · agitated", ic="hand", frac=0.62, state="hold"):
    """A: a cream card under the crosshair; the key cap wears the progress ring"""
    d = shot.d; cx, y = 640, 404
    title = f"{verb} {target}" if state == "hold" else STATES[state][2]
    detail = sub if state == "hold" else STATES[state][3]
    w = max(tw(d, title, slab(17)), tw(d, detail, sans(12))) + 84
    x0 = cx - w / 2
    edge = None if state == "hold" else col(STATES[state][1])
    shadowed(shot, R(x0, y, x0 + w, y + 62), 10, col("paper", 245), outline=edge, width=2 if edge else 0); d = shot.d
    kx, ky = x0 + 14, y + 17
    if state == "hold":
        ring(d, kx + 14, ky + 14, 19, 3.2, frac, col("gold"), track=col("rule"))
        keycap(d, kx + 2, ky + 2, "E", 24)
    else:
        k, c = STATES[state][0], col(STATES[state][1]); d.ellipse(R(kx - 4, ky - 4, kx + 32, ky + 32), fill=c)
        icon(shot.im, k, kx + 3, ky + 3, 22, col("bright"))
    d = shot.d
    if state == "hold":
        icon(shot.im, ic, x0 + 58, y + 9, 15, col("teal")); d = shot.d
        text(d, (x0 + 78, y + 8), "HOLD E", sans(10, "Bold"), col("teal"))
    text(d, (x0 + 58, y + 21 if state == "hold" else y + 12), title, slab(17), col("ink"))
    text(d, (x0 + 58, y + 42 if state == "hold" else y + 35), detail, sans(12), col("inkm"))


def prompt_B(shot, verb="Calm", target="Ollie Moorcroft", ic="hand", frac=0.62, state="hold"):
    """B: the progress ring lives round the crosshair; a slim dark pill names the action"""
    d = shot.d; cx, cy = 640, 360
    if state == "hold":
        ring(d, cx, cy, 20, 3.5, 1.0, (0, 0, 0, 90)); ring(d, cx, cy, 20, 3.5, frac, col("hivis"))
        label = f"Hold  E   {verb} {target}"
    else:
        k, c = STATES[state][0], col(STATES[state][1]); ring(d, cx, cy, 20, 3.5, 1.0, c)
        label = STATES[state][2] + "  ·  " + STATES[state][3].split(".")[0]
    f = sans(14, "SemiBold"); w = tw(d, label, f) + 50
    x0 = cx - w / 2; y = cy + 34
    lay = shot.layer(); dd = ImageDraw.Draw(lay); dd.rounded_rectangle(R(x0, y, x0 + w, y + 28), 14 * S, fill=col("bardeep", 200)); shot.put(lay); d = shot.d
    if state == "hold":
        keycap(d, x0 + 40, y + 4, "E", 20)
        icon(shot.im, ic, x0 + 12, y + 6, 16, col("hivis")); d = shot.d
        text(d, (x0 + 68, y + 14), f"{verb} {target}", f, col("bartext"), "lm")
    else:
        icon(shot.im, STATES[state][0], x0 + 12, y + 6, 16, col(STATES[state][1]) if state != "cancel" else col("barmuted")); d = shot.d
        text(d, (x0 + 34, y + 14), label, f, col("bartext"), "lm")
    crosshair(shot, cx, cy)


def prompt_C(shot, head=(640, 300), verb="Calm", target="Ollie Moorcroft", ic="hand", frac=0.62, state="hold", mood="Agitated"):
    """C: a tag over the target's head (world-anchored), with a hold bar under the name"""
    d = shot.d; hx_, hy = head
    f = slab(15); w = max(tw(d, target, f) + 64, 170)
    x0 = hx_ - w / 2; y0 = hy - 66
    edge = col(STATES[state][1]) if state != "hold" else None
    shadowed(shot, R(x0, y0, x0 + w, y0 + 50), 8, col("paper", 245), outline=edge, width=2 if edge else 0); d = shot.d
    d.polygon([((hx_ - 7) * S, (y0 + 50) * S), ((hx_ + 7) * S, (y0 + 50) * S), (hx_ * S, (y0 + 58) * S)], fill=col("paper", 245))
    icon(shot.im, "triangle-alert" if state == "hold" else STATES[state][0], x0 + 10, y0 + 8, 16, col("alert") if state in ("hold", "fail") else col(STATES[state][1])); d = shot.d
    text(d, (x0 + 32, y0 + 6), target if state == "hold" else STATES[state][2], f, col("ink"))
    text(d, (x0 + 32, y0 + 24), mood if state == "hold" else STATES[state][3].split(".")[0], sans(11), col("inkm"))
    if state == "hold":
        bx0, bx1, by = x0 + 10, x0 + w - 10, y0 + 40
        d.rounded_rectangle(R(bx0, by, bx1, by + 5), 3 * S, fill=col("rule")); d.rounded_rectangle(R(bx0, by, bx0 + (bx1 - bx0) * frac, by + 5), 3 * S, fill=col("gold"))
        lab = f"Hold E · {verb}"; lw = tw(d, lab, sans(12, "Bold")) + 18
        lay = shot.layer(); dd = ImageDraw.Draw(lay); dd.rounded_rectangle(R(hx_ - lw / 2, y0 + 62, hx_ + lw / 2, y0 + 82), 10 * S, fill=col("bardeep", 200)); shot.put(lay); d = shot.d
        text(d, (hx_, y0 + 72), lab, sans(12, "Bold"), col("bartext"), "mm")
    crosshair(shot)


# ============================================================ 3. compass
BEARINGS = {"music": (-62, "Stage"), "beer": (38, "Bar 2"), "door-closed": (118, "Toilets"), "heart-pulse": (-150, "First aid")}


def compass_A(shot, heading=20, obj=(48, "Bar 2", 64), urgent=False):
    """A: a slim strip along the top: ticks, N/E/S/W, landmark icons, the call's pin with its distance"""
    d = shot.d; cx, y, half = 640, 14, 260
    lay = shot.layer(); dd = ImageDraw.Draw(lay)
    for i in range(40):                                                     # fade out at both ends
        a = int(150 * min(1, (40 - abs(i - 20) * 2) / 12))
    dd.rounded_rectangle(R(cx - half, y, cx + half, y + 26), 13 * S, fill=col("bardeep", 150)); shot.put(lay); d = shot.d
    ppd = half / 75                                                           # pixels per degree: 150 degrees across
    for b in range(-180, 181, 15):
        rel = ((b - heading + 180) % 360) - 180
        if abs(rel) > 74: continue
        x = cx + rel * ppd
        if b % 90 == 0:
            text(d, (x, y + 13), {0: "N", 90: "E", 180: "S", -180: "S", -90: "W"}[b], sans(13, "Bold"), col("bartext"), "mm")
        else:
            d.line([(x * S, (y + 9) * S), (x * S, (y + 17) * S)], fill=col("barmuted", 160), width=S)
    for name, (b, label) in BEARINGS.items():
        rel = ((b - heading + 180) % 360) - 180
        if abs(rel) < 72: icon(shot.im, name, cx + rel * ppd - 7, y + 6, 14, col("barmuted", 200)); d = shot.d
    rel = ((obj[0] - heading + 180) % 360) - 180; c = col("alert") if urgent else col("gold")
    x = cx + max(-72, min(72, rel)) * ppd
    d.polygon([((cx - 5) * S, (y + 27) * S), ((cx + 5) * S, (y + 27) * S), (cx * S, (y + 22) * S)], fill=col("bartext"))
    icon(shot.im, "map-pin", x - 10, y + 2, 20, c); d = shot.d
    lab = f"{obj[1]} · {obj[2]} m"; lw = tw(d, lab, sans(12, "Bold")) + 16
    d.rounded_rectangle(R(x - lw / 2, y + 30, x + lw / 2, y + 48), 9 * S, fill=c)
    text(d, (x, y + 39), lab, sans(12, "Bold"), col("bright") if urgent else col("bar"), "mm")


def compass_B(shot, heading=20, obj_screen=(905, 236), dist=64, label="Bar 2", offscreen=None, urgent=False):
    """B: no strip: a tiny heading readout, and the call's marker drawn in the world (or a chevron on the screen edge)"""
    d = shot.d; cx = 640
    names = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"]; hd = names[int(((heading % 360) + 22.5) // 45) % 8]
    lay = shot.layer(); dd = ImageDraw.Draw(lay); dd.rounded_rectangle(R(cx - 40, 11, cx + 40, 33), 11 * S, fill=col("bardeep", 170)); shot.put(lay); d = shot.d
    text(d, (cx, 22), f"{hd}  {heading % 360:03d}°", sans(13, "Bold"), col("bartext"), "mm")
    c = col("alert") if urgent else col("gold")
    if offscreen:
        x, y, ang = offscreen
        pts = [(0, -12), (10, 8), (0, 3), (-10, 8)]; ca, sa = math.cos(ang), math.sin(ang)
        d.polygon([((x + px * ca - py * sa) * S, (y + px * sa + py * ca) * S) for px, py in pts], fill=c, outline=col("bardeep"))
        text(d, (x, y + 22), f"{label} · {dist} m", sans(12, "Bold"), col("bartext"), "mm")
        return
    x, y = obj_screen
    d.polygon([(x * S, (y - 14) * S), ((x + 11) * S, y * S), (x * S, (y + 14) * S), ((x - 11) * S, y * S)], fill=c, outline=col("bardeep"), width=S)
    icon(shot.im, "beer" if not urgent else "heart-pulse", x - 6, y - 6, 12, col("bar") if not urgent else col("bright")); d = shot.d
    lab = f"{label} · {dist} m"; lw = tw(d, lab, sans(12, "Bold")) + 16
    lay = shot.layer(); dd = ImageDraw.Draw(lay); dd.rounded_rectangle(R(x - lw / 2, y + 18, x + lw / 2, y + 36), 9 * S, fill=col("bardeep", 190)); shot.put(lay); d = shot.d
    text(d, (x, y + 27), lab, sans(12, "Bold"), col("bartext"), "mm")


# ============================================================ 4. radio call
def radio_A(shot, msg="Fight by Bar 2!", who="CONTROL", sub="Bar 2 · 64 m north-east", urgent=False, t=0.0):
    """A: the bottom-left strip (the prototype's place), grown up: a radio icon, call sign, the call, where; urgent goes alert orange"""
    d = shot.d; x0, y1 = 16, 704; h = 62 if not urgent else 74; w = 470 if not urgent else 520; y0 = y1 - h
    lay = shot.layer(); dd = ImageDraw.Draw(lay)
    dd.rounded_rectangle(R(x0, y0, x0 + w, y1), 8 * S, fill=(col("alert", 238) if urgent else col("bardeep", 205)))
    dd.rectangle(R(x0, y0, x0 + 4, y1), fill=col("bright") if urgent else col("hivis")); shot.put(lay); d = shot.d
    icon(shot.im, "heart-pulse" if urgent else "radio", x0 + 16, y0 + 14, 26 if urgent else 22, col("bright") if urgent else col("hivis")); d = shot.d
    tag = f"URGENT  ·  {who}" if urgent else f"RADIO  ·  {who}"
    text(d, (x0 + 52, y0 + 9), tag, sans(10.5, "Bold"), col("bright") if urgent else col("barmuted"))
    text(d, (x0 + 52, y0 + 22), msg, slab(21 if urgent else 18), col("bright") if urgent else col("bartext"))
    text(d, (x0 + 52, y0 + (49 if urgent else 43)), sub, sans(12), col("bright", 220) if urgent else col("barmuted"))
    if urgent:                                                              # the response timer drains along the bottom
        d.rectangle(R(x0 + 4, y1 - 4, x0 + 4 + (w - 4) * 0.7, y1), fill=col("bright"))
        text(d, (x0 + w - 14, y0 + 9), "0:42", sans(12, "Bold"), col("bright"), "ra")
    hint = "R  Answer   ·   Tab  Ignore"
    text(d, (x0 + w - 14, y1 - 18 if not urgent else y1 - 22), hint, sans(10.5, "SemiBold"), col("bright", 200) if urgent else col("barmuted"), "ra")


def urgent_vignette(shot, strength=0.32):
    """the collapse call's one-off edge pulse: a warm red-orange vignette that fades over a second"""
    W, H = shot.im.size; m = Image.new("L", (W // 8, H // 8), 0); dm = ImageDraw.Draw(m)
    dm.rectangle([0, 0, W // 8, H // 8], fill=255); dm.rounded_rectangle([W // 8 * 0.03, H // 8 * 0.05, W // 8 * 0.97, H // 8 * 0.95], 40, fill=0)
    m = m.filter(ImageFilter.GaussianBlur(8)).resize((W, H), Image.BICUBIC).point(lambda v: int(v * strength))
    lay = Image.new("RGBA", (W, H), col("alert")); lay.putalpha(m); shot.put(lay)


def radio_B(shot, msg="Fight by Bar 2!", who="Control → Steward 4", where="Bar 2", dist=64, urgent=False):
    """B: a cream card that drops in top-left: who's calling, the call, a location chip; urgent is hazard-striped"""
    d = shot.d; x0, y0, w, h = 16, 16, 330, 104
    shadowed(shot, R(x0, y0, x0 + w, y0 + h), 10, col("paper", 248), outline=col("alert") if urgent else None, width=2 if urgent else 0); d = shot.d
    if urgent:                                                              # hazard header
        lay = shot.layer(); dd = ImageDraw.Draw(lay); hdr = Image.new("RGBA", lay.size, (0, 0, 0, 0)); hd = ImageDraw.Draw(hdr)
        for k in range(-10, 40): hd.polygon([((x0 + k * 16) * S, (y0 + 26) * S), ((x0 + k * 16 + 8) * S, (y0 + 26) * S), ((x0 + k * 16 + 34) * S, y0 * S), ((x0 + k * 16 + 26) * S, y0 * S)], fill=col("alert"))
        mask = Image.new("L", lay.size, 0); ImageDraw.Draw(mask).rounded_rectangle(R(x0, y0, x0 + w, y0 + 26), 10 * S, fill=255); ImageDraw.Draw(mask).rectangle(R(x0, y0 + 12, x0 + w, y0 + 26), fill=255)
        base = Image.new("RGBA", lay.size, col("alertwash")); base.alpha_composite(hdr); lay.paste(base, (0, 0), mask); shot.put(lay); d = shot.d
        d.rounded_rectangle(R(x0 + 8, y0 + 4, x0 + 118, y0 + 22), 9 * S, fill=col("alert")); text(d, (x0 + 63, y0 + 13), "URGENT CALL", sans(11, "Bold"), col("bright"), "mm")
    else:
        d.rounded_rectangle(R(x0, y0, x0 + w, y0 + 26), 10 * S, fill=col("bar")); d.rectangle(R(x0, y0 + 13, x0 + w, y0 + 26), fill=col("bar"))
        icon(shot.im, "radio", x0 + 10, y0 + 5, 16, col("hivis")); d = shot.d
        text(d, (x0 + 32, y0 + 13), who.upper(), sans(11, "Bold"), col("bartext"), "lm")
    if not urgent: text(d, (x0 + w - 12, y0 + 13), "now", sans(11, "SemiBold"), col("barmuted"), "rm")
    icon(shot.im, "heart-pulse" if urgent else "users", x0 + 12, y0 + 36, 22, col("alert") if urgent else col("teal")); d = shot.d
    text(d, (x0 + 44, y0 + 32), msg, slab(19), col("ink"))
    chip = f"{where} · {dist} m"; cw = tw(d, chip, sans(12, "Bold")) + 34
    d.rounded_rectangle(R(x0 + 44, y0 + 66, x0 + 44 + cw, y0 + 88), 11 * S, fill=col("alertwash") if urgent else col("tealwash"))
    icon(shot.im, "map-pin", x0 + 50, y0 + 70, 14, col("alert") if urgent else col("teal")); d = shot.d
    text(d, (x0 + 68, y0 + 77), chip, sans(12, "Bold"), col("alert") if urgent else col("teal"), "lm")
    keycap(d, x0 + w - 98, y0 + 66, "R", 22, dark=True); text(d, (x0 + w - 70, y0 + 77), "Answer", sans(12, "Bold"), col("ink"), "lm")


# ============================================================ 5. own needs
def needs_A(shot, thirst=0.62, heat=0.38, x0=24, y1=626):
    """A: two thin bars with icons, tucked above the radio strip, quiet until they matter"""
    d = shot.d
    for i, (ic, v, c, lab) in enumerate((("droplet", thirst, "warn" if thirst > 0.5 else "good", "Thirst"), ("thermometer", heat, "warn" if heat > 0.5 else "good", "Heat"))):
        y = y1 - 22 * (2 - i)
        lay = shot.layer(); dd = ImageDraw.Draw(lay); dd.rounded_rectangle(R(x0 - 6, y - 3, x0 + 118, y + 17), 9 * S, fill=col("bardeep", 120)); shot.put(lay); d = shot.d
        icon(shot.im, ic, x0, y, 14, col("bartext", 220)); d = shot.d
        d.rounded_rectangle(R(x0 + 22, y + 5, x0 + 108, y + 10), 3 * S, fill=col("bartext", 60))
        d.rounded_rectangle(R(x0 + 22, y + 5, x0 + 22 + 86 * v, y + 10), 3 * S, fill=col(c))


def needs_B(shot, thirst=0.62, heat=0.38, at=(726, 487)):
    """B: on the radio's own screen (diegetic): two tiny glyphs that fill; heat also warms the screen edge a touch"""
    d = shot.d; x, y = at
    for i, (ic, v) in enumerate((("droplet", thirst), ("thermometer", heat))):
        xx = x + i * 13
        icon(shot.im, ic, xx, y, 11, (12, 40, 44, 255)); d = shot.d
        d.rectangle(R(xx + 1, y + 13, xx + 1 + 10 * v, y + 15), fill=(12, 40, 44, 255))


def heat_edge(shot, s=0.22):
    W, H = shot.im.size; m = Image.new("L", (W // 8, H // 8), 0); dm = ImageDraw.Draw(m)
    dm.rectangle([0, 0, W // 8, H // 8], fill=255); dm.ellipse([W // 8 * -0.1, H // 8 * -0.2, W // 8 * 1.1, H // 8 * 1.2], fill=0)
    m = m.filter(ImageFilter.GaussianBlur(18)).resize((W, H), Image.BICUBIC).point(lambda v: int(v * s))
    lay = Image.new("RGBA", (W, H), hx("f2a65a")); lay.putalpha(m); shot.put(lay)
