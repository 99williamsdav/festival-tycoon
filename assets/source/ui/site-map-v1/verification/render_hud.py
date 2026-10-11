# python render_hud.py : the approved site map in the HUD, over the cleaned first-person captures with the installed arms.
import os, sys, math
from PIL import Image, ImageDraw
V = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, os.path.join(V, "..")); sys.path.insert(0, "C:/Projects/festival-tycoon/assets/source/ui/steward-shift-hud-v1")
import compose_reference as CR, hud_reference as H
from hud_reference import Shot, R, text, col, sans, icon, keycap, S
H.L.ICONS["map"] = ["M14.106 5.553a2 2 0 0 0 1.788 0l3.659-1.83A1 1 0 0 1 21 4.619v12.764a1 1 0 0 1-.553.894l-4.553 2.277a2 2 0 0 1-1.788 0l-4.212-2.106a2 2 0 0 0-1.788 0l-3.659 1.83A1 1 0 0 1 3 19.381V6.618a1 1 0 0 1 .553-.894l4.553-2.277a2 2 0 0 1 1.788 0z", "M15 5.764v15", "M9 3.236v15"]
BG = "C:/Projects/festival-tycoon/assets/source/ui/steward-shift-hud-v1/verification/bg/"; ARMS = "C:/Projects/festival-tycoon/assets/source/characters/steward-fp-arms-v1/verification/"
CALLS = (("urgent", 15, 15, "collapse!"), ("call", -7, -1, "loo 2 stuck?")); CREW = ((4, 4, False, "Clive"), (-8, 18, False, "Jo"), (14, 8, True, "Terry"))
def hud(s, allrounder):
    H.crosshair(s); H.compass_A(s, heading=55, obj=(98, "Pond Stage", 24), urgent=True)
    H.radio_A(s, msg="Collapse at the Pond Stage. Go now!", sub="Pond Stage front · 24 m", urgent=True); H.needs_A(s, thirst=0.62, heat=0.35, y1=704 - 74 - 6)
    if allrounder:
        d = s.d; x, y = 1264 - 178, 16; d.rounded_rectangle(R(x, y, x + 178, y + 36), 8 * S, fill=H.hx("22453a"), outline=H.hx("3b5a50"), width=S)
        keycap(d, x + 7, y + 7, "V", 22); icon(s.im, "map", x + 36, y + 9, 18, col("gold")); d = s.d; text(d, (x + 60, y + 18), "Manager view", sans(14, "Bold"), col("bartext"), "lm")
def mini(s, allrounder, alpha=1.0):
    m = CR.minimap(2, (-9, 15), 55, CALLS, CREW if allrounder else (), allrounder)
    if alpha < 1: a = m.split()[3].point(lambda v: int(v * alpha)); m.putalpha(a)
    x, y = (1264 - 176, 64) if allrounder else (1264 - 176, 16); s.im.alpha_composite(m, (int(x * S), int(y * S))); s.d = ImageDraw.Draw(s.im)
def shot(arm="_arms_idle.png", bg="04-crowd-stage"):
    s = Shot(BG + bg + ".png"); s.arms(ARMS + arm); return s
s = shot(); hud(s, True); mini(s, True); s.save(os.path.join(V, "hud-minimap-allrounder.png"))
s = shot(); hud(s, False); mini(s, False); s.save(os.path.join(V, "hud-minimap-steward-only.png"))
s = shot("_arms_calm.png"); hud(s, True); H.prompt_A(s, frac=0.5); mini(s, True); s.save(os.path.join(V, "hud-minimap-with-action.png"))
s = shot(); s.arms(os.path.join(V, "_held-half.png")); hud(s, True); mini(s, True, 0.45); s.save(os.path.join(V, "hud-plan-raising-minimap-fading.png"))
s = shot(); s.arms(os.path.join(V, "_held-up.png")); hud(s, True)
d = s.d; t = "WALKING SLOWLY · M to put away"; w = d.textlength(t, font=sans(11, "Bold")) / S + 20
lay = s.layer(); dd = ImageDraw.Draw(lay); dd.rounded_rectangle(R(640 - w / 2, 70, 640 + w / 2, 92), 11 * S, fill=col("bardeep", 190)); s.put(lay); text(s.d, (640, 81), t, sans(11, "Bold"), col("bartext"), "mm")
s.save(os.path.join(V, "hud-plan-up.png"))
print("ok")
