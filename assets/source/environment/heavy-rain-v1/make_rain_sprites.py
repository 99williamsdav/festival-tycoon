# Rain sprites for GPU particles and puddle ripples (approved heavy-rain board: rain B as the norm, C as a rare downpour).
#   python make_rain_sprites.py -> out/
#     lwf_rain_streak_v1.png        32 x 256  one streak, white, soft sides, faint tail to bright head (alpha carries the shape)
#     lwf_rain_ripple_v1.png        512 x 512 4 x 4 flipbook (16 frames): a ring expanding and fading, seen straight down
#     lwf_rain_splash_v1.png        512 x 128 4 x 1 flipbook: a drop's crown bouncing up and falling, side-on billboard
#     lwf_rain_ripple_strip_v1.png  preview strip of the ripple frames on puddle colour (board/verification only)
from PIL import Image, ImageDraw, ImageFilter
import os, math
HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
K = 4

# ---- streak: alpha ramps 0 at the tail (top) to 1 at the head (bottom), gaussian across
W, H = 32 * K, 256 * K
a = Image.new("L", (W, H), 0); px = a.load()
for y in range(H):
    t = y / (H - 1); along = (t ** 1.6) * (1 - max(0, (t - 0.96) / 0.04))
    for x in range(W):
        d = (x - (W - 1) / 2) / (W * 0.18); px[x, y] = int(255 * along * math.exp(-d * d))
img = Image.merge("RGBA", (Image.new("L", (W, H), 255),) * 3 + (a,)).resize((32, 256), Image.LANCZOS)
img.save(os.path.join(OUT, "lwf_rain_streak_v1.png"))

# ---- ripple flipbook: 16 frames, radius 0.08 -> 0.95 of the cell, alpha 0.9 -> 0, a faint second ring trailing inside
S = 128 * K; sheet = Image.new("RGBA", (S * 4, S * 4), (255, 255, 255, 0))
for f in range(16):
    t = f / 15; fr = Image.new("L", (S, S), 0); d = ImageDraw.Draw(fr); c = S / 2
    for ring, (rmul, amul) in enumerate(((1.0, 1.0), (0.62, 0.45))):
        r = (0.08 + 0.87 * t) * rmul * S / 2; aa = int(255 * amul * (1 - t) ** 1.3 * min(1, t * 6 + 0.3)); w = max(2, int((3.2 - 1.6 * t) * K))
        if r > 2: d.ellipse([c - r, c - r, c + r, c + r], outline=aa, width=w)
    fr = fr.filter(ImageFilter.GaussianBlur(K * 0.6))
    sheet.paste(Image.merge("RGBA", (Image.new("L", (S, S), 255),) * 3 + (fr,)), ((f % 4) * S, (f // 4) * S))
sheet = sheet.resize((512, 512), Image.LANCZOS); sheet.save(os.path.join(OUT, "lwf_rain_ripple_v1.png"))
strip = Image.new("RGBA", (16 * 128, 128), (86, 102, 110, 255))
for f in range(16): strip.alpha_composite(sheet.crop(((f % 4) * 128, (f // 4) * 128, (f % 4) * 128 + 128, (f // 4) * 128 + 128)), (f * 128, 0))
strip.save(os.path.join(OUT, "lwf_rain_ripple_strip_v1.png"))

# ---- splash flipbook: 4 frames, a crown of beads rising then dropping (billboard, base at the bottom centre)
S = 128 * K; sp = Image.new("RGBA", (S * 4, S), (255, 255, 255, 0))
for f, (h, spread, alpha) in enumerate(((0.25, 0.25, 1.0), (0.55, 0.38, 0.9), (0.45, 0.48, 0.6), (0.15, 0.55, 0.3))):
    fr = Image.new("L", (S, S), 0); d = ImageDraw.Draw(fr); cx, base = S / 2, S * 0.92
    d.ellipse([cx - spread * S * 0.5, base - 6 * K, cx + spread * S * 0.5, base + 6 * K], outline=int(200 * alpha), width=3 * K)
    for k in range(5):
        x = cx + (k - 2) * spread * S * 0.22; y = base - h * S * (1 - abs(k - 2) * 0.25); r = (5 - abs(k - 2)) * K
        d.ellipse([x - r, y - r, x + r, y + r], fill=int(255 * alpha))
    fr = fr.filter(ImageFilter.GaussianBlur(K * 0.5))
    sp.paste(Image.merge("RGBA", (Image.new("L", (S, S), 255),) * 3 + (fr,)), (f * S, 0))
sp.resize((512, 128), Image.LANCZOS).save(os.path.join(OUT, "lwf_rain_splash_v1.png"))
print("ok")
