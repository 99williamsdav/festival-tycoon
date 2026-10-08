# The chewed-cable world icon for option C: an amber badge, ink outline, a lightning bolt, and a bite out of the rim.
from PIL import Image, ImageDraw, ImageChops
import math


def hx(h, a=255): return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (a,)


def chewed_icon(px=34):
    big = 8; S = px * big; im = Image.new("RGBA", (S, S + S // 4), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    c = S / 2; r = S * 0.44
    def disc(rr, col): d.ellipse([c - rr, c - rr, c + rr, c + rr], fill=col)
    d.polygon([(c - r * 0.42, c + r * 0.82), (c + r * 0.42, c + r * 0.82), (c, c + r * 1.45)], fill=hx("1d2a25"))   # pointer tail
    disc(r + S * 0.05, hx("1d2a25")); disc(r, hx("f2a65a"))
    bite = Image.new("L", im.size, 0); bd = ImageDraw.Draw(bite)                                                   # bite: three scallops
    for k, a in enumerate((-60, -35, -10)):
        bx, by = c + (r + S * 0.03) * math.cos(math.radians(a)), c + (r + S * 0.03) * math.sin(math.radians(a))
        rr = S * 0.11; bd.ellipse([bx - rr, by - rr, bx + rr, by + rr], fill=255)
    im.putalpha(ImageChops.subtract(im.split()[3], bite))
    d = ImageDraw.Draw(im)
    z = [(0.10, -0.62), (-0.30, 0.06), (-0.02, 0.06), (-0.14, 0.62), (0.30, -0.10), (0.02, -0.10), (0.10, -0.62)]
    d.polygon([(c + x * r, c + y * r) for x, y in z], fill=hx("1d2a25"))
    return im.resize((px, px + px // 4), Image.LANCZOS)


if __name__ == "__main__":
    # python make_icon.py -> out/pin_chewed_cable.png: the world icon at 2x (60 x 75), in the field-note pin family.
    # Shows 30 x 37 at 1280 x 720; the pointer tip is at pixel (30, 68).
    import os
    here = os.path.dirname(os.path.abspath(__file__)); os.makedirs(os.path.join(here, "out"), exist_ok=True)
    im = chewed_icon(60); im.save(os.path.join(here, "out", "pin_chewed_cable.png"))
    a = im.split()[3]; bb = a.getbbox(); print("ICON", im.size, "alpha bbox", bb)
