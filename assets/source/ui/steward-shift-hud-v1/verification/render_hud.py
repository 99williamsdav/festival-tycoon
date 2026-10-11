# python render_hud.py : the approved HUD (prompt A, compass A, radio A, needs A) over the first-person captures, with the INSTALLED
# arms (renders from ../../../characters/steward-fp-arms-v1/verification). Backgrounds: the prototype tour captures (run 3) with
# the placeholder arm and prototype labels painted out (bg/clean_captures.py).
import os, sys
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, os.path.join(HERE, ".."))
import hud_reference as H
ARMS = "C:/Projects/festival-tycoon/assets/source/characters/steward-fp-arms-v1/verification/"
def shot(bg, arms):
    s = H.Shot(os.path.join(HERE, "bg", bg + ".png")); s.arms(ARMS + f"_arms_{arms}.png"); return s
def save(s, n): s.save(os.path.join(HERE, n + ".png"))
for state in ("hold", "success", "fail", "cancel"):
    s = shot("04-crowd-stage", "calm" if state in ("hold", "success", "fail") else "calm-half"); H.crosshair(s); H.prompt_A(s, state=state, frac=0.62)
    H.compass_A(s, obj=(38, "Bar 2", 64)); save(s, f"prompt-{state}")
s = shot("06-near-bar", "door"); H.crosshair(s); H.prompt_A(s, verb="Open", target="Toilet 3", sub="engaged for 25 minutes", ic="door-closed", frac=0.4); save(s, "prompt-door")
s = shot("05-crowd-side", "litter"); H.crosshair(s); H.prompt_A(s, verb="Pick up", target="crushed cup", sub="litter · 1 m", ic="trash-2", frac=0.8); save(s, "prompt-litter")
s = shot("02-post-towards-stage", "idle"); H.crosshair(s); H.compass_A(s, heading=20, obj=(48, "Bar 2", 64)); H.radio_A(s); save(s, "compass-radio")
s = shot("02-post-towards-stage", "idle"); H.urgent_vignette(s); H.crosshair(s); H.compass_A(s, heading=20, obj=(-120, "Stage front", 38), urgent=True)
H.radio_A(s, msg="Collapse at the stage. Go now!", sub="Stage front · 38 m behind you · medic on the way", urgent=True); save(s, "urgent-clamped")
s = shot("06-near-bar", "idle"); H.crosshair(s); H.compass_A(s, heading=40, obj=(40, "Bar 2", 6)); H.radio_A(s, msg="Quiet one. Keep an eye on the queue.", sub="Bar 2 · you're here"); save(s, "needs-hidden")
s = shot("06-near-bar", "idle"); H.crosshair(s); H.compass_A(s, heading=40, obj=(40, "Bar 2", 6)); H.radio_A(s, msg="Quiet one. Keep an eye on the queue.", sub="Bar 2 · you're here"); H.needs_A(s, thirst=0.66, heat=0.42); save(s, "needs-shown")
print("ok")
