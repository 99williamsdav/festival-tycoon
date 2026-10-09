# Straw as a ground-map state (approved heavy-rain board, item 5, with rougher edges).
#   python make_straw_textures.py -> out/
#     lwf_straw_fresh_v1.png     1024 x 1024 RGBA, tiles every 3 m: loose golden strands; alpha = where straw covers the mud
#     lwf_straw_trampled_v1.png  1024 x 1024 RGBA, tiles every 3 m: flattened, browner, sparser, with boot-print mud smears
#     lwf_straw_breakup_v1.png   512 x 512 RGB, tiles every 6 m: R = broad wobble (uneven widths), G = tuft clumps (ragged spill),
#                                B = fine fray (single strands poking out)
# Every texture wraps seamlessly: strands are drawn at all wrap offsets, and noise is built from periodic octaves.
from PIL import Image, ImageDraw, ImageFilter
import numpy as np, os, math, random
HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
def hx(h): return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def strands(seed, n, cols, length, width, flat, mud=0):
    S = 2; W = 1024 * S; ppm = W / 3.0
    rgb = Image.new("RGB", (W, W), hx("6b5536")); a = Image.new("L", (W, W), 0)
    dr, da = ImageDraw.Draw(rgb), ImageDraw.Draw(a); g = random.Random(seed)
    for _ in range(n):
        x, y = g.uniform(0, W), g.uniform(0, W); L = g.uniform(*length) * ppm; ang = g.uniform(0, math.pi)
        if flat: ang = g.gauss(0.6, 0.5)                                   # trampled straw lies roughly one way (the flow of feet)
        dx, dy = math.cos(ang) * L / 2, math.sin(ang) * L / 2; c = cols[g.randrange(len(cols))]
        sh = g.uniform(0.8, 1.12); c = tuple(min(255, int(v * sh)) for v in c); w = max(1, int(g.uniform(*width) * S))
        for ox in (-W, 0, W):
            for oy in (-W, 0, W):
                p = [(x - dx + ox, y - dy + oy), (x + dx + ox, y + dy + oy)]
                dr.line(p, fill=c, width=w); da.line(p, fill=255, width=w)
    if mud:                                                                # boot-print smears of mud over trampled straw
        md = Image.new("L", (W, W), 0); mdd = ImageDraw.Draw(md)
        for _ in range(mud):                                               # a boot-sized smear: an irregular sole shape along the flow
            x, y = g.uniform(0, W), g.uniform(0, W); L = g.uniform(0.18, 0.30) * ppm; wd = g.uniform(0.06, 0.10) * ppm; ang = g.gauss(0.6, 0.35)
            pts = []
            for k in range(14):
                t = 2 * math.pi * k / 14; rr = 1 + 0.25 * math.sin(3 * t + g.uniform(0, 6))
                px, py = math.cos(t) * L / 2 * rr, math.sin(t) * wd / 2 * rr * (1.15 if math.cos(t) > 0 else 0.85)
                pts.append((x + px * math.cos(ang) - py * math.sin(ang), y + px * math.sin(ang) + py * math.cos(ang)))
            for ox in (-W, 0, W):
                for oy in (-W, 0, W): mdd.polygon([(px + ox, py + oy) for px, py in pts], fill=int(g.uniform(120, 200)))
        md = md.filter(ImageFilter.GaussianBlur(4 * S))
        rgb = Image.composite(Image.new("RGB", (W, W), hx("4f3d2a")), rgb, md); a = Image.composite(Image.new("L", (W, W), 235), a, md)
    rgb = rgb.resize((1024, 1024), Image.LANCZOS); a = a.resize((1024, 1024), Image.LANCZOS)
    return Image.merge("RGBA", (*rgb.split(), a))


strands(3, 9000, [hx(c) for c in ("e8c768", "d9b24f", "f0d78a", "c99a3d", "e2bf5c")], (0.10, 0.26), (2.5, 4.5), False).save(os.path.join(OUT, "lwf_straw_fresh_v1.png"))
strands(4, 6200, [hx(c) for c in ("a8874f", "8a6a3f", "b59a5e", "7a6040", "c2a466")], (0.12, 0.30), (3, 5), True, mud=26).save(os.path.join(OUT, "lwf_straw_trampled_v1.png"))


def periodic_noise(N, sigma_px, seed):
    """white noise low-passed in the frequency domain (a Gaussian of sigma_px): wraps exactly, no grid artefacts"""
    g = np.random.default_rng(seed); f = np.fft.fft2(g.standard_normal((N, N)))
    k = np.fft.fftfreq(N); kx, ky = np.meshgrid(k, k); f *= np.exp(-2 * (np.pi * sigma_px) ** 2 * (kx ** 2 + ky ** 2))
    out = np.real(np.fft.ifft2(f)); out = (out - out.mean()) / (out.std() * 6) + 0.5; return np.clip(out, 0, 1)
N = 512                                                                   # 6 m across: 85 px per metre
R = periodic_noise(N, 40, 1)                                              # broad wobble: about 1 m blobs, uneven widths
G = periodic_noise(N, 9, 2)                                               # tuft clumps: 20-30 cm
B = periodic_noise(N, 2.2, 3)                                             # fray: a few cm
Image.fromarray((np.dstack([R, G, B]) * 255).astype(np.uint8), "RGB").save(os.path.join(OUT, "lwf_straw_breakup_v1.png"))
print("ok")
