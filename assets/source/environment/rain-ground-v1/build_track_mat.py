# Track matting A: plastic ground-protection grid mats (approved heavy-rain board, item 6).
#   python build_track_mat.py texture             -> out/lwf_track_mat_grid_v1.png (1024 x 512, alpha = the holes)
#   blender -b --python build_track_mat.py        -> out/lwf_track_mat_grid_v1.glb
# One panel, 2.0 x 1.0 m, snapped to the 0.5 m ground grid (4 x 2 cells). Godot frame: origin at the panel's ground centre, the long
# side across the path on X, the short side along the path on Z. Laid as a 2.0 m wide walkway: one panel per 1.0 m of run.
import os, sys, math
HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "out"); os.makedirs(OUT, exist_ok=True)
PNG = os.path.join(OUT, "lwf_track_mat_grid_v1.png")
if "texture" in sys.argv:
    from PIL import Image, ImageDraw
    def hx(h, a=255): return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (a,)
    W, H = 1024, 512; im = Image.new("RGBA", (W, H), hx("33393a")); d = ImageDraw.Draw(im); step = 32
    for gy in range(16, H, step):
        for gx in range(16, W, step):
            ox = (step // 2) if ((gy - 16) // step) % 2 else 0; cx = gx + ox
            if 26 < cx < W - 26 and 26 < gy < H - 26:
                r = 11; d.polygon([(cx, gy - r), (cx + r, gy), (cx, gy + r), (cx - r, gy)], fill=(0, 0, 0, 0))   # diamond holes: the grass shows
    d.rectangle([0, 0, W - 1, H - 1], outline=hx("4a5254"), width=16)
    for x in (8, W - 9):
        for y in (H // 4, 3 * H // 4): d.rectangle([x - 8, y - 22, x + 8, y + 22], fill=hx("d9a43b"))                    # gold connector lugs
    for y in (8, H - 9):
        for x in (W // 4, W // 2, 3 * W // 4): d.rectangle([x - 22, y - 8, x + 22, y + 8], fill=hx("d9a43b"))
    im.save(PNG); print("texture ok"); sys.exit(0)

import bpy, bmesh, json, hashlib
def G(x, y, z): return (x, -z, y)
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
LX, LZ, TH, RIM = 2.0, 1.0, 0.03, 0.035
bm = bmesh.new(); uv = bm.loops.layers.uv.new("UVMap")
def quad(pts, uvs, mi):
    f = bm.faces.new([bm.verts.new(G(*p)) for p in pts]); f.material_index = mi
    for l, c in zip(f.loops, uvs): l[uv].uv = c
hx_, hz = LX / 2, LZ / 2
quad([(-hx_, TH, hz), (hx_, TH, hz), (hx_, TH, -hz), (-hx_, TH, -hz)], [(0, 0), (1, 0), (1, 1), (0, 1)], 0)       # grid top (alpha clip)
for (ax, az), (bx, bz) in (((-hx_, hz), (hx_, hz)), ((hx_, hz), (hx_, -hz)), ((hx_, -hz), (-hx_, -hz)), ((-hx_, -hz), (-hx_, hz))):
    quad([(ax, 0, az), (bx, 0, bz), (bx, TH, bz), (ax, TH, az)], [(0.5, 0.5)] * 4, 1)                               # solid rim sides
me = bpy.data.meshes.new("LWF_TrackMat_Grid"); bm.to_mesh(me); bm.free()
grid = bpy.data.materials.new("LWF_TrackMat_Grid"); grid.use_nodes = True; b = grid.node_tree.nodes["Principled BSDF"]
tx = grid.node_tree.nodes.new("ShaderNodeTexImage"); tx.image = bpy.data.images.load(PNG); tx.interpolation = 'Closest'
grid.node_tree.links.new(tx.outputs["Color"], b.inputs["Base Color"]); grid.node_tree.links.new(tx.outputs["Alpha"], b.inputs["Alpha"])
b.inputs["Roughness"].default_value = 0.55; grid.blend_method = 'CLIP'; grid.alpha_threshold = 0.5; grid.use_backface_culling = True
rim = bpy.data.materials.new("LWF_TrackMat_Rim"); rim.use_nodes = True; rb = rim.node_tree.nodes["Principled BSDF"]
rb.inputs["Base Color"].default_value = (0.068, 0.081, 0.085, 1); rb.inputs["Roughness"].default_value = 0.6; rim.use_backface_culling = True
me.materials.append(grid); me.materials.append(rim)
o = bpy.data.objects.new("LWF_TrackMat_Grid", me); sc.collection.objects.link(o)
for p in me.polygons:
    if p.material_index == 1 and p.normal.length and (p.center.to_2d().length > 0) and p.normal.dot(p.center.normalized()) < 0: p.flip()
o.select_set(True); bpy.context.view_layer.objects.active = o
path = os.path.join(OUT, "lwf_track_mat_grid_v1.glb")
bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_yup=True)
json.dump(dict(file="lwf_track_mat_grid_v1.glb", node="LWF_TrackMat_Grid", size_m=[LX, TH, LZ], cells=[4, 2], triangles=10,
               materials=["LWF_TrackMat_Grid (alpha scissor 0.5)", "LWF_TrackMat_Rim"], sha256=hashlib.sha256(open(path, "rb").read()).hexdigest()),
          open(os.path.join(OUT, "lwf_track_mat_grid_v1_report.json"), "w"), indent=1)
print("EXPORTED")
