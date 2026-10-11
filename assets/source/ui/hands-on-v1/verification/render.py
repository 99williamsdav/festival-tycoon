# python render.py : every approved piece built from ../out art at the INTEGRATION.md numbers (1280 mockup units, drawn at 2x).
import os, sys, math
from PIL import Image, ImageDraw, ImageFilter
V = os.path.dirname(os.path.abspath(__file__)); O = os.path.join(V, "..", "out")
sys.path.insert(0, "C:/Projects/festival-tycoon/assets/source/ui/steward-shift-hud-v1"); import hud_reference as H
from hud_reference import Shot, R, text, tw, col, slab, sans, icon, shadowed, S, keycap
for n in ("truck", "wrench", "user", "chevron-right", "file-text", "chart-column", "lock"): H.L.ICONS[n] = H.load_svg(n)
H.L.ICONS["map"] = ["M14.106 5.553a2 2 0 0 0 1.788 0l3.659-1.83A1 1 0 0 1 21 4.619v12.764a1 1 0 0 1-.553.894l-4.553 2.277a2 2 0 0 1-1.788 0l-4.212-2.106a2 2 0 0 0-1.788 0l-3.659 1.83A1 1 0 0 1 3 19.381V6.618a1 1 0 0 1 .553-.894l4.553-2.277a2 2 0 0 1 1.788 0z", "M15 5.764v15", "M9 3.236v15"]
BG = "C:/Projects/festival-tycoon/assets/source/ui/steward-shift-hud-v1/verification/bg/"
ARM_MENU = os.path.join(V, "arm_menu_raise.png")
def art(n): return Image.open(os.path.join(O, n)).convert("RGBA")
def put(s, img, cx, cy, rot=0.0):
    if rot: img = img.rotate(-rot, resample=Image.BICUBIC)                     # clockwise
    s.im.alpha_composite(img, (int(cx * S - img.width / 2), int(cy * S - img.height / 2))); s.d = ImageDraw.Draw(s.im)


# ============================================================ 1B radial
CX, CY = 640, 360
SPRITE = {"free": "normal"}                                               # a free entry uses the normal sprite
MAIN = [(0, "heart-pulse", "Send the medic", "Hattie", "free", "42 m"), (120, "truck", "Call Dirty Henry", "emptying Toilet 2", "busy", "back in 3 min"),
        (240, "users", "Send crew", "2 free · 1 busy", "free", "")]
def wedge_label(s, ang, r, ic, name, line, st, dist, crew=False):
    t = math.radians(ang - 90); x, y = CX + r * math.cos(t), CY + r * math.sin(t); d = s.d
    icol = col("alert") if ic == "heart-pulse" else (col("inkm") if st == "disabled" else col("teal"))
    if st == "hover": icol = col("bar")
    icon(s.im, ic, x - 10, y - 30, 20, icol if st != "disabled" else col("barmuted")); d = s.d
    text(d, (x, y + 1), name, slab(13 if not crew else 12.5, True), col("ink") if st != "disabled" else col("bartext"), "mm")
    sc = {"free": col("teal"), "busy": col("alert"), "hover": col("bar"), "disabled": col("barmuted")}[st]
    text(d, (x, y + 17), line if st == "disabled" else ((st.upper() if st != "hover" else "FREE") + (" · " + dist if dist else "")), sans(10, "Bold"), sc, "mm")
def radial(s, hover=0, crew_open=False, crew_hover=None, tier1=False):
    put(s, art("lwf_radial_backdrop_v1.png"), CX, CY)
    for i, (ang, ic, name, line, st, dist) in enumerate(MAIN):
        state = "hover" if i == hover else st
        if i == 2 and tier1: state = "disabled"
        put(s, art(f"lwf_radial_wedge3_{SPRITE.get(state, state)}_v1.png"), CX, CY, ang)
    for i, (ang, ic, name, line, st, dist) in enumerate(MAIN):
        state = "hover" if i == hover else st
        if i == 2 and tier1: state, line = "disabled", "No crew hired · hire in Staff"
        wedge_label(s, ang, 104, ic, name, line, state, dist)
    if crew_open:
        crew = [("shield", "Clive Pugh", "steward", "free", "18 m"), ("wrench", "Terry Bunce", "fixing a tap", "busy", "60 m"), ("shield", "Jo Marsh", "steward", "free", "70 m")]
        N = len(crew); span = (180 - 6 * (N - 1)) / N
        for j, (ic, nm, role, st, dist) in enumerate(crew):
            ang = 240 - 90 + span / 2 + j * (span + 6); state = "hover" if j == crew_hover else st
            put(s, art(f"lwf_radial_crew{N}_{SPRITE.get(state, state)}_v1.png"), CX, CY, ang)
            wedge_label(s, ang, 199, ic, nm, role, state, dist, crew=True)
    put(s, art("lwf_radial_hub_v1.png"), CX, CY); d = s.d
    hov = MAIN[hover][2] if not crew_open else (["Clive Pugh", "Terry Bunce", "Jo Marsh"][crew_hover] if crew_hover is not None else "pick crew")
    text(d, (CX, CY - 14), "CONTROL", sans(10, "Bold"), col("hivis"), "mm"); text(d, (CX, CY + 2), hov, slab(12, True), col("bartext"), "mm")
    text(d, (CX, CY + 18), "release to send", sans(9.5, "SemiBold"), col("barmuted"), "mm")
    ang = (MAIN[hover][0] if not crew_open else (240 - 90 + (60 * crew_hover + 30 if crew_hover is not None else 90)))
    t = math.radians(ang - 90); d.line([(CX * S, CY * S), ((CX + 46 * math.cos(t)) * S, (CY + 46 * math.sin(t)) * S)], fill=col("gold"), width=3 * S)
    d.ellipse(R(CX + 46 * math.cos(t) - 4, CY + 46 * math.sin(t) - 4, CX + 46 * math.cos(t) + 4, CY + 46 * math.sin(t) + 4), fill=col("gold"))
