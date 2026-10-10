# Falling rain as a screen-space layer over a game-camera render.
#   python overlay.py <in.png> <out.png> <zoom> <preset A|B|C> [dusk]
# Streak length is in world metres (so it shrinks with zoom), density is per screen area (so zooming out never fogs the crowd).
from PIL import Image, ImageDraw, ImageFilter, ImageChops
import sys, math, random
PRESETS = {   #   per megapixel, streak metres, alpha range, veil, mist
    "A": dict(n=700, m=(0.5, 0.8), a=(0.14, 0.26), veil=0.0, mist=0.0, w=1.0),
    "B": dict(n=1500, m=(0.7, 1.1), a=(0.20, 0.34), veil=0.07, mist=0.0, w=1.0),
    "C": dict(n=2800, m=(0.9, 1.4), a=(0.22, 0.40), veil=0.15, mist=0.10, w=1.3),
}


def rain(img, zoom, preset, dusk=False, seed=3, wind=8.0):
    P = PRESETS[preset]; W, H = img.size; ppm = W / (zoom * 16 / 9)
    base = img.convert("RGB")
    if P["veil"]:                                                       # the air thickens: a cool grey veil
        base = Image.blend(base, Image.new("RGB", base.size, (150, 160, 170) if not dusk else (40, 44, 60)), P["veil"])
    if P["mist"]:                                                       # drifting bands of heavier rain
        rng = random.Random(seed + 9); m = Image.new("L", (W // 16, H // 16), 0); md = ImageDraw.Draw(m)
        for _ in range(14):
            x, y = rng.uniform(-20, W // 16), rng.uniform(0, H // 16); md.ellipse([x, y, x + rng.uniform(20, 60), y + rng.uniform(6, 16)], fill=int(255 * rng.uniform(0.4, 1)))
        m = m.filter(ImageFilter.GaussianBlur(5)).resize((W, H), Image.BICUBIC)
        base = Image.composite(Image.blend(base, Image.new("RGB", base.size, (170, 178, 186) if not dusk else (50, 54, 70)), P["mist"] * 2), base, m)
    S = 2; layer = Image.new("L", (W * S, H * S), 0); d = ImageDraw.Draw(layer)
    rng = random.Random(seed); n = int(P["n"] * W * H / 1e6); dx = math.sin(math.radians(wind)); dy = math.cos(math.radians(wind))
    for _ in range(n):
        x, y = rng.uniform(-40, W + 40) * S, rng.uniform(-40, H + 40) * S
        L = rng.uniform(*P["m"]) * ppm * S * 0.62                          # a falling streak seen at 39 deg: about 0.62 of its length on screen
        L = max(L, 5 * S); a = rng.uniform(*P["a"])
        for k in range(3):                                              # tapered: faint tail, bright head
            t0, t1 = k / 3, (k + 1) / 3
            d.line([(x + dx * L * t0, y + dy * L * t0), (x + dx * L * t1, y + dy * L * t1)], fill=int(255 * a * (0.45 + 0.35 * k)), width=max(1, int(P["w"] * S)))
    layer = layer.resize((W, H), Image.LANCZOS)
    if dusk:                                                            # rain only shows where light catches it
        lum = base.convert("L").filter(ImageFilter.GaussianBlur(28)).point(lambda v: min(255, int(40 + 3.2 * v)))
        layer = ImageChops.multiply(layer, lum)
        col = Image.new("RGB", base.size, (255, 226, 180))
    else:
        col = Image.new("RGB", base.size, (226, 234, 240))
    return Image.composite(col, base, layer)


if __name__ == "__main__":
    a = sys.argv[1:]
    rain(Image.open(a[0]), float(a[2]), a[3], dusk=len(a) > 4 and a[4] == "dusk").save(a[1])
