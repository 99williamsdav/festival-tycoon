# blender -b --python build_curry_van.py
# -> out/lwf_food_van_name_panel_korma_v1.glb   2.5 x 0.31 m decal facing +Z; child of the fascia node at (0, 0, 0.056)
#    out/lwf_food_van_sign_curry_v1.glb         1.55 m double-sided cut-out roof board; child of ApprovedFoodVanAssembly at (2.10, 2.75, 0)
#    out/lwf_food_van_kitchen_dressing_v1.glb   roof vents, foil tray stacks on the counter, and two steam marker nodes;
#                                               authored in ApprovedFoodVanAssembly space: child of it at (0, 0, 0)
#    out/lwf_food_van_menu_board_korma_v1.glb   chalk A-board, origin at its ground centre; child of ApprovedFoodVanAssembly at (4.10, 0, 2.30)
# All coordinates below are Godot (x, y, z); G() converts to Blender.
import bpy, bmesh, os, json, hashlib, math
HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "out")
def G(x, y, z): return (x, -z, y)
REPORT = {}


def lin(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in c) + (1,)


def flat(name, hexc, rough=0.8):
    m = bpy.data.materials.new(name); m.use_nodes = True; b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = lin(hexc); b.inputs["Roughness"].default_value = rough; m.use_backface_culling = True
    return m


def textured(name, png, clip=False):
    m = bpy.data.materials.new(name); m.use_nodes = True; b = m.node_tree.nodes["Principled BSDF"]
    tx = m.node_tree.nodes.new("ShaderNodeTexImage"); tx.image = bpy.data.images.load(os.path.join(OUT, png))
    m.node_tree.links.new(tx.outputs["Color"], b.inputs["Base Color"]); b.inputs["Roughness"].default_value = 0.8
    b.inputs["Specular IOR Level"].default_value = 0.3
    if clip: m.node_tree.links.new(tx.outputs["Alpha"], b.inputs["Alpha"]); m.blend_method = 'CLIP'; m.alpha_threshold = 0.5
    m.use_backface_culling = True; return m


class Kit:
    def __init__(self): self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new("UVMap")
    def quad(self, pts, mi, uvs=((0, 0), (1, 0), (1, 1), (0, 1))):
        f = self.bm.faces.new([self.bm.verts.new(G(*p)) for p in pts]); f.material_index = mi
        for l, c in zip(f.loops, uvs): l[self.uv].uv = c
        return f
    def box(self, c, s, mi, rx=0.0):
        """axis box centred at c (Godot), size s, optionally tilted about Godot X by rx (radians)"""
        hx_, hy, hz = s[0] / 2, s[1] / 2, s[2] / 2
        def P(x, y, z):
            y2, z2 = y * math.cos(rx) - z * math.sin(rx), y * math.sin(rx) + z * math.cos(rx)
            return (c[0] + x, c[1] + y2, c[2] + z2)
        v = [P(x, y, z) for x in (-hx_, hx_) for y in (-hy, hy) for z in (-hz, hz)]
        for f in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
            self.quad([v[i] for i in f], mi, ((0.5, 0.5),) * 4)
    def cyl(self, c, r, h, n, mi, top_mi=None):
        ring = [(c[0] + r * math.cos(2 * math.pi * i / n), c[2] + r * math.sin(2 * math.pi * i / n)) for i in range(n)]
        y0, y1 = c[1], c[1] + h
        for i in range(n):
            (ax, az), (bx, bz) = ring[i], ring[(i + 1) % n]
            self.quad([(ax, y0, az), (ax, y1, az), (bx, y1, bz), (bx, y0, bz)], mi, ((0.5, 0.5),) * 4)
        self.quad([(x, y1, z) for x, z in ring], top_mi if top_mi is not None else mi, ((0.5, 0.5),) * n)
    def mesh(self, name, mats, fix_normals=True):
        # closed solids get outward normals; the picture quads (material 0 when fix_normals is False) keep their authored facing
        bmesh.ops.recalc_face_normals(self.bm, faces=[f for f in self.bm.faces if fix_normals or f.material_index != 0])
        me = bpy.data.meshes.new(name); self.bm.to_mesh(me); self.bm.free()
        for m in mats: me.materials.append(m)
        for p in me.polygons: p.use_smooth = False
        o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); return o


