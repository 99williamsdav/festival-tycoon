# Stage sets v1 palette: 128x8, 16 swatches (u = (8*slot+4)/128). Rugs and the lamp glow use their own flat materials.
from PIL import Image
import os
HERE = os.path.dirname(os.path.abspath(__file__)); os.makedirs(os.path.join(HERE, "tex"), exist_ok=True)
SLOTS = ["5A4632", "4A3626", "F0E6CC", "3A3A3A", "141414", "E04E8A", "D8D8E0", "EDE6D2",
         "2E6862", "5A5A5A", "0E0E0E", "C9B48A", "8A7A5A", "B0B4B8", "000000", "000000"]
# 0 wood/poles, 1 dark wood, 2 cream amp, 3 grill, 4 black amp, 5 tinsel pink, 6 tinsel silver, 7 bedsheet,
# 8 teal, 9 rail grey, 10 amp head, 11 trestle top, 12 trestle legs, 13 laptop
im = Image.new("RGB", (128, 8))
for i, h in enumerate(SLOTS):
    im.paste(tuple(int(h[k:k + 2], 16) for k in (0, 2, 4)), (8 * i, 0, 8 * i + 8, 8))
im.save(os.path.join(HERE, "tex", "stage_set_palette.png")); print("palette ok")
