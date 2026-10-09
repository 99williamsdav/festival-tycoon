# Rain forecast icons for the top-bar weather chip: Lucide-style (24 grid, stroke 2, round caps), written as SVG and drawn
# with a tiny SVG-path rasteriser so the board shows them exactly as authored. The cloud outline is Lucide's own
# (from game/assets/ui/icons/cloud-rain.svg, ISC).
from PIL import Image, ImageDraw, ImageFont
import math, re, os
H = os.path.dirname(os.path.abspath(__file__)) + "/"; os.makedirs(H + "icons", exist_ok=True)
FD = "C:/Projects/festival-tycoon/game/assets/ui/fonts/"
CLOUD = "M4 14.899A7 7 0 1 1 15.71 8h1.79a4.5 4.5 0 0 1 2.5 8.242"
ICONS = {
    "cloud-showers": [CLOUD, "M8 15v1", "M8 19v1", "M12 17v1", "M12 21v1", "M16 15v1", "M16 19v1"],
    "cloud-heavy-showers": [CLOUD, "M9 13l-2 8", "M13 14l-2 8", "M17 13l-2 8"],
    "cloud-sun-clearing": ["M12 2v2", "m4.93 4.93 1.41 1.41", "M20 12h2", "m19.07 4.93-1.41 1.41", "M15.95 12.65A4 4 0 0 0 10.02 8.52",
                           "M13 22H7a5 5 0 1 1 4.9-6H13a3 3 0 0 1 0 6Z"],
    "sun-rising-dry-spell": ["M12 2v6", "m8.5 5 3.5-3.5L15.5 5", "M4.22 10.22l1.42 1.42", "M2 18h2", "M20 18h2", "M19.78 10.22l-1.42 1.42",
                             "M22 22H2", "M16 18a4 4 0 0 0-8 0"],
}


def arc_pts(x0, y0, rx, ry, phi, fa, fs, x1, y1, n=24):
    phi = math.radians(phi); c, s = math.cos(phi), math.sin(phi)
    dx, dy = (x0 - x1) / 2, (y0 - y1) / 2; xp, yp = c * dx + s * dy, -s * dx + c * dy
    lam = xp * xp / (rx * rx) + yp * yp / (ry * ry)
    if lam > 1: rx, ry = rx * math.sqrt(lam), ry * math.sqrt(lam)
    num = rx * rx * ry * ry - rx * rx * yp * yp - ry * ry * xp * xp; den = rx * rx * yp * yp + ry * ry * xp * xp
    k = math.sqrt(max(0, num / den)) * (-1 if fa == fs else 1)
    cxp, cyp = k * rx * yp / ry, -k * ry * xp / rx
    cx, cy = c * cxp - s * cyp + (x0 + x1) / 2, s * cxp + c * cyp + (y0 + y1) / 2
    ang = lambda ux, uy, vx, vy: math.atan2(ux * vy - uy * vx, ux * vx + uy * vy)
    t1 = ang(1, 0, (xp - cxp) / rx, (yp - cyp) / ry); dt = ang((xp - cxp) / rx, (yp - cyp) / ry, (-xp - cxp) / rx, (-yp - cyp) / ry)
    if not fs and dt > 0: dt -= 2 * math.pi
    if fs and dt < 0: dt += 2 * math.pi
    return [(cx + rx * math.cos(t1 + dt * i / n) * c - ry * math.sin(t1 + dt * i / n) * s, cy + rx * math.cos(t1 + dt * i / n) * s + ry * math.sin(t1 + dt * i / n) * c) for i in range(1, n + 1)]


def parse(d):
    toks = re.findall(r"[MmLlHhVvAaZz]|-?\d*\.?\d+(?:e-?\d+)?", d.replace(",", " "))
    subs, cur, x, y, sx, sy, cmd, i = [], [], 0, 0, 0, 0, None, 0
    def num():
        nonlocal i; v = float(toks[i]); i += 1; return v
    while i < len(toks):
        if re.match(r"[A-Za-z]", toks[i]): cmd = toks[i]; i += 1
        if cmd in "Mm":
            nx, ny = num(), num(); x, y = (x + nx, y + ny) if cmd == "m" else (nx, ny)
            if cur: subs.append(cur)
            cur = [(x, y)]; sx, sy = x, y; cmd = "l" if cmd == "m" else "L"
        elif cmd in "Ll":
            nx, ny = num(), num(); x, y = (x + nx, y + ny) if cmd == "l" else (nx, ny); cur.append((x, y))
        elif cmd in "Hh": v = num(); x = x + v if cmd == "h" else v; cur.append((x, y))
        elif cmd in "Vv": v = num(); y = y + v if cmd == "v" else v; cur.append((x, y))
        elif cmd in "Aa":
            rx, ry, ph, fa, fs, nx, ny = num(), num(), num(), num(), num(), num(), num()
            if cmd == "a": nx, ny = x + nx, y + ny
            cur += arc_pts(x, y, rx, ry, ph, int(fa), int(fs), nx, ny); x, y = nx, ny
        elif cmd in "Zz": cur.append((sx, sy)); x, y = sx, sy; subs.append(cur); cur = []; cmd = None
    if cur: subs.append(cur)
    return subs


