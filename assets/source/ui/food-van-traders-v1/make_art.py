# python make_art.py -> out/trader_art_<art>_v1.png : each trader's van, trimmed to its outline, 2x, fitted to 324 x 160 px
# (= the 162 x 80 unit art box on a 182-wide flyer). Vans are 3/4 renders of the installed liveries (render_van_art.py);
# coffee is a crop of the approved concept painting (coffee-van-v2/references) until its van is integrated and rendered.
from PIL import Image
import os
H = os.path.dirname(os.path.abspath(__file__)); O = os.path.join(H, "out")
def fit(im, w=324, h=160):
    r = min(w / im.width, h / im.height); return im.resize((int(im.width * r), int(im.height * r)), Image.LANCZOS)
for art in ("chip_block", "pizza", "korma"):
    im = Image.open(os.path.join(H, f"van_{art}.png")).convert("RGBA"); fit(im.crop(im.getbbox())).save(os.path.join(O, f"trader_art_{art}_v1.png"))
c = Image.open("C:/Projects/festival-tycoon/assets/source/environment/coffee-van-v2/references/authoritative-concept.png").convert("RGBA").crop((10, 170, 1030, 900))
fit(c).save(os.path.join(O, "trader_art_coffee_concept_v1.png"))
print("ok")
