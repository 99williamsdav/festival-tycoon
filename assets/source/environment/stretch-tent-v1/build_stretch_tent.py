# blender -b --python build_stretch_tent.py
# -> out/lwf_stretch_tent_v1.glb            the "UNDER MY UMBRELLA" stretch tent (approved: heavy-rain board, marquee C)
#    out/lwf_stretch_tent_layout_v1.json    footprint, colliders, shelter polygon and rectangle, anchors, triangle counts
# Godot frame: origin at the ground centre of the sail, front (banner side) +Z, metres. G() converts to Blender.
import bpy, bmesh, math, os, json, hashlib
HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "out")
def G(x, y, z): return (x, -z, y)
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene


def lin(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in c) + (1,)


def flat(name, hexc, rough=0.8, double=False):
    m = bpy.data.materials.new(name); m.use_nodes = True; b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = lin(hexc); b.inputs["Roughness"].default_value = rough; m.use_backface_culling = not double
    return m


# ---------------------------------------------------------------- the sail
FX, FZ = 7.4, 5.4
ANCH = {(0, 0): 1.85, (1, 0): 1.95, (0, 1): 2.35, (1, 1): 2.25}          # corner heights: front (+Z) high for the banner, back low for shade
POLES = ((-1.7, -0.3, 3.7), (1.9, 0.3, 3.5))                             # the two tall poles (x, z, sail height at the pole)


def warp(u, v):
    x, z = (u - 0.5) * FX, (v - 0.5) * FZ                                 # scalloped edges curve in between the corners
    return x * (1 - 0.10 * math.sin(math.pi * v) * abs(2 * u - 1) ** 3), z * (1 - 0.12 * math.sin(math.pi * u) * abs(2 * v - 1) ** 3)


def height(u, v):
    x, z = warp(u, v)
    base = ANCH[(0, 0)] * (1 - u) * (1 - v) + ANCH[(1, 0)] * u * (1 - v) + ANCH[(0, 1)] * (1 - u) * v + ANCH[(1, 1)] * u * v
    bump = 0.0
    for px, pz, ph in POLES: bump = max(bump, (ph - base) * max(0.0, 1 - math.hypot(x - px, z - pz) / 3.0) ** 1.3)
    return base + bump


NX, NZ = 36, 28
bm = bmesh.new(); uvl = bm.loops.layers.uv.new("UVMap"); vs = {}
for j in range(NZ + 1):
    for i in range(NX + 1):
        u, v = i / NX, j / NZ; x, z = warp(u, v); vs[i, j] = bm.verts.new(G(x, height(u, v), z))
for j in range(NZ):
    for i in range(NX):
        f = bm.faces.new((vs[i, j], vs[i + 1, j], vs[i + 1, j + 1], vs[i, j + 1]))
        for l, (a, b) in zip(f.loops, ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))): l[uvl].uv = (a / NX, b / NZ)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
if sum(f.normal.z for f in bm.faces) < 0:
    for f in bm.faces: f.normal_flip()
me = bpy.data.meshes.new("LWF_StretchTent_Sail"); bm.to_mesh(me); bm.free()
for p in me.polygons: p.use_smooth = True
canvas = flat("LWF_StretchTent_Canvas", "cfae7c", 0.75, double=True); me.materials.append(canvas)
sail = bpy.data.objects.new("LWF_StretchTent_Sail", me); sc.collection.objects.link(sail)

# ---------------------------------------------------------------- frame: poles, guy ropes, pegs
bm = bmesh.new(); uvl = bm.loops.layers.uv.new("UVMap"); MI = []
def cyl(a, b, r, n, mi):
    import mathutils
    a, b = mathutils.Vector(G(*a)), mathutils.Vector(G(*b)); d = b - a
    q = d.to_track_quat('Z', 'Y'); M = mathutils.Matrix.Translation((a + b) / 2) @ q.to_matrix().to_4x4()
    res = bmesh.ops.create_cone(bm, cap_ends=True, segments=n, radius1=r, radius2=r, depth=d.length, matrix=M)
    for f in {f for v in res["verts"] for f in v.link_faces}: f.material_index = mi
def box(c, s, mi):
    import mathutils
    res = bmesh.ops.create_cube(bm, size=1.0, matrix=mathutils.Matrix.Translation(G(*c)) @ mathutils.Matrix.Diagonal((s[0], s[2], s[1], 1)))
    for f in {f for v in res["verts"] for f in v.link_faces}: f.material_index = mi
WOOD, ROPE, STEEL = 0, 1, 2
COLLIDERS, PEGS = [], []
for px, pz, ph in POLES:
    cyl((px, 0, pz), (px, ph + 0.18, pz), 0.07, 10, WOOD); cyl((px, ph + 0.18, pz), (px, ph + 0.26, pz), 0.035, 8, STEEL)
    COLLIDERS.append(dict(kind="main_pole", x=px, z=pz, r=0.07, h=round(ph + 0.26, 2)))
for (iu, iv), hh in ANCH.items():
    x, z = warp(iu, iv); top = height(iu, iv)
    cyl((x, 0, z), (x, top + 0.06, z), 0.05, 8, WOOD); COLLIDERS.append(dict(kind="corner_pole", x=round(x, 2), z=round(z, 2), r=0.05, h=round(top + 0.06, 2)))
    gx, gz = x * 1.15 + math.copysign(0.35, x), z * 1.15 + math.copysign(0.25, z)          # guy rope out to a peg
    cyl((x, top, z), (gx, 0.02, gz), 0.009, 4, ROPE); box((gx, 0.03, gz), (0.05, 0.08, 0.05), STEEL); PEGS.append((round(gx, 2), round(gz, 2)))
bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
me = bpy.data.meshes.new("LWF_StretchTent_Frame"); bm.to_mesh(me); bm.free()
for m in (flat("LWF_StretchTent_PoleWood", "8a6a45", 0.75), flat("LWF_StretchTent_Rope", "d9d2c0", 0.85), flat("LWF_StretchTent_Steel", "6e757b", 0.45)): me.materials.append(m)
frame = bpy.data.objects.new("LWF_StretchTent_Frame", me); sc.collection.objects.link(frame)

# ---------------------------------------------------------------- banner: hung under the front edge, both faces read the right way
ex, ez = warp(0.5, 1.0); edge = height(0.5, 1.0); BW, BH = 2.8, 0.29; top = edge - 0.03; zb = ez - 0.04
bm = bmesh.new(); uvl = bm.loops.layers.uv.new("UVMap")
for dz, flip in ((0.006, False), (-0.006, True)):
    pts = [(-BW / 2, top - BH, zb + dz), (BW / 2, top - BH, zb + dz), (BW / 2, top, zb + dz), (-BW / 2, top, zb + dz)]
    if flip: pts = [pts[1], pts[0], pts[3], pts[2]]
    f = bm.faces.new([bm.verts.new(G(*p)) for p in pts])
    for l, c in zip(f.loops, ((0, 0), (1, 0), (1, 1), (0, 1))): l[uvl].uv = c
me = bpy.data.meshes.new("LWF_StretchTent_Banner"); bm.to_mesh(me); bm.free()
bmat = bpy.data.materials.new("LWF_StretchTent_Banner"); bmat.use_nodes = True; bb = bmat.node_tree.nodes["Principled BSDF"]
tx = bmat.node_tree.nodes.new("ShaderNodeTexImage"); tx.image = bpy.data.images.load(os.path.join(OUT, "lwf_stretch_tent_banner_v1.png"))
bmat.node_tree.links.new(tx.outputs[0], bb.inputs["Base Color"]); bb.inputs["Roughness"].default_value = 0.8; bmat.use_backface_culling = True; me.materials.append(bmat)
banner = bpy.data.objects.new("LWF_StretchTent_Banner", me); sc.collection.objects.link(banner)

# ---------------------------------------------------------------- anchors
def empty(name, p):
    e = bpy.data.objects.new(name, None); e.location = G(*p); sc.collection.objects.link(e); return e
root = empty("LWF_StretchTent", (0, 0, 0))
for o in (sail, frame, banner): o.parent = root
shelter = empty("LWF_StretchTent_Shelter", (0, 0, 0)); shelter.parent = root

# ---------------------------------------------------------------- shelter: the sail's outline pulled in 0.3 m, and the minimum headroom inside it
outline = []
for k in range(64):
    t = k / 64
    if t < 0.25: u, v = t * 4, 0.0
    elif t < 0.5: u, v = 1.0, (t - 0.25) * 4
    elif t < 0.75: u, v = 1 - (t - 0.5) * 4, 1.0
    else: u, v = 0.0, 1 - (t - 0.75) * 4
    x, z = warp(u, v); L = math.hypot(x, z); s = max(0.0, L - 0.3) / L
    outline.append([round(x * s, 2), round(z * s, 2)])
inner = [height(i / 20, j / 20) for i in range(2, 19) for j in range(2, 19)]
bpy.ops.object.select_all(action='DESELECT')
for o in (root, sail, frame, banner, shelter): o.select_set(True)
bpy.context.view_layer.objects.active = root
path = os.path.join(OUT, "lwf_stretch_tent_v1.glb")
bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_yup=True, export_animations=False)
tris = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in (sail, frame, banner)}
xs = [p[0] for p in PEGS] + [c["x"] for c in COLLIDERS]; zs = [p[1] for p in PEGS] + [c["z"] for c in COLLIDERS]
layout = dict(
    file="lwf_stretch_tent_v1.glb", frame="Godot, origin at the ground centre, banner side +Z, metres", triangles=tris,
    sail_size_m=[FX, FZ], sail_corner_heights_m={"back_left": ANCH[(0, 0)], "back_right": ANCH[(1, 0)], "front_left": ANCH[(0, 1)], "front_right": ANCH[(1, 1)]},
    sail_peaks_m=[dict(x=px, z=pz, h=ph) for px, pz, ph in POLES], min_headroom_inside_m=round(min(inner), 2),
    footprint_m=dict(x=[round(min(xs) - 0.1, 2), round(max(xs) + 0.1, 2)], z=[round(min(zs) - 0.1, 2), round(max(zs) + 0.1, 2)]),
    colliders=COLLIDERS, pegs=PEGS,
    shelter_polygon_m=outline, shelter_rect_m=dict(x=[-3.0, 3.0], z=[-2.1, 2.1]), shelter_area_m2=round(6.0 * 4.2, 1),
    banner=dict(node="LWF_StretchTent_Banner", centre=[0, round(top - BH / 2, 3), round(zb, 3)], size_m=[BW, BH]),
    fade_material="LWF_StretchTent_Canvas", sha256=hashlib.sha256(open(path, "rb").read()).hexdigest())
json.dump(layout, open(os.path.join(OUT, "lwf_stretch_tent_layout_v1.json"), "w"), indent=1)
print("EXPORTED", tris, "headroom", layout["min_headroom_inside_m"], "footprint", layout["footprint_m"])