def fp(bg="04-crowd-stage"):
    s = Shot(BG + bg + ".png"); s.arms(ARM_MENU); H.compass_A(s, heading=-30, obj=(-40, "Stage front", 38), urgent=True); return s
s = fp(); radial(s, 0); s.save(os.path.join(V, "radial-hover-medic.png"))
s = fp(); radial(s, 1); s.save(os.path.join(V, "radial-hover-busy-henry.png"))
s = fp("05-crowd-side"); radial(s, 2, crew_open=True, crew_hover=0); s.save(os.path.join(V, "radial-crew-open.png"))
s = fp(); radial(s, 0, tier1=True); s.save(os.path.join(V, "radial-tier1-crew-disabled.png"))
s = Shot(BG + "04-crowd-stage.png"); s.arms(os.path.join(V, "arm_talk_raise.png")); H.crosshair(s); H.compass_A(s, heading=-30, obj=(-40, "Stage front", 38), urgent=True)
H.radio_A(s, msg="Hattie: on my way. Stage front, 42 m.", who="MEDIC → YOU", sub="Sent by you · the call stays on your compass"); s.save(os.path.join(V, "radial-released-sent.png"))


# ============================================================ 2A "Your shift" tab
def paper_shell(active, dot=False):
    fld = Image.open(BG + "02-post-towards-stage.png").convert("RGB").filter(ImageFilter.GaussianBlur(8)); fld = Image.blend(fld, Image.new("RGB", fld.size, (14, 31, 26)), 0.6)
    s = Shot.__new__(Shot); s.im = fld.resize((1280 * S, 720 * S), Image.BICUBIC).convert("RGBA"); s.d = ImageDraw.Draw(s.im); d = s.d
    text(d, (36, 34), "THE MORNING AFTER", sans(10, "Bold"), col("gold")); text(d, (36, 50), "Festival results", slab(22, True), col("bartext"))
    shadowed(s, R(230, 30, 1060, 690), 0, H.hx("f6f0e2"), shadow=22, salpha=150); d = s.d
    for i, (t, ic) in enumerate((("Newspaper", "file-text"), ("Accounts", "chart-column"), ("Performances", "music"), ("Your shift", "radio"))):
        y = 70 + i * 52; on = t == active
        d.rounded_rectangle(R(1048, y, 1230, y + 46), 8 * S, fill=H.hx("f6f0e2") if on else H.hx("d5c6a0"))
        icon(s.im, ic, 1074, y + 13, 18, col("ink")); d = s.d; text(d, (1100, y + 23), t, sans(15, "Bold"), col("ink"), "lm")
        if t == "Your shift" and dot: d.ellipse(R(1210, y + 8, 1222, y + 20), fill=col("alert"), outline=H.hx("f6f0e2"), width=2 * S)
    for k, lab in enumerate(("Next festival", "Return to menu")):
        y = 566 + k * 58; d.rounded_rectangle(R(1076, y, 1264, y + 46), 8 * S, fill=col("gold")); text(d, (1170, y + 23), lab, sans(15, "Bold"), col("bar"), "mm")
    return s
s = paper_shell("Your shift"); d = s.d
text(d, (260, 52), "Your shift", slab(30, True), col("ink")); text(d, (260, 92), "You on the radio · organiser and steward · 18:00–21:00", sans(13), col("inkm"))
d.line([(260 * S, 118 * S), (1016 * S, 118 * S)], fill=col("ink"), width=2 * S)
cards = [("CALLS ANSWERED", "9 of 11", "avg response 0:48", False), ("FIGHTS", "3 · 1", "prevented · broken up", False),
         ("MOOD YOU ADDED", "+8%", "64% → 72% while on shift", False), ("CONTROL'S GRADE", "Headliner", "4 of 5 · litter 14", True)]
