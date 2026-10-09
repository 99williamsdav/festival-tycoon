# "UNDER MY UMBRELLA" banner for the stretch tent (1536 x 160, Zilla Slab Bold, OFL): gold slab capitals on festival teal.
#   python make_banner.py -> out/lwf_stretch_tent_banner_v1.png
from PIL import Image, ImageDraw, ImageFont
import os
HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
FD = "C:/Projects/festival-tycoon/game/assets/ui/fonts/"
def hx(h): return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))
K = 2; W, H = 1536 * K, 160 * K; t = "UNDER MY UMBRELLA"
im = Image.new("RGB", (W, H), hx("1f5f5a")); d = ImageDraw.Draw(im)
d.rectangle([8 * K, 8 * K, W - 8 * K, H - 8 * K], outline=hx("e8bf5a"), width=4 * K)
s = 104 * K
while d.textlength(t, font=ImageFont.truetype(FD + "ZillaSlab-Bold.ttf", s)) > W - 260 * K: s -= 2
d.text((W / 2, H / 2 + 3 * K), t, font=ImageFont.truetype(FD + "ZillaSlab-Bold.ttf", s), fill=hx("e8bf5a"), anchor="mm")
for x in (70 * K, W - 70 * K):                                # little umbrellas either end (drawn, no font)
    cy = H / 2 - 6 * K; r = 34 * K
    d.pieslice([x - r, cy - r, x + r, cy + r], 180, 360, fill=hx("e8bf5a"))
    for k in (-1, 0, 1): d.ellipse([x + k * r * 0.66 - r * 0.34, cy - r * 0.2, x + k * r * 0.66 + r * 0.34, cy + r * 0.2], fill=hx("1f5f5a"))
    d.line([x, cy - 2 * K, x, cy + 40 * K], fill=hx("e8bf5a"), width=5 * K); d.arc([x, cy + 30 * K, x + 20 * K, cy + 50 * K], 0, 180, fill=hx("e8bf5a"), width=5 * K)
im.resize((W // K, H // K), Image.LANCZOS).save(os.path.join(OUT, "lwf_stretch_tent_banner_v1.png"))
print("ok")