def export(objs, name, **meta):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    path = os.path.join(OUT, name + ".glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_yup=True, export_animations=False)
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs if o.type == 'MESH')
    REPORT[name] = dict(file=name + ".glb", nodes=[o.name for o in objs], triangles=tris, sha256=hashlib.sha256(open(path, "rb").read()).hexdigest(), **meta)
    print("EXPORTED", name, tris)


# ---------------------------------------------------------------- name panel decal (as the chip and pizza panels)
bpy.ops.wm.read_factory_settings(use_empty=True)
k = Kit(); hw, hh = 1.25, 0.155
k.quad([(-hw, -hh, 0), (hw, -hh, 0), (hw, hh, 0), (-hw, hh, 0)], 0)
panel = k.mesh("LWF_FoodVan_NamePanel", [textured("LWF_FoodVan_NamePanel_Korma", "lwf_food_van_name_panel_korma_v1.png")], fix_normals=False)
export([panel], "lwf_food_van_name_panel_korma_v1", size_m=[2.5, 0.31], faces="+Z (Godot)", place="child of the fascia node at (0, 0, 0.056)")

# ---------------------------------------------------------------- roof sign (same construction as food-van-signs-v1)
bpy.ops.wm.read_factory_settings(use_empty=True)
k = Kit(); bw = bh = 1.55; y0, y1, hw, dz = 0.30, 0.30 + 1.55, 1.55 / 2, 0.015
k.quad([(-hw, y0, dz), (hw, y0, dz), (hw, y1, dz), (-hw, y1, dz)], 0)                  # front, faces +Z
k.quad([(hw, y0, -dz), (-hw, y0, -dz), (-hw, y1, -dz), (hw, y1, -dz)], 0)              # back, faces -Z, U mirrored
k.box((0, (y0 + bh * 0.5) / 2, 0), (0.07, y0 + bh * 0.5, 0.02), 1)                     # flat pole between the faces
k.box((0, 0.012, 0), (0.36, 0.024, 0.22), 1)                                           # roof plate
iron = flat("LWF_FoodVanSign_Iron", "2b2e31", 0.75)
sign = k.mesh("LWF_FoodVan_Sign_Curry", [textured("LWF_FoodVanSign_Curry", "lwf_food_van_sign_curry_v1.png", clip=True), iron], fix_normals=False)
export([sign], "lwf_food_van_sign_curry_v1", board_m=[bw, bh], board_bottom_m=y0, top_m=y1,
       place="child of ApprovedFoodVanAssembly at (2.10, 2.75, 0.0); in vendor space (-0.05, 2.75, 0.0)")

# ---------------------------------------------------------------- kitchen dressing: roof vents + steam markers, foil tray stacks
# ApprovedFoodVanAssembly space. Roof top y 2.75 (x -0.06..4.26, z +-1.14). Counter top y 1.24 (x ~0.8..3.6, z 1.16..1.50).
bpy.ops.wm.read_factory_settings(use_empty=True)
mats = [flat("LWF_KitchenDressing_Galvanised", "9a9da2", 0.55), flat("LWF_KitchenDressing_VentCap", "5e6266", 0.6),
        flat("LWF_KitchenDressing_Foil", "c9cfd4", 0.45), flat("LWF_KitchenDressing_Lid", "f4ede0"), flat("LWF_KitchenDressing_Label", "d9a43b")]
GALV, CAP, FOIL, LID, LABEL = range(5)
k = Kit()
VENTS = {"L": (0.95, 0.55), "R": (3.50, 0.55)}
for x, z in VENTS.values():
    k.cyl((x, 2.75, z), 0.12, 0.16, 10, GALV)                                          # vent stack on the roof
    k.cyl((x, 2.91, z), 0.17, 0.03, 10, CAP)                                           # rain cap (steam escapes under it)
    for a in range(3): k.box((x + 0.1 * math.cos(a * 2.1), 2.925, z + 0.1 * math.sin(a * 2.1)), (0.02, 0.05, 0.02), GALV)
