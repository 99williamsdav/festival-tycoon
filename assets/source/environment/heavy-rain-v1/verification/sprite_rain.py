# python sprite_rain.py <base.png> <out.png> <zoom> <B|C> [dusk]
# A 2D stand-in for the GPU particle setup in INTEGRATION.md, drawn with the INSTALLED sprite (game/assets/environment/
# lwf_rain_streak_v1.png) at the documented numbers: streak count per screen constant at any zoom, streak length in world
# metres, alpha range, wind slant, the veil (and C's drifting bands).
import sys, math, random
from PIL import Image, ImageFilter, ImageChops, ImageDraw
SPR = Image.open("C:/Projects/festival-tycoon/game/assets/environment/lwf_rain_streak_v1.png").convert("RGBA")
P = {"B": dict(per_mp=1500, m=(0.7, 1.1), a=(0.45, 0.75), veil=0.07, bands=0.0),
     "C": dict(per_mp=2800, m=(0.9, 1.4), a=(0.50, 0.85), veil=0.15, bands=0.10)}


def rain(img, zoom, k, dusk=False, seed=5, wind=8.0):
    p = P[k]; W, H = img.size; ppm = W / (zoom * 16 / 9); base = img.convert("RGB")
    tint = (40, 44, 60) if dusk else (150, 160, 170)
    base = Image.blend(base, Image.new("RGB", base.size, tint), p["veil"])
    if p["bands"]:
        g = random.Random(seed + 9); m = Image.new("L", (W // 16, H // 16), 0); d = ImageDraw.Draw(m)
        for _ in range(14):
            x, y = g.uniform(-20, W // 16), g.uniform(0, H // 16); d.ellipse([x, y, x + g.uniform(20, 60), y + g.uniform(6, 16)], fill=int(255 * g.uniform(.4, 1)))
        m = m.filter(ImageFilter.GaussianBlur(5)).resize((W, H), Image.BICUBIC)
        base = Image.composite(Image.blend(base, Image.new("RGB", base.size, tint), p["bands"] * 2), base, m)
    layer = Image.new("L", (W, H), 0); g = random.Random(seed)
    sizes = {}
    for _ in range(int(p["per_mp"] * W * H / 1e6)):
        L = max(6, int(g.uniform(*p["m"]) * ppm * 0.62)); w = 3                          # 0.62: a vertical streak seen from the 39 deg camera
        if L not in sizes: sizes[L] = SPR.split()[3].resize((w, L), Image.LANCZOS).rotate(wind, expand=True, resample=Image.BICUBIC)
        s = sizes[L].point(lambda v, a=g.uniform(*p["a"]): int(v * a))
        x, y = int(g.uniform(-20, W)), int(g.uniform(-40, H)); layer.paste(ImageChops.lighter(layer.crop((x, y, x + s.width, y + s.height)), s), (x, y))
    if dusk: layer = ImageChops.multiply(layer, base.convert("L").filter(ImageFilter.GaussianBlur(28)).point(lambda v: min(255, int(40 + 3.2 * v))))
    return Image.composite(Image.new("RGB", base.size, (255, 226, 180) if dusk else (226, 234, 240)), base, layer)


if __name__ == "__main__":
    a = sys.argv[1:]; rain(Image.open(a[0]), float(a[2]), a[3], len(a) > 4 and a[4] == "dusk").save(a[1])
