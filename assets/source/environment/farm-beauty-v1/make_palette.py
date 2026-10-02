# Farm beauty pass v1 palette: 128x8, 16 swatches of 8 px (u = (8*slot+4)/128). Run with Pillow.
from PIL import Image
import os
HERE = os.path.dirname(os.path.abspath(__file__)); os.makedirs(os.path.join(HERE, "tex"), exist_ok=True)
SLOTS = ["24381B", "2F4722", "3B5629", "4A6633", "5C7840", "6E8A4C",   # 0-5 foliage, dark base to light top
         "F3EEE2", "E7A6B4", "A8332A", "5E4632",                       # 6 hawthorn blossom, 7 dog rose, 8 haws/apples, 9 wood/bark
         "6A5236", "B8B070", "8A6A48", "2F5A3E", "D9A13A", "4E7A3A"]   # 10 pond mud, 11 reed, 12 duck, 13 drake head, 14 bill, 15 lily pad
im = Image.new("RGB", (128, 8))
for i, h in enumerate(SLOTS):
    im.paste(tuple(int(h[k:k + 2], 16) for k in (0, 2, 4)), (8 * i, 0, 8 * i + 8, 8))
im.save(os.path.join(HERE, "tex", "farm_beauty_palette.png")); print("palette ok")
