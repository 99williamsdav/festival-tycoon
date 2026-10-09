# python make_sheets.py <dir>: joins the per-frame tiles from verify_rain_guests.py into one contact sheet per sex
import sys, os, glob
from PIL import Image
D = sys.argv[1]
for sex in ("male", "female"):
    tiles = glob.glob(os.path.join(D, f"_{sex}_*_*.png"))
    if not tiles: continue
    rows = 1 + max(int(os.path.basename(t).split("_")[2]) for t in tiles)
    sheet = Image.new("RGB", (300 * 4, 360 * rows), "white")
    for t in tiles:
        _, _, r, c = os.path.basename(t)[:-4].split("_"); sheet.paste(Image.open(t).convert("RGB"), (int(c) * 300, int(r) * 360)); os.remove(t)
    sheet.save(os.path.join(D, f"rain-guests-{sex}-clips-and-poncho.png")); print("sheet", sex)
