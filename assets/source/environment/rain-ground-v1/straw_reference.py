# The straw ground-state rule, implemented in numpy exactly as the ground shader should do it (see INTEGRATION.md), and used
# to make the verification tile set.
#   python straw_reference.py -> verification/straw_tileset_ground.png (top-down, 128 px per metre, alpha = the mud field's ragged edge)
#
# Per ground point p (metres):
#   s    = straw cell map (0.5 m cells, 1 = straw laid), box-blurred over 3 x 3 cells, then sampled bilinearly
#   n    = lwf_straw_breakup_v1 at p / 6 m (R wobble, G clumps, B fray)
#   e    = s + (R - .5) * 0.70 + (G - .5) * 0.55 + (B - .5) * 0.30          # ragged, uneven-width edge
#   body = smoothstep(0.40, 0.52, e)
#   tuft = smoothstep(0.64, 0.70, G) * smoothstep(0.04, 0.30, s)          # clumps spilling up to ~0.6 m into the mud
#   fray = smoothstep(0.70, 0.74, B) * smoothstep(0.10, 0.35, s)          # single strands poking out
#   a    = max(body, tuft, fray) * straw.a                               # straw texture at p / 3 m (fresh or trampled)
#   colour = mix(mud colour, straw.rgb, a)
import numpy as np, os
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__)); O = os.path.join(HERE, "out"); V = os.path.join(HERE, "verification"); os.makedirs(V, exist_ok=True)
PPM = 128


def smooth(a, b, x): t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)


def tex(path, W, H, metres):
    im = np.asarray(Image.open(path).convert("RGBA")).astype(np.float32) / 255; N = im.shape[0]
    y, x = np.mgrid[0:H, 0:W] / PPM; u = ((x / metres) % 1 * N).astype(int) % N; v = ((y / metres) % 1 * N).astype(int) % N
    return im[v, u]


def strawfield(cells, W, H, kind, seed):
    """cells: 2D 0/1 array of 0.5 m ground cells; returns (rgba straw over mud, mud-field alpha)"""
    c = cells.astype(np.float32); k = np.pad(c, 1, mode="edge")
    c = sum(k[1 + dy:1 + dy + c.shape[0], 1 + dx:1 + dx + c.shape[1]] for dy in (-1, 0, 1) for dx in (-1, 0, 1)) / 9   # 3 x 3 box blur
    y, x = np.mgrid[0:H, 0:W] / PPM / 0.5 - 0.5; x0 = np.clip(np.floor(x).astype(int), 0, c.shape[1] - 2); y0 = np.clip(np.floor(y).astype(int), 0, c.shape[0] - 2)
    fx = np.clip(x - x0, 0, 1); fy = np.clip(y - y0, 0, 1)
    s = (c[y0, x0] * (1 - fx) * (1 - fy) + c[y0, x0 + 1] * fx * (1 - fy) + c[y0 + 1, x0] * (1 - fx) * fy + c[y0 + 1, x0 + 1] * fx * fy)
    n = tex(os.path.join(O, "lwf_straw_breakup_v1.png"), W, H, 6.0)
    off = seed * 1.7; R, G, B = np.roll(n[..., 0], int(off * PPM), 1), np.roll(n[..., 1], int(off * PPM), 0), n[..., 2]
    e = s + (R - .5) * 0.70 + (G - .5) * 0.55 + (B - .5) * 0.30
    body = smooth(0.40, 0.52, e); tuft = smooth(0.64, 0.70, G) * smooth(0.04, 0.30, s); fray = smooth(0.70, 0.74, B) * smooth(0.10, 0.35, s)
    st = tex(os.path.join(O, f"lwf_straw_{kind}_v1.png"), W, H, 3.0)
    a = np.maximum(np.maximum(body, tuft), fray) * st[..., 3]
    # the game's churned mud (Main.Ground.cs: 0.36, 0.28, 0.20 with noise) and a couple of sludge pools
    mn = tex(os.path.join(O, "lwf_straw_breakup_v1.png"), W, H, 4.0)[..., 0]
    mud = np.dstack([np.full((H, W), v) for v in (0.36, 0.28, 0.20)]) * (0.82 + 0.36 * mn)[..., None]
    rgb = mud * (1 - a[..., None]) + st[..., :3] * a[..., None]
    return rgb


def layout():
    c = np.zeros((16, 42))                                   # 21 x 8 m in 0.5 m cells: the same path twice, fresh (left) and trampled (right)
    for ox in (0, 22):
        c[1:15, ox + 8:ox + 11] = 1                          # the main straight, with an end at one side
        c[6:9, ox + 11:ox + 19] = 1                          # a T off to the right, ending at x 18
        c[12:15, ox + 2:ox + 8] = 1                          # a turn, running left to an end
    return c


W, H = 42 * 64, 16 * 64
fresh = strawfield(layout(), W, H, "fresh", 0); tram = strawfield(layout(), W, H, "trampled", 0)
img = fresh.copy(); img[:, W // 2:] = tram[:, W // 2:]
# a ragged outline to the whole mud field (alpha), so it sits on the grass like the game's churned patches
y, x = np.mgrid[0:H, 0:W] / PPM
n = tex(os.path.join(O, "lwf_straw_breakup_v1.png"), W, H, 6.0)
cx, cy = W / PPM / 2, H / PPM / 2
d = np.maximum(np.abs(x - cx) / cx, np.abs(y - cy) / cy) + (n[..., 0] - .5) * 0.35
alpha = 1 - smooth(0.90, 0.96, d)
out = np.dstack([img, alpha])
Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8), "RGBA").save(os.path.join(V, "straw_tileset_ground.png"))
print(out.shape)
