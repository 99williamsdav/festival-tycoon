# Remove the prototype's placeholder arm and HUD text from the first-person tour captures (multi-scale diffusion inpaint).
import numpy as np, os, sys
from PIL import Image, ImageFilter
SRC = "C:/Users/99wil/AppData/Local/Temp/claude/C--Projects-festival-tycoon/df97e6d2-acd7-465f-815b-39552b670eca/scratchpad/tour-fp/run3/"
OUT = os.path.dirname(os.path.abspath(__file__)) + "/bg/"; os.makedirs(OUT, exist_ok=True)


def inpaint(img, mask, iters=40):
    """img float HxWx3, mask bool HxW (True = fill)"""
    levels = []; im, m = img.copy(), mask.copy()
    while min(im.shape[:2]) > 12:
        levels.append((im, m))
        h, w = im.shape[0] // 2, im.shape[1] // 2
        known = (~m).astype(float)
        num = (im * known[..., None])[:h * 2, :w * 2].reshape(h, 2, w, 2, 3).sum((1, 3))
        den = known[:h * 2, :w * 2].reshape(h, 2, w, 2).sum((1, 3))
        im = np.where(den[..., None] > 0, num / np.maximum(den, 1e-6)[..., None], 0); m = den == 0
    im[m] = im[~m].mean(0) if (~m).any() else 0.5
    for fine, fm in reversed(levels):
        up = np.repeat(np.repeat(im, 2, 0), 2, 1)
        up = np.pad(up, ((0, fine.shape[0] - up.shape[0]), (0, fine.shape[1] - up.shape[1]), (0, 0)), mode="edge")
        cur = np.where(fm[..., None], up, fine)
        for _ in range(iters):
            p = np.pad(cur, ((1, 1), (1, 1), (0, 0)), mode="edge")
            avg = (p[:-2, 1:-1] + p[2:, 1:-1] + p[1:-1, :-2] + p[1:-1, 2:]) / 4
            cur = np.where(fm[..., None], avg, fine)
        im = cur
    return im


def arm_hull():
    """the placeholder arm never moves on screen: take the union of its yellow and black pixels over every capture, then the convex hull"""
    pts = []
    for n in ("04-crowd-stage", "06-near-bar", "05-crowd-side", "02-post-towards-stage", "03-walking-a", "03-walking-b", "10-shift-over", "01-at-post"):
        a = np.asarray(Image.open(SRC + n + ".png").convert("RGB")).astype(float) / 255; r, g, b = a[..., 0], a[..., 1], a[..., 2]
        m = (r > 0.55) & (g > 0.55) & (b < 0.35) & (np.abs(r - g) < 0.25)
        m[:430] = False; m[:, :700] = False
        ys, xs = np.nonzero(m); pts += list(zip(xs[::7].tolist(), ys[::7].tolist()))
    pts = sorted(set(pts))
    def cross(o, a, b): return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lo, hi = [], []
    for p in pts:
        while len(lo) >= 2 and cross(lo[-2], lo[-1], p) <= 0: lo.pop()
        lo.append(p)
    for p in reversed(pts):
        while len(hi) >= 2 and cross(hi[-2], hi[-1], p) <= 0: hi.pop()
        hi.append(p)
    return lo[:-1] + hi[:-1]
from PIL import ImageDraw
HULL = arm_hull()


def clean(name, rects, arm=True, out=None):
    a = np.asarray(Image.open(SRC + name + ".png").convert("RGB")).astype(float) / 255; H, W = a.shape[:2]
    mask = np.zeros((H, W), bool)
    if arm:                                                         # the yellow sleeve, grey band, skin and black radio in the bottom right
        mi = Image.new("L", (W, H), 0); ImageDraw.Draw(mi).polygon(HULL, fill=255)
        mi = mi.filter(ImageFilter.MaxFilter(21))
        mask |= np.asarray(mi) > 0
    if arm: mask[440:560, 700:840] = True                           # the radio and hand at the arm's tip
    for x0, y0, x1, y1 in rects: mask[y0:y1, x0:x1] = True
    res = inpaint(a, mask)
    Image.fromarray((np.clip(res, 0, 1) * 255).astype(np.uint8)).save(OUT + (out or name) + ".png")
    Image.fromarray((mask * 255).astype(np.uint8)).save(OUT + (out or name) + "_mask.png")


HUD = [(520, 0, 760, 64), (6, 654, 545, 712), (1000, 662, 1278, 694)]        # clock, radio strip, key hints
clean("04-crowd-stage", HUD + [(560, 402, 720, 465)])                     # + Ryan's nameplate
clean("06-near-bar", HUD)
clean("05-crowd-side", HUD + [(545, 410, 725, 472)])
clean("02-post-towards-stage", HUD)
clean("10-shift-over", HUD + [(470, 205, 810, 345)], out="10-shift-over")
print("ok")
