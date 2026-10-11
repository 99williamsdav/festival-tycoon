# python compose_reference.py : the report card composed ONLY from the out/ art files, at the layout in INTEGRATION.md
# (1280 x 720 mockup units = Ui.S units, drawn at 2x). -> verification/report-<case>.png
import os, sys
from PIL import Image, ImageDraw, ImageFilter
HERE = os.path.dirname(os.path.abspath(__file__)); O = os.path.join(HERE, "out"); V = os.path.join(HERE, "verification"); os.makedirs(V, exist_ok=True)
sys.path.insert(0, "C:/Projects/festival-tycoon/assets/source/ui/steward-shift-hud-v1"); import hud_reference as H
K = 2; col = H.col; slab, sans = H.slab, H.sans
FIELD = "C:/Projects/festival-tycoon/assets/source/ui/steward-shift-hud-v1/verification/bg/10-shift-over.png"
BOARD = (380, 18); SHEET_IN_BOARD = (22, 44); SHEET = (476, 634); SHEET_ROT = -1.2
STAMP_CENTRE_IN_BOARD = (408, 114); STAMP_SCALE = 0.6; STAMP_ROT = 14
GRADES = [("encore", "Encore!"), ("headliner", "Headliner"), ("crowd_pleaser", "Crowd-pleaser"), ("b_side", "B-side"), ("off_key", "Off-key")]
ICON = {"Calls answered": "radio", "Average response": "clock", "Fights prevented": "shield", "Fights broken up": "triangle-alert", "Crowd mood": "users", "Litter picked up": "trash-2"}


