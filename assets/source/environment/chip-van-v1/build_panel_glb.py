# blender -b --python build_panel_glb.py -> out/lwf_food_van_name_panel_chip_block_v1.glb
# A 2-triangle decal, 2.5 x 0.31 m, centred on its origin and facing Godot +Z, with the name panel texture embedded.
# Add it as a child of the fascia node at Position (0, 0, 0.056): 6 mm proud of the fascia's front face.
import bpy, os, hashlib, json
HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.join(HERE, "out")
bpy.ops.wm.read_factory_settings(use_empty=True)
hw, hh = 1.25, 0.155
me = bpy.data.meshes.new("LWF_FoodVan_NamePanel")
me.from_pydata([(-hw, 0, -hh), (hw, 0, -hh), (hw, 0, hh), (-hw, 0, hh)], [], [(0, 1, 2, 3)])   # Blender -Y = Godot +Z: faces -Y
me.polygons[0].normal_flip() if me.polygons[0].normal.y > 0 else None
uv = me.uv_layers.new(name="UVMap")
for i, c in enumerate(((0, 0), (1, 0), (1, 1), (0, 1))): uv.data[i].uv = c
me.update()
m = bpy.data.materials.new("LWF_FoodVan_NamePanel_ChipBlock"); m.use_nodes = True; b = m.node_tree.nodes["Principled BSDF"]
tx = m.node_tree.nodes.new("ShaderNodeTexImage"); tx.image = bpy.data.images.load(os.path.join(OUT, "lwf_food_van_name_panel_chip_block_v1.png"))
m.node_tree.links.new(tx.outputs[0], b.inputs["Base Color"]); b.inputs["Roughness"].default_value = 0.8; b.inputs["Specular IOR Level"].default_value = 0.3
me.materials.append(m)
o = bpy.data.objects.new("LWF_FoodVan_NamePanel", me); bpy.context.scene.collection.objects.link(o)
o.select_set(True); bpy.context.view_layer.objects.active = o
path = os.path.join(OUT, "lwf_food_van_name_panel_chip_block_v1.glb")
bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_yup=True, export_image_format='AUTO')
n = me.polygons[0].normal
json.dump(dict(file=os.path.basename(path), node="LWF_FoodVan_NamePanel", size_m=[2.5, 0.31], faces="+Z (Godot)", triangles=2,
               place="child of the fascia node, Position (0, 0, 0.056)", sha256=hashlib.sha256(open(path, "rb").read()).hexdigest(),
               normal_blender=[round(n.x, 3), round(n.y, 3), round(n.z, 3)]), open(os.path.join(OUT, "chip_van_report.json"), "w"), indent=1)
print("EXPORTED", path)
