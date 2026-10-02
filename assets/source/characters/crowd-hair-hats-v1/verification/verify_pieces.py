import bpy, math, os, sys
from mathutils import Vector, Matrix
exec(open("C:/Users/99wil/AppData/Local/Temp/claude/C--Projects-festival-tycoon/54ef13f4-4691-4176-a16c-f249c73119a8/scratchpad/hair/render_common.py").read())
STRIP = "C:/Users/99wil/AppData/Local/Temp/claude/C--Projects-festival-tycoon/54ef13f4-4691-4176-a16c-f249c73119a8/scratchpad/strip/"
P = "C:/Projects/festival-tycoon/assets/source/characters/crowd-hair-hats-v1/out/"
OUTD = HERE + "vtiles/"; os.makedirs(OUTD, exist_ok=True)
import json
C = json.load(open("C:/Projects/festival-tycoon/game/assets/characters/attendee_palette_v1_contract.json"))
sc, cam = setup((240, 240))
def imp(path):
    before = set(bpy.data.objects); bpy.ops.import_scene.gltf(filepath=path); return [o for o in bpy.data.objects if o not in before]
def recolour(objs, hair, cloth):
    slots = dict(C["clothing_colourways"][cloth]["slots"]); slots.update(C["hair_colours"][hair]["slots"])
    for o in objs:
        if o.type != 'MESH': continue
        for i, m in enumerate(o.data.materials):
            img = [n for n in m.node_tree.nodes if n.type == 'TEX_IMAGE'][0]
            if img.image.size[0] != 96: continue
            m2 = m.copy(); n2 = [n for n in m2.node_tree.nodes if n.type == 'TEX_IMAGE'][0]
            im = n2.image.copy(); px = list(im.pixels); W = im.size[0]
            for sl, hx in slots.items():
                rgb = [int(hx[k:k + 2], 16) / 255 for k in (0, 2, 4)]
                for y in range(im.size[1]):
                    for x in range(8 * int(sl), 8 * int(sl) + 8): j = (y * W + x) * 4; px[j:j + 3] = rgb
            im.pixels = px; n2.image = im; n2.interpolation = 'Closest'
            o.data.materials[i] = m2
combos = [  # name, body file, pieces, hair colour, cloth
 ("m_bald", "lwf_attendee_male_relaxed_v2", [], 0, 0),
 ("m_default", "lwf_attendee_male_relaxed_v2", ["lwf_hair_male_default_v1"], 1, 1),
 ("m_default_beard", "lwf_attendee_male_relaxed_v2", ["lwf_hair_male_default_v1", "lwf_beard_male_v1"], 2, 2),
 ("m_mohawk", "lwf_attendee_male_relaxed_v2", ["lwf_hair_male_mohawk_v1"], 0, 3),
 ("m_cap", "lwf_attendee_male_relaxed_v2", ["lwf_hair_male_default_under_cap_v1", "lwf_hat_cap_male_v1"], 3, 0),
 ("m_cap_glasses_beard", "lwf_attendee_male_relaxed_v2", ["lwf_hair_male_default_under_cap_v1", "lwf_hat_cap_male_v1", "lwf_sunglasses_male_v1", "lwf_beard_male_v1"], 1, 1),
 ("m_bald_cap", "lwf_attendee_male_relaxed_v2", ["lwf_hat_cap_male_v1"], 0, 2),
 ("m_crown_glasses", "lwf_attendee_male_relaxed_v2", ["lwf_hair_male_default_v1", "lwf_hat_flower_crown_male_v1", "lwf_sunglasses_male_v1"], 2, 3),
 ("f_default", "lwf_attendee_female_relaxed_v2", ["lwf_hair_female_default_v1"], 0, 0),
 ("f_mohawk", "lwf_attendee_female_relaxed_v2", ["lwf_hair_female_mohawk_v1"], 3, 1),
 ("f_cap", "lwf_attendee_female_relaxed_v2", ["lwf_hair_female_default_under_cap_v1", "lwf_hat_cap_female_v1"], 2, 2),
 ("f_cap_glasses", "lwf_attendee_female_relaxed_v2", ["lwf_hair_female_default_under_cap_v1", "lwf_hat_cap_female_v1", "lwf_sunglasses_female_v1"], 1, 3),
 ("f_crown", "lwf_attendee_female_relaxed_v2", ["lwf_hair_female_default_v1", "lwf_hat_flower_crown_female_v1"], 3, 0),
 ("f_glasses", "lwf_attendee_female_relaxed_v2", ["lwf_hair_female_default_v1", "lwf_sunglasses_female_v1"], 0, 1),
 ("m_beer_beard_glasses", "lwf_attendee_male_drinking_beer_v2", ["lwf_hair_male_default_v1", "lwf_beard_male_v1", "lwf_sunglasses_male_v1"], 0, 2),
 ("m_eating_beard_cap", "lwf_attendee_male_eating_v2", ["lwf_hair_male_default_under_cap_v1", "lwf_hat_cap_male_v1", "lwf_beard_male_v1"], 1, 3),
 ("m_soft_beard", "lwf_attendee_male_drinking_soft_v2", ["lwf_hair_male_default_v1", "lwf_beard_male_v1"], 2, 0),
 ("f_beer_cap_glasses", "lwf_attendee_female_drinking_beer_v2", ["lwf_hair_female_default_under_cap_v1", "lwf_hat_cap_female_v1", "lwf_sunglasses_female_v1"], 3, 1),
 ("f_eating_crown", "lwf_attendee_female_eating_v2", ["lwf_hair_female_default_v1", "lwf_hat_flower_crown_female_v1"], 0, 2),
 ("perf_m_mohawk", "lwf_performer_male_body_v2", ["lwf_hair_male_mohawk_v1"], 1, 0),
 ("perf_f_mohawk", "lwf_performer_female_body_v2", ["lwf_hair_female_mohawk_v1"], 2, 0),
]
names = []
for k, (name, bodyf, pieces, hc, cc) in enumerate(combos):
    objs = imp(STRIP + bodyf + ".glb")
    for p in pieces: objs += imp(P + p + ".glb")
    if "performer" in bodyf:
        for o in objs:
            if "Idle" in o.name: pass
    recolour(objs, hc, cc)
    root = bpy.data.objects.new("r", None); sc.collection.objects.link(root); root.location = (k * 3.0, 0, 0)
    for o in objs:
        if o.parent is None: o.parent = root
    for yaw, el in ((30, 8), (150, 18)):
        cam.data.ortho_scale = 0.66; cam.data.clip_start = 4.5; cam.data.clip_end = 7.5
        aim(cam, Vector((k * 3.0, 0, 1.52 if name.startswith(("m", "perf_m")) else 1.47)), yaw, el)
        pth = OUTD + f"{name}_{yaw}.png"; render(pth); names.append(pth)
open(HERE + "verify.txt", "w").write(chr(10).join(names))
