# usage: blender -b --python refit_overlay.py -- <role> <female|male> <out_dir>
# Refits the approved v1 staff overlay onto the v6 attendee body. Only vertex positions change:
# topology, UVs, trim palette material and object name are kept.
import bpy, sys, os, json, hashlib
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from refit_common import ROOT, append, Transfer

role, sex, out = sys.argv[sys.argv.index("--") + 1:][:3]
bpy.ops.wm.read_factory_settings(use_empty=True)
src = append(ROOT + f"role-assets-v1/lwf_{role}_{sex}_overlay_v1.blend",
             lambda n: n.startswith("LWF_") or "Attendee" in n)
garment = next(o for o in src if o.name.startswith("LWF_"))
old = next(o for o in src if o is not garment)
body = append(ROOT + f"attendee-v6-draft/{sex}-attendee-v6.blend", lambda n: True)[0]
body.name = "REVIEW_v6_Body"

stats = Transfer(old, body).fit(garment.data)
bpy.data.objects.remove(old)
base = os.path.join(out, f"lwf_{role}_{sex}_overlay_v2")
bpy.ops.wm.save_as_mainfile(filepath=base + ".blend")
bpy.ops.object.select_all(action='DESELECT'); garment.select_set(True)
bpy.context.view_layer.objects.active = garment
bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format='GLB', use_selection=True,
                          export_yup=True, export_animations=False)
stats.update(file=os.path.basename(base) + ".glb", node=garment.name,
             triangles=sum(len(p.vertices) - 2 for p in garment.data.polygons),
             sha256=hashlib.sha256(open(base + ".glb", "rb").read()).hexdigest())
json.dump(stats, open(base + ".json", "w"), indent=1)
print("REFIT", json.dumps(stats))