w = (756 - 36) / 4
for i, (cap, big, small, dark) in enumerate(cards):
    x0 = 260 + i * (w + 12)
    d.rounded_rectangle(R(x0, 134, x0 + w, 218), 6 * S, fill=col("ink") if dark else H.hx("fffdf6"), outline=None if dark else col("rule"), width=S)
    text(d, (x0 + 12, 146), cap, sans(9.5, "SemiBold"), col("gold") if dark else col("inkm"))
    text(d, (x0 + 12, 160), big, slab(26, True), col("bartext") if dark else col("teal")); text(d, (x0 + 12, 196), small, sans(11.5), col("barmuted") if dark else col("inkm"))
text(d, (260, 240), "Calls on the night", slab(19, True), col("ink"))
calls = [("19:04", "Lost child by the stage", "you"), ("19:40", "Fight by Bar 2", "you"), ("20:12", "Toilet 3 engaged for ages", "henry"), ("20:31", "Collapse at the stage", "medic"), ("20:55", "Litter by the van", "missed")]
WHO = {"you": ("you went", "user", col("teal")), "medic": ("sent the medic", "heart-pulse", col("teal")), "henry": ("sent Henry", "truck", col("teal")), "missed": ("missed", "x", col("alert"))}
for i, (t, what, who) in enumerate(calls):
    y = 272 + i * 30; d.line([(260 * S, (y + 24) * S), (640 * S, (y + 24) * S)], fill=col("rule"), width=S)
    text(d, (260, y), t, sans(13, "Bold"), col("inkm")); text(d, (310, y), what, sans(13.5), col("ink"))
    lab, ic, c = WHO[who]; text(d, (640, y), lab, sans(12, "Bold"), c, "ra"); icon(s.im, ic, 640 - tw(d, lab, sans(12, "Bold")) - 20, y + 1, 14, c); d = s.d
d.rectangle(R(260, 440, 640, 536), fill=H.hx("efe6d2")); d.rectangle(R(260, 440, 264, 536), fill=col("ink"))
text(d, (278, 452), "IN THE PAPER", sans(9.5, "Bold"), col("inkm")); text(d, (278, 468), "\u201cOrganiser seen breaking up a scuffle\u201d", slab(19, True), col("ink"))
text(d, (278, 510), "The Lower Wittering Gazette, page 2", sans(11.5), col("inkm"))
mini = art("lwf_results_mini_clipboard_v1.png"); s.im.alpha_composite(mini, (int(684 * S), int(240 * S))); s.d = ImageDraw.Draw(s.im)
st = Image.open("C:/Projects/festival-tycoon/game/assets/ui/steward/lwf_report_stamp_headliner_v1.png").convert("RGBA")
st = st.resize((int(st.width * 0.4), int(st.height * 0.4)), Image.LANCZOS); put(s, st, 885, 420, -14)
s.save(os.path.join(V, "results-your-shift.png"))
s = paper_shell("Newspaper", dot=True); d = s.d; text(d, (638, 92), "The Lower Wittering Gazette", slab(40, True), col("ink"), "mm"); s.save(os.path.join(V, "results-tab-unseen-dot.png"))


# ============================================================ 3A title buttons (three modes), cues, YOU marker
TITLE = "C:/Users/99wil/AppData/Local/Temp/claude/C--Projects-festival-tycoon/df97e6d2-acd7-465f-815b-39552b670eca/scratchpad/hands-on/01-title.png"
CREAM, LINK, MUTED, FILL, EDGE = H.hx("f3e8c9"), H.hx("e8f23a"), H.hx("b4bfb2"), H.hx("15201f"), H.hx("5d6a64")
MODES = [("MANAGEMENT ONLY", "map", CREAM, "Run the festival from the office.", False), ("ALL-ROUNDER", "user", LINK, "Run it, and steward it yourself until you can afford the help.", True),
         ("STEWARD ONLY", "radio", LINK, "Pull on the hi-vis for a single shift.", False)]