STACKS = ((1.30, 1.36, 3), (1.55, 1.30, 2), (2.85, 1.34, 2), (3.12, 1.38, 4))           # (x, z, trays): either side of the hatch centre (2.15)
for x, z, n in STACKS:
    for i in range(n):
        yb = 1.24 + i * 0.042
        k.box((x, yb + 0.018, z), (0.20, 0.036, 0.13), FOIL)
        k.box((x, yb + 0.039, z), (0.215, 0.006, 0.145), LID if i == n - 1 else FOIL)
    k.box((x - 0.05, 1.24 + n * 0.042 + 0.0005, z + 0.02), (0.06, 0.002, 0.04), LABEL)  # a sticker on the top lid
dress = k.mesh("LWF_FoodVan_KitchenDressing", mats)
markers = []
for side, (x, z) in VENTS.items():
    e = bpy.data.objects.new(f"LWF_FoodVan_SteamVent_{side}", None); e.empty_display_type = 'SINGLE_ARROW'; e.empty_display_size = 0.3
    e.location = G(x, 2.90, z)                                                        # under the cap; unrotated, so its +Y (Godot) points up
    bpy.context.scene.collection.objects.link(e); e.parent = dress; markers.append(e)
export([dress] + markers, "lwf_food_van_kitchen_dressing_v1",
       place="child of ApprovedFoodVanAssembly at (0, 0, 0)",
       steam_markers={f"LWF_FoodVan_SteamVent_{s}": [x, 2.90, z] for s, (x, z) in VENTS.items()},
       tray_stacks=[dict(x=x, z=z, trays=n) for x, z, n in STACKS])

# ---------------------------------------------------------------- chalk A-board menu (both faces show the menu)
bpy.ops.wm.read_factory_settings(use_empty=True)
mats = [textured("LWF_MenuBoard_Korma", "lwf_food_van_menu_board_korma_v1.png"), flat("LWF_MenuBoard_Wood", "8a6a45"), flat("LWF_MenuBoard_Hinge", "2b2e31", 0.6)]
k = Kit(); BW, BH, LEAN, T = 0.44, 0.62, math.radians(13), 0.018
for s in (1, -1):                                                                     # two leaves, feet apart, tops meeting at the hinge
    rx = -s * LEAN
    cz = s * (BH / 2) * math.sin(LEAN); cy = (BH / 2) * math.cos(LEAN) + 0.02
    k.box((0, cy, cz), (BW, BH, T), 1, rx=rx)                                         # wooden leaf
    # menu face on the outside of the leaf, 1 mm proud
    def P(u, v):
        x = (u - 0.5) * (BW - 0.04); yl = (v - 0.5) * (BH - 0.04); zl = s * (T / 2 + 0.001)
        y2, z2 = yl * math.cos(rx) - zl * math.sin(rx), yl * math.sin(rx) + zl * math.cos(rx)
        return (x, cy + y2, cz + z2)
    if s > 0: k.quad([P(0, 0), P(1, 0), P(1, 1), P(0, 1)], 0)
    else: k.quad([P(1, 0), P(0, 0), P(0, 1), P(1, 1)], 0, ((0, 0), (1, 0), (1, 1), (0, 1)))
k.box((0, 0.02 + BH * math.cos(LEAN) - 0.01, 0), (BW * 0.9, 0.02, 0.03), 2)          # hinge bar where the leaves meet
board = k.mesh("LWF_FoodVan_MenuBoard_Korma", mats, fix_normals=False)
export([board], "lwf_food_van_menu_board_korma_v1", size_m=[BW, round(0.02 + BH * math.cos(LEAN), 3), round(2 * BH * math.sin(LEAN) + T, 3)],
       place="child of ApprovedFoodVanAssembly at (4.10, 0, 2.30), yaw 0 (faces +Z and -Z); vendor space (1.95, 0, 2.30)")
json.dump(REPORT, open(os.path.join(OUT, "curry_van_report.json"), "w"), indent=1)