def card(stats, prevented, broken, grade, note, out):
    fld = Image.open(FIELD).convert("RGB").filter(ImageFilter.GaussianBlur(6)); fld = Image.blend(fld, Image.new("RGB", fld.size, (14, 31, 26)), 0.55)
    im = fld.resize((1280 * K, 720 * K), Image.BICUBIC).convert("RGBA"); d = ImageDraw.Draw(im)
    def T(dd, xy, t, f, c, a="la"): dd.text((xy[0] * K, xy[1] * K), t, font=f, fill=c, anchor=a)
    T(d, (36, 34), "END OF SHIFT", sans(10, "Bold"), col("gold")); T(d, (36, 50), "Steward's report", slab(22, True), col("bartext"))
    bx, by = BOARD
    sh = Image.new("RGBA", im.size, (0, 0, 0, 0)); ImageDraw.Draw(sh).rounded_rectangle([(bx + 4) * K, (by + 6) * K, (bx + 524) * K, (by + 700) * K], 18 * K, fill=(0, 0, 0, 120))
    im.alpha_composite(sh.filter(ImageFilter.GaussianBlur(16)))
    im.alpha_composite(Image.open(os.path.join(O, "lwf_report_clipboard_v1.png")), (bx * K, by * K))
    sheet = Image.open(os.path.join(O, "lwf_report_sheet_v1.png")).convert("RGBA"); sd = ImageDraw.Draw(sheet); W_ = SHEET[0]
    T(sd, (24, 30), "Steward shift report", slab(28, True), col("ink")); T(sd, (24, 66), "Steward 4  ·  Saturday  ·  18:00 – 21:00  ·  Lower Wittering", sans(12.5), col("inkm"))
    sd.line([(24 * K, 90 * K), ((W_ - 24) * K, 90 * K)], fill=col("ink"), width=2 * K)
    for i, (lab, val) in enumerate(stats):
        y = 108 + i * 44
        sd.line([(24 * K, (y + 36) * K), ((W_ - 24) * K, (y + 36) * K)], fill=col("rule"), width=K)
        sheet.alpha_composite(H.L.draw_icon(ICON[lab], 18 * K, col("teal")), (28 * K, (y + 6) * K))
        T(sd, (58, y + 6), lab, sans(15, "Regular"), col("ink")); T(sd, (W_ - 28, y + 4), val, slab(20, True), col("ink"), "ra")
    T(sd, (24, 384), "Fights", slab(17, True), col("ink"))
    x0, x1, y0, y1 = 24, W_ - 24, 412, 426
    if prevented + broken == 0:                                              # a quiet shift
        sd.rounded_rectangle([x0 * K, y0 * K, x1 * K, y1 * K], 7 * K, fill=col("rule"))
        T(sd, ((x0 + x1) / 2, 432), "No fights on your shift. Unplugged!", sans(12, "SemiBold"), col("inkm"), "ma")
    else:
        sd.rounded_rectangle([x0 * K, y0 * K, x1 * K, y1 * K], 7 * K, fill=col("alertwash"))
        share = prevented / (prevented + broken)
        if share > 0: sd.rounded_rectangle([x0 * K, y0 * K, (x0 + (x1 - x0) * share) * K, y1 * K], 7 * K, fill=col("teal"))
        T(sd, (x0, 432), f"{prevented} prevented before a punch", sans(12, "SemiBold"), col("teal")); T(sd, (x1, 432), f"{broken} broken up", sans(12, "SemiBold"), col("alert"), "ra")
    T(sd, (24, 470), "Grade", slab(17, True), col("ink"))
    gx = 24
    for key, gname in GRADES:
        gw = sd.textlength(gname, font=sans(12.5, "Bold")) / K + 18; on = key == grade
        sd.rounded_rectangle([gx * K, 496 * K, (gx + gw) * K, 520 * K], 12 * K, fill=col("ink") if on else None, outline=col("ink") if on else col("rule"), width=K)
        T(sd, (gx + gw / 2, 508), gname, sans(12.5, "Bold"), col("bartext") if on else col("inkm"), "mm"); gx += gw + 6
    T(sd, (24, 548), "CONTROL'S NOTE", sans(10, "Bold"), col("inkm"))
    for k, line in enumerate(note): T(sd, (24, 564 + k * 20), line, sans(13.5, "Regular"), col("ink"))
    T(sd, (W_ - 24, 612), "~ Control", slab(16, True), col("inkm"), "ra")
    rs = sheet.rotate(SHEET_ROT, resample=Image.BICUBIC, expand=True)
    sx, sy = bx + SHEET_IN_BOARD[0] + SHEET[0] / 2, by + SHEET_IN_BOARD[1] + SHEET[1] / 2                       # rotate about the sheet's centre
    im.alpha_composite(rs, (int(sx * K - rs.width / 2), int(sy * K - rs.height / 2)))
    clip = Image.open(os.path.join(O, "lwf_report_clip_v1.png")); im.alpha_composite(clip, (int((bx + 260 - 75) * K), (by + 2) * K))
    st = Image.open(os.path.join(O, f"lwf_report_stamp_{grade}_v1.png")); st = st.resize((int(st.width * STAMP_SCALE), int(st.height * STAMP_SCALE)), Image.LANCZOS).rotate(STAMP_ROT, resample=Image.BICUBIC, expand=True)
    cx, cy = bx + STAMP_CENTRE_IN_BOARD[0], by + STAMP_CENTRE_IN_BOARD[1]; im.alpha_composite(st, (int(cx * K - st.width / 2), int(cy * K - st.height / 2)))
    d = ImageDraw.Draw(im)
    for k, lab in enumerate(("Next shift", "Return to menu")):
        y = 590 + k * 56; d.rounded_rectangle([1076 * K, y * K, 1264 * K, (y + 46) * K], 8 * K, fill=col("gold")); T(d, (1170, y + 23), lab, sans(15, "Bold"), col("bar"), "mm")
    im.convert("RGB").resize((1280, 720), Image.LANCZOS).save(os.path.join(V, out))


STATS = [("Calls answered", "9 of 11"), ("Average response", "0:48"), ("Fights prevented", "3"), ("Fights broken up", "1"), ("Crowd mood", "72%  (from 64%)"), ("Litter picked up", "14")]
card(STATS, 3, 1, "headliner", ["Cool head at the bar. Collapse reached in 41 s.", "A touch slow on the toilets."], "report-headliner.png")
card([("Calls answered", "11 of 11"), ("Average response", "0:29"), ("Fights prevented", "0"), ("Fights broken up", "0"), ("Crowd mood", "88%  (from 70%)"), ("Litter picked up", "31")],
     0, 0, "encore", ["Flawless. Not a punch thrown all night.", "Take tomorrow off. You've earned it."], "report-encore-no-fights.png")
card([("Calls answered", "4 of 12"), ("Average response", "2:51"), ("Fights prevented", "0"), ("Fights broken up", "5"), ("Crowd mood", "41%  (from 62%)"), ("Litter picked up", "2")],
     0, 5, "off_key", ["Where were you for the collapse?", "We'll talk on Monday."], "report-off-key.png")
print("ok")