def title(hover=None, focus=None):
    im = Image.open(TITLE).convert("RGB").resize((1280, 720), Image.LANCZOS); bgc = im.getpixel((40, 600))
    ImageDraw.Draw(im).rectangle([200, 508, 1080, 720], fill=bgc)
    s = Shot.__new__(Shot); s.im = im.resize((1280 * S, 720 * S), Image.BICUBIC).convert("RGBA"); s.d = ImageDraw.Draw(s.im); d = s.d
    bw, bh, gap = 244, 58, 16; x0 = 640 - (3 * bw + 2 * gap) / 2; y = 514
    for i, (lab, ic, c, strap, new) in enumerate(MODES):
        x = x0 + i * (bw + gap); hov = hover == i
        if focus == i: d.rounded_rectangle(R(x - 4, y - 4, x + bw + 4, y + bh + 4), 8 * S, outline=col("gold", 160), width=2 * S)
        d.rounded_rectangle(R(x, y, x + bw, y + bh), 5 * S, fill=H.hx("1d2b29") if hov else FILL, outline=col("gold") if hov else (CREAM if i == 0 else EDGE), width=2 * S)
        icon(s.im, ic, x + 16, y + bh / 2 - 11, 22, c); d = s.d
        text(d, (x + 48, y + bh / 2), lab, sans(18, "SemiBold"), c, "lm")
        if new: d.rounded_rectangle(R(x + bw - 46, y - 9, x + bw - 6, y + 9), 9 * S, fill=col("gold")); text(d, (x + bw - 26, y), "NEW", sans(10, "Bold"), col("bar"), "mm")
        lines, line = [], ""                                                      # strapline under the button, wrapped to its width
        for wd in strap.split():
            if tw(d, (line + " " + wd).strip(), sans(13, "Regular")) > bw: lines.append(line); line = wd
            else: line = (line + " " + wd).strip()
        lines.append(line)
        for k, l in enumerate(lines): text(d, (x + bw / 2, y + bh + 14 + k * 19), l, sans(13, "Regular"), MUTED, "mm")
    text(d, (640, 640), "Field guide", sans(15, "Regular"), CREAM, "mm")
    text(d, (556, 690), "Try the Pond Stage · trial", sans(14, "Regular"), H.hx("9fd3c2"), "mm"); text(d, (738, 690), "Steward shift · prototype", sans(14, "Regular"), LINK, "mm")
    return s
title().save(os.path.join(V, "title-modes.png")); title(hover=1, focus=1).save(os.path.join(V, "title-modes-hover-allrounder.png"))
MANAGER = os.path.join(V, "manager_bg.png")
def cue(s, x, y, key, ic, label, iconcol):
    d = s.d; w = 34 + 24 + tw(d, label, sans(14, "Bold")) + 16
    d.rounded_rectangle(R(x, y, x + w, y + 36), 8 * S, fill=H.hx("22453a"), outline=H.hx("3b5a50"), width=S)
    keycap(d, x + 7, y + 7, key, 22); icon(s.im, ic, x + 36, y + 9, 18, iconcol); d = s.d
    text(d, (x + 60, y + 18), label, sans(14, "Bold"), col("bartext"), "lm"); return w
s = Shot.__new__(Shot); s.im = Image.open(MANAGER).convert("RGBA").resize((1280 * S, 720 * S), Image.BICUBIC); s.d = ImageDraw.Draw(s.im)
s.d.rectangle(R(250, 666, 1108, 708), fill=H.hx("0f1e1a")); cue(s, 1108 - 180, 668, "V", "user", "Walk the field", col("hivis"))
ring = art("lwf_you_ring_v1.png").resize((int(60 * S), int(30 * S)), Image.LANCZOS); put(s, ring, 650, 412)
put(s, art("lwf_you_tag_v1.png"), 650, 346); s.save(os.path.join(V, "manager-you-tag.png"))
s = Shot.__new__(Shot); s.im = Image.open(MANAGER).convert("RGBA").resize((1280 * S, 720 * S), Image.BICUBIC); s.d = ImageDraw.Draw(s.im)
s.d.rectangle(R(250, 666, 1108, 708), fill=H.hx("0f1e1a")); cue(s, 1108 - 180, 668, "V", "user", "Walk the field", col("hivis"))
put(s, art("lwf_you_edge_arrow_v1.png"), 1236, 380, 0); put(s, art("lwf_you_edge_disc_v1.png"), 1236, 380); d = s.d; text(d, (1228, 414), "You · 85 m", sans(12, "Bold"), col("bartext"), "mm")
put(s, art("lwf_you_edge_arrow_v1.png"), 900, 92, -60); put(s, art("lwf_you_edge_disc_v1.png"), 900, 92); d = s.d; text(d, (900, 126), "You · 40 m", sans(12, "Bold"), col("bartext"), "mm")
s.save(os.path.join(V, "manager-you-edge.png"))
s = Shot(BG + "06-near-bar.png"); s.arms("C:/Projects/festival-tycoon/assets/source/characters/steward-fp-arms-v1/verification/_arms_idle.png"); H.crosshair(s); H.compass_A(s, heading=40, obj=(40, "Bar 2", 6))
lay = s.layer(); dd = ImageDraw.Draw(lay); s.put(lay); cue(s, 1264 - 178, 16, "V", "map", "Manager view", col("gold"))
H.radio_A(s, msg="Quiet one. Keep an eye on the queue.", sub="Bar 2 · you're here"); s.save(os.path.join(V, "fp-manager-view-cue.png"))
print("ok")
