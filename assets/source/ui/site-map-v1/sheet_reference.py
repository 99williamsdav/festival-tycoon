# The laminated site plan on Control's clipboard: header, the printed plan, Control's marker notes, live stickers, legend.
import sys, math
from PIL import Image, ImageDraw, ImageFilter
import plan_reference as plan
from plan_reference import hx, slab, sans, marker, marker_text
H = plan.H
VIEW = (-32, -13, 32, 35.6)                                                  # the festival area, landscape
def make(tier=2, calls=(("urgent", 15, 15, "collapse!"), ("call", -7, -1, "loo 2 stuck?")), crew=(), you=(2, 8, -40), out="sheet.png", W=1400):
    Hh = 1400; sh = Image.new("RGBA", (W, Hh), hx("fbf7ec")); d = ImageDraw.Draw(sh)
    d.text((40, 34), "LOWER WITTERING FESTIVAL", font=sans(26, "Bold"), fill=hx("56615a"))
    d.text((40, 64), "Site plan", font=slab(54), fill=hx("1f2a26")); d.text((W - 40, 82), f"TIER {tier} · STEWARDS' COPY", font=sans(22, "Bold"), fill=hx("b8551e"), anchor="ra")
    d.line([(40, 130), (W - 40, 130)], fill=hx("1f2a26"), width=4)
    kk = (W - 80) / (VIEW[2] - VIEW[0]); full, P0, k = plan.draw_site(tier, int(72 * kk), "printed", label_k=1.55)
    x0, y0 = P0(VIEW[0], VIEW[1]); x1, y1 = P0(VIEW[2], VIEW[3]); pl = full.crop((int(x0), int(y0), int(x1), int(y1)))
    P = lambda x, z: (P0(x, z)[0] - int(x0), P0(x, z)[1] - int(y0))
    # Control's hand annotations, in marker, printed in: arrows and notes
    pd = ImageDraw.Draw(pl)
    notes = [(-6.5, 30, "IN / OUT"), (-9.5, -9, "keep the lane\nclear for Henry")] + ([(27, 11, "generator -\nno guests!")] if tier >= 2 else [(13, 24, "chips queue\ngets long here")])
    for (x, z, txt) in notes:
        X, Y = P(x, z); marker_text(pl, (X, Y), txt, k * 1.5, hx("2b4a8c"), "mm", multiline=True)
    pd.line([P(-3.8, -9), P(-2.4, -9)], fill=hx("2b4a8c"), width=3); pd.polygon([P(-2.2, -9), P(-3.0, -9.6), P(-3.0, -8.4)], fill=hx("2b4a8c"))
    sh.alpha_composite(pl, (40, 150))
    pl = pl  # noqa
    # the live layer: stickers (calls), crew dots, you
    live = Image.new("RGBA", pl.size, (0, 0, 0, 0))
    for kind, x, z, lab in calls: X, Y = P(x, z); plan.pin(live, X, Y, k, kind == "urgent", lab)
    for x, z, busy, name in crew: X, Y = P(x, z); plan.crew_dot(live, X, Y, k, busy, name)
    if you: X, Y = P(you[0], you[1]); plan.you_arrow(live, X, Y, k, you[2])
    sh.alpha_composite(live, (40, 150))
    # legend
    ly = 150 + pl.height + 18; d = ImageDraw.Draw(sh); d.line([(40, ly - 8), (W - 40, ly - 8)], fill=hx("e2d3b3"), width=2)
    items = [("droplet", "2b6e66", "Water"), ("door-closed", "2b6e66", "Loos"), ("beer", "9c7424", "Bar"), ("utensils", "9c7424", "Food"), ("heart-pulse", "b8551e", "First aid"), ("trash-2", "56615a", "Bin"), ("shield", "1f2a26", "Post")]
    x = 40
    for ic, c, lab in items:
        r = 15; x = int(x); d.ellipse([x, ly + 4, x + 2 * r, ly + 4 + 2 * r], fill=hx("ffffff"), outline=hx(c), width=3)
        sh.alpha_composite(H.L.draw_icon(ic, 20, hx(c)), (int(x + 5), ly + 9)); d = ImageDraw.Draw(sh)
        d.text((x + 2 * r + 6, ly + 4 + r), lab, font=sans(22, "Bold"), fill=hx("1f2a26"), anchor="lm"); x += 2 * r + 16 + d.textlength(lab, font=sans(22, "Bold")) + 18
    ly2 = ly + 50
    plan.pin(sh, 56, ly2 + 18, 9, False); d = ImageDraw.Draw(sh); d.text((84, ly2 + 18), "radio call", font=sans(22, "Bold"), fill=hx("1f2a26"), anchor="lm")
    plan.pin(sh, 246, ly2 + 18, 9, True); d = ImageDraw.Draw(sh); d.text((274, ly2 + 18), "urgent", font=sans(22, "Bold"), fill=hx("1f2a26"), anchor="lm")
    plan.you_arrow(sh, 410, ly2 + 18, 8, 0); d = ImageDraw.Draw(sh); d.text((436, ly2 + 18), "you", font=sans(22, "Bold"), fill=hx("1f2a26"), anchor="lm")
    if crew: plan.crew_dot(sh, 530, ly2 + 18, 9); d = ImageDraw.Draw(sh); d.text((548, ly2 + 18), "crew", font=sans(22, "Bold"), fill=hx("1f2a26"), anchor="lm")
    d.text((W - 40, ly2 + 18), "M  put away", font=sans(22, "Bold"), fill=hx("56615a"), anchor="rm")
    # lamination: a soft diagonal sheen
    gl = Image.new("L", sh.size, 0); gd_ = ImageDraw.Draw(gl); gd_.polygon([(W * 0.55, 0), (W * 0.75, 0), (W * 0.2, Hh), (0, Hh), (0, Hh * 0.9)], fill=40)
    gl = gl.filter(ImageFilter.GaussianBlur(60)); white = Image.new("RGBA", sh.size, (255, 255, 255, 255)); white.putalpha(gl); sh.alpha_composite(white)
    sh = sh.crop((0, 0, W, ly2 + 56)); sh.convert("RGB").save(out); return sh
if __name__ == "__main__":
    make(2, crew=((4, 4, False, "Clive"), (-8, 15, False, "Jo"), (14, 8, True, "Terry")), out="sheet_t2_allrounder.png")
    make(2, out="sheet_t2_steward.png")
    make(1, calls=(("call", -7, -1, "loo 2 stuck?"),), you=(-4, 6, 60), out="sheet_t1.png")
