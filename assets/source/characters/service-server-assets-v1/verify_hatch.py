import bpy,json,math,hashlib
from pathlib import Path
from mathutils import Vector as V
R=Path(__file__).resolve().parent;REPO=Path('C:/Projects/festival-tycoon')
def signatures(path):
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(path))
 ob=next(o for o in bpy.data.objects if o.type=='MESH');me=ob.data
 out=[];uv=me.uv_layers.active
 for p in me.polygons:
  points=[(round(me.vertices[me.loops[i].vertex_index].co.x,5),round(me.vertices[me.loops[i].vertex_index].co.y,5),round(me.vertices[me.loops[i].vertex_index].co.z,5),round(uv.data[i].uv.x,6),round(uv.data[i].uv.y,6)) for i in p.loop_indices]
  out.append(tuple(sorted(points)))
 return out,ob
old,_=signatures(REPO/'game/assets/environment/lwf_food_van_chassis_v1.glb')
new,ob=signatures(R/'runtime/lwf_food_van_chassis_open_service_v1.glb')
removed=set(old)-set(new);added=set(new)-set(old);retained=set(old)&set(new)
assert len(removed)==14,(len(removed),len(added))
assert len(added)==8,(len(removed),len(added))
for poly in removed:
 coords=[V((p[0],p[2],-p[1])) for p in poly]
 assert all(abs(v.z-1.1)<1e-4 for v in coords) or all(.8999<=v.x<=3.4001 and 1.2099<=v.y<=2.2301 and 1.0924<=v.z<=1.1376 for v in coords)
out={'original_triangles':len(old),'new_triangles':len(new),'unchanged_triangles_with_exact_positions_uv':len(retained),'removed_triangles':14,'added_triangles':8,'scope':'Only solid front face and dark hatch box removed; four front rectangles replace front around original hatch bounds.'}
(R/'verification/hatch-diff.json').write_text(json.dumps(out,indent=2));print(json.dumps(out))

