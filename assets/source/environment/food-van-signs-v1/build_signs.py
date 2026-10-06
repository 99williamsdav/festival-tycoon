# blender -b --python build_signs.py -> out/lwf_food_van_sign_chips_v1.glb and out/lwf_food_van_sign_pizza_v1.glb
# A double-sided cut-out picture board on a flat iron pole and roof plate. Origin at the roof contact point (pole foot centre).
# The board faces the van's front (+Z) and back (-Z). The two faces are separate single-sided quads 30 mm apart, the back one
# with mirrored U, so the picture reads the right way round from either side. The pole runs between them.
# Place as a child of ApprovedFoodVanAssembly at (2.10, 2.75, 0.0): the centre of the roof top (roof x -0.06..4.26, z +-1.14).
import bpy, bmesh, os, json, hashlib
HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "out")
def G(x, y, z): return (x, -z, y)                 # Godot -> Blender
REPORT = {}


def picture_mat(name, png):
    m = bpy.data.materials.new(name); m.use_nodes = True; b = m.node_tree.nodes["Principled BSDF"]
    tx = m.node_tree.nodes.new("ShaderNodeTexImage"); tx.image = bpy.data.images.load(os.path.join(OUT, png))
    m.node_tree.links.new(tx.outputs["Color"], b.inputs["Base Color"]); m.node_tree.links.new(tx.outputs["Alpha"], b.inputs["Alpha"])
    b.inputs["Roughness"].default_value = 0.8; b.inputs["Specular IOR Level"].default_value = 0.3
    m.blend_method = 'CLIP'; m.alpha_threshold = 0.5; m.use_backface_culling = True
    return m


def iron_mat():
    m = bpy.data.materials.new("LWF_FoodVanSign_Iron"); m.use_nodes = True; b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (0.024, 0.027, 0.030, 1); b.inputs["Roughness"].default_value = 0.75; m.use_backface_culling = True
    return m


def build(key, png, bw, bh, lift=0.30):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    pic, iron = picture_mat(f"LWF_FoodVanSign_{key.title()}", png), iron_mat()
    bm = bmesh.new(); uv = bm.loops.layers.uv.new("UVMap")
    y0, y1, hw, dz = lift, lift + bh, bw / 2, 0.015
    def quad(pts, uvs, mi):
        f = bm.faces.new([bm.verts.new(G(*p)) for p in pts]); f.material_index = mi
        for l, c in zip(f.loops, uvs): l[uv].uv = c
        return f
    quad([(-hw, y0, dz), (hw, y0, dz), (hw, y1, dz), (-hw, y1, dz)], [(0, 0), (1, 0), (1, 1), (0, 1)], 0)       # front, faces +Z
    quad([(hw, y0, -dz), (-hw, y0, -dz), (-hw, y1, -dz), (hw, y1, -dz)], [(0, 0), (1, 0), (1, 1), (0, 1)], 0)    # back, faces -Z, U mirrored
    def box(c, s):
        r = bmesh.ops.create_cube(bm, size=1.0)["verts"]
        for v in r: v.co = (G(c[0] + v.co.x * s[0], c[1] + v.co.z * s[1], c[2] - v.co.y * s[2]))
        for f in {f for v in r for f in v.link_faces}:
            f.material_index = 1
            for l in f.loops: l[uv].uv = (0.5, 0.5)
    box((0, (y0 + bh * 0.5) / 2, 0), (0.07, y0 + bh * 0.5, 0.02))      # flat pole, roof to the board's middle, between the faces
    box((0, 0.012, 0), (0.36, 0.024, 0.22))                           # roof plate
    bmesh.ops.recalc_face_normals(bm, faces=[f for f in bm.faces if f.material_index == 1])
    me = bpy.data.meshes.new(f"LWF_FoodVan_Sign_{key.title()}"); bm.to_mesh(me); bm.free()
    me.materials.append(pic); me.materials.append(iron)
    for p in me.polygons: p.use_smooth = False
    o = bpy.data.objects.new(me.name, me); bpy.context.scene.collection.objects.link(o)
    o.select_set(True); bpy.context.view_layer.objects.active = o
    name = f"lwf_food_van_sign_{key}_v1"; path = os.path.join(OUT, name + ".glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_yup=True)
    REPORT[name] = dict(file=name + ".glb", node=me.name, board_m=[bw, bh], board_bottom_m=y0, top_m=round(y1, 3),
                        triangles=sum(len(p.vertices) - 2 for p in me.polygons),
                        place="child of ApprovedFoodVanAssembly at (2.10, 2.75, 0.0); in vendor space (-0.05, 2.75, 0.0)",
                        sha256=hashlib.sha256(open(path, "rb").read()).hexdigest())
    print("EXPORTED", name, REPORT[name]["triangles"])


build("chips", "lwf_food_van_sign_chips_v1.png", 1.4, 1.75)
build("pizza", "lwf_food_van_sign_pizza_v1.png", 1.55, 1.55)
json.dump(REPORT, open(os.path.join(OUT, "food_van_signs_report.json"), "w"), indent=1)