def draw_icon(name, px, col, sw=2.0):
    S = 8; im = Image.new("RGBA", (px * S, px * S), (0, 0, 0, 0)); d = ImageDraw.Draw(im); k = px * S / 24; w = sw * k
    for path in ICONS[name]:
        for sub in parse(path):
            pts = [(a * k, b * k) for a, b in sub]
            if len(pts) > 1: d.line(pts, fill=col, width=int(round(w)), joint="curve")
            for p in (pts[0], pts[-1]): d.ellipse([p[0] - w / 2, p[1] - w / 2, p[0] + w / 2, p[1] + w / 2], fill=col)
    return im.resize((px, px), Image.LANCZOS)


def write_svg(name):
    body = "\n".join(f'  <path d="{p}" />' for p in ICONS[name])
    open(H + f"icons/{name}.svg", "w").write('<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="#ffffff" '
                                             f'stroke-width="2" stroke-linecap="round" stroke-linejoin="round">\n{body}\n</svg>\n')


def hx(h, a=255): return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (a,)
BAR, GOLD, TEXT, MUTED = hx("17302a"), hx("d8a43b"), hx("f5ebd6"), hx("b9c4b8")


def chip(icon, value, suffix=None, Z=3):
    """the top bar's Stat(): a 30 px gold-wash disc with an 18 px gold icon, CAPS caption, Zilla Slab value (at Z x)"""
    W, Hh = 330 * Z, 60 * Z; im = Image.new("RGBA", (W, Hh), BAR); d = ImageDraw.Draw(im)
    cx, cy, r = 24 * Z, Hh / 2, 15 * Z
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(0x2f, 0x3e, 0x2c, 255))            # gold at 16% over the bar
    ic = draw_icon(icon, 18 * Z, GOLD); im.alpha_composite(ic, (int(cx - 9 * Z), int(cy - 9 * Z)))
    cap = ImageFont.truetype(FD + "SourceSans3-Variable.ttf", 11 * Z); cap.set_variation_by_name("SemiBold")
    d.text((50 * Z, 13 * Z), "W E A T H E R", font=cap, fill=MUTED)
    vf = ImageFont.truetype(FD + "ZillaSlab-SemiBold.ttf", 19 * Z); d.text((50 * Z, 27 * Z), value, font=vf, fill=TEXT)
    if suffix:
        sf = ImageFont.truetype(FD + "SourceSans3-Variable.ttf", 14 * Z); sf.set_variation_by_name("SemiBold")
        d.text((50 * Z + d.textlength(value, font=vf) + 8 * Z, 31 * Z), suffix, font=sf, fill=MUTED)
    return im


if __name__ == "__main__":
    for n in ICONS: write_svg(n)
    rows = [("sun", None), ("cloud-showers", "Showers"), ("cloud-heavy-showers", "Heavy showers"), ("cloud-sun-clearing", "Dry spell soon"), ("sun-rising-dry-spell", "Dry spell soon")]
    ICONS["sun"] = ["M12 8a4 4 0 1 0 0.001 0", "M12 2v2", "M12 20v2", "m4.93 4.93 1.41 1.41", "m17.66 17.66 1.41 1.41", "M2 12h2", "M20 12h2", "m6.34 17.66-1.41 1.41", "m19.07 4.93-1.41 1.41"]
    big = Image.new("RGBA", (5 * 220, 220), hx("f5ebd6"))
    for i, (n, _) in enumerate(rows):
        t = Image.new("RGBA", (200, 200), BAR); t.alpha_composite(draw_icon(n, 144, GOLD), (28, 28)); big.paste(t, (i * 220 + 10, 10))
    big.save(H + "icons/sheet_big.png")
    chips_a = [chip("sun", "Hot"), chip("cloud-showers", "Showers"), chip("cloud-heavy-showers", "Heavy showers"), chip("cloud-sun-clearing", "Dry spell soon")]
    chips_b = [chip("cloud-showers", "Showers", "till 3:40"), chip("cloud-heavy-showers", "Heavy", "till 4:15"), chip("sun-rising-dry-spell", "Dry spell", "in 20 min")]
    for tag, cs in (("A", chips_a), ("B", chips_b)):
        sheet = Image.new("RGBA", (len(cs) * 340 * 3, 60 * 3), BAR)
        for i, c in enumerate(cs): sheet.alpha_composite(c, (i * 340 * 3, 0))
        sheet.save(H + f"icons/chips_{tag}.png")
    print("ok")
